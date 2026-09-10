using KSP.UI.Screens;
using UnityEngine;

namespace SimpleSplitter
{
    internal sealed partial class SimpleSplitterAddon
    {
        private bool panelOpen;
        private bool gameUiVisible = true;
        private bool toolbarSubscribed;
        private ApplicationLauncherButton? toolbarButton;
        private Texture2D? toolbarIcon;

        private void InitializeToolbar()
        {
            if (toolbarSubscribed) return;
            toolbarSubscribed = true;
            GameEvents.onGUIApplicationLauncherReady.Add(CreateToolbarButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnLauncherDestroyed);
            GameEvents.onHideUI.Add(HideGameUi);
            GameEvents.onShowUI.Add(ShowGameUi);
            CreateToolbarButton();
        }

        private void CreateToolbarButton()
        {
            if (toolbarButton != null || !ApplicationLauncher.Ready || ApplicationLauncher.Instance == null) return;
            if (toolbarIcon == null) toolbarIcon = CreateToolbarIcon();
            toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                OpenPanel, ClosePanel, null, null, null, null,
                ApplicationLauncher.AppScenes.MAPVIEW, toolbarIcon);
            if (panelOpen) toolbarButton.SetTrue(false);
        }

        private void OpenPanel() => panelOpen = true;

        private void ClosePanel()
        {
            panelOpen = false;
            if (toolbarButton != null) toolbarButton.SetFalse(false);
            ClearPanelInput();
        }

        private void HideGameUi()
        {
            gameUiVisible = false;
            ClearPanelInput();
        }

        private void ShowGameUi() => gameUiVisible = true;

        private void OnLauncherDestroyed()
        {
            toolbarButton = null;
            ClosePanel();
        }

        private void ShutdownToolbar()
        {
            if (toolbarSubscribed)
            {
                GameEvents.onGUIApplicationLauncherReady.Remove(CreateToolbarButton);
                GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnLauncherDestroyed);
                GameEvents.onHideUI.Remove(HideGameUi);
                GameEvents.onShowUI.Remove(ShowGameUi);
                toolbarSubscribed = false;
            }
            ClosePanel();
            if (toolbarButton != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);
            toolbarButton = null;
            if (toolbarIcon != null) Destroy(toolbarIcon);
            toolbarIcon = null;
        }

        private static Texture2D CreateToolbarIcon()
        {
            // Crisp SS initials at toolbar size; generated once, no asset dependency.
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
            {
                name = "Simple Splitter",
                filterMode = FilterMode.Point
            };
            var pixels = new Color32[32 * 32];
            string[] glyph = { "01111", "11000", "11000", "01110", "00011", "00011", "11110" };
            for (int letter = 0; letter < 2; letter++)
                for (int row = 0; row < glyph.Length; row++)
                    for (int column = 0; column < glyph[row].Length; column++)
                        if (glyph[row][column] == '1')
                            for (int dy = 0; dy < 2; dy++)
                                for (int dx = 0; dx < 2; dx++)
                                    pixels[(21 - row * 2 + dy) * 32 + 4 + letter * 13 + column * 2 + dx] =
                                        new Color32(132, 225, 240, 255);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
