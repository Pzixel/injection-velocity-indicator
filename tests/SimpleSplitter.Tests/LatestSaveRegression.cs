using System;

namespace SimpleSplitter
{
    internal static partial class Program
    {
        private static void LatestSaveRegression()
        {
            // modded2/quicksave #1, 2026-09-12. Orbital elements from the save,
            // propulsion segments from the failed comparison's embedded KER log.
            var body = new CelestialBody { gravParameter = 3.5316e12, Radius = 600000,
                atmosphere = true, atmosphereDepth = 70000, sphereOfInfluence = 84159286 };
            const double a = 713565.58509334503, e = .022037855873002301;
            const double now = 536348700.55607706, ut = 538506255.28266978;
            double mean = Math.IEEERemainder(1.6220658722654562 +
                Math.Sqrt(body.gravParameter / (a * a * a)) * (ut - now), 2 * Math.PI);
            double anomaly = mean;
            for (int i = 0; i < 12; i++)
                anomaly -= (anomaly - e * Math.Sin(anomaly) - mean) / (1 - e * Math.Cos(anomaly));
            double radius = a * (1 - e * Math.Cos(anomaly));
            var position = new Vector3d(a * (Math.Cos(anomaly) - e),
                a * Math.Sqrt(1 - e * e) * Math.Sin(anomaly), 0);
            var velocity = new Vector3d(-Math.Sin(anomaly), Math.Sqrt(1 - e * e) * Math.Cos(anomaly), 0) *
                (Math.Sqrt(body.gravParameter * a) / radius);
            Orbit source = SplitPlanner.OrbitFromState(position, velocity, body, ut);
            var node = new ManeuverNode { patch = source, UT = ut, DeltaV = new Vector3d(0, 0, 2067.3798303163885) };
            var propulsion = new StagedPropulsion(new[] {
                new BurnStage(10, new BurnPhysics(275.8986626593703, 3999.999511718765, 3089.09428512877), 185.05672267119283),
                new BurnStage(3, new BurnPhysics(175.31457678085624, 359.99990844726335, 6540.5910929697138), 116.82768830317009),
                new BurnStage(3, new BurnPhysics(172.21091839436218, 299.99996948242205, 8041.4526115194867), 7674.0404450231672)
            });
            var request = new SplitRequest(node, body, 560045006.76004517, now, propulsion, 6);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            PlanResult result = RunPlan(request)!;
            Console.WriteLine("Latest save: anomaly=" + source.TrueAnomalyAtUT(ut) * 180 / Math.PI +
                " deg, candidates=" + result.Candidates.Count + ", error=" + result.Error +
                ", elapsed=" + watch.Elapsed.TotalSeconds.ToString("F3") + " s");
            True(result.Candidates.Exists(c => c.Nodes.Count <= 5), "latest eccentric parking orbit has finite split options through five burns");
            for (int count = 4; count <= 6; count++)
                True(result.Candidates.Exists(c => c.Nodes.Count == count &&
                    Math.Abs(c.Nodes[0].BurnDeltaV - propulsion.Stages[0].DeltaV) < 1e-6),
                    "latest save retains a full high-thrust first burn for count " + count);
            foreach (SplitCandidate candidate in result.Candidates)
            {
                ReplayLatest(source, propulsion, candidate, node.DeltaV, ut);
                Console.WriteLine("Latest save " + candidate.Nodes.Count + " burns: total=" + candidate.Score.TotalDeltaV.ToString("F2") +
                    ", outgoing error=" + candidate.ExcessVelocityError.ToString("F3") +
                    ", stage slots=" + string.Join("/", candidate.Schedule!.StageKickCounts) +
                    ", durations=" + string.Join("/", candidate.Nodes.ConvertAll(n => n.Duration.ToString("F1"))) +
                    ", starts=" + ((ut - candidate.Nodes[0].Ut) / KSPUtil.dateTimeFormatter.Day).ToString("F2") + " game days before departure");
            }
            var invalid = new SplitRequest(node, body, request.ArrivalUt, ut - 10, propulsion, 6);
            PlanResult failure = RunPlan(invalid)!;
            True(invalid.ValidationError.Contains("too close") && invalid.ValidationError == failure.Error,
                "request rejection preserves its actionable reason instead of reporting an empty search");
            True(invalid.Cache.GetCandidates(6).Count == 0, "invalid requests do not attempt burn searches");
            // The same eccentric orbit on its outbound leg must not depend on
            // which side of periapsis the original maneuver occupies.
            Orbit outbound = SplitPlanner.OrbitFromState(new Vector3d(position.x, -position.y, 0),
                new Vector3d(-velocity.x, velocity.y, 0), body, ut);
            var outboundNode = new ManeuverNode { patch = outbound, UT = ut, DeltaV = node.DeltaV };
            PlanResult outboundResult = RunPlan(new SplitRequest(outboundNode, body, request.ArrivalUt, now, propulsion, 5))!;
            True(outboundResult.Candidates.Exists(c => c.Nodes.Count <= 5), "off-periapsis setup works on outbound as well as inbound orbits");
            if (outboundResult.Candidate != null) ReplayLatest(outbound, propulsion, outboundResult.Candidate, node.DeltaV, ut);
        }

        private static void ReplayLatest(Orbit source, StagedPropulsion propulsion, SplitCandidate candidate, Vector3d originalDv, double originalUt)
        {
            ValidateTimedPlan(source, propulsion, candidate, originalDv, originalUt);
        }
    }
}
