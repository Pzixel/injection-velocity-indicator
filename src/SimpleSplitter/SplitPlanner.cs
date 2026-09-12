using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SimpleSplitter
{
    internal static class SplitPlanner
    {
        private const double MinimumLeadTime = 30.0;
        private const double ComponentTolerance = 0.01;
        private const int MaximumLeadOrbits = 4096;
        // Geometric safety and score precision are independent of angular loss.
        private const double SoiMargin = 0.005;
        private const double RankingBand = 0.005;

        internal static IEnumerator PlanAsync(SplitRequest request, Action<PlanResult> completed,
            int onlyCount = 0, bool redistribute = false, double? refinementBias = null)
        {
            string? error = ValidateRequest(request);
            if (error != null)
            {
                completed(PlanResult.Failure(error));
                yield break;
            }
            Orbit originalOrbit = request.SourceOrbit;
            CelestialBody body = originalOrbit.referenceBody;
            originalOrbit.GetFixedState(request.OriginalUt,
                out Vector3d position, out Vector3d velocity);
            Vector3d targetVelocity = velocity +
                (NodeRotation(originalOrbit, request.OriginalUt) * request.OriginalDeltaV).xzy;
            double magnitude = request.OriginalDeltaV.magnitude;
            double tangentFraction = Math.Min(1.0,
                Vector3d.Cross(position.normalized, velocity.normalized).magnitude);
            double reserve = request.Propulsion.Cursor().Engine.Duration(Math.Min(magnitude, request.Propulsion.Cursor().Remaining)) + MinimumLeadTime;
            int available = (int)Math.Min(MaximumLeadOrbits, Math.Floor(
                (request.OriginalUt - request.Now - reserve) / originalOrbit.period));
            double minimumRadius = body.Radius + (body.atmosphere ? body.atmosphereDepth : 0.0);
            PlanSearchCache cache = request.Cache;
            if (!redistribute) cache.Prepare(request);
            for (int totalCount = 2; totalCount <= request.MaximumBurns; totalCount++)
            {
                if (onlyCount != 0 && totalCount != onlyCount) continue;
                if (redistribute ? cache.IsRefined(totalCount) : cache.Contains(totalCount)) continue;
                var candidates = redistribute ? cache.ForCount(totalCount) : new List<SplitCandidate>();
                var testedSchedules = new HashSet<string>();
                // Explore setup energy as well as stage layouts. No angular-loss
                // ceiling is used: long burns compete on their simulated error.
                // A collision is a constraint on a whole trajectory, not proof
                // that the burn count is impossible. Rebalance each segment and
                // resolve the resonance afresh, including the setup energy.
                foreach (double bias in refinementBias.HasValue ? new[] { refinementBias.Value } :
                    redistribute ? new[] { 0.0, 0.2, -0.2, 0.45, -0.45, 0.75, -0.75 } : new[] { 0.0 })
                foreach (double fraction in redistribute ? new[] { 1.0, 0.97, 0.94, 0.9, 0.85, 0.8, 0.7 } : new[] { 1.0, 0.8, 0.6, 0.4, 0.2 })
                {
                    for (int plane = 0; plane <= 1; plane++)
                    {
                        int count = totalCount - 1 - plane;
                        if (count < 1 || count >= available ||
                            (plane == 1 && Math.Abs(request.OriginalDeltaV.y) < ComponentTolerance)) continue;
                        double setupCeiling = Math.Min(request.OriginalDeltaV.z, magnitude);
                        if (redistribute) setupCeiling = Math.Min(setupCeiling,
                            Math.Sqrt(2 * body.gravParameter / position.magnitude) - velocity.magnitude);
                        setupCeiling *= fraction;
                        request.Progress = (redistribute ? "Redistributing " : "Searching ") + totalCount + " burns (max " + request.MaximumBurns + ")...";
                        List<KickSchedule> schedules = KickSchedule.FindAll(count, body.gravParameter,
                            position.magnitude, velocity.magnitude, tangentFraction, minimumRadius,
                            body.sphereOfInfluence * (1.0 - SoiMargin), originalOrbit.period,
                            available, setupCeiling, request.Propulsion, double.PositiveInfinity, distributionBias: bias);
                        yield return null;
                        // Bound full simulations per sampled energy while keeping
                        // alternatives in case the stock solver rejects the winner.
                        for (int option = 0; option < Math.Min(3, schedules.Count); option++)
                        {
                            KickSchedule schedule = schedules[option];
                            // Different search parameters can produce the same
                            // burns (especially a one-kick stage, where bias has
                            // no effect). Deduplicate before expensive integration.
                            string key = plane + ":" + string.Join(",", schedule.StageKickCounts) + ":" +
                                string.Join(",", schedule.DeltaVs.ConvertAll(dv => Math.Round(dv, 5)));
                            if (!testedSchedules.Add(key)) continue;
                            EvaluationBudget budget = new EvaluationBudget();
                            StagedPropulsion nominalPropulsion = request.Propulsion;
                            KickSchedule? adjustedSchedule = schedule;
                            int adjustments = 0;
                            bool done = false;
                            while (!done)
                            {
                                budget.BeginSlice();
                                try
                                {
                                    Collect(candidates, BuildCandidate(request, position, velocity, targetVelocity, adjustedSchedule!, plane == 1));
                                    done = true;
                                }
                                catch (YieldPlanningException) { }
                                catch (StageCapacityAdjustment adjustment)
                                {
                                    if (++adjustments > 12) done = true;
                                    else
                                    {
                                        var stages = new List<BurnStage>(nominalPropulsion.Stages);
                                        BurnStage stage = stages[adjustment.Index];
                                        stages[adjustment.Index] = new BurnStage(stage.Stage, stage.Engine,
                                            stage.DeltaV + adjustment.Adjustment);
                                        nominalPropulsion = new StagedPropulsion(stages);
                                        adjustedSchedule = KickSchedule.Find(count, body.gravParameter,
                                            position.magnitude, velocity.magnitude, tangentFraction, minimumRadius,
                                            body.sphereOfInfluence * (1 - SoiMargin), originalOrbit.period,
                                            available, setupCeiling, nominalPropulsion, double.PositiveInfinity,
                                            adjustedSchedule!.StageKickCounts, bias);
                                        if (adjustedSchedule == null) done = true;
                                        budget = new EvaluationBudget();
                                    }
                                }
                                finally { EvaluationBudget.Current = null; }
                                yield return null;
                            }
                        }
                    }
                }
                candidates.Sort(CompareChoices);
                if (!redistribute && candidates.Count > 3) candidates.RemoveRange(3, candidates.Count - 3);
                cache.Store(totalCount, candidates);
                if (redistribute && !refinementBias.HasValue) cache.MarkRefined(totalCount);
                yield return null;
            }
            var results = cache.GetCandidates(request.MaximumBurns);
            completed(results.Count == 0
                ? PlanResult.Failure("No executable option found in the sampled distributions through " + request.MaximumBurns + " burns.")
                : PlanResult.Success(results));
        }

        internal static int CompareChoices(SplitCandidate a, SplitCandidate b)
        {
            double Error(SplitCandidate c) => CandidateRules.IsFinite(c.ExcessVelocityError)
                ? c.ExcessVelocityError : c.ExecutionEstimate.VelocityError;
            int comparison = Error(a).CompareTo(Error(b));
            if (comparison != 0) return comparison;
            comparison = a.Score.TotalDeltaV.CompareTo(b.Score.TotalDeltaV);
            return comparison != 0 ? comparison : a.Score.CompareTo(b.Score);
        }

        // Reuse the searched stage layout, distribution and setup ceiling, but
        // solve its resonance and burn timers against the current parking orbit.
        // This is one selected recipe, not another search through all counts.
        internal static IEnumerator RefreshAsync(SplitRequest request, SplitCandidate candidate,
            Action<SplitCandidate?> completed)
        {
            KickSchedule? recipe = candidate.Schedule;
            if (recipe == null) { completed(candidate); yield break; }
            Orbit source = request.SourceOrbit;
            CelestialBody body = source.referenceBody;
            source.GetFixedState(request.OriginalUt, out Vector3d position, out Vector3d velocity);
            Vector3d targetVelocity = velocity + (NodeRotation(source, request.OriginalUt) * request.OriginalDeltaV).xzy;
            double tangent = Math.Min(1, Vector3d.Cross(position.normalized, velocity.normalized).magnitude);
            double reserve = request.Propulsion.Cursor().Engine.Duration(Math.Min(request.OriginalDeltaV.magnitude,
                request.Propulsion.Cursor().Remaining)) + MinimumLeadTime;
            int available = (int)Math.Min(MaximumLeadOrbits, Math.Floor((request.OriginalUt - request.Now - reserve) / source.period));
            StagedPropulsion nominalPropulsion = request.Propulsion;
            bool plane = candidate.Nodes.Count == recipe.DeltaVs.Count + 2;
            for (int adjustment = 0; adjustment <= 12; adjustment++)
            {
                KickSchedule? schedule = KickSchedule.Find(recipe.DeltaVs.Count, body.gravParameter,
                    position.magnitude, velocity.magnitude, tangent,
                    body.Radius + (body.atmosphere ? body.atmosphereDepth : 0),
                    body.sphereOfInfluence * (1 - SoiMargin), source.period, available,
                    recipe.SearchCeiling, nominalPropulsion, double.PositiveInfinity,
                    recipe.StageKickCounts, recipe.DistributionBias);
                if (schedule == null) { completed(null); yield break; }
                var budget = new EvaluationBudget();
                bool adjustStage = false;
                while (!adjustStage)
                {
                    bool done = false;
                    SplitCandidate? refreshed = null;
                    budget.BeginSlice();
                    try
                    {
                        refreshed = BuildCandidate(request, position, velocity, targetVelocity, schedule, plane);
                        done = true;
                    }
                    catch (YieldPlanningException) { }
                    catch (StageCapacityAdjustment change)
                    {
                        var stages = new List<BurnStage>(nominalPropulsion.Stages);
                        BurnStage stage = stages[change.Index];
                        stages[change.Index] = new BurnStage(stage.Stage, stage.Engine, stage.DeltaV + change.Adjustment);
                        nominalPropulsion = new StagedPropulsion(stages);
                        adjustStage = true;
                    }
                    finally { EvaluationBudget.Current = null; }
                    if (done) { completed(refreshed); yield break; }
                    yield return null;
                }
            }
            completed(null);
        }

        private static string? ValidateRequest(SplitRequest request)
        {
            Orbit orbit = request.SourceOrbit;
            if (orbit == null || orbit.referenceBody == null || !IsBound(orbit))
                return "The selected node must have a usable bound source orbit.";
            if (request.MaximumBurns < 2 || request.MaximumBurns > 10)
                return "Maximum burns must be between 2 and 10.";
            if (!request.Propulsion.IsUsable)
                return "No usable staged propulsion estimate is available.";
            if (!CandidateRules.ArrivalIsWithinTolerance(request.OriginalUt, request.ArrivalUt, request.ArrivalUt))
                return "The target encounter does not have a usable arrival time.";
            if (request.OriginalUt <= request.Now + MinimumLeadTime)
                return "The maneuver is too close to the current time to split safely.";
            if (!CandidateRules.IsFinite(request.OriginalDeltaV.magnitude) || request.OriginalDeltaV.z <= ComponentTolerance)
                return "The maneuver must contain a positive prograde ejection burn.";
            if (orbit.eccentricity >= 1e-3)
            {
                double anomaly = orbit.TrueAnomalyAtUT(request.OriginalUt);
                double wrapped = Math.Abs(Math.Atan2(Math.Sin(anomaly), Math.Cos(anomaly)));
                if (wrapped > Math.Max(1e-3, 2.0 * Math.PI / orbit.period))
                    return "Place the selected maneuver at periapsis before splitting it.";
            }
            if ((request.OriginalUt - request.Now - MinimumLeadTime) / orbit.period < 2.0)
                return "At least two complete parking-orbit periods are required before the node.";
            return null;
        }

        private static SplitCandidate? BuildCandidate(SplitRequest request, Vector3d position,
            Vector3d velocity, Vector3d targetVelocity, KickSchedule schedule, bool splitPlane)
        {
            CelestialBody body = request.SourceOrbit.referenceBody;
            double firstUt = request.OriginalUt - schedule.LeadOrbits * request.SourceOrbit.period;
            double ut = firstUt;
            double spent = 0.0;
            double fuelSpent = 0.0;
            StageCursor propulsion = request.Propulsion.Cursor();
            double largest = 0.0;
            double maxSetupLoss = 0.0;
            List<NodeSpec> nodes = new List<NodeSpec>();
            Orbit orbit = request.SourceOrbit;
            Orbit executed = orbit;
            for (int i = 0; i < schedule.DeltaVs.Count; i++)
            {
                double kick = schedule.DeltaVs[i];
                if (!propulsion.IsUsable) return null;
                BurnPhysics engine = propulsion.Engine;
                NodeSpec node = new NodeSpec(ut, new Vector3d(0.0, 0.0, kick),
                    engine.Duration(kick), engine.StartOffset(kick), "Periapsis kick (stage " + propulsion.Stage + ")");
                Orbit beforeKick = orbit;
                orbit = OrbitFromState(position, velocity.normalized * (velocity.magnitude + spent + kick), body, ut);
                if (!IsSafeIntermediateOrbit(orbit)) return null;
                if (!FiniteBurnEstimate.TargetEnergy(beforeKick, orbit, node, engine, 0, executed,
                    out node, out executed, out FiniteBurnEstimate kickEstimate,
                    maximumBurnDeltaV: schedule.StageEnds[i] ? double.PositiveInfinity : propulsion.Remaining) ||
                    !IsSafeIntermediateOrbit(executed)) return null;
                if (schedule.StageEnds[i])
                {
                    double adjustment = propulsion.Remaining - node.BurnDeltaV;
                    if (Math.Abs(adjustment) > 1e-7)
                        throw new StageCapacityAdjustment(propulsion.SegmentIndex, adjustment);
                }
                if (node.Ut - node.StartOffset <= request.Now + MinimumLeadTime) return null;
                if (!propulsion.Consume(node.BurnDeltaV)) return null;
                nodes.Add(node);
                largest = Math.Max(largest, node.BurnDeltaV);
                fuelSpent += node.BurnDeltaV;
                maxSetupLoss = Math.Max(maxSetupLoss, kickEstimate.CosineLoss);
                spent += kick;
                if (i + 1 < schedule.DeltaVs.Count) ut += schedule.Periods[i];
            }
            double planeMagnitude = 0.0;
            Vector3d preFinalVelocity = velocity.normalized * (velocity.magnitude + spent);
            double unsplitFinal = (targetVelocity - preFinalVelocity).magnitude;
            if (splitPlane)
            {
                // The intersection of the old and target planes is +/- the
                // departure radius. With small radial velocity, this is near Ap.
                double opposite = (orbit.TrueAnomalyAtUT(ut) + Math.PI) % (2.0 * Math.PI);
                double dt = orbit.GetDTforTrueAnomalyAtUT(opposite, ut);
                if (dt <= 0.0) dt += orbit.period;
                double planeUt = ut + dt;
                orbit.GetFixedState(planeUt, out Vector3d planePosition, out Vector3d planeVelocity);
                if (Vector3d.Dot(planePosition.normalized, position.normalized) > -1.0 + 1e-10) return null;
                Vector3d radial = planePosition.normalized;
                Vector3d targetNormal = Vector3d.Cross(position, targetVelocity).normalized;
                Vector3d tangent = Vector3d.Cross(targetNormal, radial).normalized;
                double radialSpeed = Vector3d.Dot(planeVelocity, radial);
                double tangentSpeed = (planeVelocity - radial * radialSpeed).magnitude;
                Vector3d rotatedVelocity = radial * radialSpeed + tangent * tangentSpeed;
                Vector3d planeDeltaV = ToNodeCoordinates(orbit, planeUt, rotatedVelocity - planeVelocity);
                planeMagnitude = planeDeltaV.magnitude;
                if (planeMagnitude < ComponentTolerance) return null;
                if (!propulsion.IsUsable) return null;
                BurnPhysics planeEngine = propulsion.Engine;
                NodeSpec planeNode = new NodeSpec(planeUt, planeDeltaV,
                    planeEngine.Duration(planeMagnitude),
                    planeEngine.StartOffset(planeMagnitude), "Plane change (stage " + propulsion.Stage + ")");
                // Preserve speed: pure normal adds energy and moves periapsis.
                Orbit changed = OrbitFromState(planePosition, rotatedVelocity, body, planeUt);
                if (!IsSafeIntermediateOrbit(changed)) return null;
                if (!FiniteBurnEstimate.TargetPlane(orbit, changed, planeNode, planeEngine, 0, executed,
                    out planeNode, out executed, out FiniteBurnEstimate planeEstimate, propulsion.Remaining) ||
                    !IsSafeIntermediateOrbit(executed)) return null;
                maxSetupLoss = Math.Max(maxSetupLoss, planeEstimate.CosineLoss);
                if (!propulsion.Consume(planeNode.BurnDeltaV)) return null;
                nodes.Add(planeNode);
                orbit = changed;
                spent += planeMagnitude;
                fuelSpent += planeNode.BurnDeltaV;
                largest = Math.Max(largest, planeNode.BurnDeltaV);
            }
            orbit.GetFixedState(request.OriginalUt, out Vector3d finalPosition, out Vector3d finalVelocity);
            if ((finalPosition - position).magnitude > Math.Max(1.0, position.magnitude * 1e-7)) return null;
            Vector3d finalDeltaV = ToNodeCoordinates(orbit, request.OriginalUt, targetVelocity - finalVelocity);
            if (splitPlane && Math.Abs(finalDeltaV.y) > ComponentTolerance) return null;
            double finalMagnitude = finalDeltaV.magnitude;
            if (splitPlane && !CandidateRules.NormalSplitSavesDeltaV(unsplitFinal, planeMagnitude + finalMagnitude)) return null;
            if (!propulsion.IsUsable) return null;
            BurnPhysics finalEngine = propulsion.Engine;
            NodeSpec final = new NodeSpec(request.OriginalUt, finalDeltaV,
                finalEngine.Duration(finalMagnitude), finalEngine.StartOffset(finalMagnitude), "Departure (stage " + propulsion.Stage + ")");
            Orbit targetOrbit = OrbitFromState(position, targetVelocity, body, request.OriginalUt);
            if (!FiniteBurnEstimate.TargetDeparture(orbit, targetOrbit, final, finalEngine, 0, executed,
                out final, out FiniteBurnEstimate execution, out double excessError,
                propulsion.Remaining)) return null;
            double total = fuelSpent + final.BurnDeltaV;
            largest = Math.Max(largest, final.BurnDeltaV);
            if (!CandidateRules.DeltaVIsAllowed(request.OriginalDeltaV.magnitude, total, largest)) return null;
            nodes.Add(final);
            for (int i = 1; i < nodes.Count; i++)
            {
                NodeSpec previous = nodes[i - 1];
                if (previous.Ut + previous.Duration - previous.StartOffset + MinimumLeadTime >=
                    nodes[i].Ut - nodes[i].StartOffset) return null;
            }
            FiniteBurnEstimate departure = FiniteBurnEstimate.Measure(orbit, targetOrbit, final, finalEngine, 0);
            if (!departure.IsFinite) return null;
            return new SplitCandidate(nodes,
                new CandidateScore(largest, total, 0.0, firstUt, nodes.Count,
                    request.OriginalDeltaV.magnitude * RankingBand), schedule.LeadOrbits,
                schedule.SetupDeltaV, maxSetupLoss, departure, execution, excessError, request.OriginalDeltaV.magnitude, schedule);
        }

        internal static Orbit OrbitFromState(Vector3d position, Vector3d velocity, CelestialBody body, double ut)
        {
            Orbit orbit = new Orbit();
            orbit.UpdateFromFixedVectors(position, velocity, body, ut);
            return orbit;
        }

        internal static Vector3d ToNodeCoordinates(Orbit orbit, double ut, Vector3d deltaV)
            => QuaternionD.Inverse(NodeRotation(orbit, ut)) * deltaV.xzy;

        internal static QuaternionD NodeRotation(Orbit orbit, double ut)
        {
            orbit.GetFixedState(ut, out Vector3d position, out Vector3d velocity);
            return QuaternionD.LookRotation(velocity.xzy, Vector3d.Cross(-position.xzy, velocity.xzy));
        }

        private static bool IsSafeIntermediateOrbit(Orbit orbit)
        {
            if (!IsBound(orbit) || orbit.referenceBody == null) return false;
            double minimumRadius = orbit.referenceBody.Radius +
                (orbit.referenceBody.atmosphere ? orbit.referenceBody.atmosphereDepth : 0.0);
            return CandidateRules.IsFinite(orbit.PeR) && CandidateRules.IsFinite(orbit.ApR) &&
                orbit.PeR > minimumRadius && orbit.ApR < orbit.referenceBody.sphereOfInfluence;
        }

        private static bool IsBound(Orbit orbit) => CandidateRules.IsFinite(orbit.period) && orbit.period > 0.0 &&
            CandidateRules.IsFinite(orbit.eccentricity) && orbit.eccentricity >= 0.0 && orbit.eccentricity < 1.0;

        private static void Collect(List<SplitCandidate> candidates, SplitCandidate? candidate)
        {
            if (candidate == null) return;
            foreach (SplitCandidate prior in candidates)
            {
                if (prior.Nodes.Count != candidate.Nodes.Count) continue;
                bool equal = true;
                for (int i = 0; i < prior.Nodes.Count; i++)
                    if (Math.Abs(prior.Nodes[i].Ut - candidate.Nodes[i].Ut) > 0.01 ||
                        (prior.Nodes[i].DeltaV - candidate.Nodes[i].DeltaV).magnitude > 0.001) { equal = false; break; }
                if (equal) return;
            }
            candidates.Add(candidate);
        }
    }
}
