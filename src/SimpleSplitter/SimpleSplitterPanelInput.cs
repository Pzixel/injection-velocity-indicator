using UnityEngine;

namespace SimpleSplitter
{
    internal sealed partial class SimpleSplitterAddon
    {
        private readonly PanelPointerCapture panelPointer = new PanelPointerCapture();
        private ManeuverGizmo? panelInputGizmo;
        private bool panelOwnsFocus;

        private void UpdatePanelInput()
        {
            if (!PanelVisible || !Application.isFocused)
            {
                ClearPanelInput();
                return;
            }
            ManeuverGizmo? selected = SelectedGizmo;
            if (panelInputGizmo != selected)
            {
                ClearPanelInput();
                panelInputGizmo = selected;
            }
            bool pointerOverPanel = panelPositioned && panelRect.Contains(
                new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            bool ownsPointer = panelPointer.Update(pointerOverPanel,
                Input.GetMouseButtonDown(0), Input.GetMouseButton(0), Input.GetMouseButtonUp(0));
            if (ownsPointer)
            {
                InputLockManager.SetControlLock(ControlTypes.MAP_UI, InputLockName);
                // MAP_UI doesn't guard ManeuverGizmo's raw mouse-up close path.
                // Share focus through the same API used by the stock node editor.
                // Update runs before the gizmo's LateUpdate, including on release.
                if (selected != null)
                {
                    selected.SetMouseOverGizmo(true);
                    panelOwnsFocus = true;
                }
            }
            else
            {
                ReleasePanelFocus();
            }
        }

        private void ReleasePanelFocus()
        {
            InputLockManager.RemoveControlLock(InputLockName);
            if (panelOwnsFocus && panelInputGizmo != null)
            {
                // Don't clear focus that has passed to KSP's own editor. The
                // gizmo API also preserves hover over its physical handles.
                ManeuverNodeEditorManager editor = ManeuverNodeEditorManager.Instance;
                bool stockEditorOwnsFocus = panelInputGizmo == SelectedGizmo &&
                    editor != null && editor.MouseWithinTool;
                panelInputGizmo.SetMouseOverGizmo(stockEditorOwnsFocus);
            }
            panelOwnsFocus = false;
        }

        private void ClearPanelInput()
        {
            ReleasePanelFocus();
            panelPointer.Reset();
            panelInputGizmo = null;
        }
    }
}
