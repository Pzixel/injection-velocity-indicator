# Simple Splitter

Simple Splitter is a separately packaged KSP 1.12.5 mod for turning one long
ejection maneuver into earlier periapsis kicks, an optional plane change, and
a finite departure, which can itself use several burns. It optimizes real burn timing and direction
for low-thrust and heavily loaded vessels.

## Use

1. Create an ejection maneuver that produces a target-body encounter.
   Periapsis is usually efficient, but the maneuver need not be exactly there.
   Setup kicks return to its orbital position on earlier revolutions; each
   resulting timed burn and coast must still pass safety checks.
2. Make sure it is the vessel's only maneuver node and that at least two
   parking-orbit periods remain before it.
3. Open map view and click the **SS** button in the stock app toolbar to open
   Simple Splitter.
4. Adjust the **Max burns** slider (default **5**, range 2–10, whole-number steps). This includes
   every setup kick, plane change, and departure part. Propulsion follows
   the vessel's stage order automatically; there is no engine selector.
5. Click **Compare plans**. The original maneuver is restored between checks.
6. Review the comparison table and click **Apply** on the preferred row.

The table includes every count from 2 through the chosen maximum. A count for
which no safe plan was found remains visible as an unavailable row; it does
not stop searches at higher counts. Available rows show:

- **Total Δv:** consumption across the timed burns, in m/s, compared with the
  original maneuver displayed above the table.
- **Added Δv:** extra m/s and percentage of the original maneuver's Δv.
  Negative values mean savings. This is a cost comparison, not encounter error.
- **Longest burn:** the longest continuous full-throttle firing. Hover for
  total engine-on time and the geometric burn-spread diagnostic.
- **Arrival shift:** simulated arrival early/late at the target's sphere of
  influence, compared with the live original maneuver. All offered plans pass
  the arrival window and safety checks. Apply plots the exact checked route,
  after checking that the vessel, fuel and original maneuver still match.

The former "Cosine" column measured the maximum `1 − cos(angle)` between a
burn's nominal node position and the vessel's position during execution. It
is a peak geometric diagnostic, not an integrated Δv loss or encounter error.
It remains available in the longest-burn tooltip, explicitly labeled, and is
never a filter. Hover over arrival shift for the predicted departure position
and velocity error, measured at the same time on the original trajectory.
Within a count, plans matching within 1 m and 0.001 m/s rank by total Δv.
Otherwise, the smallest combined position/velocity error ranks first. These
are numerical matching tolerances, not a guarantee of player execution accuracy.

The toolbox starts closed and opens only when requested through its toolbar
button. Selecting or deselecting a maneuver does not change whether it is open.
Click **SS** again or the title-bar **×** to close it. Use **−** to collapse it
to a small draggable bar and **+** to expand it. Settings and the collapsed
state are retained while closing and reopening the toolbox in the same flight.
The toolbox hides outside map view and when the game UI is hidden with F2.
Panel interactions share mouse focus with the stock maneuver gizmo, so sliders,
scrolling, and window dragging keep the selection open. A drag remains protected
through its release even outside the panel; a subsequent outside click deselects
normally.
Splitting requires exactly one maneuver node on the active vessel; the gizmo
does not need to be selected. The toolbox can also be opened without a node to
adjust settings. After splitting, the burn list and Undo remain available.

The setup consists of prograde kicks on successive earlier orbits. Its total
budget comes from the body's gravitational parameter, the current state, and
the largest safe bound orbit inside the SOI. There is no Kerbin-specific
delta-v constant. Each kick increases the next orbital period; the sum of
these periods is solved against the original departure UT.

When a separate plane change saves delta-v, it occurs near the raised
apoapsis, at the opposite intersection of the old and desired planes. It
includes normal/antinormal and retrograde components: this rotates velocity
without adding energy or displacing the return periapsis. The final nominal
node then has only prograde/radial components. Near-circular orbits with small
radial velocity are supported; the plane intersection need not be exactly Ap.

Five burns remains a practical default. The maximum limits the table size,
including setup kicks, a plane change when useful, and final departure.

Burn duration uses an embedded copy of Kerbal Engineer Redux's fuel-flow and
staging simulator. **KER does not need to be installed, and stock stage delta-v
is never used.** The simulator snapshots the vessel, then calculates remaining
vacuum propulsion at full throttle and the configured engine limiters in the
background. Active engines execute first, followed by the remaining stages.
Individual resource-drain intervals preserve changes of engine and Isp within
a stage. Contiguous intervals with the same effective engine are merged, so
draining another tank does not require another maneuver.
Each burn stays within one propulsion segment. For each total count, the planner
searches stage allocations and samples setup-energy ceilings. It solves the
resonant timing for each layout. A cheap Δv-weighted angular-spread estimate
orders up to three layouts per sample for full simulation. It then converts
departures from the current and lower-count seeds, spending spare node slots
on finite burns along the outgoing orbit. Ignition time, each burn's Δv and
fixed direction, and the intervening coast durations can change. Coasts leave
at least 30 seconds to reorient; each burn stays in one propulsion segment.
It initially retains three distinct converted finalists per count. If every
finalist is unsafe, it retries with different setup energies and redistributes
delta-v within each stage, solving resonance timing again. The retry retains
all its simulated alternatives for safety checks. This bounded search
reports the best safe plan found; it does not prove global optimality or that
an unsuccessful count is physically impossible.

Initial options for all counts are checked before redistribution retries.
Refinement shares a two-second allowance, checked between batches, so a
difficult low-count row cannot hold up the whole table. When the status says
refinement paused, **Compare plans** resumes unfinished batches while the
inputs still match. Every exposed row has completed timed execution, actual
patched-conic coast checks and command-node retention checks; exhausting the
allowance never bypasses safety. All checked finalists are ranked using their
refreshed finite execution, rather than accepting the first safe stale score.

Shares are balanced within a propulsion segment. A new count is optimized
independently: three same-stage kicks may all be redistributed, rather than
halving one of two existing kicks. Stage boundaries and fuel capacities can
require unequal shares between different engines. The finite simulation
calibrates stage boundaries, burn budgets and offsets to recover intended
energy/period while carrying earlier execution errors into later burns.

Completed searches are cached separately for each count. Raising the maximum
from 7 to 8 reuses counts 2–7 and searches 8; lowering it only filters results.
The key includes source orbit, node vector/time, target, stage order,
remaining fuel, mass, thrust and exhaust velocity. Changed inputs invalidate
the cache, and counts whose first burns are now too close are recomputed.
Float-scale orbital noise is compared near the original capture time instead
of being amplified by months of extrapolation. Each offered option retains its
checked commands, source snapshot and encounter. Apply verifies the inputs and
commits those commands without another optimization or encounter search.
Each new comparison refreshes stock safety results against its current baseline
while retaining the numerical searches, including completed redistribution.
During comparison, each alternative's cached stage layout, redistribution and
setup ceiling are reused to refresh resonance timing and burn timers against
one captured live orbit. Conversion and safety replay share that snapshot.
Genuine orbit/fuel edits, a changed original node or an expired first burn require
another comparison; ordinary coasting does not discard an already checked route.
Cancellation retains completed counts for the next comparison in this flight.

Planning yields between batches of integrations, using a 4 ms time budget and
at most 16 new integrations per slice. **Cancel calculation** remains available.
There is no global timeout that prevents later counts from being searched.
Individual integrations and stock solver calls still run on KSP's main thread.

Converted burns are mapped to effective instantaneous impulses. Their node times
and magnitudes are fitted to the finite solution's coast positions and outgoing
state; inserting the full thrust impulse directly would corrupt waiting periods
and place later departures far from periapsis. The effective node remains within
its physical burn window and gives the original fixed thrust heading when it is
the next node on the actual pre-burn orbit. Actual ignition, duration, staging and
fuel expenditure do not change. Both the real finite path and the mapped stock
encounter are checked before the option is offered. Apply commits that mapping.

Use the panel's **ignition countdown and full-throttle duration**, not KSP's burn
time or its suggested start offset. The stock node magnitude is an effective map
impulse, not the amount of thrust to deliver. At ignition, align to the next node,
lock inertial attitude, and run its full-throttle timer. Remove the completed node:
Simple Splitter then refits the remaining map from the vessel's actual orbit,
without changing the original burn headings or timers. This also handles the
small differences between the real burnout orbit and the effective impulse.
Do not keep steering toward the changing residual maneuver marker during a burn.

A single instantaneous impulse cannot generally reproduce a finite burn's full
position and velocity state. The final remaining node therefore remains an
approximation even after fitting; the timed execution, rather than zeroing the
stock node's residual Δv, is what recovers the computed real trajectory.

The finite simulator rejects sampled surface/atmosphere crossings throughout
thrust. Finalists also sample moon SOIs during each burn and use independent
stock patched-conic copies to check actual finite coasts for atmosphere entry
and unintended encounters. Burn windows use the computed start offsets, which
can be more accurate than assuming a strict 50/50 split around the node.
Preview and Apply share the same transactional command writer. Failed writes
restore the original maneuver and report the reason. Numerical sampling and
stock solver precision still limit these predictions.

Extra delta-v and the largest burn are comparison properties, with no hidden
percentage cap. Each burn must still fit the fuel available in its propulsion
segment. The simulated timed departure must encounter the original body within
one day of the original arrival, using
KSP's displayed calendar. A timed path that never reaches the target is rejected.
The original maneuver defines an immutable reference trajectory. Every candidate
targets its position and velocity at a common time after thrust, inside the
departure body's SOI. Burn positions and times can move while that reference
remains fixed. See [the objective investigation](docs/finite-burn-objective.md)
for the equations, comparison experiments, and relationship to the NASA paper.

## Executing the timed burns

The compact burn table shows the burn/stage, timed delta-v, and duration.
The header compares the original maneuver delta-v with the total timed split
delta-v. Hover over a burn for its absolute start UT and stock node delta-v. **Use the displayed full-throttle burn
duration while holding a fixed attitude.** After completing and removing each
node, coast to the next burn. Align to that next remaining node immediately
before ignition and lock the attitude throughout the burn. Do not continuously
track the changing remaining-maneuver marker or stop at zero remaining Δv;
use the supplied start time and duration. The model holds a fixed inertial
direction within each burn and can use a different direction for the next one.

The timers are numerically adjusted to recover the intended orbital energy
and period for the setup seed. This matters particularly near escape, where a
small energy error can cause a large return-time error. A bounded shooting
solver then matches the entire executed departure to the original full state,
and reduces Δv while retaining that match. The offsets can differ from half
the burn duration because thrust acceleration increases as fuel is consumed.

The model propagates the full sequence of finite burns and intervening coasts.
The KSP log records residual burnout and common-epoch position/velocity error.
Small burn counts may have insufficient freedom to meet the matching tolerance;
their best available approximation can still be offered if the real path passes
the encounter checks. Numerical integration, propulsion estimates and player
execution limit accuracy. Recheck the trajectory after each burn.

Estimates assume constant full vacuum thrust at the configured limiter and
constant Isp within each propulsion segment. Follow the displayed stage order;
stage transitions happen between burns. The model uses KER's post-decoupling
masses and fuel budgets. A burn that would need another propulsion segment is
rejected unless the setup can be redistributed within the node limit. The mod
waits for its embedded simulator to finish; failed calculations and
unsupported air-dependent, throttle-locked, or thrust-curve propulsion produce
an explanation instead of an engine-selection prompt. Changes to staging,
resources, or engine settings during calculation invalidate the result.
The KER simulator's limitations still apply: for example, electric generation
and consumption do not constrain its delta-v estimate. The embedded version is
pinned and does not automatically change when the installed KER is updated.

Click **Undo split** in the same panel to undo the last split. Undo is
refused if the active vessel changed or any generated node was edited, so it
never overwrites subsequent work. Simple Splitter intentionally has no keyboard
shortcuts because flight-control bindings such as Shift must remain untouched.

## Eligibility and safety

Simple Splitter leaves the plan unchanged unless all of these conditions hold:

- flight map view is open and the vessel has exactly one maneuver node;
- the node is sufficiently far in the future and includes a positive prograde
  component; it need not be exactly at periapsis;
- the starting and setup trajectories remain bound; all burns and intervening
  coasts remain above the atmosphere and in the required sphere of influence;
- the simulated finite trajectory reaches the original target inside the
  arrival-time tolerance, and KSP retains all checked command nodes.

Failures are reported on screen. Node replacement is transactional: if KSP's
command creation fails, the original node is restored.

Principia is not supported because its trajectories are not represented by
KSP's stock patched-conic `Orbit` data. The mod detects Principia and disables
itself.

## Build

The default Windows Steam location is detected automatically. For another KSP
installation, pass its root explicitly:

```powershell
dotnet build .\src\SimpleSplitter\SimpleSplitter.csproj -c Release -p:KSPRoot='D:\Games\Kerbal Space Program'
```

The Release build prepares:

```text
GameData/SimpleSplitter/
    Plugins/SimpleSplitter.dll
```

Simple Splitter has no runtime dependency on KER, Harmony or either of the other
mods in this repository. Its embedded simulator is derived from
[Kerbal Engineer Redux](https://github.com/jrbudda/KerbalEngineer/tree/54b8b73890b34a8342737f52e3062efc9fd21cc5).
Upstream provenance and changes are documented in
`src/SimpleSplitter/Vendor/KerbalEngineer/README.vendor.md`.

Run the headless regression suite with:

```powershell
dotnet run --project .\tests\SimpleSplitter.Tests -c Release
```

The suite links the production planner to an independent analytic Kepler
adapter. It covers per-count alternatives, cache extension and invalidation,
stage allocation/fuel, energy compensation, nominal terminal-state preservation,
normal/antinormal alternatives, different bodies, atmosphere crossings, and
intermediate transition timing. The `testburn` regression uses its saved orbit
and measured KER propulsion, including the rotating low-orbit coordinate frame.
Propulsion adapter tests cover active-stage
ordering, tank/engine boundaries, vacuum/full-throttle requests, stale snapshots,
failures and cancellation. The headless adapter does not execute Unity's real
part snapshot or KER's complete resource simulation. This does not replace testing the stock
encounter solver or maneuver UI inside KSP.

The duration model follows the [ideal rocket equation](https://www1.grc.nasa.gov/beginners-guide-to-aeronautics/ideal-rocket-equation/).
The plane-change vector uses the speed-preserving rotation described in
[NASA's plane-change capability analysis](https://ntrs.nasa.gov/api/citations/19880020470/downloads/19880020470.pdf).

## Install and remove

Copy `GameData/SimpleSplitter` into the game's existing `GameData` directory.
To uninstall, delete only that directory.

## License

The combined Simple Splitter distribution, including the embedded KER code,
is provided under GPLv3 (`LICENSE.SimpleSplitter`). Original MIT notices are
retained in `LICENSE`. Other mods in this repository retain their existing
licenses. Release ZIPs contain the GPL license, attribution and corresponding
Simple Splitter source under `Source/`; proprietary KSP assemblies are not included.
