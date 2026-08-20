# Injection Velocity Indicator

A KSP 1.12.5 mod that adds the target-relative speed to the stock planet closest-approach tooltip. It uses the post-maneuver trajectory patch represented by the marker and does not create a separate window or overlay.

The mod applies one narrow Harmony postfix to `OrbitTargeter.ClApprMarker.OnUpdateCaption`. KSP has already regenerated the stock separation and time text when the postfix runs. The mod then adds relative speed in the same three-line layout used by KSP's vessel-rendezvous marker.

The expensive part is not run from `ClApprMarker.Update` or any map-position method. While a caption is hovered or pinned, the mod compares the stock separation text and a snapshot of the current trajectory/target orbit inputs. It recalculates only when one of those inputs changes, including maneuver-plan patch replacement or in-place orbital-element changes after a delta-v edit.

The calculation follows KSP's stock vessel-rendezvous logic: subtract the current trajectory and target patch orbital velocities at KSP's closest-approach UT, then display the magnitude using KSP's localized `Relative Speed` label.

## Dependency

[HarmonyKSP / Harmony2](https://github.com/KSPModdingLibs/HarmonyKSP) must already be installed, normally through CKAN or at `GameData/000_Harmony/0Harmony.dll`.

This mod only references the shared Harmony assembly. It does not include, replace, patch, or delete `0Harmony.dll`.

## Build

The default Windows Steam location is detected automatically. For another KSP installation, pass its root explicitly:

```powershell
dotnet build .\InjectionVelocityIndicator.sln -c Release -p:KSPRoot='D:\Games\Kerbal Space Program'
```

The Release build prepares:

```text
GameData/InjectionVelocityIndicator/
└── Plugins/InjectionVelocityIndicator.dll
```

## Install and remove

Copy the `InjectionVelocityIndicator` directory into the game's existing `GameData` directory. The result must be `GameData/InjectionVelocityIndicator`, not `GameData/GameData/InjectionVelocityIndicator`.

To uninstall, delete only `GameData/InjectionVelocityIndicator`. The mod never installs files into another mod's directory.

Principia is detected at startup and disables this feature because its displayed trajectory is not represented by KSP's stock patched-conic `Orbit` data.
