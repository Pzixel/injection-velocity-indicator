namespace SimpleSplitter
{
    // Unity reports the release during Update; the stock gizmo handles it in
    // LateUpdate. Keep ownership for that entire frame, including outside drags.
    internal sealed class PanelPointerCapture
    {
        private bool dragging;

        internal bool Update(bool pointerOverPanel, bool pressed, bool held, bool released)
        {
            if (pressed) dragging = pointerOverPanel;
            bool ownsPointer = pointerOverPanel || (dragging && (held || released));
            if (!held) dragging = false;
            return ownsPointer;
        }

        internal void Reset() => dragging = false;
    }
}
