# Complete Resolution: Direct Ballistic Dominance Is False

## Summary

Prove that neither strategy universally dominates:

\[
\exists\,\mathcal I_1:\Delta v_{\rm node}<\Delta v_{\rm ballistic},\qquad
\exists\,\mathcal I_2:\Delta v_{\rm ballistic}<\Delta v_{\rm node},
\]

with equality in a coplanar instance. Thus the strongest universal ordering statement is that the two optimized transfer classes are incomparable.

No software interface is involved. Introduce the proof notation

\[
H(x)=\sqrt{x^2+2c^2},\qquad F(x)=H(x)-c.
\]

## Exact Counterexample and Global Certificates

- Normalize the primary parameter and planetary orbital radius to one. Use two circular planetary orbits whose planes meet along the \(x\)-axis at inclination \(I\), singleton departure/arrival epochs, and

  \[
  T=2\pi(3/2)^{3/2},\quad q_\odot\ge1,\quad
  \rho=10^{-6},\quad\mu_p=10^{-10},\quad c=1/100.
  \]

- Construct explicit common initial and final circular planetocentric states at radius \(\rho\). Choose their periapsis directions so the prograde node-transfer excess velocities attain the exact endpoint costs \(F(s)\), where

  \[
  v_p=2/\sqrt3,\quad v_a=1/\sqrt3,\quad s=v_p-1.
  \]

- Prove the endpoint lemma: every admissible escape or capture with excess magnitude \(x\) costs at least \(F(x)\), with equality exactly for a prograde tangential periapsis burn.

- Exhaust the node class globally. The solar periapsis floor, common node, and fixed time force one ellipse with \(a=3/2,e=1/3\), an interior plane change at aphelion \(-2e_x\), and no multirevolution or radial alternatives. Enumerate all four prograde/retrograde sign branches and obtain

  \[
  \Delta v_{\rm node}
  =2F(s)+\frac{2}{\sqrt3}\sin(I/2).
  \]

- Exhaust the direct class globally. Any admissible same-position Kepler arc must be the unique one-revolution \(a=3/2,e=1/3\) ellipse, with endpoint velocity \(v_pu\), \(u\perp e_x\). Enforce the fixed departure state through

  \[
  (1/3-z^2)(c^2+z^2)-2z(c^2+s^2)=0.
  \]

  Use a Sturm sequence to isolate \(z=s\) and the sole additional admissible root in \((0.483754,0.483755)\); exact interval substitution into the fixed target-periapsis condition excludes both directions associated with the latter root. This leaves one feasible direct branch and supplies its exact radical cost, establishing its global optimum rather than merely a lower bound.

- Set \(I=\pi/2\). Certify symbolically that

  \[
  \Delta v_{\rm ballistic}\ge\sqrt2-\frac1{50}
  >2F(s)+\sqrt{\frac23}
  =\Delta v_{\rm node}.
  \]

  Include the exact direct-optimum expression and rational isolating intervals alongside this shorter inequality proof.

## Reverse Ordering, Equality, and Alternatives

- Set \(\sin(I/2)=1/20\) and reuse the same construction. The unrotated direct ellipse gives a certified feasible upper bound strictly below the exact node optimum, proving \(\Delta v_{\rm ballistic}<\Delta v_{\rm node}\).

- At \(I=0\), the zero-angle plane change and the direct arc both attain \(2F(s)\); endpoint lower bounds prove equality.

- Briefly classify alternatives outside the two definitions: arbitrary deep-space maneuvers, split plane changes, bi-elliptic transfers, and combined arrival burns belong to a broader multi-impulse class. If its optimum is denoted \(\Delta v_{\rm free}\), only

  \[
  \Delta v_{\rm free}\le
  \min(\Delta v_{\rm ballistic},\Delta v_{\rm node})
  \]

  follows automatically; these alternatives do not alter the requested comparison.

## Verification and Assumptions

- Audit independently through three approach families: exact Kepler-arc classification, real-algebraic/Sturm certification, and a separate noncollinear or singular-limit counterexample.
- Require adversarial checks of identical endpoint states, all revolution/sign/node branches, periapsis constraints, and the prohibition on merging the pure node burn with insertion.
- Use no grids or local numerical optimization; decimals may illustrate only after exact inequalities and root certificates are supplied.
- Interpret the stated ideal patched-conic model as instantaneous center-point \(v_\infty\) patching, periapsis constraints as osculating-conic constraints, and the node maneuver as a separately counted pure rotation followed by a genuine coast. Finite-SOI patching is a different model and will be identified as such.
