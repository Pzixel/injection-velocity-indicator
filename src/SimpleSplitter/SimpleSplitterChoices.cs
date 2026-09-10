using System;
using System.Collections;
using System.Collections.Generic;

namespace SimpleSplitter
{
    internal sealed partial class SimpleSplitterAddon
    {
        private readonly Dictionary<int, string> choiceErrors = new Dictionary<int, string>();

        private static bool SourceNodeMatches(Vessel vessel, SplitRequest request)
        {
            if (!(FlightGlobals.ActiveVessel == vessel && vessel != null && vessel.patchedConicSolver != null &&
                NodesMatch(vessel.patchedConicSolver.maneuverNodes,
                    new[] { new NodeSpec(request.OriginalUt, request.OriginalDeltaV) }))) return false;
            Orbit current = vessel.patchedConicSolver.maneuverNodes[0].patch;
            if (current == null || current.referenceBody != request.SourceOrbit.referenceBody) return false;
            current.GetOrbitalStateVectorsAtUT(request.OriginalUt, out Vector3d r, out Vector3d v);
            request.SourceOrbit.GetOrbitalStateVectorsAtUT(request.OriginalUt, out Vector3d expectedR, out Vector3d expectedV);
            return (r - expectedR).magnitude < 1e-3 && (v - expectedV).magnitude < 1e-6;
        }

        private IEnumerator ValidatePreview(Vessel vessel, SplitRequest request, SplitCandidate candidate, Action<bool> completed)
        {
            // Every synchronous prefix check restores the original in finally.
            // Cancellation, scene switches, and user edits at any yield therefore
            // never leave a partially constructed preview in the flight plan.
            for (int prefix = candidate.Nodes.Count; prefix >= 1; prefix--)
            {
                if (!SourceNodeMatches(vessel, request) ||
                    candidate.Nodes[0].Ut - candidate.Nodes[0].StartOffset <= Planetarium.GetUniversalTime() + 30)
                { completed(false); yield break; }
                request.Progress = "Checking " + candidate.Nodes.Count + " burns: coast after burn " + prefix + "...";
                if (!CheckPrefix(vessel.patchedConicSolver, request, candidate, prefix, out string error))
                {
                    choiceErrors[candidate.Nodes.Count] = error;
                    UnityEngine.Debug.Log("[SimpleSplitter] " + candidate.Nodes.Count + "-burn option rejected: " + error);
                    completed(false);
                    yield break;
                }
                yield return null;
            }
            bool finiteSafe = false;
            yield return ValidateFinitePath(vessel, request, candidate, value => finiteSafe = value);
            completed(finiteSafe);
        }

        private static bool CheckPrefix(PatchedConicSolver solver, SplitRequest request, SplitCandidate candidate,
            int prefix, out string error)
        {
            error = string.Empty;
            try
            {
                RemoveAllNodes(solver);
                AddNodes(solver, candidate.Nodes.GetRange(0, prefix));
                solver.UpdateFlightPlan();
                if (solver.maneuverNodes.Count != prefix)
                { error = "KSP dropped a preview node."; return false; }
                CelestialBody source = request.SourceOrbit.referenceBody;
                // Include the waiting orbit before the very first setup burn.
                if (solver.flightPlan.Count == 0 || !StockTrajectorySafety.CoastIsSafe(
                    solver.flightPlan[0], source, Planetarium.GetUniversalTime(), candidate.Nodes[0].Ut, out error)) return false;
                if (prefix == candidate.Nodes.Count)
                {
                    if (!ValidateCommittedPlan(solver, source, request.TargetBody, request.OriginalUt,
                        request.ArrivalUt, prefix, out double arrival, out error)) return false;
                    Orbit? patch = solver.maneuverNodes[prefix - 1].nextPatch;
                    for (int i = 0; patch != null && i < 64; i++, patch = patch.nextPatch)
                    {
                        if (patch.referenceBody == request.TargetBody && patch.patchStartTransition == Orbit.PatchTransitionType.ENCOUNTER)
                            return true;
                        double end = Math.Min(patch.EndUT, arrival);
                        if (!CandidateRules.IsFinite(end) || end < patch.StartUT ||
                            !StockTrajectorySafety.AboveAtmosphere(patch, patch.StartUT, end))
                        { error = "Departure path intersects a surface or atmosphere."; return false; }
                        if (patch.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER &&
                            (patch.nextPatch == null || patch.nextPatch.referenceBody != request.TargetBody))
                        { error = "Departure encounters an unintended body."; return false; }
                    }
                    error = "Target encounter was not fully solved.";
                    return false;
                }
                return StockTrajectorySafety.CoastIsSafe(solver.maneuverNodes[prefix - 1].nextPatch,
                    source, candidate.Nodes[prefix - 1].Ut, candidate.Nodes[prefix].Ut, out error);
            }
            catch (Exception exception)
            {
                error = "Stock trajectory check failed: " + exception.Message;
                return false;
            }
            finally
            {
                RestoreSingleNode(solver, new NodeSpec(request.OriginalUt, request.OriginalDeltaV));
            }
        }

        private IEnumerator ApplyChoice(Vessel vessel, SplitRequest request, SplitCandidate candidate)
        {
            yield return null;
            yield return WaitForStockPropulsion(vessel);
            if (!SourceNodeMatches(vessel, request) || candidate.Nodes.Count > maximumBurns ||
                !PlanSearchCache.MatchesPropulsion(request.Propulsion, PropulsionReader.Read(vessel, out _)))
            {
                planningCoroutine = null;
                Post("The vessel, fuel or maneuver changed. Compare plans again.");
                yield break;
            }
            activeRequest = request;
            bool safe = false;
            yield return ValidatePreview(vessel, request, candidate, value => safe = value);
            yield return WaitForStockPropulsion(vessel);
            if (safe && SourceNodeMatches(vessel, request) &&
                PlanSearchCache.MatchesPropulsion(request.Propulsion, PropulsionReader.Read(vessel, out _)))
                ApplyCandidate(vessel, request, candidate);
            else
            {
                choices.Remove(candidate.Nodes.Count);
                searchCache.StockSafety.Remove(candidate);
                Post("The option is no longer safe or the vessel changed. Compare plans again.");
            }
            planningCoroutine = null;
        }
    }
}
