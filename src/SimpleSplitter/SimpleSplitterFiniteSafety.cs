using System;
using System.Collections;

namespace SimpleSplitter
{
    internal sealed partial class SimpleSplitterAddon
    {
        private IEnumerator ValidateFinitePath(Vessel vessel, SplitRequest request, SplitCandidate candidate, Orbit liveSource, Action<bool> completed)
        {
            candidate.TimedArrivalOffsetSeconds = double.NaN;
            Orbit nominal = liveSource, executed = liveSource;
            StageCursor engine = request.Propulsion.Cursor();
            for (int i = 0; i < candidate.Nodes.Count; i++)
            {
                if (!SourceNodeMatches(vessel, request)) { completed(false); yield break; }
                NodeSpec node = candidate.Nodes[i];
                string error = string.Empty;
                bool safe = false;
                request.Progress = "Checking timed execution: " + candidate.Nodes.Count + " burns, burn " + (i + 1) + "...";
                try
                {
                    nominal.GetFixedState(node.Ut, out Vector3d r, out Vector3d v);
                    Orbit after = SplitPlanner.OrbitFromState(r,
                        v + (SplitPlanner.NodeRotation(nominal, node.Ut) * node.DeltaV).xzy, nominal.referenceBody, node.Ut);
                    CelestialBody source = nominal.referenceBody;
                    // Only replays selected finalists. Safety samples don't add
                    // moon propagation to every numerical optimization trial.
                    FiniteBurnEstimate estimate = FiniteBurnEstimate.Measure(nominal, after, node, engine.Engine, 0,
                        executed, out executed, (position, ut) => OutsideMoonSois(source, position, ut));
                    if (!estimate.IsFinite || !engine.Consume(node.BurnDeltaV))
                        error = "Timed burn intersects a surface, atmosphere or SOI, or exceeds stage fuel.";
                    else
                    {
                        double end = node.Ut + node.Duration - node.StartOffset;
                        double until = i + 1 < candidate.Nodes.Count
                            ? candidate.Nodes[i + 1].Ut - candidate.Nodes[i + 1].StartOffset
                            : request.ArrivalUt + CandidateRules.ArrivalToleranceSeconds;
                        safe = CheckFiniteCoast(executed, end, until, i + 1 == candidate.Nodes.Count,
                            request, vessel.patchedConicSolver, out error, out double arrivalUt);
                        if (safe && i + 1 == candidate.Nodes.Count)
                            candidate.TimedArrivalOffsetSeconds = arrivalUt - request.ArrivalUt;
                    }
                    nominal = after;
                }
                catch (Exception exception) { error = "Timed trajectory check failed: " + exception.Message; }
                if (!safe)
                {
                    choiceErrors[candidate.Nodes.Count] = error;
                    UnityEngine.Debug.Log("[SimpleSplitter] " + candidate.Nodes.Count + "-burn timed option rejected: " + error);
                    completed(false);
                    yield break;
                }
                yield return null;
            }
            completed(true);
        }

        private static bool OutsideMoonSois(CelestialBody source, Vector3d position, double ut)
        {
            foreach (CelestialBody moon in source.orbitingBodies)
            {
                moon.orbit.GetFixedState(ut, out Vector3d moonPosition, out _);
                if ((position - moonPosition).magnitude <= moon.sphereOfInfluence) return false;
            }
            return true;
        }

        private static bool CheckFiniteCoast(Orbit orbit, double start, double until, bool departure,
            SplitRequest request, PatchedConicSolver solver, out string error, out double arrivalUt)
        {
            error = string.Empty;
            arrivalUt = double.NaN;
            string trace = string.Empty;
            var parameters = new PatchedConics.SolverParameters
            {
                maxGeometrySolverIterations = solver.maxGeometrySolverIterations,
                maxTimeSolverIterations = solver.maxTimeSolverIterations,
                FollowManeuvers = false
            };
            // Work on independent copies. The real flight plan stays untouched.
            for (int i = 0; i < 64 && start < until; i++)
            {
                orbit.GetFixedState(start, out Vector3d r, out Vector3d v);
                Orbit patch = SplitPlanner.OrbitFromState(r, v, orbit.referenceBody, start);
                patch.StartUT = start;
                // Match KSP's initial search span. It bounds CLOSEST APPROACH,
                // which may occur well after the SOI entry we need to check.
                // Clipping this to `until` misses an encounter whose entry is
                // before the next burn/arrival deadline but closest approach
                // is later. Clip the resulting transition below instead.
                patch.EndUT = patch.eccentricity < 1.0 ? start + patch.period : double.PositiveInfinity;
                Orbit next = new Orbit();
                bool transition = PatchedConics.CalculatePatch(patch, next, start, parameters, request.TargetBody);
                if (departure) trace += string.Format(" {0}:{1:R}->{2:R} {3} next={4};",
                    patch.referenceBody.bodyName, start, patch.EndUT, patch.patchEndTransition,
                    next.referenceBody == null ? "none" : next.referenceBody.bodyName);
                double end = Math.Min(until, patch.EndUT);
                if (!CandidateRules.IsFinite(end) || end <= start || !StockTrajectorySafety.AboveAtmosphere(patch, start, end))
                { error = "Timed coast intersects a surface/atmosphere or could not be solved."; return false; }
                if (patch.EndUT <= until && patch.patchEndTransition != Orbit.PatchTransitionType.FINAL)
                {
                    if (!departure || (patch.patchEndTransition != Orbit.PatchTransitionType.ESCAPE &&
                        patch.patchEndTransition != Orbit.PatchTransitionType.ENCOUNTER))
                    { error = "Timed intermediate coast impacts or changes SOI."; return false; }
                    if (patch.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER)
                    {
                        if (next.referenceBody == request.TargetBody)
                        {
                            if (!CandidateRules.ArrivalIsWithinTolerance(request.OriginalUt, request.ArrivalUt, next.StartUT))
                            { error = "Timed target encounter is outside the arrival window (" +
                                (next.StartUT - request.ArrivalUt).ToString("+0;-0;0") + " s)."; return false; }
                            UnityEngine.Debug.Log(string.Format("[SimpleSplitter] Timed encounter: target={0}, SOI-entry UT={1:R}, difference={2:R} s.",
                                request.TargetBody.bodyName, next.StartUT, next.StartUT - request.ArrivalUt));
                            arrivalUt = next.StartUT;
                            return true;
                        }
                        error = "Timed departure encounters an unintended body.";
                        return false;
                    }
                    if (!transition || next.referenceBody == null)
                    { error = "Timed SOI transition could not be solved."; return false; }
                    orbit = next;
                }
                else orbit = patch;
                start = end;
            }
            if (start >= until)
            {
                if (!departure) return true;
                error = "Timed departure does not reach the target within the arrival window.";
                UnityEngine.Debug.Log("[SimpleSplitter] Coast trace:" + trace);
                return false;
            }
            error = "Timed coast exceeded the patch-check limit.";
            return false;
        }
    }
}
