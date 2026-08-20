# Injection Velocity Indicator

## TDLR

Before:

<img width="687" height="376" alt="image" src="https://github.com/user-attachments/assets/3f38cb59-b52b-4be6-917b-6bcf97ff7151" />

After:

<img width="661" height="535" alt="image" src="https://github.com/user-attachments/assets/f2fb73f2-ebcb-4f6a-8f2e-e67925d1621d" />



A KSP 1.12.5 mod that adds the target-relative speed to the stock planet closest-approach tooltip. 

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
