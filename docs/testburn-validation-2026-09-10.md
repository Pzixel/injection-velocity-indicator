# Simple Splitter 1.2.3: testburn validation

Validated in the running KSP installation on 2026-09-10, using `modded2/testburn`
and the Laythe expedition vessel at normal time speed. The original maneuver
was 2354.3 m/s and targeted Jool.

## Acceptance and fixes

Both the nominal maneuver chain and the simulated timed execution must encounter
the original target within one day in the player's calendar (21600 seconds in
this save). Position and departure-vector errors are comparison metrics, not
independent rejection thresholds. Fuel, atmosphere, impact and unintended SOI
checks remain in effect.

The fixes cover fixed-frame orbital states, the search interval KSP needs to
detect encounters on copied coast patches, and refreshing a cached distribution's
resonance and burn timing against the live parking orbit. Small numerical changes
in the source orbit no longer require repeating the entire distribution search.
Rejected distributions are retried with redistributed stage delta-v.

## Live results

| Burns | Total timed delta-v, m/s | Additional delta-v, m/s | Result |
| --- | ---: | ---: | --- |
| 2 | — | — | No executable option in tested distributions |
| 3 | — | — | Tested alternatives missed the timed arrival window |
| 4 | 2427.5 | 73.1 | Safe option; Apply and Undo passed |
| 5 | 2414.2 | 59.8 | Safe option |
| 6 | 2414.7 | 60.4 | Safe option after extending the cached search |

The applied four-burn plan used stage 11 in order:

| Burn | Timed delta-v, m/s | Duration, seconds |
| --- | ---: | ---: |
| Kick | 372.0 | 195.7 |
| Kick | 564.1 | 280.1 |
| Plane change | 13.3 | 6.4 |
| Departure | 1478.1 | 646.1 |

On Apply, the finite-burn simulation reached Jool 211.19 seconds later than its
fresh original reference. The committed stock maneuver chain also passed, with
SOI entry 7193.93 seconds later than its original reference. Both are within the
21600-second window. These are separate validations with refreshed references.

Undo restored the original single maneuver. Increasing the maximum from five to
six then produced three safe choices and reported `Reused 4 burn counts.` Counts
two through five reused their numerical searches; live safety was checked again.
The final table was left open with the original maneuver restored.

The initial comparison was observed complete by approximately 130 seconds. It
remains a substantial search, even though the game continued advancing at normal
speed. The tests do not prove that the sampled search finds a global optimum or
that the unsuccessful lower counts are physically impossible.

## Verification evidence

The production build passed with zero warnings and errors. The headless test suite
passed, including testburn redistribution, fixed-frame orbit handling, stage
boundaries, cache reuse/invalidation, and refreshing a cached recipe after small
source-orbit perturbations. `git diff --check` passed.

The packaged and installed DLLs have the same SHA-256:

```text
27B3981025C6B8F4080838548D1583315BE700144D674884D8D738BBBCBBDA84
```

Relevant entries from KSP's `Player.log` during this run:

```text
Timed encounter: target=Jool, SOI-entry UT=556724297.24158871, difference=211.19439804553986 s.
Accepted encounter: target=Jool, original SOI-entry UT=556726037.8936708, new SOI-entry UT=556733231.82622182, difference=7193.9325510263443 s, tolerance=21600 s.
Restored the original maneuver node.
3 safe options. Choose a row to apply. Reused 4 burn counts.
```

Validation used KSP's patched-conic solver and the mod's finite-burn integration.
The burns were not physically flown. Actual execution still depends on following
the modeled thrust, attitude and timing.

## Completion audit, 2026-09-11

Rechecked the current source, installed/package DLL hashes and original KSP log.
The hashes still match the validated build above; the log retains the successful
timed encounter, Apply/Undo and three-option cache-extension results. Inspected
the current per-prefix and finite-path checks: both reject unsafe paths, and the
timed path must actually enter the target SOI within the calendar-day window.
Reran `dotnet run --project tests/SimpleSplitter.Tests -c Release`; all tests
passed, including 57 redistributed four-burn alternatives in the regression.
No additional code changes were needed for this audit.
