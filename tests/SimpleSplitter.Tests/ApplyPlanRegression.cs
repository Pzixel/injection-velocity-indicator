using System;
using System.Collections.Generic;

namespace SimpleSplitter
{
    internal static partial class Program
    {
        private static void ApplyPlanRegression()
        {
            // AAAA.sfs, Laythe expedition: same orbital elements as quicksave
            // #3, including the saved orientation. Propulsion/arrival below
            // are from the failed Compare/Apply session's KSP.log.
            SplitRequest basis = FiniteConversionStudy.Request();
            basis.SourceOrbit.GetFixedState(basis.OriginalUt, out Vector3d r, out Vector3d v);
            Vector3d Rotate(Vector3d value)
            {
                Vector3d Z(Vector3d p, double angle) => new Vector3d(p.x * Math.Cos(angle) - p.y * Math.Sin(angle),
                    p.x * Math.Sin(angle) + p.y * Math.Cos(angle), p.z);
                value = Z(value, 23.813799577376596 * Math.PI / 180);
                double inc = .099043820771725091 * Math.PI / 180;
                value = new Vector3d(value.x, value.y * Math.Cos(inc) - value.z * Math.Sin(inc),
                    value.y * Math.Sin(inc) + value.z * Math.Cos(inc));
                return Z(value, 185.64997404301656 * Math.PI / 180);
            }
            var source = SplitPlanner.OrbitFromState(Rotate(r), Rotate(v), basis.SourceOrbit.referenceBody, basis.OriginalUt);
            var node = new ManeuverNode { patch = source, UT = basis.OriginalUt, DeltaV = basis.OriginalDeltaV };
            var propulsion = new StagedPropulsion(new[] {
                new BurnStage(11, new BurnPhysics(273.706378560098, 4000.0002441406573, 3089.094850758896), 160.41351975951045),
                new BurnStage(10, new BurnPhysics(217.85581792945888, 299.99996948242136, 8041.4526115194876), 5505.6985503795695)
            });
            var request = new SplitRequest(node, basis.TargetBody, 559632571.532919, basis.Now, propulsion, 6);
            RunPlan(request);
            double worstRebase = 0;
            for (int count = 4; count <= 6; count++)
            {
                var choices = request.Cache.ForCount(count);
                True(choices.Count > 0 && choices[0].MatchesReference, "AAAA has a real matched route with " + count + " burns");
                if (choices.Count == 0) continue;
                SplitCandidate candidate = choices[0];
                Orbit Replay(Orbit initial)
                {
                    StageCursor fuel = request.Propulsion.Cursor();
                    foreach (NodeSpec command in candidate.Nodes)
                    {
                        var measured = FiniteBurnEstimate.Measure(initial, request.Reference.Target, command, fuel.Engine, 0,
                            initial, out initial);
                        True(measured.IsFinite && fuel.Consume(command.BurnDeltaV), "AAAA replay preserves finite burn safety and fuel");
                    }
                    return initial;
                }
                TrajectoryError checkedError = request.Reference.Measure(Replay(request.SourceOrbit));
                Nearly(0, (checkedError.Position - candidate.TrajectoryError.Position).magnitude, .001,
                    "one captured source reproduces the checked trajectory consistently");
                foreach (double delay in new[] { 0.0, .02, .1, 1, 5, 15 })
                {
                    request.SourceOrbit.GetFixedState(request.Now + delay, out Vector3d cr, out Vector3d cv);
                    Orbit rebased = SplitPlanner.OrbitFromState(cr, cv, source.referenceBody, request.Now + delay);
                    TrajectoryError other = request.Reference.Measure(Replay(rebased));
                    worstRebase = Math.Max(worstRebase, (checkedError.Position - other.Position).magnitude);
                }

                // Stock encounter solving is not available headlessly; supply
                // the successful near-zero arrival residual seen in the log.
                candidate.TimedArrivalOffsetSeconds = .038763523101806641;
                var receipt = new ValidatedPlan(request, candidate);
                Orbit preview = source;
                foreach (NodeSpec effective in receipt.Nodes)
                {
                    preview.GetFixedState(effective.Ut, out Vector3d pr, out Vector3d pv);
                    preview = SplitPlanner.OrbitFromState(pr, pv + (SplitPlanner.NodeRotation(preview, effective.Ut) * effective.DeltaV).xzy,
                        preview.referenceBody, effective.Ut);
                }
                True(request.Reference.Measure(preview).PositionMeters < 1000,
                    "AAAA four/five/six-burn stock previews actually match the checked trajectory");
                var original = new List<NodeSpec> { new NodeSpec(node.UT, node.DeltaV) };
                var plotted = new List<NodeSpec>(original);
                void Replace(IList<NodeSpec> specs) { plotted = new List<NodeSpec>(specs); }
                bool Matches(IList<NodeSpec> expected)
                {
                    if (expected.Count != plotted.Count) return false;
                    for (int i = 0; i < expected.Count; i++)
                        if (expected[i].Ut != plotted[i].Ut || (expected[i].DeltaV - plotted[i].DeltaV).magnitude != 0) return false;
                    return true;
                }
                True(ManeuverPlanTransaction.Write(original, receipt.Nodes, Replace, Matches, true, out _),
                    "AAAA route can be previewed before it is offered");
                True(Matches(original), "preview restores the original maneuver");
                request.SourceOrbit.GetFixedState(request.Now + 15, out Vector3d liveR, out Vector3d liveV);
                Orbit live = SplitPlanner.OrbitFromState(liveR + new Vector3d(.02, -.02, .01),
                    liveV + new Vector3d(.00002, 0, 0), source.referenceBody, request.Now + 15);
                True(receipt.CanApply(live, propulsion, 6, request.Now + 15, out _),
                    "harmless coast/float jitter does not discard the checked route");
                True(ManeuverPlanTransaction.Write(original, receipt.Nodes, Replace, Matches, false, out _),
                    "Apply plots exactly the successfully previewed AAAA route");
                True(Matches(receipt.Nodes), "committed route preserves every previewed effective impulse and time");
                for (int i = 0; i < candidate.Nodes.Count; i++)
                {
                    True(ReferenceEquals(receipt.Nodes[i], plotted[i]), "Apply commits the checked mapping without recomputing it");
                    Nearly(candidate.Nodes[i].Duration, plotted[i].Duration, 0, "mapped receipt retains the checked burn duration");
                    Nearly(candidate.Nodes[i].BurnDeltaV, plotted[i].BurnDeltaV, 0, "mapped receipt retains the checked fuel expenditure");
                    Nearly(candidate.Nodes[i].Ut - candidate.Nodes[i].StartOffset, plotted[i].Ut - plotted[i].StartOffset, 1e-7,
                        "mapped receipt retains the checked ignition");
                }
                candidate.Nodes.Clear();
                True(receipt.Nodes.Count == count, "validated commands cannot be changed by later candidate-list mutation");
                True(receipt.Commands.Count == count, "remaining mapping retains immutable finite commands");

                Orbit burned = SplitPlanner.OrbitFromState(liveR, liveV + new Vector3d(.05, 0, 0), source.referenceBody, request.Now + 15);
                False(receipt.CanApply(burned, propulsion, 6, request.Now + 15, out _), "real velocity edits invalidate the receipt");
                False(receipt.CanApply(source, propulsion, count - 1, request.Now, out _), "lower maximum still prevents oversize application");
                False(receipt.CanApply(source, propulsion, 6, receipt.Nodes[0].Ut - receipt.Nodes[0].StartOffset, out _),
                    "expired ignition still prevents application");
                var consumed = new StagedPropulsion(new[] { new BurnStage(11, propulsion.Stages[0].Engine, 150), propulsion.Stages[1] });
                False(receipt.CanApply(source, consumed, 6, request.Now, out _), "real fuel changes invalidate the receipt");

                void Drop(IList<NodeSpec> specs)
                { Replace(specs); if (specs.Count > 1) plotted.RemoveAt(plotted.Count - 1); }
                False(ManeuverPlanTransaction.Write(original, receipt.Nodes, Drop, Matches, false, out string error),
                    "a dropped command cannot be reported as applied");
                True(Matches(original) && error.Contains("retain"), "failed commits restore and explain the failure");
                void Throw(IList<NodeSpec> specs)
                { Replace(specs); if (specs.Count > 1) throw new InvalidOperationException("simulated solver failure"); }
                False(ManeuverPlanTransaction.Write(original, receipt.Nodes, Throw, Matches, false, out error),
                    "partial solver exceptions are handled");
                True(Matches(original) && error.Contains("simulated solver failure"), "solver exceptions restore and retain the actionable reason");
            }
            Console.WriteLine("AAAA double-copy terminal discrepancy: " + worstRebase.ToString("F6") + " m; all three checked routes plot unchanged.");
        }
    }
}
