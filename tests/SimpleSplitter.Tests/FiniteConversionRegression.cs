using System;
using System.Collections;
using System.Collections.Generic;

namespace SimpleSplitter
{
    internal static partial class Program
    {
        private static void FiniteConversionRegression()
        {
            SplitRequest request = FiniteConversionStudy.Request();
            TrajectoryReference reference = request.Reference;
            Nearly(0, reference.Measure(reference.Target).SeparationMeters, 1e-9, "ideal full state has zero residual");
            True(reference.Epoch > request.OriginalUt && reference.Position.magnitude < request.SourceOrbit.referenceBody.sphereOfInfluence,
                "common reference lies on the original departure inside its SOI");
            reference.Target.GetFixedState(reference.Epoch - 60, out Vector3d delayedR, out Vector3d delayedV);
            Orbit delayed = SplitPlanner.OrbitFromState(delayedR, delayedV, reference.Target.referenceBody, reference.Epoch);
            Nearly(0, (FiniteBurnEstimate.OutgoingExcess(delayed, reference.Epoch) -
                FiniteBurnEstimate.OutgoingExcess(reference.Target, reference.Epoch)).magnitude, 1e-7,
                "v-infinity is blind to a sixty-second phase error on the same hyperbola");
            True(reference.Measure(delayed).SeparationMeters > 100000 && FiniteConversionStudy.CoastRms(reference, delayed) > 100000,
                "full state and trajectory RMS detect the phase error");
            Orbit crossing = SplitPlanner.OrbitFromState(reference.Position, reference.Velocity + new Vector3d(0, 1, 0),
                reference.Target.referenceBody, reference.Epoch);
            Nearly(0, reference.Measure(crossing).PositionMeters, .001, "single-position match can hide a wrong velocity");
            True(reference.Measure(crossing).SeparationMeters > 10000, "full state detects the wrong velocity at a crossing");

            PlanResult result = RunPlan(request)!;
            var options = request.Cache.ForCount(6);
            True(options.Count > 0, "logged S11/S10 fixture produces six-burn commands");
            if (options.Count == 0) return;
            SplitCandidate winner = options[0];
            True(winner.MatchesReference, "logged fixture reaches the full position/velocity boundary");
            True(winner.Schedule!.StageKickCounts[0] == 1 && winner.DeparturePieces >= 2,
                "best six-burn solution keeps the booster whole and splits the actual departure");
            Nearly(request.Propulsion.Stages[0].DeltaV, winner.Nodes[0].BurnDeltaV, 1e-6, "first stage exhausts in one burn without a TWR rule");
            True(winner.Score.TotalDeltaV < 2230, "state matching also reduces expenditure below the logged 2254.5 m/s plan");
            foreach (SplitCandidate candidate in result.Candidates)
            {
                Nearly(reference.Epoch, candidate.BoundaryEpoch, 0, "every candidate uses the same comparison epoch");
                True(candidate.Nodes.Count <= request.MaximumBurns, "conversion honors the player's hard node cap");
                ValidateTimedPlan(request.SourceOrbit, request.Propulsion, candidate, request.OriginalDeltaV, request.OriginalUt);
            }

            // Independent vector RK4, five times finer than the production
            // integrator. Reconstruct the direction from the next stock node
            // on the actual coast instead of using its stored inertial vector.
            Orbit fine = request.SourceOrbit;
            StageCursor cursor = request.Propulsion.Cursor();
            foreach (NodeSpec node in winner.Nodes)
            {
                fine = IntegrateCommand(fine, node, cursor.Engine, .1);
                True(cursor.Consume(node.BurnDeltaV), "independent command execution stays within stage fuel");
            }
            TrajectoryError independent = reference.Measure(fine);
            True(independent.PositionMeters < 2 && independent.VelocityMetersPerSecond < .001,
                "independent fine-step sequential execution reaches the intended real trajectory");
            Console.WriteLine("Logged fixture independent replay: position=" + independent.PositionMeters.ToString("F6") +
                " m, velocity=" + independent.VelocityMetersPerSecond.ToString("F9") + " m/s, total=" + winner.Score.TotalDeltaV.ToString("F3") + " m/s.");

            SplitCandidate? refreshed = null;
            FiniteConversionStudy.Drain(SplitPlanner.RefreshAsync(request, winner, value => refreshed = value));
            True(refreshed != null && refreshed.MatchesReference, "refresh repeats conversion and retains the state match");
            Nearly(winner.Score.TotalDeltaV, refreshed!.Score.TotalDeltaV, .01, "same-input refresh retains expenditure");

            var renamedStages = new List<BurnStage>();
            foreach (BurnStage stage in request.Propulsion.Stages)
                renamedStages.Add(new BurnStage(100 - stage.Stage, stage.Engine, stage.DeltaV));
            var renamed = new SplitRequest(request.Node, request.TargetBody, request.ArrivalUt, request.Now,
                new StagedPropulsion(renamedStages), 6);
            RunPlan(renamed);
            Nearly(winner.Score.TotalDeltaV, renamed.Cache.ForCount(6)[0].Score.TotalDeltaV, 1e-7,
                "stage labels do not affect allocation or scoring");
            False(FiniteConversion.Convert(request, winner.ConversionSeed!, 20, reference) != null,
                "conversion cannot exceed the hard burn count");

            var expanded = new SplitRequest(request.Node, request.TargetBody, request.ArrivalUt, request.Now,
                request.Propulsion, 10, request.Cache);
            PlanResult larger = RunPlan(expanded)!;
            True(request.Cache.ReusedCounts == 5 && larger.Candidates.Contains(winner), "expansion reuses prior counts and conversion seeds");
            True(larger.Candidates.Exists(c => c.Nodes.Count == 10 && c.MatchesReference), "maximum ten-burn search retains an executable match");
            True(larger.Candidates.TrueForAll(c => c.Nodes.Count <= 10), "expanded search never exceeds maximum");

            // Selection must use refreshed actual metrics, even if their order
            // differs from the cached numerical shortlist.
            var ranking = new SplitRequest(request.Node, request.TargetBody, request.ArrivalUt, request.Now, request.Propulsion, 6);
            ranking.Cache.Prepare(ranking);
            SplitCandidate first = Ranked(.1, 2500), second = Ranked(.2, 2600);
            ranking.Cache.Store(6, new List<SplitCandidate> { first, second });
            IEnumerator Validate(SplitCandidate c, Action<bool> completed)
            { c.LiveCandidate = c == first ? Ranked(100, 2500) : Ranked(.3, 2400); completed(true); yield break; }
            SplitCandidate? accepted = null;
            FiniteConversionStudy.Drain(SafePlanSearch.FindAsync(ranking, Validate, () => true,
                (count, c) => { if (count == 6) accepted = c; }, refinementSeconds: 0));
            True(ReferenceEquals(accepted, second.LiveCandidate), "safe search ranks all refreshed candidates before accepting a winner");

            ranking.Cache.Store(6, new List<SplitCandidate> { first });
            ranking.Cache.StockSafety[first] = true;
            bool unchanged = true;
            accepted = null;
            IEnumerator search = SafePlanSearch.FindAsync(ranking, Validate, () => unchanged,
                (count, c) => accepted = c, refinementSeconds: 0);
            for (int count = 2; count <= 6; count++)
            {
                True(search.MoveNext(), "cancellation regression visits each initial count");
                var check = (IEnumerator)search.Current;
                if (count == 6)
                {
                    True(check.MoveNext(), "safe finalist yields before acceptance");
                    unchanged = false;
                }
                FiniteConversionStudy.Drain(check);
            }
            FiniteConversionStudy.Drain(search);
            True(accepted == null, "an edit during the final yield prevents accepting a stale plan");

            SplitCandidate Ranked(double metres, double dv) => new SplitCandidate(winner.Nodes,
                new CandidateScore(1000, dv, 0, winner.Nodes[0].Ut), 1, 0, 0, default, default, 0, 2000,
                trajectoryError: new TrajectoryError(new Vector3d(metres, 0, 0), default, reference.Horizon));
        }

        private static Orbit IntegrateCommand(Orbit before, NodeSpec node, BurnPhysics engine, double maximumStep)
        {
            double ignition = node.Ut - node.StartOffset;
            before.GetFixedState(ignition, out Vector3d r, out Vector3d v);
            Vector3d direction = (SplitPlanner.NodeRotation(before, node.Ut) * node.DeltaV).xzy.normalized;
            int steps = Math.Max(128, (int)Math.Ceiling(node.Duration / maximumStep));
            double h = node.Duration / steps, flow = engine.Thrust / engine.ExhaustVelocity;
            Vector3d Acceleration(Vector3d position, double mass) =>
                position * (-before.referenceBody.gravParameter / (position.magnitude * position.sqrMagnitude)) + direction * (engine.Thrust / mass);
            for (int i = 0; i < steps; i++)
            {
                double mass = engine.Mass - flow * i * h;
                Vector3d a1 = Acceleration(r, mass);
                Vector3d v2 = v + a1 * (h / 2), a2 = Acceleration(r + v * (h / 2), mass - flow * h / 2);
                Vector3d v3 = v + a2 * (h / 2), a3 = Acceleration(r + v2 * (h / 2), mass - flow * h / 2);
                Vector3d v4 = v + a3 * h, a4 = Acceleration(r + v3 * h, mass - flow * h);
                r += (v + 2 * v2 + 2 * v3 + v4) * (h / 6);
                v += (a1 + 2 * a2 + 2 * a3 + a4) * (h / 6);
            }
            return SplitPlanner.OrbitFromState(r, v, before.referenceBody, ignition + node.Duration);
        }
    }
}
