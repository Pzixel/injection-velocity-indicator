# Changelog

## 1.2.3

- Replace unclear comparison diagnostics with added Δv in m/s and percent,
  longest burn duration, and actual simulated arrival early/late at the target.
  Label units and explain the geometric cosine diagnostic in a tooltip instead
  of presenting it as a percentage of Δv wasted. Reserve space for tooltips.
- Batch numerical work into measured frame slices, remove duplicate burn
  simulations, and speed up RK4 integration without reducing its resolution.
- Check initial options for every count before retries. Share a two-second
  refinement allowance across counts, prioritize near-solutions, and retain
  unfinished batches for subsequent comparisons.
- Reject failed timed paths before constructing stock preview nodes. All
  accepted options still pass every stock prefix and timed safety check.
- Keep comparison results through harmless coasting drift; refresh the live
  orbit for safety and recheck propulsion before exposing or applying results.
- Distinguish engine propellants from other supplies in snapshot validation.
  Normal life-support consumption no longer masquerades as an engine-fuel
  change; significant changes to carried supply mass still invalidate it.
- Retry rejected burn counts with redistributed stage delta-v and freshly solved
  resonance timing and setup energy. Cache retries and each safety result.
- Report an exhausted sampled search rather than claiming the count impossible.
- Compare source-orbit inputs near their capture time with float-roundoff
  tolerance. Recheck timed safety against the live source orbit before applying.
- Require the simulated timed trajectory to reach the original target within
  one day in the player's calendar. Do not accept an encounter-free coast merely
  because it is safe until the original arrival date.
- Remove the hidden 5% expenditure and largest-burn reduction filters. Report
  their values for comparison while retaining actual stage fuel limits.
- Initialize the full time interval before asking KSP to solve copied coast
  patches, so destination and moon encounters are not silently skipped. Search
  through closest approach before applying the deadline to SOI entry.
- Refresh the unchanged original encounter for each live validation, so both
  paths use the same orbital reference instead of a stale saved arrival time.
- Reuse the searched stage layout and redistribution, then refresh only that
  recipe's resonance and burn timers against the live parking orbit before
  checking or applying it. Numerical drift no longer leaves stale burn times.

## 1.2.2

- Use fixed inertial orbital states throughout planning, timed integration,
  cache checks and moon-SOI checks. KSP’s vector overload rotates the frame at
  future times in low orbit; combining it with a current-frame constructor
  could reject valid splits and immediately invalidate comparison results.
- Add a regression using the `testburn` orbit and KER propulsion snapshot,
  including a rotating reference frame and an explicit plane-change burn.

## 1.2.1

- Replace stock stage delta-v data with an embedded, isolated copy of Kerbal
  Engineer Redux's fuel-flow/staging simulator. KER need not be installed;
  stock delta-v is never consulted, including when calculation fails.
- Snapshot the vessel on the main thread and simulate vacuum, configured-limit
  full-throttle propulsion in the background. Preserve active-engine ordering,
  post-decoupling masses and individual engine-depletion intervals. Merge
  equivalent tank-drain intervals so they do not consume extra maneuver nodes.
- Recheck fuel, engine settings and staging before accepting or applying results.
  Explain simulation failures and unsupported engine models. Bound background
  calculation time and release snapshots on cancellation or failure.
- Allow float-vector noise in cached propulsion comparisons without accumulating
  drift across requests. Keep independently optimized burn-count results.
- Include KER attribution, GPL-3.0 license and corresponding source in releases.
  The combined Simple Splitter distribution is GPL-3.0; other mods retain MIT.

## 1.2.0

- Compare independently optimized plans for each total burn count, then let
  the player apply a row. Keep unsuccessful counts visible and continue higher.
- Remove the cosine slider and filter. Report maximum cosine loss, total and
  additional timed delta-v, largest burn, and finite departure/position errors.
- Search stage allocations and sampled setup energies for each count. Rank
  finite candidates by departure error, then delta-v; retain stock-validation
  alternatives rather than applying the first numerical result automatically.
- Validate every maneuver prefix with later nodes absent. Restore the original
  before yielding, and check timed coasts with independent stock solver patches.
  Reject atmosphere/surface intersections and unintended SOI entries, including
  moon-SOI samples during thrust.
- Cache completed searches by burn count and immutable maneuver/orbit/propulsion
  values. Growing the maximum computes only new counts; it does not subdivide
  previously optimized kicks. Invalidate expired burns and changed inputs.
- Use the existing 4 ms integration budget with up to 16 inexpensive integrations
  per slice. Keep cancellation and recheck safety/fuel before applying a choice.

## 1.1.10

- Replace automatic opening on maneuver selection with an opt-in toolbox,
  toggled by an SS button in the stock map-view app toolbar. No toolbar mod
  dependency is required.
- Add a title-bar close button alongside collapse/expand; keep the toolbox
  open across maneuver deselection and after generating the split plan.
- Allow settings changes without a selected gizmo and disable Split unless
  the vessel has exactly one node. Preserve Cancel during a calculation.
- Hide the toolbox outside map view and with the game UI, release input when
  hidden, and remove toolbar callbacks, buttons, and textures on shutdown.

## 1.1.9

- Draw an explicit "Simple Splitter" label inside the collapsed bar, with space
  reserved for the expand button. Keep the label area draggable.

## 1.1.8

- Share mouse focus with the selected gizmo using the stock maneuver editor API,
  preventing slider and panel clicks from closing the selection on mouse-up.
- Retain focus and the map-input lock through panel drags released outside the
  window, then allow normal outside-click deselection.
- Clear panel input ownership on selection changes, map exit, loss of application
  focus, and addon shutdown; preserve hover handed to the stock node editor.

## 1.1.7

- Show the panel only while a maneuver on the active vessel is selected in map
  view, using the stock node gizmo's current state.
- Add title-bar collapse/expand controls and a compact draggable collapsed bar.
- Release map-input locks when the panel is hidden; preserve settings and
  collapsed state across node selections.

## 1.1.6

- Remove the start-offset column and Details view from the panel.
- Show original maneuver delta-v beside total timed split delta-v in the header.
- Keep the burn table to burn/stage, timed delta-v, and duration.

## 1.1.5

- Replace automatic window layout with fixed window, viewport, row, and column
  geometry to remove competing resize calculations during scrolling. Reserve
  vertical scrollbar space and consume panel wheel events before map zoom.
- Present burns as a compact table of stage, timed delta-v, duration, and start
  offset. Move execution diagnostics and absolute start times behind Details.
- Change maximum burns to a 2–10 integer slider, default 5. Keep the cosine
  slider at 0.1%–10% with 0.1% steps, default 1%.
- Place Split/Cancel and Undo side by side; reset list scrolling for a new plan.

## 1.1.4

- Replace the cosine-loss text input with a slider spanning 0.1%–10% in 0.1%
  steps. Show the selected percentage and retain the 1% default.

## 1.1.3

- Remove the global equal-delta-v allocator that split short high-thrust stages
  before consulting their physical burn windows.
- Generate feasible burn counts per propulsion segment, then combine stage
  layouts under the shared node limit. Solve resonance separately for each
  layout and retain the layout during finite stage-boundary calibration.
- Bound full finite evaluation to three promising layouts per kick count.
- Verify high-thrust stages can finish in one burn, low-thrust first stages
  still split when required, and orbital energy limits can stop a stage early.
  Regressions vary gravity and thrust and check every kick's fuel/cosine bounds.
- The logged-ship headless regression now uses one 202.5 m/s booster burn and
  finds a seven-burn plan at 1% loss. Stock encounter validation remains required.

## 1.1.2

- Add a setup cosine-loss input in tenths of a percent: 1 = 0.1%, default 10 =
  1%. Display the percentage and retain each plan's chosen limit for diagnostics.
- Keep SOI clearance and candidate-ranking precision independent of this input.
- Describe unsuccessful searches as "search found no plan", rather than claiming
  infeasibility, and include the chosen loss limit in the message and log.
- Add a regression using logged stage propulsion (202.5 m/s booster fuel followed
  by the nuclear stage): the current search finds five burns, including a plane
  change, at 3% loss. This is headless validation, not a stock encounter guarantee.

## 1.1.1

- Add a configurable total burn limit, default 5 (range 2–10), including plane
  change and departure. Report infeasible requests without adding extra nodes.
- Remove engine selection. Use KSP's remaining propulsion segments in stage
  order, with fuel caps, post-decoupling mass, and redistribution of later kicks.
- Calibrate stage-ending nominal impulses against finite execution so exhausting
  a stage does not introduce an uncorrected parking-orbit period error.
- Yield between bounded integration batches, cache completed evaluations, show
  progress and cancellation, and stop searching after 20 seconds. Limit stock
  solver validation to three candidates. Preserve moon-encounter rejection and
  complete maneuver gizmo cleanup.
- Add regressions for fuel redistribution, stage boundaries, configurable node
  limits, finite energy/period preservation, and calculation yielding.

## 1.1.0

- Replace the two/three-node limit with a search of up to 128 earlier periapsis
  kicks. Derive the setup budget from local gravity, the orbit, and the SOI.
- Size kicks using mass, selected engine thrust/Isp, and a 0.5% cosine-loss
  bound. Preserve the departure's nominal position, velocity, and UT by solving
  the sum of the changing orbital periods.
- Numerically compensate burn durations for energy loss, carry finite states
  through intervening coasts, and optimize the departure burn window. Include
  this extra propellant expenditure in the existing 5% total-delta-v cap.
- Fix plane changes by rotating velocity at the opposite plane intersection,
  including the required retrograde component. Preserve speed and return orbit.
- Add an engine-group selector, timed execution instructions, and residual
  finite-burn error estimates. Stock encounter validation remains impulsive;
  residual direction/position errors can still require correction in flight.
- Remove maneuver nodes through KSP's complete gizmo/map-target cleanup path,
  prevent clicks through the panel, and retry ranked alternatives when the
  stock solver rejects an encounter.
- Add headless tests that execute the production planner against independent
  Kepler propagation, including the saved Ice saving orbital/engine parameters.

## 1.0.6

- Treat every node on a near-circular parking orbit as a valid periapsis
  reference, matching the documented behavior.
- Reconstruct detached candidate orbits with the stock orbital-frame API,
  fixing a half-orbit phase error that prevented otherwise valid splits.
- Validated the `Ice saving` Eeloo maneuver from `quicksave`: the accepted
  two-node plan arrives 0.76 seconds later, reduces the largest burn from
  2614.1 m/s to 2370.7 m/s, and uses 104.38% of the original delta-v.

## 1.0.5

- Split the vessel's sole maneuver without requiring the stock gizmo selection,
  avoiding conflicts with alarm and map-marker overlays.
- Rename the panel action to "Split only maneuver" to make its scope explicit.

## 1.0.4

- Replaced the crowded stock-launcher icons with a visible, draggable map-view
  panel containing explicit Split and Undo buttons.
- Preserve the last selected maneuver across the panel click so KSP cannot
  invalidate the action by closing the maneuver gizmo first.

## 1.0.3

- Treat parking orbits below 0.001 eccentricity as near-circular, so their
  maneuver node itself is accepted as the periapsis reference.
- Log detailed original/generated node and SOI-entry acceptance metrics for
  end-to-end validation.

## 1.0.2

- Made stock-toolbar registration recover when another mod throws inside KSP's
  shared launcher-ready event.

## 1.0.1

- Replaced the flight-control-conflicting keyboard shortcuts with stock app
  launcher buttons for split and undo.

All notable changes to Simple Splitter are documented here.

## Unreleased

### Added

- Mod + Shift + S splits an eligible selected ejection node into two or three
  balanced apsis maneuvers.
- Period-aware resonant timing preserves the original final state and target
  encounter.
- Optional apoapsis plane changes are used only when they reduce total delta-v.
- Mod + Shift + Z restores the untouched generated plan to its original node.
