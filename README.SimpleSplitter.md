# Simple Splitter

Simple Splitter is a separately packaged KSP 1.12.5 mod for turning one long
ejection maneuver into earlier periapsis kicks, an optional plane change, and
a final departure burn. It estimates finite burn durations and energy loss
for low-thrust and heavily loaded vessels.

## Use

1. Create an ejection maneuver at periapsis that produces a target-body
   encounter.
2. Make sure it is the vessel's only maneuver node and that at least two
   parking-orbit periods remain before it.
3. Open map view and click the **SS** button in the stock app toolbar to open
   Simple Splitter.
4. Adjust the **Max burns** slider (default **5**, range 2–10, whole-number steps). This includes
   every setup kick, plane change, and the final departure. Propulsion follows
   the vessel's stage order automatically; there is no engine selector.
5. Click **Compare plans**. The original maneuver is restored between checks.
6. Review the comparison table and click **Apply** on the preferred row.

The table includes every count from 2 through the chosen maximum. A count for
which no safe plan was found remains visible as an unavailable row; it does
not stop searches at higher counts. Available rows show total timed Δv,
additional Δv relative to the original impulse, largest timed burn, departure
error, burnout position error in km, and maximum cosine loss over all burns.
Negative additional Δv means a saving. Cosine loss is a diagnostic, never a
filter. Departure error is outgoing excess-velocity vector error in m/s (local
burnout velocity error for bound departures). Within a count, lower departure
error ranks first, followed by lower total Δv.

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

Burn duration uses KSP's remaining staged vacuum delta-v, mass, thrust, and Isp.
Each burn stays within one propulsion segment. For each total count, the planner
searches stage allocations and samples setup-energy ceilings. It solves the
resonant timing for each layout and fully simulates up to three layouts per
sample. It retains three distinct finalists per count so stock validation can
try an alternative if the lowest-error candidate is unsafe. This bounded search
reports the best safe plan found; it does not prove global optimality or that
an unsuccessful count is physically impossible.

Shares are balanced within a propulsion segment. A new count is optimized
independently: three same-stage kicks may all be redistributed, rather than
halving one of two existing kicks. Stage boundaries and fuel capacities can
require unequal shares between different engines. The finite simulation
calibrates stage boundaries, burn budgets and offsets to recover intended
energy/period while carrying earlier execution errors into later burns.

Completed searches are cached separately for each count. Raising the maximum
from 7 to 8 reuses counts 2–7 and searches 8; lowering it only filters results.
The key includes source orbit, node vector/time, target/arrival, stage order,
remaining fuel, mass, thrust and exhaust velocity. Changed inputs invalidate
the cache, and counts whose first burns are now too close are recomputed.
Cancellation retains completed counts for the next comparison in this flight.

Planning yields between batches of integrations, using a 4 ms time budget and
at most 16 new integrations per slice. **Cancel calculation** remains available.
There is no global timeout that prevents later counts from being searched.
Individual integrations and stock solver calls still run on KSP's main thread.

For nominal safety, each candidate is checked with every later-node suffix
absent so KSP solves the full coast after each intermediate burn. Only events
before the next burn invalidate that coast. The original node is restored in
`finally` before every yield, including failed previews. This uses solved patch
transitions instead of relying on the map's collision marker.

The finite simulator rejects sampled surface/atmosphere crossings throughout
thrust. Finalists also sample moon SOIs during each burn and use independent
stock patched-conic copies to check actual finite coasts for atmosphere entry
and unintended encounters. Burn windows use the computed start offsets, which
can be more accurate than assuming a strict 50/50 split around the node.
Safety is checked again before applying a chosen row. Numerical sampling and
stock solver precision still limit these predictions.

The largest timed burn must be smaller than the original impulse. Estimated
expenditure, **including finite-burn compensation**, cannot exceed 105% of the
original delta-v. The nominal stock flight plan must encounter the same body
within 1% of the original trip duration. The nominal departure state and UT
are preserved; KSP's encounter check is repeated after constructing the nodes.
Rejected stock encounters are rolled back before trying the next candidate.

## Executing the timed burns

The compact burn table shows the burn/stage, timed delta-v, and duration.
The header compares the original maneuver delta-v with the total timed split
delta-v. Hover over a burn for its absolute start UT and stock node delta-v. **Use the displayed full-throttle burn
duration while holding the maneuver direction.** Stopping at the stock node's
zero remaining delta-v does not apply the energy compensation. The model
holds a fixed inertial maneuver direction throughout each burn.

The timers are numerically adjusted to recover the intended orbital energy
and period. This matters particularly near escape, where a small energy error
can cause a large return-time error. The departure window is also searched to
reduce outgoing excess-velocity error within the expenditure limit; its time
offset can differ substantially from half the burn duration.

The model propagates the full sequence of finite burns and intervening coasts.
The KSP log records residual burnout position/velocity error. **This is not an exact finite-burn
encounter guarantee.** Period/energy compensation does not remove every
direction, orbit-shape, or timing error. A long final departure can still need
a course correction even when the nominal stock encounter is correct. Recheck
the remaining flight plan after executing each kick.

Estimates assume constant full vacuum thrust at the configured limiter and
constant Isp within each propulsion segment. Follow the displayed stage order;
stage transitions happen between burns. The model uses KSP's post-decoupling
masses and fuel budgets. A burn that would need another propulsion segment is
rejected unless the setup can be redistributed within the node limit. The mod
waits briefly for KSP's staged estimate to finish; unavailable estimates and
unsupported air-dependent, throttle-locked, or thrust-curve propulsion produce
an explanation instead of an engine-selection prompt. Changes to staging,
resources, or engine settings during calculation invalidate the result.

Click **Undo split** in the same panel to undo the last split. Undo is
refused if the active vessel changed or any generated node was edited, so it
never overwrites subsequent work. Simple Splitter intentionally has no keyboard
shortcuts because flight-control bindings such as Shift must remain untouched.

## Eligibility and safety

Simple Splitter leaves the plan unchanged unless all of these conditions hold:

- flight map view is open and the vessel has exactly one maneuver node;
- the node is in the future, at periapsis (or on a nearly circular orbit), and
  includes a positive prograde component;
- the starting and intermediate trajectories remain bound, above the
  atmosphere, and inside the current body's sphere of influence;
- the original and generated stock patched-conic plans encounter the same
  body inside the arrival-time tolerance.

Failures are reported on screen. Node replacement is transactional: if KSP's
final patched-conic validation fails, the original node is restored.

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

Simple Splitter has no runtime dependency on Harmony or on either of the other
mods in this repository.

Run the headless regression suite with:

```powershell
dotnet run --project .\tests\SimpleSplitter.Tests -c Release
```

The suite links the production planner to an independent analytic Kepler
adapter. It covers per-count alternatives, cache extension and invalidation,
stage allocation/fuel, energy compensation, nominal terminal-state preservation,
normal/antinormal alternatives, different bodies, atmosphere crossings, and
intermediate transition timing. This does not replace testing the stock
encounter solver or maneuver UI inside KSP.

The duration model follows the [ideal rocket equation](https://www1.grc.nasa.gov/beginners-guide-to-aeronautics/ideal-rocket-equation/).
The plane-change vector uses the speed-preserving rotation described in
[NASA's plane-change capability analysis](https://ntrs.nasa.gov/api/citations/19880020470/downloads/19880020470.pdf).

## Install and remove

Copy `GameData/SimpleSplitter` into the game's existing `GameData` directory.
To uninstall, delete only that directory.
