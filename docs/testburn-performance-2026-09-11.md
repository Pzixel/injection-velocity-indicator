# Simple Splitter performance validation — 2026-09-11

Build: unreleased 1.2.3, KSP 1.12.5, `modded2/testburn`, Laythe expedition.
This record covers the performance changes after the earlier trajectory
validation in `testburn-validation-2026-09-10.md`.

## Causes and changes

The long-running search was finite, but it tried a whole redistribution grid
for a rejected low-count row before checking later rows. Each numerical yield
also waited a game frame, including trivial work boundaries, and stock preview
nodes were built before rejecting unsuccessful timed paths.

The runner now batches numerical boundaries into measured 12 ms frame slices.
Scalar RK4 retains the same integration resolution and matches an independent
vector RK4 reference. Duplicate schedules are skipped before simulation. Initial
rows are all checked first; near-solutions receive incremental redistribution
within a shared two-second allowance, checked between batches. Completed batches
are cached; unfinished work can continue on a later comparison with matching inputs.
Every accepted row still completes finite execution and every stock prefix check.

Coasting roundoff no longer invalidates the selected maneuver during a search.
Recipes are refreshed against the live orbit for safety and application. Final
propulsion validation recomputes KER, as application already did, instead of
reading a seconds-old resource snapshot. Snapshot validation distinguishes engine
propellants from other carried mass. In a measured failure, cabin oxygen fell from
186.99758215838031 to 186.99757451062513 units; treating all supply amounts as engine
fuel falsely invalidated the comparison. Meaningful carried-mass changes and
engine-fuel changes still invalidate snapshots.

## Headless evidence

Both test suites and the production Release build pass. Tests cover:

- Independent cold searches through six and eight burns, with real timed replays,
  plane changes, stage capacities, and a deterministic collision oracle. The moon
  encounter decision is simulated in this test; KSP's solver remains a live check.
- Rejection and termination after exhausting all seven refinement batches for an
  unsafe count, without preventing usable higher-count rows.
- Budget exhaustion without accepting unsafe rows, followed by successful resume.
- Work batching, cancellation cleanup and preservation of explicit wait objects.
- Long nuclear burns against the original vector RK4: positions agree within
  0.00001 m, velocities within 0.0000001 m/s, cosine loss within 1e-10.
- Measured cabin oxygen consumption, the same resource used as engine propellant,
  significant supply-mass changes, stale snapshots, and fresh KER stage comparisons
  that reject meaningful propulsion loss.

Local headless measurements, including candidate refreshes and timed replays:
six burns 1.133 s (37 checked candidates); eight burns 1.448 s (45 candidates).
These are CPU test timings, not substitutes for game timings.

## Final live check

Completed in one final launch after the headless checks passed. The same running
game was used for all checks, reloading `testburn` between the cold measurements.
Installed build SHA-256:

```text
0A25F5CE82448F5BE140D09DA99BF36264A3A0EC5A32326BF6873AFF907AE484
```

| Comparison | Numerical search | Safety / redistribution | Total elapsed | Usable rows | Cached counts reused |
| --- | ---: | ---: | ---: | --- | ---: |
| Cold, maximum 8 | 1.004 s | 3.031 s | 4.130 s | 5–8 | 0 |
| Cold, maximum 6 | 0.628 s | 2.995 s | 3.687 s | 4–6 | 0 |
| Extend 6 to 8 | 0.359 s | 3.452 s | 3.880 s | 4–8 | 5 |

Total elapsed includes KER refreshes and coroutine/frame waits. Both requested
limits pass on this save: six below five seconds, eight below ten seconds.
The cached extension reuses counts 2–6, including rejected/unfinished search
state; it still refreshes and validates candidate trajectories. The cold eight
run did not find a safe four-burn row within its refinement allowance. The cold
six run and cached extension did. The UI reports tested alternatives and paused
refinement, rather than claiming mathematical impossibility.

Apply and Undo passed for both eight and four burns. Both include an explicit
plane change, and Undo restores the original single 2354.3 m/s maneuver.

| Applied plan | Total timed Δv | Timed Jool arrival difference | Stock Jool arrival difference |
| --- | ---: | ---: | ---: |
| 8 burns | 2406.1 m/s | +970.555 s | −3.904 s |
| 4 burns | 2427.5 m/s | +212.322 s | −0.300 s |

Both checks use the live original encounter as the reference and the allowed
21600-second arrival window. All accepted plans pass finite burn/coast safety
and the stock maneuver-prefix checks. The four-burn sequence is 372.0, 564.1,
13.3 (plane), and 1478.1 m/s, with durations 195.7, 280.1, 6.4, and 646.1 s.
This confirms redistribution of the initial kicks rather than equal splitting.

The search reports the best safe sampled plan found, not a global optimum or a
proof that a rejected count is impossible. Validation does not physically fly
the burns; execution must follow the modeled engine, direction and timing.
