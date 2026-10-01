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
            // Guard user intent, not a frozen state vector. Coasting drift is
            // not a node edit. Cache identity still compares orbital inputs;
            // acceptance rebuilds and checks the live trajectory.
            return current != null && current.referenceBody == request.SourceOrbit.referenceBody;
        }

        private IEnumerator ValidatePreview(Vessel vessel, SplitRequest request, SplitCandidate candidate, Action<bool> completed)
        {
            if (!SourceNodeMatches(vessel, request)) { completed(false); yield break; }
            vessel.patchedConicSolver.UpdateFlightPlan();
            ManeuverNode original = vessel.patchedConicSolver.maneuverNodes[0];
            if (!TryFindEncounter(original.nextPatch, out CelestialBody? target, out double arrival) || target != request.TargetBody)
            { completed(false); yield break; }
            // Compare with what the unchanged original node predicts NOW.
            // Both alternatives must use the same live orbital reference.
            SplitRequest searchRequest = request;
            Action<bool> report = completed;
            completed = value => { activeRequest = searchRequest; report(value); };
            request = new SplitRequest(original, request.TargetBody, arrival,
                Planetarium.GetUniversalTime(), request.Propulsion, request.MaximumBurns, request.Cache);
            activeRequest = request;
            request.Progress = "Refreshing " + candidate.Nodes.Count + " burns against the live orbit...";
            SplitCandidate? refreshed = null;
            yield return SplitPlanner.RefreshAsync(request, candidate, value => refreshed = value);
            if (refreshed == null || !SourceNodeMatches(vessel, request)) { completed(false); yield break; }
            candidate.LiveCandidate = refreshed;
            candidate = refreshed;
            // Refresh captured the live orbit once for both conversion and its
            // target. Replaying a second copy reconstructed at Now introduces
            // a different rounding history over months of resonant coasts.
            // Reject a failed timed execution before constructing stock nodes.
            // Validate the physical execution before fitting its stock map.
            bool finiteSafe = false;
            yield return ValidateFinitePath(vessel, request, candidate, request.SourceOrbit, value => finiteSafe = value);
            if (!finiteSafe || !SourceNodeMatches(vessel, request) ||
                candidate.Nodes[0].Ut - candidate.Nodes[0].StartOffset <= Planetarium.GetUniversalTime() + 30)
            { completed(false); yield break; }
            if (candidate.IsConverted)
            {
                ValidatedPlan? validation = null;
                string error = string.Empty;
                try { validation = new ValidatedPlan(request, candidate); }
                catch (Exception exception) { error = "Unable to map the finite trajectory: " + exception.Message; }
                bool plotted = validation != null && CheckCommandNodes(vessel.patchedConicSolver, request, validation, out error);
                if (plotted) candidate.Validation = validation;
                else ReportChoiceError(candidate, error);
                completed(plotted);
                yield break;
            }
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
            candidate.Validation = new ValidatedPlan(request, candidate);
            completed(true);
        }

        private void ReportChoiceError(SplitCandidate candidate, string error)
        {
            choiceErrors[candidate.Nodes.Count] = error;
            UnityEngine.Debug.Log("[SimpleSplitter] " + candidate.Nodes.Count + "-burn option rejected: " + error);
        }

        private static bool CheckCommandNodes(PatchedConicSolver solver, SplitRequest request, ValidatedPlan validation, out string error)
        {
            string trajectoryError = string.Empty;
            bool trajectoryValid = false;
            bool Checked(IList<NodeSpec> specs)
            {
                if (!NodesMatch(solver.maneuverNodes, specs)) return false;
                // The transaction also verifies its one-node restoration.
                if (specs.Count == 1) return true;
                trajectoryValid = ValidateCommittedPlan(solver, request.SourceOrbit.referenceBody, request.TargetBody,
                    request.OriginalUt, request.ArrivalUt, specs.Count, out _, out trajectoryError);
                return true;
            }
            bool result = ManeuverPlanTransaction.Write(new[] { new NodeSpec(request.OriginalUt, request.OriginalDeltaV) }, validation.Nodes,
                specs => ReplaceNodes(solver, specs), Checked, true, out error);
            if (result && !trajectoryValid) error = "The mapped stock trajectory failed validation: " + trajectoryError;
            return result && trajectoryValid;
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
            try
            {
                yield return null;
                yield return PropulsionReader.Refresh(vessel);
                StagedPropulsion propulsion = PropulsionReader.Read(vessel, out string propulsionError);
                ValidatedPlan? validation = candidate.Validation;
                string error = "This route has not completed validation. Compare plans again.";
                if (!SourceNodeMatches(vessel, request))
                { Post("The vessel or original maneuver changed. Compare plans again."); yield break; }
                if (!propulsion.IsUsable) { Post(propulsionError); yield break; }
                if (validation == null || !validation.CanApply(vessel.patchedConicSolver.maneuverNodes[0].patch,
                    propulsion, maximumBurns, Planetarium.GetUniversalTime(), out error))
                { Post(error); yield break; }
                // Commit exactly what this table row already checked. Re-running
                // RefreshAsync here would solve a different numerical problem
                // and can discard a valid route due to harmless input jitter.
                if (!ApplyCandidate(vessel, candidate, out error)) Post(error);
            }
            finally
            {
                planningCoroutine = null;
                activeRequest = null;
            }
        }
    }
}
