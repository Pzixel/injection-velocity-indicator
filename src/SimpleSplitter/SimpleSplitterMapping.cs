using System;
using System.Collections.Generic;

namespace SimpleSplitter
{
    internal sealed partial class SimpleSplitterAddon
    {
        private int mappedCompleted;

        // A finite burn ends on its true coast, not on the equivalent impulse's
        // approximate coast. Refit the remaining map once that burn is removed,
        // retaining the original fixed headings, ignition times and fuel budget.
        private void RefreshRemainingMapping(Vessel vessel)
        {
            ValidatedPlan? validation = displayedPlan?.Validation;
            if (validation == null || !displayedPlan!.IsConverted || undoSnapshot == null || vessel == null ||
                vessel.id != undoSnapshot.VesselId || vessel.patchedConicSolver == null) return;
            var solver = vessel.patchedConicSolver;
            int completed = validation.Commands.Count - solver.maneuverNodes.Count;
            if (completed <= mappedCompleted || completed >= validation.Commands.Count ||
                !RemainingNodesMatch(solver.maneuverNodes, undoSnapshot.Generated)) return;
            NodeSpec previous = validation.Commands[completed - 1];
            if (Planetarium.GetUniversalTime() < previous.Ut - previous.StartOffset + previous.Duration)
            {
                // Deleting a future burn is a user edit, not its execution.
                displayedPlan = null;
                return;
            }
            var original = undoSnapshot.Generated.GetRange(completed, solver.maneuverNodes.Count);
            try
            {
                List<NodeSpec> mapped = validation.Remap(vessel.orbit, completed);
                if (!ManeuverPlanTransaction.Write(original, mapped, specs => ReplaceNodes(solver, specs),
                    specs => NodesMatch(solver.maneuverNodes, specs), false, out string error))
                    throw new InvalidOperationException(error);
                for (int i = 0; i < mapped.Count; i++) undoSnapshot.Generated[completed + i] = mapped[i];
                UnityEngine.Debug.Log("[SimpleSplitter] Refreshed " + mapped.Count + " effective nodes after completed burn " + completed + ".");
            }
            catch (Exception exception)
            {
                // Retain the old commands if a solver write failed; never
                // repeat a failing write every frame or discard the timers.
                UnityEngine.Debug.LogError("[SimpleSplitter] Remaining map refresh failed: " + exception);
                Post("Unable to refresh the remaining map: " + exception.Message);
            }
            mappedCompleted = completed;
        }
    }
}
