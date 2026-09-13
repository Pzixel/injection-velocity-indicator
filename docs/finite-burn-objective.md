# Finite-burn objective and conversion

## Decision

Match the **position and velocity of the actual finite-burn execution** to the
original ideal trajectory at the **same time**, then minimize total expended Δv
among plans that achieve the match. Keep the player's maximum maneuver count as
a hard constraint. This is a bounded search for a good executable plan, not a
proof of global optimality.

For candidate controls `u`, integrate the actual burns and coasts to obtain
`x_u(t) = (r_u(t), v_u(t))`. The original maneuver defines `x*(t)` once, before
any candidate is adjusted. At a common epoch `t_b`, define

```
dr = r_u(t_b) - r*(t_b)
dv = v_u(t_b) - v*(t_b)
H  = t_b - original maneuver UT
J_state = sqrt(dot(dr, dr) + H² dot(dv, dv))
```

The shooting solver uses the six residual components `(dr, H dv) / r0`, with
`r0` the original maneuver radius. The scaling improves numerical conditioning;
it does not change the six zero constraints. It is a full-state error expressed
in metres, **not an estimate of literal RMS trajectory separation**.

The reference epoch is selected from the original trajectory, approximately
halfway from the departure radius to the body's SOI radius, limited to a quarter
of the original time to encounter. For bound reference orbits, it is also limited
to a quarter period. Every candidate must finish thrust before this epoch.

Selection within a maneuver count is lexicographic:

1. A match within **1 m position and 0.001 m/s velocity** beats an unmatched plan.
2. Among matches, minimize the sum of the actual burns' rocket-equation Δv.
3. If no candidate matches, minimize `J_state`, with a one-metre score band,
   then Δv. Such an approximation must still pass the actual encounter checks.

Numerical tolerances are not player accuracy guarantees. The solver has a finite
iteration allowance and cannot promise to find a feasible plan whenever one exists.

## Why the old allocation was wrong

The old planner did simulate finite burns, but ranked the outgoing **asymptotic
velocity vector**. This leaves departure position and time of flight unconstrained.
A spacecraft sixty seconds behind on exactly the same hyperbola has the same
asymptotic velocity. A local regression constructs this counterexample: outgoing
velocity difference is below `1e-7 m/s`, while full-state and sampled trajectory
separation both exceed 100 km.

The candidate space also restricted the earlier burns to bound return orbits
and left one final departure burn. Moving a split from S11 to S10 changed the
resonant coast geometry and the final targeting result. The allocation with
three short S11 burns could therefore win a small improvement in that incomplete
score despite leaving the important finite-duration error unresolved. S10 was
not excluded by a TWR rule; alternatives lost the score/shortlist competition.
Full simulation was limited to three layouts per energy sample, with nominal
setup-energy ordering, and safety selection accepted the first safe candidate.

The installed binary and checkout were not identical during the investigation.
The final installed log showed S11 burns of **3.627 / 3.565 / 3.504 s**, followed
by S10 burns of **268.688 / 255.512 / 814.625 s**, totaling **2254.5 m/s**.
These are the final logged values, rather than the approximately 287 s durations
in the initial report. A headless replay of the then-current checkout preferred
the same `3/2` setup allocation: its outgoing-velocity error was about
5.338 m/s versus 5.711 m/s for `1/4`. That narrow score advantage does not establish
which finite execution follows the intended trajectory more closely.

## Alternatives considered

| Objective | What it captures | Limitation / decision |
|---|---|---|
| Sampled trajectory position RMS | Actual separation over several common post-burn times | Good alternative and useful diagnostic. Sampling interval changes conditioning and weighting; tightly grouped samples give weak velocity information. |
| Full terminal position and velocity | Six state components, including phase and outgoing direction | Chosen: directly enforces the paper's boundary conditions and requires only one coast propagation per residual evaluation. |
| Estimated two-impulse correction effort | Approximate cost of removing both position and velocity error | Competitive alternative. A constant-velocity surrogate omits gravity in the hypothetical correction; a true Lambert/STM correction adds machinery and horizon choices. |
| Outgoing asymptotic velocity | Escape speed and asymptotic direction | Incomplete: cannot distinguish phase offsets along the same hyperbola. Retained only as a diagnostic. |
| Mean burn duration / cosine loss | How spread out thrust is in time or orbital angle | Useful search heuristic, insufficient trajectory objective. It omits placement, accumulated coast error, and compensation between burns. |

At fixed count and unchanged propulsion, average duration is just total engine-on
time divided by that count. It says little about where to place a burn. Across
counts, adding pieces can reduce the average without improving the path. A raw
cosine diagnostic is also not integrated Δv loss: it measures angular spread and
does not account for the altered gravity history or the phase error carried into
subsequent burns.

The cheap seed ordering uses `sum(Δv_i (omega_i duration_i)² / 24)`. The `1/24`
factor comes from the small-angle expansion of a uniformly spread, centred
impulse. This is only a dimensional angular-spread proxy. It naturally values
splitting a long burn more than splitting an already short one, without stage
names or TWR thresholds. **Actual full-state conversion still chooses the winner.**

For the correction-effort alternative, a constant-velocity model gives correcting
impulses `-(dv + dr/H)` now and `dr/H` at the horizon. The experiment minimizes
their squared magnitudes, scaled by `H²`. It is a competitive residual weighting,
not a claimed exact minimum correction Δv in a gravity field.

## Relationship to the NASA paper

Fogel et al., [*Multi-Impulse to Time Optimal Finite Burn Trajectory Conversion* (2020)](https://ntrs.nasa.gov/api/citations/20200000238/downloads/20200000238.pdf),
Figures 3–4, provide the key logic: convert impulsive maneuvers to finite burns,
match the reference position **and** velocity, then relax interior rendezvous
constraints when optimizing a multi-burn sequence while maintaining feasible
coasts and the final boundary.

This implementation preserves that boundary/feasibility logic for the user's
discrete fixed-direction maneuver model. It does **not** implement the paper's
full continuous-steering costate solution, switching-function equalities, or
Copernicus optimal-control software. The linked publication is an extended
abstract, not a complete reproducible solver implementation. Its burn-time/fuel
objective is adapted to total expended Δv across different propulsion segments,
as requested for this mod.

Setup resonance solutions provide inexpensive starting paths. For each output
count, conversion also tries lower-count seeds with their final departure split
into more finite burns. Interior states of those departure parts are free: they
can already be hyperbolic. The variables are the first ignition time, Δv and two
fixed-direction angles for each part, and independent positive coast durations.
Burns respect real staged mass flow and fuel capacity; coasts leave 30 seconds
for player reorientation. Setup placement remains a sampled resonance family,
so this is not exhaustive arbitrary-orbit optimal control.

A damped finite-difference shooting correction runs for at most 14 iterations
with bounded steps and backtracking. Once feasible, up to two fuel-polishing
steps project the expenditure gradient into the constraint Jacobian's nullspace
and re-correct the state. Failed polishing cannot replace a feasible result.
There is no arbitrary weighted trade of kilometres of error against fuel savings.

## Local comparison and regression

Run:

```
dotnet run --project tests/SimpleSplitter.Tests -c Release
dotnet run --project tests/SimpleSplitter.Tests -c Release -- --conversion-study
dotnet run --project tests/SimpleSplitter.Tests -c Release -- --conversion-smoke
```

The study uses the same 11 starting layouts, six-command limit, controls,
integration resolution, iteration bounds, terminal acceptance tolerances, and
fuel polishing for all three competitive residuals. Only the residual supplying
the shooting correction changes. RMS samples are at 25%, 62.5% and 100% of the
common departure horizon; these lie after all fixture burns. These measurements
use the `quicksave #3` two-stage orbit/propulsion snapshot embedded in the test.

| Shooting residual | Terminal position, m | Terminal velocity, m/s | Sampled coast RMS, m | Total Δv, m/s |
|---|---:|---:|---:|---:|
| Full state | 0.701 | 0.0000504 | 0.489 | 2224.720 |
| Position RMS | 0.664 | 0.0000485 | 0.461 | 2224.718 |
| Correction effort | 0.740 | 0.0000465 | 0.577 | 2224.723 |

All three find comparably good solutions; these tiny differences do not establish
a universal numerical winner. Full state is selected for its direct constraint
meaning and lower dependence on sample placement. An additional RMS experiment
with samples at 100%, 110% and 120% of the horizon stalled at approximately 2 km
RMS within the same iteration limit, illustrating the conditioning sensitivity.

The production six-burn winner uses approximate durations:

| Propulsion | Burn durations |
|---|---|
| S11 | **10.7 s**, one complete stage burn |
| S10 setup | **177.9 / 172.3 / 167.0 s** |
| S10 departure | **460.8 / 344.1 s**, separately steered burns with a coast |

This saves approximately 29.8 m/s against the installed final log while targeting
the complete original trajectory state. It does not merely minimize the longest
burn. Renaming the stages leaves the numerical result unchanged.

An independent vector RK4 replay reconstructs directions from each next stock
command on the actual pre-burn orbit and uses 0.1 s steps, versus the production
0.5 s maximum for these burns. It measures **0.694 m** terminal position error
and **0.0000504 m/s** velocity error. Other regressions cover eccentric parking
orbits, normal/antinormal maneuvers, a different departure body, staging,
atmosphere/SOI rejection, live refresh, cache expansion through ten burns, and
ranking changes after refresh.

Local headless cold search plus refreshed finite replays in the existing
collision-retry benchmark took approximately **0.9 s through six burns** and
**1.8 s through eight burns** in this run. The earlier implementation measured
approximately 1.2 s and 1.5 s respectively during the investigation. Wall times
vary with JIT/load. These are not KSP/Mono frame-time measurements; live moon
encounter calls are replaced by a deterministic collision oracle in this benchmark.
The production coroutine retains its 4 ms / 16-new-integrations slice budget.

## Execution and validation consequences

The generated vector encodes an actual finite command on the predicted pre-burn
orbit. After completing and removing a node, align to the next remaining node
just before ignition, lock inertial attitude, and burn at full throttle for the
specified duration. Do not keep steering toward a changing residual maneuver
marker. The next burn may have a different fixed direction.

An instantaneous multi-node map preview cannot represent this finite trajectory
exactly. Accordingly, the validator follows integrated thrust arcs and actual
coasts, including the initial wait, using independent stock patched-conic copies
for encounters. It checks the real target arrival and separately checks that KSP
retains the command nodes. Apply repeats live validation. Undo and temporary
preview restoration remain transactional.

Local tests and the production build do not establish live KSP solver behavior
or pilot accuracy. KSP was not launched and no files were installed into the game.
User validation should check command retention, sequential attitude/timer
execution, and the actual resulting encounter on the saved vessel.
