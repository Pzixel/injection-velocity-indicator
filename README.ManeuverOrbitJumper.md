# Maneuver Orbit Jumper

A separately packaged KSP 1.12.5 mod that extends the stock maneuver editor's
orbit and TIME buttons with keyboard-modified jumps.

## Orbit buttons

| Modifier | Add button | Subtract button |
| --- | ---: | ---: |
| None | +1 | -1 |
| Ctrl | +10 | -10 |
| Alt | +100 | -100 |
| Ctrl + Alt | +1,000 | -1,000 |

Either the left or right Ctrl/Alt key works. The shortcut applies to both the
advanced maneuver editor and the floating maneuver gizmo orbit buttons.

On the same bound orbit patch, Ctrl + Alt on subtract reverses an accidental
Ctrl + Alt add of 1,000 orbits in one click.

The mod changes only the stock handlers' orbit-count and period-step operands.
KSP's original guards, state writes, gizmo update, mouse handling, and button
refresh remain in the original method body and run once per click. A
1,000-orbit jump therefore does not trigger 1,000 successive flight-plan
recalculations.

This is deliberately one scaled stock action, not a simulation of 1,000
separate UI clicks. KSP does not recalculate the patched-conic plan between the
intermediate orbits. If the trajectory would cross an encounter, escape, or SOI
transition during the jump, the result can differ from clicking the unmodified
button repeatedly. The matching subtract shortcut is an exact inverse only
while KSP still reports the same bound patch and orbital period. Bulk subtract
also adds no new clamp beyond the stock handler; use a multiplier no larger
than the number of previously added orbits.

## TIME buttons

The TIME `+` and `-` buttons use the same modifier multiplier. The maneuver
editor's precision slider still determines the base time step:

| Modifier | TIME multiplier |
| --- | ---: |
| None | ×1 |
| Ctrl | ×10 |
| Alt | ×100 |
| Ctrl + Alt | ×1,000 |

For example, if the slider displays a 5-second time step, Ctrl + TIME `+` moves
the maneuver forward 50 seconds, and Ctrl + TIME `-` moves it back 50 seconds.
The slider's speed increment is not changed; only the stock TIME handler's
slider-derived step is multiplied for that click. All of the handler's other
instructions—including usage tracking, gizmo synchronization, and the stock
flight-plan update—remain unchanged.

As with the orbit buttons, a modified TIME click is one stock handler execution
and one flight-plan update. The mod does not add a new "not before the current
universal time" rule that the stock handler itself does not have, so take care
with large TIME `-` multipliers.

## Patch compatibility

At startup, each transpiler requires exactly one copy of the expected KSP
1.12.5 instruction pattern. If a different game build or another transpiler
has changed that pattern, Maneuver Orbit Jumper refuses the patch, removes any
of its patches that were already applied, and logs the mismatch instead of
guessing at a changed method body. Branch labels and exception boundaries from
other Harmony transpilers are retained when the multiplier instructions are
inserted.

## Dependency

[HarmonyKSP / Harmony2](https://github.com/KSPModdingLibs/HarmonyKSP) must already
be installed, normally through CKAN or at
`GameData/000_Harmony/0Harmony.dll`.

This mod references the shared Harmony assembly. It does not include, replace,
patch, or delete `0Harmony.dll`.

## Build

The default Windows Steam location is detected automatically. For another KSP
installation, pass its root explicitly:

```powershell
dotnet build .\src\ManeuverOrbitJumper\ManeuverOrbitJumper.csproj -c Release -p:KSPRoot='D:\Games\Kerbal Space Program'
```

The Release build prepares:

```text
GameData/ManeuverOrbitJumper/
└── Plugins/ManeuverOrbitJumper.dll
```

## Install and remove

Copy the `ManeuverOrbitJumper` directory into the game's existing `GameData`
directory. The result must be `GameData/ManeuverOrbitJumper`, not
`GameData/GameData/ManeuverOrbitJumper`.

To uninstall, delete only `GameData/ManeuverOrbitJumper`. It is independent from
Injection Velocity Indicator; either mod can be installed without the other.

## Releases and CKAN

Use the **Release Maneuver Orbit Jumper** GitHub Actions workflow with a
`MAJOR.MINOR.PATCH` version. It publishes only
`ManeuverOrbitJumper-<version>.zip` with its own version file and GameData folder.

`CKAN/ManeuverOrbitJumper.netkan` has its own CKAN identifier and selects only
matching Maneuver Orbit Jumper assets from this shared GitHub repository.
