using System;

namespace SimpleSplitter
{
    internal static partial class Program
    {
        private static void CheckMappingVariant(SplitRequest request, SplitCandidate candidate, string name)
        {
            True(candidate != null, name + " has a matched finite fixture");
            if (candidate == null) return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var mapped = new ValidatedPlan(request, candidate).Nodes;
            watch.Stop();
            Orbit stock = request.SourceOrbit, actual = request.SourceOrbit;
            StageCursor fuel = request.Propulsion.Cursor();
            foreach (NodeSpec node in mapped)
            {
                stock.GetFixedState(node.Ut, out Vector3d r, out Vector3d v);
                stock = SplitPlanner.OrbitFromState(r, v + (SplitPlanner.NodeRotation(stock, node.Ut) * node.DeltaV).xzy,
                    stock.referenceBody, node.Ut);
                actual = IntegrateCommand(actual, node, fuel.Engine, .1);
                True(fuel.Consume(node.BurnDeltaV), name + " retains staged fuel");
            }
            var mapError = request.Reference.Measure(stock);
            var realError = request.Reference.Measure(actual);
            Console.WriteLine($"Mapping {name}: map={mapError.PositionMeters:F3} m / {mapError.VelocityMetersPerSecond:F6} m/s, real={realError.PositionMeters:F3} m, mapping={watch.Elapsed.TotalMilliseconds:F1} ms.");
            True(mapError.PositionMeters < 1000 && mapError.VelocityMetersPerSecond < .1, name + " stock mapping retains the outgoing trajectory");
            True(realError.PositionMeters < 2 && realError.VelocityMetersPerSecond < .001, name + " stock headings/timers retain the finite solution");
        }

        private static void ManeuverMappingRegression()
        {
            // AAAA / 2026-09-13 log. Exercise the receipt used by Apply, then
            // reconstruct what KSP plots: instantaneous impulses in each
            // preceding stock patch. No stored inertial execution directions.
            SplitRequest basis = FiniteConversionStudy.Request();
            var propulsion = new StagedPropulsion(new[] {
                new BurnStage(11, new BurnPhysics(273.70637861729847, 4000.0002441406409, 3089.0948507588964), 160.41351972510003),
                new BurnStage(10, new BurnPhysics(217.85581798665939, 299.9999694824221, 8041.4526115194876), 5505.6985483038752)
            });
            var request = new SplitRequest(basis.Node, basis.TargetBody, 559632570.59598744, basis.Now, propulsion, 6);
            RunPlan(request);
            SplitCandidate selected = request.Cache.ForCount(6)[0];
            selected.TimedArrivalOffsetSeconds = 2.224306583404541;
            var receipt = new ValidatedPlan(request, selected);
            Orbit stock = request.SourceOrbit, executed = request.SourceOrbit;
            StageCursor fuel = propulsion.Cursor();
            for (int i = 0; i < receipt.Nodes.Count; i++)
            {
                Orbit remaining = executed;
                var remapped = receipt.Remap(executed, i);
                for (int j = i; j < receipt.Nodes.Count; j++)
                {
                    NodeSpec future = remapped[j - i];
                    remaining.GetFixedState(future.Ut, out Vector3d rr, out Vector3d vv);
                    remaining = SplitPlanner.OrbitFromState(rr, vv + (SplitPlanner.NodeRotation(remaining, future.Ut) * future.DeltaV).xzy,
                        remaining.referenceBody, future.Ut);
                }
                Console.WriteLine($"Remaining map at burn {i+1}: {request.Reference.Measure(remaining).PositionMeters:F3} m");
                // A single fixed-heading impulse has only time/magnitude to
                // fit, so it cannot generally match a finite burn's full state.
                // Keep its map error below 0.1% of the reference distance; the
                // actual flown boundary below still has the metre tolerance.
                double tolerance = remapped.Count == 1 ? request.Reference.Position.magnitude * .001 : 1000;
                True(request.Reference.Measure(remaining).PositionMeters < tolerance,
                    "remaining published nodes keep the intended trajectory after a completed finite burn is removed");
                NodeSpec plot = receipt.Nodes[i], flown = remapped[0], burn = selected.Nodes[i];
                stock.GetFixedState(plot.Ut, out Vector3d r, out Vector3d v);
                if (i == 4)
                {
                    double altitude = r.magnitude - stock.referenceBody.Radius;
                    Console.WriteLine($"AAAA plotted departure altitude: {altitude / 1000:F3} km; preceding apoapsis: {(stock.ApR - stock.referenceBody.Radius) / 1000:F3} km.");
                    True(altitude < 1000000, "AAAA published departure is near the computed periapsis passage, not at 23,424 km");
                    True(Math.Abs(stock.period / executed.period - 1) < .01,
                        "AAAA published waiting period follows the finite solution within one percent");
                }
                stock = SplitPlanner.OrbitFromState(r, v + (SplitPlanner.NodeRotation(stock, plot.Ut) * plot.DeltaV).xzy,
                    stock.referenceBody, plot.Ut);
                Nearly(burn.Ut - burn.StartOffset, plot.Ut - plot.StartOffset, 1e-7,
                    "effective node preserves the actual ignition time");
                Nearly(burn.Duration, plot.Duration, 0, "effective node preserves the full-throttle timer");
                Nearly(burn.BurnDeltaV, plot.BurnDeltaV, 0, "effective node preserves actual fuel expenditure");
                True(plot.StartOffset >= 0 && plot.StartOffset <= plot.Duration,
                    "effective impulse lies within the physical burn");
                Vector3d heading = (SplitPlanner.NodeRotation(executed, flown.Ut) * flown.DeltaV).xzy.normalized;
                Nearly(0, (heading - burn.InertialDirection!.Value).magnitude, 1e-7,
                    "next mapped game node gives the optimized inertial heading after earlier burns are removed");
                executed = IntegrateCommand(executed, flown, fuel.Engine, .1);
                True(fuel.Consume(burn.BurnDeltaV), "mapped plan retains executable stage budgets");
            }
            TrajectoryError plotted = request.Reference.Measure(stock);
            Console.WriteLine($"AAAA plotted terminal error: {plotted.PositionMeters:F3} m, {plotted.VelocityMetersPerSecond:F6} m/s.");
            True(plotted.PositionMeters < 1000 && plotted.VelocityMetersPerSecond < .1,
                "AAAA instantaneous plan depicts the intended outgoing trajectory");
            True(request.Reference.Measure(executed).MatchesReference,
                "mapping retains the good finite-burn solution");
            True(receipt.Nodes.Count == selected.Nodes.Count && receipt.Nodes.Count <= request.MaximumBurns,
                "mapping does not introduce extra player maneuvers");
        }
    }
}
