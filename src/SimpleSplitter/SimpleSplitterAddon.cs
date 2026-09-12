using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace SimpleSplitter
{

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    internal sealed partial class SimpleSplitterAddon : MonoBehaviour
    {
        private sealed class UndoSnapshot
        {
            internal Guid VesselId { get; }

            internal NodeSpec Original { get; }

            internal List<NodeSpec> Generated { get; }

            internal UndoSnapshot(Guid vesselId, NodeSpec original, List<NodeSpec> generated)
            {
                VesselId = vesselId;
                Original = original;
                Generated = generated;
            }
        }

        private const string LogPrefix = "[SimpleSplitter] ";

        private const double NodeTimeTolerance = 0.01;

        private const double NodeDeltaVTolerance = 0.001;

        private Coroutine? planningCoroutine;

        private UndoSnapshot? undoSnapshot;

        private Rect panelRect = new Rect(0f, 80f, 500f, 180f);

        private bool panelPositioned;

        private bool disabled;

        private bool splitRequested;

        private bool undoRequested;

        private SplitCandidate? displayedPlan;

        private Vector2 planScroll;

        private int maximumBurns = 5;

        private readonly PlanSearchCache searchCache = new PlanSearchCache();
        private readonly Dictionary<int, SplitCandidate> choices = new Dictionary<int, SplitCandidate>();
        private SplitRequest? choiceRequest;
        private Guid choiceVesselId;
        private SplitCandidate? applyRequested;

        private bool cancelRequested;

        private SplitRequest? activeRequest;

        private GUIStyle? wrappedLabel;

        private const string InputLockName = "SimpleSplitter.Panel";

        private void Update()
        {
            Vessel activeVessel = FlightGlobals.ActiveVessel;
            if (displayedPlan != null && (activeVessel == null || undoSnapshot == null || activeVessel.id != undoSnapshot.VesselId || activeVessel.patchedConicSolver == null || !RemainingNodesMatch(activeVessel.patchedConicSolver.maneuverNodes, undoSnapshot.Generated)))
            {
                displayedPlan = null;
            }
            if (choiceRequest != null && (activeVessel == null || activeVessel.id != choiceVesselId ||
                !SourceNodeMatches(activeVessel, choiceRequest)))
            {
                choices.Clear();
                choiceRequest = null;
            }
            UpdatePanelInput();
            if (applyRequested != null)
            {
                SplitCandidate selected = applyRequested;
                applyRequested = null;
                if (planningCoroutine == null && choiceRequest != null && activeVessel != null)
                    planningCoroutine = StartCoroutine(ApplyChoice(activeVessel, choiceRequest, selected));
            }
            if (cancelRequested)
            {
                cancelRequested = false;
                if (planningCoroutine != null)
                {
                    StopCoroutine(planningCoroutine);
                }
                planningCoroutine = null;
                activeRequest = null;
                if (choiceRequest != null) choiceRequest.Progress = "Cancelled. Compare again to reuse completed counts.";
                Post("Calculation cancelled; the original node is unchanged.");
            }
            if (splitRequested)
            {
                splitRequested = false;
                BeginSplit();
            }
            if (undoRequested)
            {
                undoRequested = false;
                UndoSplit();
            }
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            PropulsionReader.Clear();
            ShutdownToolbar();
        }

        private void OnEnable()
        {
            InitializeToolbar();
        }

        private void OnDisable()
        {
            ShutdownToolbar();
        }

        private void Awake()
        {
            if (CompatibilityDetector.IsPrincipiaInstalled())
            {
                disabled = true;
                UnityEngine.Debug.LogWarning("[SimpleSplitter] Principia was detected; stock-conics splitting is disabled.");
            }
        }

        private void BeginSplit()
        {
            if (planningCoroutine != null)
            {
                Post("A split calculation is already running.");
                return;
            }
            Vessel activeVessel = FlightGlobals.ActiveVessel;
            PatchedConicSolver? patchedConicSolver = ((activeVessel == null) ? null : activeVessel.patchedConicSolver);
            if (activeVessel == null || patchedConicSolver == null)
            {
                Post("No active vessel maneuver solver is available.");
                return;
            }
            if (patchedConicSolver.maneuverNodes.Count != 1)
            {
                Post("Simple Splitter requires exactly one maneuver node.");
                return;
            }
            ManeuverNode maneuverNode = patchedConicSolver.maneuverNodes[0];
            // Loaded saves can retain an encounter solved against an older
            // source orbit. Compare both alternatives against a fresh baseline.
            patchedConicSolver.UpdateFlightPlan();
            if (!TryFindEncounter(maneuverNode.nextPatch, out CelestialBody? targetBody, out double arrivalUt) || targetBody == null)
            {
                Post("The selected node must produce a target-body encounter.");
                return;
            }
            activeRequest = null;
            planningCoroutine = StartCoroutine(PrepareSplit(activeVessel, maneuverNode, targetBody, arrivalUt, maximumBurns));
        }

        private IEnumerator PrepareSplit(Vessel vessel, ManeuverNode node, CelestialBody targetBody, double arrivalUt, int maximumBurns)
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            double originalUt = node.UT;
            Vector3d originalDeltaV = node.DeltaV;
            yield return null;
            yield return PropulsionReader.Refresh(vessel);
            if (FlightGlobals.ActiveVessel != vessel || vessel.patchedConicSolver == null || vessel.patchedConicSolver.maneuverNodes.Count != 1 || vessel.patchedConicSolver.maneuverNodes[0] != node || Math.Abs(node.UT - originalUt) > 0.01 || (node.DeltaV - originalDeltaV).magnitude > 0.001)
            {
                planningCoroutine = null;
                Post("The vessel or maneuver changed; nothing was changed.");
                yield break;
            }
            StagedPropulsion stagedPropulsion = PropulsionReader.Read(vessel, out string error);
            if (!stagedPropulsion.IsUsable)
            {
                planningCoroutine = null;
                Post(error);
                yield break;
            }
            SplitRequest request = (activeRequest = new SplitRequest(node, targetBody, arrivalUt, Planetarium.GetUniversalTime(), stagedPropulsion, maximumBurns, searchCache));
            choices.Clear();
            choiceErrors.Clear();
            choiceRequest = request;
            choiceVesselId = vessel.id;
            UnityEngine.Debug.Log("[SimpleSplitter] Comparing through " + maximumBurns + " burns; propulsion from embedded KER; cosine loss is diagnostic only.");
            foreach (BurnStage stage in stagedPropulsion.Stages)
            {
                UnityEngine.Debug.Log(string.Format("{0}Stage {1}: remaining={2:R} m/s, mass={3:R} t, thrust={4:R} kN, exhaust={5:R} m/s.", "[SimpleSplitter] ", stage.Stage, stage.DeltaV, stage.Engine.Mass, stage.Engine.Thrust, stage.Engine.ExhaustVelocity));
            }
            UnityEngine.Debug.Log(string.Format("{0}Original node: UT={1:R}, deltaV=({2:R}, {3:R}, {4:R}), magnitude={5:R} m/s, target={6}, SOI-entry UT={7:R}.", "[SimpleSplitter] ", node.UT, node.DeltaV.x, node.DeltaV.y, node.DeltaV.z, node.DeltaV.magnitude, targetBody.bodyName, arrivalUt));
            Post("Calculating thrust-aware periapsis kicks...");
            yield return RunPlanner(vessel, request);
            UnityEngine.Debug.Log(string.Format("[SimpleSplitter] Comparison finished: max={0}, safe={1}, elapsed={2:F3} s.",
                maximumBurns, choices.Count, elapsed.Elapsed.TotalSeconds));
        }

        private IEnumerator RunPlanner(Vessel vessel, SplitRequest request)
        {
            yield return null;
            PlanResult? result = null;
            var phase = System.Diagnostics.Stopwatch.StartNew();
            IEnumerator planner = PlanningWork.Run(SplitPlanner.PlanAsync(request, delegate(PlanResult value)
            {
                result = value;
            }));
            while (true)
            {
                bool flag;
                try
                {
                    flag = planner.MoveNext();
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError("[SimpleSplitter] Planner failed: " + ex);
                    planningCoroutine = null;
                    Post("The split calculation failed; the original node is unchanged.");
                    yield break;
                }
                if (!flag)
                {
                    break;
                }
                yield return planner.Current;
            }
            if (result == null)
            {
                planningCoroutine = null;
                Post("The split calculation did not complete.");
                yield break;
            }
            request.Progress = "Checking staged fuel estimate...";
            UnityEngine.Debug.Log(string.Format("[SimpleSplitter] Numerical search: {0:F3} s.", phase.Elapsed.TotalSeconds));
            yield return PropulsionReader.Refresh(vessel);
            StagedPropulsion refreshed = PropulsionReader.Read(vessel, out string error);
            if (!refreshed.IsUsable)
            {
                planningCoroutine = null;
                request.Progress = error;
                Post(error);
                yield break;
            }
            if (FlightGlobals.ActiveVessel != vessel || !PlanSearchCache.MatchesPropulsion(request.Propulsion, refreshed) || vessel.patchedConicSolver == null || vessel.patchedConicSolver.maneuverNodes.Count != 1 || Math.Abs(request.Node.UT - request.OriginalUt) > 0.01 || (request.Node.DeltaV - request.OriginalDeltaV).magnitude > 0.001)
            {
                planningCoroutine = null;
                Post("The vessel or maneuver changed while calculating; nothing was changed.");
                yield break;
            }
            phase.Restart();
            yield return PlanningWork.Run(SafePlanSearch.FindAsync(request,
                (candidate, completed) => ValidatePreview(vessel, request, candidate, completed),
                () => SourceNodeMatches(vessel, request), (count, candidate) => choices[count] = candidate));
            UnityEngine.Debug.Log(string.Format("[SimpleSplitter] Safety and redistribution: {0:F3} s.", phase.Elapsed.TotalSeconds));
            // Read only promises the snapshot captured by Refresh. A live
            // search spans seconds, during which background resource systems
            // can change it. Recompute KER here, as ApplyChoice already does,
            // then compare the actual remaining staged propulsion.
            yield return PropulsionReader.Refresh(vessel);
            refreshed = PropulsionReader.Read(vessel, out error);
            if (!refreshed.IsUsable || !PlanSearchCache.MatchesPropulsion(request.Propulsion, refreshed))
            {
                choices.Clear();
                planningCoroutine = null;
                request.Progress = "Fuel, engines or staging changed. Compare again.";
                Post(request.Progress);
                yield break;
            }
            if (!SourceNodeMatches(vessel, request))
            {
                planningCoroutine = null;
                Post("The source orbit or maneuver changed during comparison. Compare again.");
                yield break;
            }
            planningCoroutine = null;
            request.Progress = choices.Count + " safe options. Choose a row to apply. Reused " + searchCache.ReusedCounts + " burn counts.";
            if (searchCache.RefinementBudgetReached) request.Progress += " Refinement paused; Compare continues it.";
            Post(choices.Count == 0 ? "No checked option retained a safe stock encounter; the original node is restored."
                : request.Progress);
        }

        private bool ApplyCandidate(Vessel vessel, SplitRequest request, SplitCandidate candidate)
        {
            PatchedConicSolver patchedConicSolver = vessel.patchedConicSolver;
            CelestialBody referenceBody = request.SourceOrbit.referenceBody;
            NodeSpec original = new NodeSpec(request.OriginalUt, request.OriginalDeltaV);
            List<NodeSpec> list = CopySpecs(candidate.Nodes);
            if (list.Count > request.MaximumBurns)
            {
                return false;
            }
            if (list[0].Ut - list[0].StartOffset <= Planetarium.GetUniversalTime() + 30.0)
            {
                return false;
            }
            try
            {
                patchedConicSolver.UpdateFlightPlan();
                if (!TryFindEncounter(patchedConicSolver.maneuverNodes[0].nextPatch,
                    out CelestialBody? liveTarget, out double referenceArrival) || liveTarget != request.TargetBody)
                    return false;
                RemoveAllNodes(patchedConicSolver);
                AddNodes(patchedConicSolver, list);
                patchedConicSolver.UpdateFlightPlan();
                if (!ValidateCommittedPlan(patchedConicSolver, referenceBody, request.TargetBody, request.OriginalUt, referenceArrival, list.Count, out double actualArrivalUt, out string error))
                {
                    RestoreSingleNode(patchedConicSolver, original);
                    UnityEngine.Debug.Log("[SimpleSplitter] Candidate rejected: " + error);
                    return false;
                }
                undoSnapshot = new UndoSnapshot(vessel.id, original, list);
                displayedPlan = candidate;
                planScroll = Vector2.zero;
                double num = actualArrivalUt - referenceArrival;
                double num2 = CandidateRules.ArrivalToleranceSeconds;
                UnityEngine.Debug.Log(string.Format("{0}Accepted encounter: target={1}, original SOI-entry UT={2:R}, new SOI-entry UT={3:R}, difference={4:R} s, tolerance={5:R} s.", "[SimpleSplitter] ", request.TargetBody.bodyName, referenceArrival, actualArrivalUt, num, num2));
                for (int i = 0; i < list.Count; i++)
                {
                    NodeSpec nodeSpec = list[i];
                    UnityEngine.Debug.Log(string.Format("{0}Generated node {1}: UT={2:R}, deltaV=({3:R}, {4:R}, {5:R}), magnitude={6:R} m/s, duration={7:R} s, start UT={8:R} ({9}).", "[SimpleSplitter] ", i + 1, nodeSpec.Ut, nodeSpec.DeltaV.x, nodeSpec.DeltaV.y, nodeSpec.DeltaV.z, nodeSpec.DeltaV.magnitude, nodeSpec.Duration, nodeSpec.Ut - nodeSpec.StartOffset, nodeSpec.Purpose));
                }
                UnityEngine.Debug.Log(string.Format("{0}Setup={1:R} m/s, maximum setup loss={2:R}; departure cosine loss={3:R}, energy loss={4:R}, local position error={5:R} m, local velocity error={6:R} m/s.", "[SimpleSplitter] ", candidate.SetupDeltaV, candidate.MaximumSetupLoss, candidate.DepartureEstimate.CosineLoss, candidate.DepartureEstimate.EnergyLoss, candidate.DepartureEstimate.PositionError, candidate.DepartureEstimate.VelocityError));
                UnityEngine.Debug.Log(string.Format("{0}Full-sequence finite execution estimate: valid={1}, position error={2:R} m, velocity error={3:R} m/s.", "[SimpleSplitter] ", candidate.ExecutionEstimate.IsFinite, candidate.ExecutionEstimate.PositionError, candidate.ExecutionEstimate.VelocityError));
                Post(string.Format("Split into {0} nodes; largest timed burn {1:F1} m/s, estimated total {2:F1} m/s, nominal arrival {3}{4:F1} s.", list.Count, candidate.Score.LargestBurn, candidate.Score.TotalDeltaV, (num >= 0.0) ? "+" : string.Empty, num));
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[SimpleSplitter] Failed to apply split: " + ex);
                try
                {
                    RestoreSingleNode(patchedConicSolver, original);
                }
                catch (Exception ex2)
                {
                    UnityEngine.Debug.LogError("[SimpleSplitter] Failed to restore the original node: " + ex2);
                }
                return false;
            }
        }

        private void UndoSplit()
        {
            if (planningCoroutine != null)
            {
                Post("Wait for the current split calculation to finish.");
                return;
            }
            Vessel activeVessel = FlightGlobals.ActiveVessel;
            if (undoSnapshot == null || activeVessel == null || activeVessel.id != undoSnapshot.VesselId || activeVessel.patchedConicSolver == null)
            {
                Post("There is no Simple Splitter operation to undo for this vessel.");
                return;
            }
            PatchedConicSolver patchedConicSolver = activeVessel.patchedConicSolver;
            if (!NodesMatch(patchedConicSolver.maneuverNodes, undoSnapshot.Generated))
            {
                Post("Undo refused because the generated maneuver nodes were changed.");
                return;
            }
            try
            {
                RestoreSingleNode(patchedConicSolver, undoSnapshot.Original);
                undoSnapshot = null;
                displayedPlan = null;
                Post("Restored the original maneuver node.");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[SimpleSplitter] Undo failed: " + ex);
                Post("Unable to restore the original maneuver node.");
            }
        }

        private static bool ValidateCommittedPlan(PatchedConicSolver solver, CelestialBody sourceBody, CelestialBody targetBody, double originalNodeUt, double originalArrivalUt, int expectedNodeCount, out double actualArrivalUt, out string error)
        {
            actualArrivalUt = double.NaN;
            error = string.Empty;
            if (solver.maneuverNodes.Count != expectedNodeCount)
            {
                error = "KSP did not retain every generated node.";
                return false;
            }
            for (int i = 0; i < solver.maneuverNodes.Count - 1; i++)
            {
                ManeuverNode maneuverNode = solver.maneuverNodes[i];
                if (maneuverNode.nextPatch == null || maneuverNode.nextPatch.referenceBody != sourceBody || maneuverNode.nextPatch.patchEndTransition != Orbit.PatchTransitionType.MANEUVER)
                {
                    error = "an intermediate orbit escaped, impacted, or encountered another body.";
                    return false;
                }
            }
            if (!TryFindEncounter(solver.maneuverNodes[solver.maneuverNodes.Count - 1].nextPatch, out CelestialBody? targetBody2, out actualArrivalUt) || targetBody2 != targetBody)
            {
                error = "the resulting flight plan does not encounter the original target.";
                return false;
            }
            if (!CandidateRules.ArrivalIsWithinTolerance(originalNodeUt, originalArrivalUt, actualArrivalUt))
            {
                error = "the resulting encounter is more than one day from the original arrival (" +
                    (actualArrivalUt - originalArrivalUt).ToString("+0;-0;0") + " s).";
                return false;
            }
            return true;
        }

        private static bool TryFindEncounter(Orbit? firstPatch, out CelestialBody? targetBody, out double arrivalUt)
        {
            targetBody = null;
            arrivalUt = double.NaN;
            Orbit? orbit = firstPatch;
            int num = 0;
            while (orbit != null && num < 64)
            {
                if (orbit.patchStartTransition == Orbit.PatchTransitionType.ENCOUNTER)
                {
                    targetBody = orbit.referenceBody;
                    arrivalUt = orbit.StartUT;
                    if (targetBody != null)
                    {
                        return CandidateRules.IsFinite(arrivalUt);
                    }
                    return false;
                }
                if (orbit.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER && orbit.nextPatch != null)
                {
                    targetBody = orbit.nextPatch.referenceBody;
                    arrivalUt = orbit.nextPatch.StartUT;
                    if (targetBody != null)
                    {
                        return CandidateRules.IsFinite(arrivalUt);
                    }
                    return false;
                }
                orbit = orbit.nextPatch;
                num++;
            }
            return false;
        }

        private static void RestoreSingleNode(PatchedConicSolver solver, NodeSpec original)
        {
            RemoveAllNodes(solver);
            AddNodes(solver, new List<NodeSpec> { original });
            solver.UpdateFlightPlan();
        }

        private static void AddNodes(PatchedConicSolver solver, IList<NodeSpec> specs)
        {
            for (int i = 0; i < specs.Count; i++)
            {
                solver.AddManeuverNode(specs[i].Ut).DeltaV = specs[i].DeltaV;
                // AddManeuverNode already updates the preceding nodes. The
                // caller updates once more after assigning the final delta-v.
            }
        }

        // Stock cleanup also closes the maneuver gizmo and map target.
        private static void RemoveAllNodes(PatchedConicSolver solver)
        {
            while (solver.maneuverNodes.Count > 0)
            {
                solver.maneuverNodes[solver.maneuverNodes.Count - 1].RemoveSelf();
            }
        }

        private static bool NodesMatch(IList<ManeuverNode> actual, IList<NodeSpec> expected)
        {
            if (actual.Count != expected.Count)
            {
                return false;
            }
            for (int i = 0; i < actual.Count; i++)
            {
                if (Math.Abs(actual[i].UT - expected[i].Ut) > 0.01 || (actual[i].DeltaV - expected[i].DeltaV).magnitude > 0.001)
                {
                    return false;
                }
            }
            return true;
        }

        // Completed nodes may be removed without losing the remaining burn timers.
        private static bool RemainingNodesMatch(IList<ManeuverNode> actual, IList<NodeSpec> generated)
        {
            if (actual.Count == 0 || actual.Count > generated.Count)
            {
                return false;
            }
            int num = generated.Count - actual.Count;
            for (int i = 0; i < actual.Count; i++)
            {
                if (Math.Abs(actual[i].UT - generated[num + i].Ut) > 0.01 || (actual[i].DeltaV - generated[num + i].DeltaV).magnitude > 0.001)
                {
                    return false;
                }
            }
            return true;
        }

        private static List<NodeSpec> CopySpecs(IList<NodeSpec> source)
        {
            List<NodeSpec> list = new List<NodeSpec>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                NodeSpec nodeSpec = source[i];
                list.Add(new NodeSpec(nodeSpec.Ut, nodeSpec.DeltaV, nodeSpec.Duration, nodeSpec.StartOffset, nodeSpec.Purpose, nodeSpec.BurnDeltaV));
            }
            return list;
        }

        private static void Post(string message)
        {
            UnityEngine.Debug.Log("[SimpleSplitter] " + message);
            ScreenMessages.PostScreenMessage(message, 6f, ScreenMessageStyle.UPPER_CENTER);
        }
    }
}
