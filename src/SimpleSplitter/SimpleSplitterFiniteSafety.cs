using System;
using System.Collections;

namespace SimpleSplitter
{
    internal sealed partial class SimpleSplitterAddon
    {
        private IEnumerator ValidateFinitePath(Vessel vessel, SplitRequest request, SplitCandidate candidate, Action<bool> completed)
        {
            Orbit nominal = request.SourceOrbit, executed = request.SourceOrbit;
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
                    nominal.GetOrbitalStateVectorsAtUT(node.Ut, out Vector3d r, out Vector3d v);
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
                            ? candidate.Nodes[i + 1].Ut - candidate.Nodes[i + 1].StartOffset : request.ArrivalUt;
                        safe = CheckFiniteCoast(executed, end, until, i + 1 == candidate.Nodes.Count,
                            request.TargetBody, vessel.patchedConicSolver, out error);
                    }
                    nominal = after;
                }
                catch (Exception exception) { error = "Timed trajectory check failed: " + exception.Message; }
                if (!safe)
                {
                    choiceErrors[candidate.Nodes.Count] = error;
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
                moon.orbit.GetOrbitalStateVectorsAtUT(ut, out Vector3d moonPosition, out _);
                if ((position - moonPosition).magnitude <= moon.sphereOfInfluence) return false;
            }
            return true;
        }

        private static bool CheckFiniteCoast(Orbit orbit, double start, double until, bool departure,
            CelestialBody target, PatchedConicSolver solver, out string error)
        {
            error = string.Empty;
            var parameters = new PatchedConics.SolverParameters
            {
                maxGeometrySolverIterations = solver.maxGeometrySolverIterations,
                maxTimeSolverIterations = solver.maxTimeSolverIterations,
                FollowManeuvers = false
            };
            // Work on independent copies. The real flight plan stays untouched.
            for (int i = 0; i < 64 && start < until; i++)
            {
                orbit.GetOrbitalStateVectorsAtUT(start, out Vector3d r, out Vector3d v);
                Orbit patch = SplitPlanner.OrbitFromState(r, v, orbit.referenceBody, start);
                patch.StartUT = start;
                Orbit next = new Orbit();
                bool transition = PatchedConics.CalculatePatch(patch, next, start, parameters, target);
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
                        if (next.referenceBody == target) return true;
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
            if (start >= until) return true;
            error = "Timed coast exceeded the patch-check limit.";
            return false;
        }
    }
}
