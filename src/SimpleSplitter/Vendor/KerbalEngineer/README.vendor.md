# Embedded Kerbal Engineer Redux simulator

Upstream: https://github.com/jrbudda/KerbalEngineer

Pinned commit: `54b8b73890b34a8342737f52e3062efc9fd21cc5`

Imported on 2026-09-10 from the upstream `KerbalEngineer/` directory:

- `VesselSimulator/*.cs`
- `Helpers/{Pool,ForceAccumulator,Averager,Units}.cs`
- `Extensions/PartExtensions.cs`
- `LogMsg.cs`

Copyright belongs to the upstream authors. Original notices are retained.
KER is licensed under GPL version 3 or later; ForceAccumulator also credits
ferram4's GPLv3 FAR implementation. See `LICENSE.SimpleSplitter` at the
repository root. Original Simple Splitter code remains available under the
repository's MIT license; the combined distribution is provided under GPLv3.
The other mods in this repository are not part of this combined program.

Local modifications (2026-09-10):

- Namespace changed to `SimpleSplitter.Engineer`, with nullable analysis disabled
  only in imported legacy files. XML-comment warning suppressed in PartExtensions.
- SimManager retains constants and mod compatibility detection. Its global
  request scheduler, worker and shared result storage were removed.
- The host's PropulsionReader owns a fresh Simulation, snapshots on the main
  thread, requests vacuum/full configured thrust, and runs on a background task.
  Its pools and settings are separate from an installed KER's state.
- Simulation exports each resource-drain interval through `EngineerSegment`.
  This preserves the effective exhaust velocity, actual mass drain, duration,
  and engine transitions without approximating a stage by its averaged Isp.
- Scene flags are captured at preparation; background work supports cancellation.
  Pool cleanup is owned by the caller's finally block and clears retained lists.
  Engine snapshot failures and the resource iteration limit now reject the
  calculation, rather than providing an incomplete propulsion estimate.
- EngineSim records unsupported timed-burn models (locked throttle, air flow,
  thrust curves) for intervals that actually use those engines. Tank depletion
  intervals with equal effective engines are joined by the host's mapper.
- Main-engine planning skips unused RCS preparation/readout calculations.
  Unused editor density selection is replaced by vacuum; no KER editor/UI code
  or logger component is imported. Units contains only simulator constants.
- `MyLogger.cs` is a local adapter to the game's Debug log.

No stock VesselDeltaV/DeltaVCalc data is used. An installed KerbalEngineer.dll
is neither referenced nor required. Engine/resource simulation follows this
pinned KER version, including its assumptions (for example, electric generation
and consumption do not constrain engine delta-v). Unsupported mods or engines
can still require further compatibility work.

Build instructions are in `README.SimpleSplitter.md`. Release archives include
the corresponding Simple Splitter source alongside the binary and license.
