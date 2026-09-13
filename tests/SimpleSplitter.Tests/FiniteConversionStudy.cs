using System;
using System.Collections;
using System.Collections.Generic;

namespace SimpleSplitter
{
    // Reproducible local comparison. Does not load KSP or modify saved games.
    internal static class FiniteConversionStudy
    {
        internal static SplitRequest Request()
        {
            // quicksave #3: last reported S11/S10 split, in perifocal axes.
            var body = new CelestialBody { gravParameter = 3.5316e12, Radius = 600000,
                atmosphere = true, atmosphereDepth = 70000, sphereOfInfluence = 84159286 };
            const double a = 729444.16401742154, e = 5.623389736864989E-05;
            const double epoch = 536351342.24559438, ut = 538507758.28266978;
            double mean = Math.IEEERemainder(1.0779536959987128 +
                Math.Sqrt(body.gravParameter / (a * a * a)) * (ut - epoch), 2 * Math.PI);
            double anomaly = mean;
            for (int i = 0; i < 12; i++)
                anomaly -= (anomaly - e * Math.Sin(anomaly) - mean) / (1 - e * Math.Cos(anomaly));
            double radius = a * (1 - e * Math.Cos(anomaly));
            var position = new Vector3d(a * (Math.Cos(anomaly) - e),
                a * Math.Sqrt(1 - e * e) * Math.Sin(anomaly), 0);
            var velocity = new Vector3d(-Math.Sin(anomaly), Math.Sqrt(1 - e * e) * Math.Cos(anomaly), 0) *
                (Math.Sqrt(body.gravParameter * a) / radius);
            Orbit orbit = SplitPlanner.OrbitFromState(position, velocity, body, ut);
            var node = new ManeuverNode { patch = orbit, UT = ut, DeltaV = new Vector3d(0, 0, 2067.3798303163885) };
            var propulsion = new StagedPropulsion(new[] {
                new BurnStage(11, new BurnPhysics(273.706378474081, 3999.9997558594082, 3089.0944736721458), 160.41350022952807),
                new BurnStage(10, new BurnPhysics(217.85581784344191, 299.99996948242182, 8041.4526115194867), 5505.6985535009826)
            });
            return new SplitRequest(node, body, 559632563.43939829, epoch, propulsion, 6);
        }

        internal static void Drain(IEnumerator work)
        {
            while (work.MoveNext()) if (work.Current is IEnumerator nested) Drain(nested);
        }

        internal static int Run()
        {
            SplitRequest request = Request();
            Drain(SplitPlanner.PlanAsync(request, _ => { }));
            string[] names = { "Full terminal state", "Coast position RMS", "Two-impulse correction surrogate" };
            for (int metric = 0; metric < names.Length; metric++)
            {
                int selected = metric;
                Func<Orbit, double[]> residual = actual => Residual(request.Reference, actual, selected);
                FiniteConversionResult? best = null;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                int trials = 0;
                for (int seedCount = 3; seedCount <= 6; seedCount++)
                    foreach (SplitCandidate seed in request.Cache.SeedsForCount(seedCount))
                    {
                        FiniteConversionResult? result = FiniteConversion.Convert(request, seed, 7 - seedCount, request.Reference, residual);
                        trials++;
                        if (result == null) continue;
                        if (best == null || (result.MatchesReference && !best.MatchesReference) ||
                            (result.MatchesReference == best.MatchesReference &&
                                (result.MatchesReference ? result.TotalDeltaV < best.TotalDeltaV :
                                    result.Error.SeparationMeters < best.Error.SeparationMeters))) best = result;
                    }
                if (best == null) { Console.WriteLine(names[metric] + ": no result"); return 1; }
                Console.WriteLine(names[metric] + ": r=" + best.Error.PositionMeters.ToString("F6") +
                    " m, v=" + best.Error.VelocityMetersPerSecond.ToString("F9") + " m/s, coast RMS=" +
                    CoastRms(request.Reference, best.Executed).ToString("F6") + " m, dv=" + best.TotalDeltaV.ToString("F6") +
                    " m/s, " + trials + " identical starts, seconds=" + watch.Elapsed.TotalSeconds.ToString("F3"));
            }
            return 0;
        }

        // All three use the same finite-burn controls, start layouts, iteration
        // limits, terminal acceptance tolerances, and fuel polishing. Only the
        // residual that supplies the shooting correction differs.
        internal static double[] Residual(TrajectoryReference reference, Orbit actual, int metric)
        {
            if (metric == 0) return reference.Residual(actual);
            var values = new List<double>();
            void Append(Vector3d v) { values.Add(v.x); values.Add(v.y); values.Add(v.z); }
            if (metric == 1)
            {
                foreach (double offset in new[] { .25, .625, 1.0 })
                {
                    double ut = reference.ManeuverUt + reference.Horizon * offset;
                    actual.GetFixedState(ut, out Vector3d r, out _);
                    reference.Target.GetFixedState(ut, out Vector3d target, out _);
                    Append((r - target) / (reference.Scale * Math.Sqrt(3)));
                }
            }
            else
            {
                TrajectoryError error = reference.Measure(actual);
                // In a constant-velocity model, impulses that remove dr,dv
                // over H are -(dv+dr/H) now and dr/H at the rendezvous.
                // Their squared effort, scaled to metres, is the residual.
                Append(error.Position / reference.Scale);
                Append((error.Velocity * reference.Horizon + error.Position) / reference.Scale);
            }
            return values.ToArray();
        }

        internal static double CoastRms(TrajectoryReference reference, Orbit actual)
        {
            double square = 0;
            foreach (double v in Residual(reference, actual, 1)) square += v * v;
            return Math.Sqrt(square) * reference.Scale;
        }

        internal static int ConversionSmoke()
        {
            SplitRequest request = Request();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Drain(SplitPlanner.PlanAsync(request, _ => { }));
            Console.WriteLine("Boundary horizon=" + request.Reference.Horizon + ", search seconds=" + watch.Elapsed.TotalSeconds);
            for (int count = 2; count <= request.MaximumBurns; count++)
                foreach (SplitCandidate candidate in request.Cache.ForCount(count))
                    Console.WriteLine("count=" + count + " pieces=" + candidate.DeparturePieces +
                        " r=" + candidate.TrajectoryError.PositionMeters.ToString("F6") +
                        " v=" + candidate.TrajectoryError.VelocityMetersPerSecond.ToString("F9") +
                        " dv=" + candidate.Score.TotalDeltaV.ToString("F3") +
                        " layout=" + string.Join("/", candidate.Schedule!.StageKickCounts) +
                        " durations=" + string.Join("/", candidate.Nodes.ConvertAll(n => n.Duration.ToString("F1"))));
            return 0;
        }
    }
}
