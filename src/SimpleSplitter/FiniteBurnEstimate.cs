using System;

namespace SimpleSplitter
{
    // Two-body execution diagnostic: constant full thrust, fixed inertial
    // maneuver direction within each propulsion segment. Executed state carries coast/phase errors
    // into subsequent burns. It does not retarget the stock nodes.
    internal readonly struct FiniteBurnEstimate
    {
        internal FiniteBurnEstimate(double cosineLoss, double energyLoss, double positionError, double velocityError)
        {
            CosineLoss = cosineLoss;
            EnergyLoss = energyLoss;
            PositionError = positionError;
            VelocityError = velocityError;
        }
        internal double CosineLoss { get; }
        internal double EnergyLoss { get; }
        internal double PositionError { get; }
        internal double VelocityError { get; }
        internal bool IsFinite => CandidateRules.IsFinite(CosineLoss) &&
            CandidateRules.IsFinite(EnergyLoss) && CandidateRules.IsFinite(PositionError) &&
            CandidateRules.IsFinite(VelocityError);

        internal static FiniteBurnEstimate Measure(Orbit before, Orbit after, NodeSpec node,
            BurnPhysics engine, double spent)
            => Measure(before, after, node, engine, spent, before, out _);

        internal static FiniteBurnEstimate Measure(Orbit before, Orbit after, NodeSpec node,
            BurnPhysics engine, double spent, Orbit executedBefore, out Orbit executedAfter,
            Func<Vector3d, double, bool>? positionIsSafe = null)
        {
            EvaluationBudget? budget = EvaluationBudget.Current;
            if (positionIsSafe != null)
                return MeasureUncached(before, after, node, engine, spent, executedBefore, out executedAfter, positionIsSafe);
            if (budget != null && budget.TryReplay(out FiniteBurnEstimate cached, out executedAfter)) return cached;
            FiniteBurnEstimate result = MeasureUncached(before, after, node, engine, spent, executedBefore, out executedAfter);
            budget?.Store(result, executedAfter);
            return result;
        }

        private static FiniteBurnEstimate MeasureUncached(Orbit before, Orbit after, NodeSpec node,
            BurnPhysics engine, double spent, Orbit executedBefore, out Orbit executedAfter,
            Func<Vector3d, double, bool>? positionIsSafe = null)
        {
            executedAfter = executedBefore;
            before.GetOrbitalStateVectorsAtUT(node.Ut, out Vector3d center, out Vector3d velocity);
            double startUt = node.Ut - node.StartOffset;
            executedBefore.GetOrbitalStateVectorsAtUT(startUt, out Vector3d r, out Vector3d v);
            Vector3d direction = (SplitPlanner.NodeRotation(before, node.Ut) * node.DeltaV).xzy.normalized;
            double mu = before.referenceBody.gravParameter;
            double safeRadius = before.referenceBody.Radius +
                (before.referenceBody.atmosphere ? before.referenceBody.atmosphereDepth : 0.0);
            double initialMass = engine.MassAfter(spent);
            double flow = engine.Thrust / engine.ExhaustVelocity;
            int steps = Math.Max(64, Math.Min(8192, (int)Math.Ceiling(node.Duration / 0.5)));
            double step = node.Duration / steps;
            double cosineLoss = 0.0;
            for (int i = 0; i <= steps; i++)
            {
                cosineLoss = Math.Max(cosineLoss, 1.0 - Vector3d.Dot(center.normalized, r.normalized));
                if (r.magnitude <= safeRadius || r.magnitude >= before.referenceBody.sphereOfInfluence ||
                    (positionIsSafe != null && !positionIsSafe(r, startUt + i * step)))
                    return new FiniteBurnEstimate(double.NaN, double.NaN, double.NaN, double.NaN);
                if (i == steps) break;
                double mass = initialMass - flow * (i * step);
                Vector3d a1 = Acceleration(r, direction, mu, engine.Thrust / mass);
                Vector3d v2 = v + a1 * (step * 0.5);
                Vector3d a2 = Acceleration(r + v * (step * 0.5), direction, mu,
                    engine.Thrust / (mass - flow * step * 0.5));
                Vector3d v3 = v + a2 * (step * 0.5);
                Vector3d a3 = Acceleration(r + v2 * (step * 0.5), direction, mu,
                    engine.Thrust / (mass - flow * step * 0.5));
                Vector3d v4 = v + a3 * step;
                Vector3d a4 = Acceleration(r + v3 * step, direction, mu,
                    engine.Thrust / (mass - flow * step));
                r += (v + 2.0 * v2 + 2.0 * v3 + v4) * (step / 6.0);
                v += (a1 + 2.0 * a2 + 2.0 * a3 + a4) * (step / 6.0);
            }
            double endUt = startUt + node.Duration;
            after.GetOrbitalStateVectorsAtUT(endUt, out Vector3d idealR, out Vector3d idealV);
            double initialEnergy = velocity.sqrMagnitude * 0.5 - mu / center.magnitude;
            double idealEnergy = idealV.sqrMagnitude * 0.5 - mu / idealR.magnitude;
            double actualEnergy = v.sqrMagnitude * 0.5 - mu / r.magnitude;
            executedAfter = SplitPlanner.OrbitFromState(r, v, before.referenceBody, endUt);
            double gain = idealEnergy - initialEnergy;
            double energyLoss = gain > 1.0 ? Math.Max(0.0, (idealEnergy - actualEnergy) / gain) : 0.0;
            return new FiniteBurnEstimate(cosineLoss, energyLoss, (r - idealR).magnitude, (v - idealV).magnitude);
        }

        private static Vector3d Acceleration(Vector3d r, Vector3d direction, double mu, double thrustAcceleration)
            => r * (-mu / (r.magnitude * r.sqrMagnitude)) + direction * thrustAcceleration;

        // Compensate finite-duration energy loss through the burn timer while
        // leaving the stock impulse node as the intended orbit. In particular,
        // a tiny uncorrected energy loss near escape causes a large period error.
        internal static bool TargetEnergy(Orbit before, Orbit target, NodeSpec nominal,
            BurnPhysics engine, double spent, Orbit executedBefore,
            out NodeSpec timed, out Orbit executedAfter, out FiniteBurnEstimate estimate, double centerShift = 0.0,
            double maximumCenterShift = double.PositiveInfinity,
            double maximumBurnDeltaV = double.PositiveInfinity)
        {
            double magnitude = nominal.DeltaV.magnitude;
            double budget = magnitude;
            double mu = before.referenceBody.gravParameter;
            double targetEnergy = -mu / (2.0 * target.semiMajorAxis);
            timed = nominal;
            executedAfter = executedBefore;
            estimate = default;
            for (int iteration = 0; iteration < 12; iteration++)
            {
                if (budget > maximumBurnDeltaV + 1e-6) return false;
                timed = new NodeSpec(nominal.Ut, nominal.DeltaV, engine.Duration(budget, spent),
                    engine.StartOffset(budget, spent) + centerShift, nominal.Purpose, budget);
                if (!CandidateRules.IsFinite(timed.StartOffset) || Math.Abs(centerShift) > maximumCenterShift) return false;
                estimate = Measure(before, target, timed, engine, spent, executedBefore, out executedAfter);
                if (!estimate.IsFinite) return false;
                double actualEnergy = -mu / (2.0 * executedAfter.semiMajorAxis);
                double error = targetEnergy - actualEnergy;
                if (Math.Abs(error) < 0.001) return true;
                executedAfter.GetOrbitalStateVectorsAtUT(timed.Ut + timed.Duration - timed.StartOffset,
                    out _, out Vector3d velocity);
                Vector3d direction = (SplitPlanner.NodeRotation(before, nominal.Ut) * nominal.DeltaV).xzy.normalized;
                double derivative = Vector3d.Dot(velocity, direction);
                if (derivative <= 0.0) return false;
                budget += error / derivative;
                if (!CandidateRules.IsFinite(budget) || budget < magnitude * 0.5 || budget > magnitude * 1.5) return false;
            }
            return false;
        }

        internal static bool TargetPlane(Orbit before, Orbit target, NodeSpec nominal,
            BurnPhysics engine, double spent, Orbit executedBefore,
            out NodeSpec timed, out Orbit executedAfter, out FiniteBurnEstimate estimate,
            double maximumBurnDeltaV = double.PositiveInfinity)
        {
            // Earlier finite kicks can rotate the line of apsides slightly even
            // after their energy/period is corrected. Burn at the actual opposite
            // plane intersection, not blindly at the nominal apoapsis time.
            before.GetOrbitalStateVectorsAtUT(nominal.Ut, out Vector3d desiredPosition, out _);
            executedBefore.GetOrbitalStateVectorsAtUT(nominal.Ut, out Vector3d r, out Vector3d v);
            Vector3d normal = Vector3d.Cross(r, v).normalized;
            double angle = Math.Atan2(Vector3d.Dot(Vector3d.Cross(r, desiredPosition), normal),
                Vector3d.Dot(r, desiredPosition));
            double anomaly = (executedBefore.TrueAnomalyAtUT(nominal.Ut) + angle + 2 * Math.PI) % (2 * Math.PI);
            double dt = executedBefore.GetDTforTrueAnomalyAtUT(anomaly, nominal.Ut);
            return TargetEnergy(before, target, nominal, engine, spent, executedBefore,
                out timed, out executedAfter, out estimate, -dt, executedBefore.period * 0.25, maximumBurnDeltaV);
        }

        // Energy determines excess speed; moving the burn window adjusts the
        // outgoing in-plane direction. This corrects the dominant finite-burn
        // ejection error without asking the pilot to steer away from the node.
        internal static bool TargetDeparture(Orbit before, Orbit target, NodeSpec nominal,
            BurnPhysics engine, double spent, Orbit executedBefore, out NodeSpec timed,
            out FiniteBurnEstimate estimate, out double excessError, double maximumBurnDeltaV)
        {
            excessError = double.NaN;
            if (!TargetEnergy(before, target, nominal, engine, spent, executedBefore,
                out timed, out _, out estimate, maximumBurnDeltaV: maximumBurnDeltaV)) return false;
            if (target.eccentricity <= 1.0) return timed.BurnDeltaV <= maximumBurnDeltaV;
            Vector3d desired = OutgoingExcess(target, nominal.Ut);
            double bestError = double.PositiveInfinity;
            double bestShift = 0.0;
            NodeSpec bestNode = timed;
            FiniteBurnEstimate bestEstimate = estimate;
            double spacing = timed.Duration / 4.0;
            // Bracket a feasible minimum before refinement. An unconstrained
            // Newton step can jump into an unaffordable very early burn.
            for (int sample = -3; sample <= 3; sample++)
            {
                double shift = sample * spacing;
                double error = DepartureTrial(before, target, nominal, engine, spent, executedBefore,
                    desired, maximumBurnDeltaV, shift, out NodeSpec trial, out FiniteBurnEstimate trialEstimate);
                if (error < bestError)
                { bestError = error; bestShift = shift; bestNode = trial; bestEstimate = trialEstimate; }
            }
            double low = bestShift - spacing, high = bestShift + spacing;
            for (int i = 0; i < 12; i++)
            {
                double left = low + (high - low) / 3.0;
                double right = high - (high - low) / 3.0;
                double leftError = DepartureTrial(before, target, nominal, engine, spent, executedBefore,
                    desired, maximumBurnDeltaV, left, out NodeSpec leftNode, out FiniteBurnEstimate leftEstimate);
                double rightError = DepartureTrial(before, target, nominal, engine, spent, executedBefore,
                    desired, maximumBurnDeltaV, right, out NodeSpec rightNode, out FiniteBurnEstimate rightEstimate);
                if (leftError < bestError) { bestError = leftError; bestNode = leftNode; bestEstimate = leftEstimate; }
                if (rightError < bestError) { bestError = rightError; bestNode = rightNode; bestEstimate = rightEstimate; }
                if (leftError < rightError) high = right; else low = left;
            }
            timed = bestNode;
            estimate = bestEstimate;
            excessError = bestError;
            return CandidateRules.IsFinite(bestError);
        }

        private static double DepartureTrial(Orbit before, Orbit target, NodeSpec nominal, BurnPhysics engine,
            double spent, Orbit executedBefore, Vector3d desired, double maximumBurnDeltaV, double shift,
            out NodeSpec node, out FiniteBurnEstimate estimate)
        {
            if (!TargetEnergy(before, target, nominal, engine, spent, executedBefore,
                out node, out Orbit executed, out estimate, shift, maximumBurnDeltaV: maximumBurnDeltaV) || node.BurnDeltaV > maximumBurnDeltaV)
                return double.PositiveInfinity;
            return (OutgoingExcess(executed, node.Ut + node.Duration - node.StartOffset) - desired).magnitude;
        }

        internal static Vector3d OutgoingExcess(Orbit orbit, double ut)
        {
            if (orbit.eccentricity <= 1.0 || orbit.semiMajorAxis >= 0.0)
                return new Vector3d(double.NaN, double.NaN, double.NaN);
            orbit.GetOrbitalStateVectorsAtUT(ut, out Vector3d r, out Vector3d v);
            Vector3d h = Vector3d.Cross(r, v);
            Vector3d e = Vector3d.Cross(v, h) / orbit.referenceBody.gravParameter - r.normalized;
            Vector3d p = e.normalized;
            Vector3d q = Vector3d.Cross(h.normalized, p);
            double cosine = -1.0 / e.magnitude;
            return (p * cosine + q * Math.Sqrt(1.0 - cosine * cosine)) *
                Math.Sqrt(-orbit.referenceBody.gravParameter / orbit.semiMajorAxis);
        }
    }
}
