using System;
using System.Collections;
using System.Collections.Generic;

namespace SimpleSplitter
{
    internal static partial class Program
    {
        private static void TestburnRegression()
        {
            var body = new CelestialBody { gravParameter = 3.5316e12, Radius = 600000,
                atmosphere = true, atmosphereDepth = 70000, sphereOfInfluence = 84159286 };
            // Saved orbital elements in perifocal axes; absolute inclination
            // does not change the spherical-body finite-burn calculation.
            const double a = 686779.82322367816, e = 5.5040703887490178e-6;
            const double now = 536340872.28518379, ut = 538378057.5413729;
            double mean = 0.57286246753731207 + Math.Sqrt(body.gravParameter / (a * a * a)) * (ut - now);
            mean = Math.IEEERemainder(mean, 2 * Math.PI);
            double anomaly = mean;
            for (int i = 0; i < 10; i++)
                anomaly -= (anomaly - e * Math.Sin(anomaly) - mean) / (1 - e * Math.Cos(anomaly));
            double r = a * (1 - e * Math.Cos(anomaly));
            var position = new Vector3d(a * (Math.Cos(anomaly) - e),
                a * Math.Sqrt(1 - e * e) * Math.Sin(anomaly), 0);
            var velocity = new Vector3d(-Math.Sin(anomaly), Math.Sqrt(1 - e * e) * Math.Cos(anomaly), 0) *
                (Math.Sqrt(body.gravParameter * a) / r);
            Orbit source = SplitPlanner.OrbitFromState(position, velocity, body, ut);
            var node = new ManeuverNode { patch = source, UT = ut, DeltaV = new Vector3d(0, -1046.8, 2108.8) };
            var propulsion = new StagedPropulsion(new[] { new BurnStage(11,
                new BurnPhysics(161.53975012686689, 299.99996948242159, 8041.4526115194876), 4744.9420483556423) });
            // Reproduce KSP's rotating low-orbit reference frame. The vector
            // overload is intentionally different from its fixed State overload.
            Orbit.FrameEpoch = now;
            Orbit.FrameRate = 2 * Math.PI / 21600;
            try
            {
                source.GetOrbitalStateVectorsAtUT(ut, out Vector3d localR, out Vector3d localV);
                var legacyCopy = new Orbit();
                legacyCopy.UpdateFromStateVectors(localR, localV, body, ut);
                legacyCopy.GetFixedState(ut, out Vector3d wrongR, out _);
                True((wrongR - position).magnitude > 1000,
                    "testburn exposes the future-frame/current-frame mismatch");

                var request = new SplitRequest(node, body, 556649587.92786407, now, propulsion, 5);
                request.SourceOrbit.GetFixedState(ut, out Vector3d copiedR, out Vector3d copiedV);
                Nearly(0, (copiedR - position).magnitude, 1e-6, "fixed snapshot preserves departure position");
                Nearly(0, (copiedV - velocity).magnitude, 1e-8, "fixed snapshot preserves departure velocity");
                source.GetFixedState(now, out Vector3d currentR, out Vector3d currentV);
                Orbit noisy = SplitPlanner.OrbitFromState(currentR + new Vector3d(.08, .08, 0),
                    currentV + new Vector3d(.0002, .0002, 0), body, now);
                True(OrbitState.MatchesInputs(source, noisy, now), "float-scale flight jitter is not a maneuver edit");
                False(OrbitState.MatchesInputs(source, noisy, ut), "distant propagation amplifies harmless input noise");
                Orbit burned = SplitPlanner.OrbitFromState(currentR, currentV + currentV.normalized * .05, body, now);
                False(OrbitState.MatchesInputs(source, burned, now), "real velocity changes invalidate the orbit snapshot");
                PlanResult result = RunPlan(request)!;
                True(result.Candidates.Exists(c => c.Nodes.Count == 4), "testburn finds four-burn alternatives");
                True(result.Candidates.Exists(c => c.Nodes.Count == 5 &&
                    c.Nodes.Exists(n => n.Purpose.StartsWith("Plane change"))),
                    "testburn finds five burns including the plane change");
                for (int count = 2; count <= 5; count++)
                    True(request.Cache.Contains(count), "testburn completes independent count " + count);
                var accepted = new Dictionary<int, SplitCandidate>();
                int checks = 0;
                IEnumerator Check(SplitCandidate candidate, Action<bool> completed)
                {
                    checks++;
                    // A deterministic collision oracle: reject every equal-kick
                    // four-node path and admit a redistributed finite alternative.
                    // Real moon encounters are still checked in the game.
                    completed(candidate.Nodes.Count == 5 || (candidate.Nodes.Count == 4 &&
                        Math.Abs(candidate.Nodes[0].DeltaV.z - candidate.Nodes[1].DeltaV.z) > 1));
                    yield break;
                }
                void Drain(IEnumerator iterator)
                {
                    while (iterator.MoveNext())
                        if (iterator.Current is IEnumerator nested) Drain(nested);
                }
                Drain(SafePlanSearch.FindAsync(request, Check, () => true, (count, candidate) => accepted[count] = candidate));
                True(accepted.ContainsKey(4), "rejected four-burn paths trigger redistribution and a new safety check");
                True(request.Cache.IsRefined(4), "four-burn redistribution is cached");
                False(request.Cache.IsRefined(5), "already safe counts do not need a collision retry");
                if (accepted.TryGetValue(4, out SplitCandidate? redistributed))
                    ValidateTimedPlan(source, propulsion, redistributed, node.DeltaV);
                int priorChecks = checks;
                Drain(SafePlanSearch.FindAsync(request, Check, () => true, (count, candidate) => accepted[count] = candidate));
                True(checks == priorChecks, "comparison reuses safety results and completed redistribution");
                var budgetCache = new PlanSearchCache();
                var budgetRequest = new SplitRequest(node, body, request.ArrivalUt, now, propulsion, 5, budgetCache);
                budgetCache.Prepare(budgetRequest);
                for (int count = 2; count <= 5; count++)
                    budgetCache.Store(count, result.Candidates.FindAll(c => c.Nodes.Count == count));
                var budgetChoices = new Dictionary<int, SplitCandidate>();
                Drain(SafePlanSearch.FindAsync(budgetRequest, Check, () => true,
                    (count, candidate) => budgetChoices[count] = candidate, refinementSeconds: 0));
                True(budgetChoices.ContainsKey(5), "baseline rows complete even with no refinement allowance");
                False(budgetChoices.ContainsKey(4), "budget exhaustion cannot accept an unsafe baseline");
                True(budgetCache.RefinementBudgetReached && budgetCache.RefinementStep(4) == 0,
                    "budget exhaustion retains unfinished refinement work");
                Drain(SafePlanSearch.FindAsync(budgetRequest, Check, () => true,
                    (count, candidate) => budgetChoices[count] = candidate, refinementSeconds: double.PositiveInfinity));
                True(budgetChoices.ContainsKey(4) && budgetCache.RefinementStep(4) == 1,
                    "resuming the search repairs the rejected four-burn option incrementally");
                var identityCache = new PlanSearchCache();
                identityCache.Prepare(request);
                identityCache.Store(4, request.Cache.ForCount(4));
                identityCache.StockSafety[request.Cache.ForCount(4)[0]] = false;
                var noisyNode = new ManeuverNode { patch = noisy, UT = ut, DeltaV = node.DeltaV };
                var liveRequest = new SplitRequest(noisyNode, body, request.ArrivalUt, now + 1, propulsion, 5, identityCache);
                SplitCandidate? liveCandidate = null;
                SplitCandidate cachedRecipe = accepted[4];
                Drain(SplitPlanner.RefreshAsync(liveRequest, cachedRecipe, value => liveCandidate = value));
                True(liveCandidate != null && liveCandidate.Nodes.Count == 4,
                    "a cached four-burn recipe refreshes against live orbital roundoff");
                if (liveCandidate != null)
                {
                    ValidateTimedPlan(noisy, propulsion, liveCandidate, node.DeltaV);
                    Nearly(cachedRecipe.Schedule!.DistributionBias, liveCandidate.Schedule!.DistributionBias, 0,
                        "live refresh preserves the optimized redistribution");
                    True(liveCandidate.Nodes.Exists(n => n.Purpose.StartsWith("Plane change")),
                        "live refresh retains the plane-change slot");
                }
                identityCache.Prepare(new SplitRequest(noisyNode, body, request.ArrivalUt + 2 * CandidateRules.ArrivalToleranceSeconds, now + 1,
                    propulsion, 5, identityCache));
                True(identityCache.Contains(4), "roundoff and derived encounter-time noise reuse completed searches");
                True(identityCache.StockSafety.Count == 0, "a new live comparison rechecks earlier safety rejections");
                var changedNode = new ManeuverNode { patch = burned, UT = ut, DeltaV = node.DeltaV };
                identityCache.Prepare(new SplitRequest(changedNode, body, request.ArrivalUt, now + 1,
                    propulsion, 5, identityCache));
                False(identityCache.Contains(4), "real orbit edits invalidate cached counts");
                // Run the real numerical/finite-burn work for the requested
                // table sizes before live KSP validation. Only the stock moon
                // encounter decision is replaced by a deterministic oracle.
                foreach (int maximum in new[] { 6, 8 })
                {
                    var timing = System.Diagnostics.Stopwatch.StartNew();
                    var benchmark = new SplitRequest(node, body, request.ArrivalUt, now, propulsion, maximum);
                    PlanResult? baseline = null;
                    int frameYields = 0;
                    IEnumerator runner = PlanningWork.Run(SplitPlanner.PlanAsync(benchmark, value => baseline = value));
                    while (runner.MoveNext()) frameYields++;
                    True(baseline != null && benchmark.Cache.ReusedCounts == 0,
                        "performance fixture starts with a cold independent search");
                    var rows = new Dictionary<int, SplitCandidate>();
                    int liveReplays = 0;
                    IEnumerator Replay(SplitCandidate candidate, Action<bool> completed)
                    {
                        SplitCandidate? refreshed = null;
                        yield return SplitPlanner.RefreshAsync(benchmark, candidate, value => refreshed = value);
                        liveReplays++;
                        bool safe = refreshed != null && refreshed.Nodes.Count >= 4 &&
                            Math.Abs(refreshed.Nodes[0].DeltaV.z - refreshed.Nodes[1].DeltaV.z) > 1;
                        if (safe)
                        {
                            ValidateTimedPlan(source, propulsion, refreshed!, node.DeltaV);
                            candidate.LiveCandidate = refreshed;
                        }
                        completed(safe);
                    }
                    runner = PlanningWork.Run(SafePlanSearch.FindAsync(benchmark, Replay, () => true,
                        (count, candidate) => rows[count] = candidate, refinementSeconds: double.PositiveInfinity));
                    while (runner.MoveNext()) frameYields++;
                    for (int count = 4; count <= maximum; count++)
                    {
                        True(rows.ContainsKey(count), "cold search redistributes and fully replays count " + count);
                        True(benchmark.Cache.RefinementStep(count) == 1,
                            "a usable higher-count row does not wait for exhaustive low-count retries");
                        True(rows[count].Nodes.Exists(n => n.Purpose.StartsWith("Plane change")),
                            "each timed performance fixture preserves its plane-change burn");
                    }
                    False(rows.ContainsKey(3), "an exhausted unsafe count never becomes an accepted row");
                    True(benchmark.Cache.IsRefined(3) && benchmark.Cache.RefinementStep(3) == 7,
                        "an entirely rejected count terminates after the finite refinement grid");
                    Console.WriteLine("Cold " + maximum + "-burn headless search + timed replays: " +
                        timing.Elapsed.TotalSeconds.ToString("F3") + " s, " + liveReplays +
                        " checked candidates, " + frameYields + " frame yields.");
                }
                Console.WriteLine("testburn redistributed four-burn alternatives=" + request.Cache.ForCount(4).Count);
                foreach (SplitCandidate candidate in result.Candidates)
                {
                    ValidateTimedPlan(source, propulsion, candidate, node.DeltaV);
                    True(candidate.Nodes.TrueForAll(n => n.Purpose.Contains("stage 11")),
                        "testburn uses the active nuclear stage throughout");
                    Console.WriteLine("testburn " + candidate.Nodes.Count + " burns: timed dv=" +
                        candidate.Score.TotalDeltaV.ToString("F2") + ", excess velocity error=" +
                        candidate.ExcessVelocityError.ToString("F3"));
                }
            }
            finally { Orbit.FrameRate = 0; Orbit.FrameEpoch = 0; }
        }
    }
}
