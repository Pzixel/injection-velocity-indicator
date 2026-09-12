using System;

namespace SimpleSplitter
{
    internal static partial class Program
    {
        private static int failures;

        private static int Main()
        {
            PlanningWorkTests();
            IntegrationRegression();
            TestburnRegression();
            PanelPointerTests();
            ArrivalWindowTests();
            DeltaVPolicyTests();
            CandidateRankingTests();
            ResonanceTests();
            NumericFailureTests();
            BurnPhysicsTests();
            StagedPropulsionTests();
            EngineerPropulsionTests();
            StageAllocationTests();
            PlannerRegressionTests();

            if (failures == 0)
            {
                Console.WriteLine("All SimpleSplitter tests passed.");
                return 0;
            }

            Console.Error.WriteLine(failures + " test(s) failed.");
            return 1;
        }

        private static void PanelPointerTests()
        {
            var pointer = new PanelPointerCapture();
            True(pointer.Update(true, false, false, false), "panel hover owns focus");
            False(pointer.Update(false, false, false, false), "leaving panel releases hover");
            True(pointer.Update(true, true, true, false), "slider press owns focus");
            True(pointer.Update(false, false, true, false), "slider drag outside retains focus");
            True(pointer.Update(false, false, false, true), "outside release stays protected through LateUpdate");
            False(pointer.Update(false, false, false, false), "release protection ends next frame");
            False(pointer.Update(false, true, true, false), "outside click is not captured");
            False(pointer.Update(false, false, false, true), "outside release can deselect");

            pointer.Update(true, true, true, false);
            pointer.Reset();
            False(pointer.Update(false, false, true, false), "hide or selection change cancels drag");
            False(pointer.Update(false, false, false, true), "old drag cannot protect new selection");
            pointer.Update(true, true, true, false);
            False(pointer.Update(false, false, false, false), "lost mouse release cannot leave capture stuck");
            False(pointer.Update(false, false, false, true), "stale capture is cleared");
        }

        private static void ArrivalWindowTests()
        {
            double window = CandidateRules.ArrivalToleranceSeconds;
            True(
                CandidateRules.ArrivalIsWithinTolerance(100.0, 110000.0, 110000.0 + window),
                "arrival at positive tolerance boundary");
            True(
                CandidateRules.ArrivalIsWithinTolerance(100.0, 110000.0, 110000.0 - window),
                "arrival at negative tolerance boundary");
            False(
                CandidateRules.ArrivalIsWithinTolerance(100.0, 110000.0, 110000.01 + window),
                "arrival outside tolerance");
            False(
                CandidateRules.ArrivalIsWithinTolerance(100.0, 100.0, 100.0),
                "zero trip duration");
        }

        private static void DeltaVPolicyTests()
        {
            True(
                CandidateRules.DeltaVIsAllowed(1000.0, 1050.0, 600.0),
                "valid timed expenditure");
            True(
                CandidateRules.DeltaVIsAllowed(1000.0, 1050.01, 600.0),
                "extra delta-v is a comparison diagnostic, not a hidden cap");
            True(
                CandidateRules.DeltaVIsAllowed(1000.0, 1500.0, 1100.0),
                "largest burn is not an encounter feasibility gate");
            False(
                CandidateRules.DeltaVIsAllowed(1000.0, double.NaN, 600.0),
                "non-finite expenditure is rejected");
            True(
                CandidateRules.NormalSplitSavesDeltaV(500.0, 499.98),
                "normal split saves delta-v");
            False(
                CandidateRules.NormalSplitSavesDeltaV(500.0, 499.995),
                "normal split saving below tolerance");
        }

        private static void CandidateRankingTests()
        {
            CandidateScore balanced = new CandidateScore(400.0, 900.0, 10.0, 50.0);
            CandidateScore cheaperButLarger =
                new CandidateScore(401.0, 800.0, 0.0, 100.0);
            True(
                balanced.CompareTo(cheaperButLarger) < 0,
                "largest burn ranks first");

            CandidateScore cheaper = new CandidateScore(400.0, 899.0, 10.0, 50.0);
            True(cheaper.CompareTo(balanced) < 0, "total delta-v ranks second");

            CandidateScore later = new CandidateScore(400.0, 900.0, 10.0, 60.0);
            True(later.CompareTo(balanced) < 0, "later first burn breaks ties");
        }

        private static void ResonanceTests()
        {
            const double mu = 3.5316e12;
            const double radius = 700000.0;
            double circularSpeed = Math.Sqrt(mu / radius);
            double circularPeriod = 2.0 * Math.PI *
                Math.Sqrt(radius * radius * radius / mu);
            double deltaV = ResonanceMath.DeltaVForPeriod(
                mu,
                radius,
                circularSpeed,
                circularPeriod * 2.0);
            True(deltaV > 0.0, "period-doubling burn is prograde");
            Nearly(
                1000.0,
                ResonanceMath.TwoNodeCompletionUt(200.0, 800.0),
                1e-12,
                "two-node completion uses raised period");
            Nearly(
                1000.0,
                ResonanceMath.ThreeNodeCompletionUt(200.0, 600.0, 1000.0),
                1e-12,
                "three-node completion uses both changed periods");
            const double originalNodeUt = 50000.0;
            const double originalPeriod = 2000.0;
            const int leadOrbits = 3;
            double firstBurnUt = originalNodeUt - leadOrbits * originalPeriod;
            Nearly(
                originalNodeUt,
                ResonanceMath.TwoNodeCompletionUt(
                    firstBurnUt,
                    leadOrbits * originalPeriod),
                1e-12,
                "resonant raised orbit returns at original node UT");
            Nearly(
                originalNodeUt,
                ResonanceMath.ThreeNodeCompletionUt(
                    firstBurnUt,
                    5000.0,
                    7000.0),
                1e-12,
                "changed half-periods return at original node UT");
        }

        private static void NumericFailureTests()
        {
            False(CandidateRules.IsFinite(double.NaN), "NaN rejected");
            False(CandidateRules.IsFinite(double.PositiveInfinity), "infinity rejected");
            True(
                double.IsNaN(ResonanceMath.DeltaVForPeriod(1.0, 0.0, 1.0, 1.0)),
                "invalid resonance radius rejected");
        }

        private static void BurnPhysicsTests()
        {
            const double exampleLoss = .01;
            BurnPhysics engine = new BurnPhysics(100, 120, 800 * 9.80665);
            double duration = engine.Duration(800);
            double offset = engine.StartOffset(800);
            True(offset > duration / 2 && offset < duration, "mass loss shifts impulse centroid after temporal midpoint");
            double integratedDv = 0, moment = 0;
            for (int i = 0; i < 10000; i++)
            {
                double t = duration * (i + 0.5) / 10000;
                double dv = engine.Thrust / (engine.Mass - engine.Thrust / engine.ExhaustVelocity * t) * duration / 10000;
                integratedDv += dv;
                moment += t * dv;
            }
            Nearly(800, integratedDv, 1e-6, "rocket equation matches integrated acceleration");
            Nearly(offset, moment / integratedDv, 1e-6, "burn start centres integrated impulse at UT");
            True(engine.Duration(100, 500) < engine.Duration(100), "earlier propellant use shortens later burns");
            False(new BurnPhysics(100, 0, 8000).IsUsable, "no thrust rejected");
            True(double.IsNaN(engine.Duration(double.NaN)), "invalid duration rejected");

            double mu = 3.5316e12, radius = 700000, speed = Math.Sqrt(mu / radius);
            True(OrbitScalars.TryCreate(mu, radius, speed, 1, out OrbitScalars circular), "circular scalar orbit");
            Nearly(radius, circular.Periapsis, 0.02, "circular periapsis");
            False(OrbitScalars.TryCreate(mu, radius, Math.Sqrt(2 * mu / radius) * 1.001, 1, out _), "unbound setup rejected");
            True(engine.KickFits(mu, radius, speed, 1, 0, 50, 0.005), "short kick fits cosine window");
            False(engine.KickFits(mu, radius, speed, 1, 0, 500, 0.005), "long kick fails cosine window");
            False(engine.KickFits(mu, radius, speed, 1, 0, 90, .005), "90 m/s kick fails old loss bound");
            True(engine.KickFits(mu, radius, speed, 1, 0, 90, exampleLoss), "same kick fits a 1% example window");
            CandidateScore fewer = new CandidateScore(1004, 2000, 0, 100, 5, 10);
            CandidateScore more = new CandidateScore(1001, 1990, 0, 100, 6, 10);
            True(fewer.CompareTo(more) < 0, "fewer nodes preferred within precision band");
        }

        private static void PlannerRegressionTests()
        {
            var body = new CelestialBody { gravParameter = 3.5316e12, Radius = 600000,
                atmosphere = true, atmosphereDepth = 70000, sphereOfInfluence = 84159286 };
            var propulsion = new StagedPropulsion(new[] {
                new BurnStage(2, new BurnPhysics(257.6939697265625, 4500.0003679147312, 3089.09500256077), 202.50192260742188),
                new BurnStage(1, new BurnPhysics(190.71542358398438, 299.27631964607076, 8022.0548614895142), 8921.9267578125) });
            Orbit source = SplitPlanner.OrbitFromState(new Vector3d(800150.8902937919, 0, 0),
                new Vector3d(-0.4309441513021902, 2101.242068581272, 0), body, 1000);
            var node = new ManeuverNode { patch = source, UT = 1000 + 1465 * source.period,
                DeltaV = new Vector3d(0.1, 1822.6, 1874) };
            var cache = new PlanSearchCache();
            var request = new SplitRequest(node, body, node.UT + 1e7, 1000, propulsion, 7, cache);
            PlanResult result = RunPlan(request)!;
            True(result.Candidates.Count > 0, "logged ship produces comparison options without a cosine filter");
            for (int n = 2; n <= 7; n++) True(cache.Contains(n), "search completes count " + n + " even if earlier counts fail");
            int previousCount = 0;
            SplitCandidate? previous = null;
            foreach (SplitCandidate candidate in result.Candidates)
            {
                True(candidate.Nodes.Count >= previousCount && candidate.Nodes.Count <= 7, "options grouped by total burn count");
                if (candidate.Nodes.Count == previousCount)
                    True(SplitPlanner.CompareChoices(previous!, candidate) <= 0, "best departure error retained first within count");
                previousCount = candidate.Nodes.Count;
                previous = candidate;
                ValidateTimedPlan(source, propulsion, candidate, node.DeltaV);
                Console.WriteLine(candidate.Nodes.Count + " burns: extra dv=" + candidate.AdditionalDeltaV.ToString("F1") +
                    ", departure error=" + candidate.ExcessVelocityError.ToString("F2") + ", cosine=" + candidate.MaximumCosineLoss.ToString("P1"));
            }
            True(result.Candidates.Exists(c => c.MaximumSetupLoss > 0.01), "plans above the old default cosine cap are retained");
            True(cache.GetCandidates(2).Count == 0 && result.Candidates.Exists(c => c.Nodes.Count >= 4),
                "failed small counts do not prevent larger viable counts");
            True(result.Candidates.Exists(c => c.Nodes.Exists(n => n.Purpose.StartsWith("Plane change"))), "normal component gets plane-change alternatives");
            True(result.Candidates.Exists(c => Math.Abs(c.Nodes[0].BurnDeltaV - propulsion.Stages[0].DeltaV) < 1e-6), "short booster stage can finish in one burn");
            var expanded = new SplitRequest(node, body, node.UT + 1e7, 1000, propulsion, 8, cache);
            PlanResult more = RunPlan(expanded)!;
            True(cache.ReusedCounts == 6, "raising max 7 to 8 reuses completed counts 2 through 7");
            foreach (SplitCandidate candidate in result.Candidates)
                True(more.Candidates.Contains(candidate), "cached finite candidates reused by identity");
            var smaller = new SplitRequest(node, body, node.UT + 1e7, 1000, propulsion, 5, cache);
            PlanResult fewer = RunPlan(smaller)!;
            True(fewer.Candidates.TrueForAll(c => c.Nodes.Count <= 5), "lowering maximum filters the table");
            var changed = new StagedPropulsion(new[] { new BurnStage(2, propulsion.Stages[0].Engine, 200), propulsion.Stages[1] });
            cache.Prepare(new SplitRequest(node, body, node.UT + 1e7, 1000, changed, 8, cache));
            False(cache.Contains(7), "fuel change invalidates cached trajectories");
            cache.Prepare(request);
            cache.Store(7, result.Candidates.FindAll(c => c.Nodes.Count == 7));
            SplitCandidate earliest = cache.GetCandidates(7)[0];
            double expiredAt = earliest.Nodes[0].Ut - earliest.Nodes[0].StartOffset;
            cache.Prepare(new SplitRequest(node, body, node.UT + 1e7, expiredAt, propulsion, 8, cache));
            False(cache.Contains(7), "expired first burn invalidates that count");

            cache.Prepare(request);
            cache.Store(7, result.Candidates.FindAll(c => c.Nodes.Count == 7));
            var originalEngine = propulsion.Stages[0].Engine;
            for (int step = 1; step <= 2; step++)
            {
                var drifting = new StagedPropulsion(new[] {
                    new BurnStage(propulsion.Stages[0].Stage, new BurnPhysics(originalEngine.Mass,
                        originalEngine.Thrust * (1 + step * 0.75e-6), originalEngine.ExhaustVelocity), propulsion.Stages[0].DeltaV),
                    propulsion.Stages[1]
                });
                cache.Prepare(new SplitRequest(node, body, node.UT + 1e7, 1000, drifting, 8, cache));
                True(cache.Contains(7) == (step == 1), "cache tolerance is anchored and cannot accumulate drift " + step);
            }

            var fast = StagedPropulsion.Single(new BurnPhysics(100, 12000, 800 * 9.80665));
            var antiNode = new ManeuverNode { patch = source, UT = node.UT, DeltaV = new Vector3d(0.1, -1822.6, 1874) };
            PlanResult antinormal = RunPlan(new SplitRequest(antiNode, body, node.UT + 1e7, 1000, fast, 4))!;
            True(antinormal.Candidates.Exists(c => c.Nodes.Exists(n => n.Purpose.StartsWith("Plane change"))),
                "antinormal high-thrust alternatives remain available");
            foreach (SplitCandidate candidate in antinormal.Candidates) ValidateTimedPlan(source, fast, candidate, antiNode.DeltaV);
            var moon = new CelestialBody { gravParameter = 6.5138398e10, Radius = 200000, sphereOfInfluence = 2429559 };
            Orbit lunar = SplitPlanner.OrbitFromState(new Vector3d(300000, 0, 0),
                new Vector3d(0, Math.Sqrt(moon.gravParameter / 300000), 0), moon, 1000);
            var lunarNode = new ManeuverNode { patch = lunar, UT = 1000 + 200 * lunar.period, DeltaV = new Vector3d(0, 220, 600) };
            PlanResult lunarResult = RunPlan(new SplitRequest(lunarNode, moon, lunarNode.UT + 1e7, 1000,
                new BurnPhysics(20, 40, 800 * 9.80665), 4))!;
            True(lunarResult.Candidates.Count > 0 && lunarResult.Candidates.TrueForAll(c => c.SetupDeltaV < 200),
                "comparison search uses the departure body's bound-energy budget");

            // An atmosphere intersection rejects execution without aborting the
            // independent larger-count searches.
            Orbit low = SplitPlanner.OrbitFromState(new Vector3d(669999, 0, 0),
                new Vector3d(0, Math.Sqrt(body.gravParameter / 669999), 0), body, 1000);
            var unsafeNode = new ManeuverNode { patch = low, UT = 1000 + 100 * low.period, DeltaV = new Vector3d(0, 0, 1500) };
            var unsafeRequest = new SplitRequest(unsafeNode, body, unsafeNode.UT + 1e7, 1000, propulsion, 4);
            PlanResult unsafeResult = RunPlan(unsafeRequest)!;
            True(unsafeResult.Candidates.Count == 0 && unsafeRequest.Cache.Contains(4), "atmosphere failures still reach highest requested count");
            var burn = new NodeSpec(unsafeNode.UT, new Vector3d(0, 0, 1), 1, .5);
            False(FiniteBurnEstimate.Measure(low, low, burn, propulsion.Stages[0].Engine, 0).IsFinite,
                "finite integration rejects atmosphere before engine ignition");

            Orbit safe = SplitPlanner.OrbitFromState(new Vector3d(800000, 0, 0),
                new Vector3d(0, Math.Sqrt(body.gravParameter / 800000), 0), body, 1000);
            safe.patchEndTransition = Orbit.PatchTransitionType.ENCOUNTER;
            safe.EndUT = 1100;
            False(StockTrajectorySafety.CoastIsSafe(safe, body, 1000, 1200, out _), "hidden intermediate SOI before next burn rejected");
            True(StockTrajectorySafety.CoastIsSafe(safe, body, 1000, 1050, out _), "SOI after next burn does not falsely reject plan");
            safe.patchEndTransition = Orbit.PatchTransitionType.IMPACT;
            False(StockTrajectorySafety.CoastIsSafe(safe, body, 1000, 1200, out _), "hidden intermediate collision rejected");
            False(StockTrajectorySafety.CoastIsSafe(low, body, 1000, 1100, out _), "atmospheric coast rejected");
            double ellipseSpeed = Math.Sqrt(body.gravParameter * (2.0 / 800000 - 2.0 / (800000 + 650000)));
            Orbit dipping = SplitPlanner.OrbitFromState(new Vector3d(800000, 0, 0), new Vector3d(0, ellipseSpeed, 0), body, 1000);
            False(StockTrajectorySafety.AboveAtmosphere(dipping, 1000, 1000 + dipping.period),
                "atmosphere crossing between safe endpoints is detected at periapsis");
            var safeBurn = new NodeSpec(1100, new Vector3d(0, 0, 1), 2, 1);
            int samples = 0;
            False(FiniteBurnEstimate.Measure(safe, safe, safeBurn, fast.Cursor().Engine, 0, safe, out _,
                (position, ut) => ++samples < 3).IsFinite, "finite burn samples can reject SOI entry during thrust");
            True(samples == 3, "SOI callback is evaluated throughout the burn");
        }

        private static void ValidateTimedPlan(Orbit source, StagedPropulsion propulsion, SplitCandidate candidate, Vector3d originalDeltaV)
        {
            Orbit nominal = source, executed = source;
            StageCursor cursor = propulsion.Cursor();
            double spent = 0, loss = 0;
            foreach (NodeSpec node in candidate.Nodes)
            {
                nominal.GetFixedState(node.Ut, out Vector3d r, out Vector3d v);
                Orbit after = SplitPlanner.OrbitFromState(r,
                    v + (SplitPlanner.NodeRotation(nominal, node.Ut) * node.DeltaV).xzy, source.referenceBody, node.Ut);
                FiniteBurnEstimate estimate = FiniteBurnEstimate.Measure(nominal, after, node, cursor.Engine, 0, executed, out executed);
                True(estimate.IsFinite, "every retained option has safe finite execution");
                Nearly(-source.referenceBody.gravParameter / (2 * after.semiMajorAxis),
                    -source.referenceBody.gravParameter / (2 * executed.semiMajorAxis), .005, "energy compensation retained");
                True(cursor.Consume(node.BurnDeltaV), "finite execution respects stage boundaries");
                loss = Math.Max(loss, estimate.CosineLoss);
                spent += node.BurnDeltaV;
                nominal = after;
            }
            Nearly(spent - candidate.OriginalDeltaV, candidate.AdditionalDeltaV, 1e-6, "additional dv includes every timed burn");
            Nearly(loss, candidate.MaximumCosineLoss, 1e-7, "reported cosine includes departure and every prior burn");
            double departureUt = candidate.Nodes[candidate.Nodes.Count - 1].Ut;
            source.GetFixedState(departureUt, out Vector3d expectedR, out Vector3d sourceV);
            nominal.GetFixedState(departureUt, out Vector3d actualR, out Vector3d actualV);
            Vector3d targetV = sourceV + (SplitPlanner.NodeRotation(source, departureUt) * originalDeltaV).xzy;
            Nearly(0, (actualR - expectedR).magnitude, 1, "nominal departure position is preserved");
            Nearly(0, (actualV - targetV).magnitude, .01, "nominal departure velocity is preserved");
        }

        private static void StagedPropulsionTests()
        {
            StagedPropulsion stages = new StagedPropulsion(new[] {
                new BurnStage(3, new BurnPhysics(257.7, 4500, 330 * 9.80665), 800),
                new BurnStage(1, new BurnPhysics(180, 300, 820 * 9.80665), 4000)
            });
            StageCursor cursor = stages.Cursor();
            False(cursor.Consume(801), "burn cannot cross stage fuel boundary");
            True(cursor.Consume(500), "first kick consumes stage fuel");
            Nearly(300, cursor.Remaining, 1e-8, "stage fuel carried between kicks");
            True(cursor.Consume(300) && cursor.Stage == 1, "depleted stage advances automatically");
            Nearly(180, cursor.Engine.Mass, 1e-8, "next stage uses post-decoupling mass");
            Nearly(300, cursor.Engine.Thrust, 1e-8, "next stage uses its own thrust");
            False(cursor.Consume(4001), "insufficient next-stage fuel rejected");
        }

        private static void StageAllocationTests()
        {
            const double mu = 3.5316e12, radius = 800000;
            double speed = Math.Sqrt(mu / radius);
            double period = 2 * Math.PI * Math.Sqrt(radius * radius * radius / mu);
            StagedPropulsion fuelRich = new StagedPropulsion(new[] {
                new BurnStage(4, new BurnPhysics(100, 5000, 3000), 2000),
                new BurnStage(0, new BurnPhysics(70, 200, 8000), 3000) });
            KickSchedule? partial = KickSchedule.Find(1, mu, radius, speed, 1, 670000, 8e7,
                period, 1500, 850, fuelRich, .01);
            True(partial != null && partial.StageKickCounts.Length == 1 && !partial.StageEnds[0],
                "available bound energy can require stopping before first-stage exhaustion");
            for (int count = 2; count <= 3; count++)
            {
                KickSchedule? balanced = KickSchedule.Find(count, mu, radius, speed, 1, 670000, 8e7,
                    period, 1500, 400, fuelRich, double.PositiveInfinity);
                True(balanced != null, "new same-stage count can be solved independently");
                if (balanced != null)
                    foreach (double kick in balanced.DeltaVs)
                        Nearly(balanced.SetupDeltaV / count, kick, 1e-9,
                            "new count redistributes the entire same-stage budget equally");
            }
            foreach (double gravityScale in new[] { 1.0, .04 })
            {
                // Similar orbital geometry with different gravity/velocities.
                // Engine acceleration and fuel are scaled by dimensional physics.
                double velocityScale = Math.Sqrt(gravityScale);
                foreach (double firstThrust in new[] { 100.0, 5000.0 })
                {
                    StagedPropulsion stages = new StagedPropulsion(new[] {
                        new BurnStage(8, new BurnPhysics(100, firstThrust * gravityScale, 3000 * velocityScale), 200 * velocityScale),
                        new BurnStage(3, new BurnPhysics(70, 200 * gravityScale, 8000 * velocityScale), 3000 * velocityScale) });
                    var schedules = KickSchedule.FindAll(5, mu * gravityScale, radius, speed * velocityScale,
                        1, 670000, 8e7, period / velocityScale, 1500, 850 * velocityScale, stages, .01);
                    var crossing = schedules.FindAll(s => s.StageKickCounts.Length == 2);
                    True(crossing.Count > 0, "stage combinations found for scaled gravity/thrust");
                    foreach (KickSchedule schedule in crossing)
                    {
                        int firstCount = schedule.StageKickCounts[0];
                        if (firstThrust == 100)
                            True(firstCount > 1, "slow first stage must split on physical loss limit");
                        Nearly(0, schedule.Elapsed - schedule.LeadOrbits * period / velocityScale, .01,
                            "each stage layout solves its own return resonance");
                        StageCursor cursor = stages.Cursor();
                        double spent = 0;
                        foreach (double kick in schedule.DeltaVs)
                        {
                            True(cursor.Engine.KickFits(mu * gravityScale, radius, speed * velocityScale + spent,
                                1, 0, kick, .01), "stage-local kick respects changing mass and orbit");
                            True(cursor.Consume(kick), "stage-local kick does not cross fuel boundary");
                            spent += kick;
                        }
                    }
                    if (firstThrust == 5000 && crossing.Count > 0)
                    {
                        True(crossing.Exists(s => s.StageKickCounts[0] == 1), "fast first stage offered as one burn");
                        True(crossing[0].StageKickCounts[0] == 1, "global setup optimization assigns slots to slow later stage");
                    }
                }
            }
        }

        private static PlanResult? RunPlan(SplitRequest request)
        {
            PlanResult? result = null;
            var iterator = SplitPlanner.PlanAsync(request, value => result = value);
            int yields = 0;
            double longestSlice = 0;
            var timer = new System.Diagnostics.Stopwatch();
            while (true)
            {
                timer.Restart();
                bool moved = iterator.MoveNext();
                longestSlice = Math.Max(longestSlice, timer.Elapsed.TotalMilliseconds);
                True(EvaluationBudget.Current == null, "integration replay scope clears between frames");
                if (!moved) break;
                yields++;
            }
            if (result?.Candidate != null)
            {
                if (request.Cache.ReusedCounts == 0) True(yields > 1, "finite simulation yields within candidate evaluation");
                Console.WriteLine("Planner slices=" + yields + ", longest headless slice=" + longestSlice.ToString("F2") + " ms");
            }
            return result;
        }

        private static void Nearly(
            double expected,
            double actual,
            double tolerance,
            string name)
        {
            if (!CandidateRules.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
            {
                Fail(name + ": expected " + expected + ", got " + actual + ".");
            }
        }

        private static void True(bool value, string name)
        {
            if (!value)
            {
                Fail(name + ": expected true.");
            }
        }

        private static void False(bool value, string name)
        {
            if (value)
            {
                Fail(name + ": expected false.");
            }
        }

        private static void Fail(string message)
        {
            failures++;
            Console.Error.WriteLine("FAIL " + message);
        }
    }
}
