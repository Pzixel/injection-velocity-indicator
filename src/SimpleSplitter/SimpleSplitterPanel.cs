using System;
using UnityEngine;

namespace SimpleSplitter
{
    internal sealed partial class SimpleSplitterAddon
    {
        private bool panelCollapsed;
        private GUIStyle? collapsedTitle;
        private GUIStyle? numberLabel;
        private GUIStyle? headingLabel;

        private bool PanelVisible => panelOpen && gameUiVisible &&
            HighLogic.LoadedSceneIsFlight && MapView.MapIsEnabled;

        private static ManeuverGizmo? SelectedGizmo
        {
            get
            {
                if (!HighLogic.LoadedSceneIsFlight || !MapView.MapIsEnabled) return null;
                Vessel vessel = FlightGlobals.ActiveVessel;
                if (vessel == null || vessel.patchedConicSolver == null) return null;
                // Stock selection attaches the gizmo; deselection detaches it.
                // Read live state rather than retaining a previously selected node.
                foreach (ManeuverNode node in vessel.patchedConicSolver.maneuverNodes)
                    if (node.attachedGizmo != null && node.attachedGizmo.gameObject.activeInHierarchy)
                        return node.attachedGizmo;
                return null;
            }
        }

        private void OnGUI()
        {
            if (!PanelVisible)
            {
                ClearPanelInput();
                return;
            }
            // GUI.Window keeps these dimensions. Automatic GUILayout resizing
            // previously fought the height reset during scroll/layout/repaint.
            panelRect.width = Math.Min(panelCollapsed ? 220f : choiceRequest != null && displayedPlan == null ? 760f : 500f, Screen.width);
            panelRect.height = Math.Min(panelCollapsed ? 28f : displayedPlan != null ? 450f : choiceRequest != null ? 252f + (maximumBurns - 1) * 30f : 180f, Screen.height);
            if (!panelPositioned)
            {
                panelRect.x = (Screen.width - panelRect.width) * 0.5f;
                panelPositioned = true;
            }
            panelRect.x = Mathf.Clamp(panelRect.x, 0, Math.Max(0, Screen.width - panelRect.width));
            panelRect.y = Mathf.Clamp(panelRect.y, 0, Math.Max(0, Screen.height - panelRect.height));
            panelRect = GUI.Window(GetInstanceID(), panelRect, DrawPanel,
                panelCollapsed ? string.Empty : "Simple Splitter");
            if (Event.current.type == EventType.ScrollWheel && panelRect.Contains(Event.current.mousePosition))
                Event.current.Use();
        }

        private void DrawPanel(int windowId)
        {
            if (GUI.Button(new Rect(panelRect.width - 30, 3, 24, 21), new GUIContent("×", "Close")))
            {
                ClosePanel();
                return;
            }
            if (GUI.Button(new Rect(panelRect.width - 58, 3, 24, 21),
                new GUIContent(panelCollapsed ? "+" : "−", panelCollapsed ? "Expand" : "Collapse")))
            {
                panelCollapsed = !panelCollapsed;
                return;
            }
            if (panelCollapsed)
            {
                // A short window can clip the skin's built-in title. Draw a
                // label inside the bar, reserving room for the expand button.
                if (collapsedTitle == null)
                    collapsedTitle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        fontStyle = FontStyle.Bold,
                        wordWrap = false
                    };
                GUI.Label(new Rect(10, 3, panelRect.width - 72, 22), "Simple Splitter", collapsedTitle);
                GUI.DragWindow(new Rect(0, 0, panelRect.width - 62, 24));
                return;
            }
            if (wrappedLabel == null) wrappedLabel = new GUIStyle(GUI.skin.label) { wordWrap = true };
            if (numberLabel == null) numberLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };
            if (headingLabel == null) headingLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            float width = panelRect.width - 24;
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !disabled && planningCoroutine == null;
            GUI.Label(new Rect(12, 30, 120, 24), new GUIContent("Max burns", "2–10, including setup, plane change, and departure."));
            maximumBurns = Mathf.Clamp(Mathf.RoundToInt(GUI.HorizontalSlider(
                new Rect(140, 38, width - 190, 18), maximumBurns, 2, 10)), 2, 10);
            GUI.Label(new Rect(width - 40, 30, 52, 24), maximumBurns.ToString(), numberLabel);
            Vessel vessel = FlightGlobals.ActiveVessel;
            int nodeCount = vessel != null && vessel.patchedConicSolver != null
                ? vessel.patchedConicSolver.maneuverNodes.Count : 0;
            GUI.enabled = previousEnabled && !disabled && (planningCoroutine != null || nodeCount == 1);
            if (GUI.Button(new Rect(12, 66, width * .66f - 4, 28),
                planningCoroutine == null ? "Compare plans" : "Cancel calculation"))
            {
                if (planningCoroutine == null) splitRequested = true;
                else cancelRequested = true;
            }
            GUI.enabled = previousEnabled && planningCoroutine == null && undoSnapshot != null;
            if (GUI.Button(new Rect(12 + width * .66f + 4, 66, width * .34f - 4, 28), "Undo split"))
                undoRequested = true;
            GUI.enabled = previousEnabled;
            if (displayedPlan == null)
            {
                string status = disabled ? "Disabled: Principia detected." : planningCoroutine != null
                    ? activeRequest == null ? "Reading staged propulsion..." : activeRequest.Progress
                    : choiceRequest != null ? choiceRequest.Progress
                    : nodeCount == 0 ? "Create one departure maneuver with an encounter to split it."
                    : nodeCount != 1 ? "Splitting requires exactly one maneuver node."
                    : "One departure node with an encounter. Stages are used automatically.";
                if (choiceRequest == null && !string.IsNullOrEmpty(GUI.tooltip)) status = GUI.tooltip;
                GUI.Label(new Rect(12, 104, width, 44), status, wrappedLabel);
                if (choiceRequest != null) DrawChoices(width);
            }
            else
            {
                GUI.Label(new Rect(12, 138, width, 26), new GUIContent(
                    string.Format("Δv: {0:F1} → {1:F1} m/s  |  {2} burns",
                        displayedPlan.OriginalDeltaV, displayedPlan.Score.TotalDeltaV, displayedPlan.Nodes.Count),
                    "Original maneuver → total timed split Δv"), headingLabel);
                Rect viewport = new Rect(12, 198, width, Math.Max(28, panelRect.height - 278));
                DrawBurns(displayedPlan, viewport);
                GUI.Label(new Rect(12, panelRect.height - 70, width, 44),
                    "Full throttle, hold maneuver direction, and use these timers. Recheck the trajectory after each burn.", wrappedLabel);
                if (!string.IsNullOrEmpty(GUI.tooltip))
                    GUI.Label(new Rect(12, panelRect.height - 26, width, 22), GUI.tooltip);
            }
            GUI.DragWindow(new Rect(0, 0, panelRect.width - 62, 24));
        }

        private void DrawBurns(SplitCandidate plan, Rect viewport)
        {
            // Fixed row geometry and reserved scrollbar space avoid text-driven
            // width changes and horizontal scrollbar feedback while scrolling.
            float width = viewport.width - 20;
            float burnWidth = width * .44f, deltaWidth = width * .30f, durationWidth = width - burnWidth - deltaWidth;
            GUI.Label(new Rect(12, 172, burnWidth, 24), "Burn / stage", headingLabel);
            GUI.Label(new Rect(12 + burnWidth, 172, deltaWidth, 24), "Timed Δv", numberLabel);
            GUI.Label(new Rect(12 + burnWidth + deltaWidth, 172, durationWidth, 24), "Duration", numberLabel);
            planScroll.x = 0;
            planScroll = GUI.BeginScrollView(viewport, planScroll,
                new Rect(0, 0, width, Math.Max(viewport.height, plan.Nodes.Count * 28f)), false, true);
            for (int i = 0; i < plan.Nodes.Count; i++)
            {
                NodeSpec node = plan.Nodes[i];
                float y = i * 28f;
                GUI.Label(new Rect(0, y, burnWidth, 26), new GUIContent((i + 1) + ". " + BurnLabel(node),
                    string.Format("Start UT {0:F1}  |  Stock node {1:F1} m/s", node.Ut - node.StartOffset, node.DeltaV.magnitude)));
                GUI.Label(new Rect(burnWidth, y, deltaWidth, 26), string.Format("{0:F1} m/s", node.BurnDeltaV), numberLabel);
                GUI.Label(new Rect(burnWidth + deltaWidth, y, durationWidth, 26), string.Format("{0:F1} s", node.Duration), numberLabel);
            }
            GUI.EndScrollView();
        }

        private static string BurnLabel(NodeSpec node) => node.Purpose
            .Replace("Periapsis kick", "Kick").Replace("Plane change", "Plane").Replace("stage ", "S");
    }
}
