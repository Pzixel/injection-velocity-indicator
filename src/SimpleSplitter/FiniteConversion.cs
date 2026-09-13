using System;
using System.Collections.Generic;

namespace SimpleSplitter
{
    internal sealed class FiniteConversionResult
    {
        internal FiniteConversionResult(List<NodeSpec> nodes, Orbit executed, TrajectoryError error,
            double totalDeltaV, double setupCosineLoss, double departureCosineLoss)
        {
            Nodes = nodes; Executed = executed; Error = error; TotalDeltaV = totalDeltaV;
            SetupCosineLoss = setupCosineLoss; DepartureCosineLoss = departureCosineLoss;
        }
        internal List<NodeSpec> Nodes { get; }
        internal Orbit Executed { get; }
        internal TrajectoryError Error { get; }
        internal double TotalDeltaV { get; }
        internal double SetupCosineLoss { get; }
        internal double DepartureCosineLoss { get; }
        internal double CosineLoss => Math.Max(SetupCosineLoss, DepartureCosineLoss);
        internal bool MatchesReference => Error.MatchesReference;
    }

    // A bounded direct shooting conversion for the player's discrete fixed-
    // direction burns. The paper's pseudo-rendezvous boundary is preserved;
    // its continuously steered costate solution is not executable as stock
    // maneuver nodes. Interior departure constraints are relaxed, and positive
    // coasts, staged fuel, and the final full-state boundary remain explicit.
    internal static class FiniteConversion
    {
        internal const double CoastSeconds = 30;

        internal static FiniteConversionResult? Convert(SplitRequest request, SplitCandidate seed,
            int pieces, TrajectoryReference reference, Func<Orbit, double[]>? residual = null)
        {
            if (pieces < 1 || seed.Nodes.Count - 1 + pieces > request.MaximumBurns || !reference.IsUsable) return null;
            var prefix = new List<NodeSpec>();
            Orbit nominal = request.SourceOrbit, executed = nominal;
            StageCursor fuel = request.Propulsion.Cursor();
            double prefixDeltaV = 0, prefixCosine = 0, earliest = request.Now + CoastSeconds;
            for (int i = 0; i + 1 < seed.Nodes.Count; i++)
            {
                NodeSpec source = seed.Nodes[i];
                if (!fuel.IsUsable) return null;
                Vector3d direction = source.InertialDirection ??
                    (SplitPlanner.NodeRotation(nominal, source.Ut) * source.DeltaV).xzy.normalized;
                nominal.GetFixedState(source.Ut, out Vector3d r, out Vector3d v);
                Orbit after = SplitPlanner.OrbitFromState(r,
                    v + (SplitPlanner.NodeRotation(nominal, source.Ut) * source.DeltaV).xzy, nominal.referenceBody, source.Ut);
                var command = new NodeSpec(source.Ut,
                    SplitPlanner.ToNodeCoordinates(executed, source.Ut, direction * source.BurnDeltaV),
                    source.Duration, source.StartOffset, source.Purpose, source.BurnDeltaV, direction);
                FiniteBurnEstimate estimate = FiniteBurnEstimate.Measure(nominal, after, command, fuel.Engine, 0, executed, out executed);
                if (!estimate.IsFinite || !fuel.Consume(source.BurnDeltaV)) return null;
                prefix.Add(command);
                prefixDeltaV += source.BurnDeltaV;
                prefixCosine = Math.Max(prefixCosine, estimate.CosineLoss);
                earliest = source.Ut + source.Duration - source.StartOffset + CoastSeconds;
                nominal = after;
            }
            if (!fuel.IsUsable) return null;
            NodeSpec departure = seed.Nodes[seed.Nodes.Count - 1];
            var problem = new Problem(reference, executed, fuel.Engine, fuel.Remaining, fuel.Stage,
                departure.InertialDirection ?? (SplitPlanner.NodeRotation(nominal, departure.Ut) * departure.DeltaV).xzy.normalized,
                departure, pieces, earliest, prefix, prefixDeltaV, prefixCosine, residual);
            return problem.Solve();
        }

        private sealed class Problem
        {
            private readonly TrajectoryReference reference;
            private readonly Orbit before;
            private readonly BurnPhysics engine;
            private readonly double capacity, seedDeltaV, pieceDeltaV, start, timeScale, earliest;
            private readonly double prefixDeltaV, prefixCosine;
            private readonly int stage, pieces;
            private readonly Vector3d direction, tangent, normal;
            private readonly List<NodeSpec> prefix;
            private readonly Func<Orbit, double[]> residual;

            internal Problem(TrajectoryReference reference, Orbit before, BurnPhysics engine, double capacity, int stage,
                Vector3d direction, NodeSpec seed, int pieces, double earliest, List<NodeSpec> prefix,
                double prefixDeltaV, double prefixCosine, Func<Orbit, double[]>? residual)
            {
                this.reference = reference; this.before = before; this.engine = engine; this.capacity = capacity;
                this.residual = residual ?? reference.Residual;
                this.stage = stage; this.pieces = pieces; this.earliest = earliest; this.prefix = prefix;
                this.prefixDeltaV = prefixDeltaV; this.prefixCosine = prefixCosine;
                this.direction = direction.normalized;
                Vector3d axis = Math.Abs(direction.x) < .8 ? new Vector3d(1, 0, 0) : new Vector3d(0, 1, 0);
                tangent = Vector3d.Cross(this.direction, axis).normalized;
                normal = Vector3d.Cross(this.direction, tangent).normalized;
                seedDeltaV = seed.BurnDeltaV;
                pieceDeltaV = seedDeltaV / pieces;
                double elapsed = 0, centroid = 0, used = 0;
                for (int i = 0; i < pieces; i++)
                {
                    centroid += pieceDeltaV * (elapsed + engine.StartOffset(pieceDeltaV, used));
                    elapsed += engine.Duration(pieceDeltaV, used) + (i + 1 < pieces ? 2 * CoastSeconds : 0);
                    used += pieceDeltaV;
                }
                // Preserve the acceleration-weighted impulse centre when a
                // departure is subdivided. No TWR or stage identity heuristic.
                start = seed.Ut - seed.StartOffset + engine.StartOffset(seedDeltaV) - centroid / seedDeltaV;
                timeScale = Math.Max(1, elapsed * .1);
            }

            private FiniteConversionResult? Trial(double[] x)
            {
                double ignition = start + x[0] * timeScale;
                if (ignition < earliest) return null;
                Orbit actual = before;
                double used = 0, cosine = 0;
                var nodes = new List<NodeSpec>(prefix);
                for (int i = 0; i < pieces; i++)
                {
                    double dv = pieceDeltaV * (1 + .05 * x[1 + 3 * i]);
                    if (!CandidateRules.IsFinite(dv) || dv <= 0 || used + dv > capacity + 1e-6) return null;
                    double yaw = .1 * x[2 + 3 * i], pitch = .1 * x[3 + 3 * i];
                    Vector3d thrust = (direction * Math.Cos(yaw) + tangent * Math.Sin(yaw)) * Math.Cos(pitch) + normal * Math.Sin(pitch);
                    var currentEngine = new BurnPhysics(engine.MassAfter(used), engine.Thrust, engine.ExhaustVelocity);
                    double duration = currentEngine.Duration(dv), offset = currentEngine.StartOffset(dv);
                    double ut = ignition + offset, end = ignition + duration;
                    if (!CandidateRules.IsFinite(end) || end >= reference.Epoch) return null;
                    // Crossing an SOI during these short coasts is independently
                    // checked by the stock finite-path validator on finalists.
                    double coastStart = i == 0 ? earliest - CoastSeconds :
                        nodes[nodes.Count - 1].Ut + nodes[nodes.Count - 1].Duration - nodes[nodes.Count - 1].StartOffset;
                    if (coastStart < ignition && !StockTrajectorySafety.AboveAtmosphere(actual, coastStart, ignition)) return null;
                    var node = new NodeSpec(ut, SplitPlanner.ToNodeCoordinates(actual, ut, thrust * dv),
                        duration, offset, (pieces == 1 ? "Departure" : "Departure part " + (i + 1)) + " (stage " + stage + ")", dv, thrust);
                    FiniteBurnEstimate estimate = FiniteBurnEstimate.Measure(actual, reference.Target, node,
                        currentEngine, 0, actual, out Orbit after);
                    if (!estimate.IsFinite) return null;
                    actual = after;
                    nodes.Add(node);
                    cosine = Math.Max(cosine, estimate.CosineLoss);
                    used += dv;
                    // Independent positive coasts let later maneuvers move
                    // along the outgoing orbit. Keep a player reorientation
                    // interval without introducing an active inequality kink.
                    if (i + 1 < pieces)
                        ignition = end + CoastSeconds * (1 + Math.Exp(x[1 + 3 * pieces + i]));
                }
                TrajectoryError error = reference.Measure(actual);
                return error.IsFinite ? new FiniteConversionResult(nodes, actual, error, prefixDeltaV + used, prefixCosine, cosine) : null;
            }

            internal FiniteConversionResult? Solve()
            {
                double[] x = new double[4 * pieces];
                FiniteConversionResult? current = Trial(x);
                if (current == null)
                {
                    foreach (double shift in new[] { 1.0, -1.0, 2.0, -2.0 })
                    {
                        x[0] = shift;
                        current = Trial(x);
                        if (current != null) break;
                    }
                }
                if (current == null) return null;
                FiniteConversionResult best = current;
                double damping = 1e-4;
                for (int iteration = 0; iteration < 14; iteration++)
                {
                    if (current.MatchesReference) break;
                    double[] residual = this.residual(current.Executed);
                    double[,] jacobian = Jacobian(x, residual);
                    double[] step = Step(jacobian, residual, damping);
                    double scale = 1;
                    foreach (double value in step) if (Math.Abs(value) > 2) scale = Math.Min(scale, 2 / Math.Abs(value));
                    bool improved = false;
                    for (int line = 0; line < 6; line++)
                    {
                        double[] next = Add(x, step, scale);
                        FiniteConversionResult? candidate = Trial(next);
                        if (candidate != null && Merit(candidate) < Merit(current))
                        {
                            x = next; current = candidate; improved = true;
                            if (Better(candidate, best)) best = candidate;
                            damping = Math.Max(1e-9, damping * .3);
                            break;
                        }
                        scale *= .5;
                    }
                    if (!improved) damping *= 10;
                    if (damping > 1e5) break;
                }
                // Feasibility before economy: reduce fuel only along directions
                // tangent to the six terminal constraints, then re-correct.
                if (current.MatchesReference)
                    for (int polish = 0; polish < 2; polish++)
                    {
                        double[] residual = this.residual(current.Executed);
                        double[,] jacobian = Jacobian(x, residual);
                        var gradient = new double[x.Length];
                        for (int i = 0; i < pieces; i++) gradient[1 + 3 * i] = 1.0 / pieces;
                        double[] projected = Project(jacobian, gradient);
                        double magnitude = 0;
                        foreach (double value in projected) magnitude = Math.Max(magnitude, Math.Abs(value));
                        if (magnitude < 1e-6) break;
                        bool accepted = false;
                        for (int line = 0; line < 3; line++)
                        {
                            double[] next = Add(x, projected, -Math.Pow(.5, line) / magnitude);
                            FiniteConversionResult? candidate = Trial(next);
                            if (candidate == null) continue;
                            for (int correction = 0; correction < 3 && !candidate.MatchesReference; correction++)
                            {
                                double[] r = this.residual(candidate.Executed);
                                double[] correctionStep = Step(Jacobian(next, r), r, 1e-7);
                                next = Add(next, correctionStep, 1);
                                candidate = Trial(next);
                                if (candidate == null) break;
                            }
                            if (candidate != null && candidate.MatchesReference && candidate.TotalDeltaV < current.TotalDeltaV - .001)
                            { x = next; current = candidate; if (Better(candidate, best)) best = candidate; accepted = true; break; }
                        }
                        if (!accepted) break;
                    }
                return best;
            }

            private double[,] Jacobian(double[] x, double[] residual)
            {
                var result = new double[residual.Length, x.Length];
                for (int j = 0; j < x.Length; j++)
                {
                    double epsilon = 1e-4;
                    var trial = (double[])x.Clone(); trial[j] += epsilon;
                    FiniteConversionResult? candidate = Trial(trial);
                    if (candidate == null)
                    { epsilon = -epsilon; trial[j] = x[j] + epsilon; candidate = Trial(trial); }
                    if (candidate == null) continue;
                    double[] r = this.residual(candidate.Executed);
                    for (int i = 0; i < r.Length; i++) result[i, j] = (r[i] - residual[i]) / epsilon;
                }
                return result;
            }
            private double Merit(FiniteConversionResult candidate)
            {
                double sum = 0;
                foreach (double value in residual(candidate.Executed)) sum += value * value;
                return sum;
            }
            private bool Better(FiniteConversionResult a, FiniteConversionResult b)
            {
                if (a.MatchesReference != b.MatchesReference) return a.MatchesReference;
                if (a.MatchesReference) return a.TotalDeltaV < b.TotalDeltaV;
                return Merit(a) < Merit(b);
            }
        }

        private static double[] Add(double[] x, double[] step, double scale)
        { var result = new double[x.Length]; for (int i = 0; i < x.Length; i++) result[i] = x[i] + scale * step[i]; return result; }

        private static double[] Step(double[,] jacobian, double[] residual, double damping)
        {
            int n = jacobian.GetLength(1);
            var a = new double[n, n]; var b = new double[n];
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < residual.Length; k++) b[i] -= jacobian[k, i] * residual[k];
                for (int j = 0; j < n; j++) for (int k = 0; k < residual.Length; k++) a[i, j] += jacobian[k, i] * jacobian[k, j];
                a[i, i] += damping * Math.Max(1e-10, a[i, i]);
            }
            return SolveLinear(a, b);
        }

        private static double[] Project(double[,] jacobian, double[] gradient)
        {
            int n = gradient.Length, rows = jacobian.GetLength(0);
            var a = new double[rows, rows]; var b = new double[rows];
            for (int i = 0; i < rows; i++)
            {
                for (int k = 0; k < n; k++) b[i] += jacobian[i, k] * gradient[k];
                for (int j = 0; j < rows; j++) for (int k = 0; k < n; k++) a[i, j] += jacobian[i, k] * jacobian[j, k];
                a[i, i] += 1e-10;
            }
            double[] y = SolveLinear(a, b);
            var result = (double[])gradient.Clone();
            for (int i = 0; i < n; i++) for (int k = 0; k < rows; k++) result[i] -= jacobian[k, i] * y[k];
            return result;
        }

        private static double[] SolveLinear(double[,] matrix, double[] rhs)
        {
            int n = rhs.Length;
            for (int i = 0; i < n; i++)
            {
                int pivot = i;
                for (int j = i + 1; j < n; j++) if (Math.Abs(matrix[j, i]) > Math.Abs(matrix[pivot, i])) pivot = j;
                for (int k = i; k < n; k++) { double temp = matrix[i, k]; matrix[i, k] = matrix[pivot, k]; matrix[pivot, k] = temp; }
                double b = rhs[i]; rhs[i] = rhs[pivot]; rhs[pivot] = b;
                double divisor = matrix[i, i];
                if (!CandidateRules.IsFinite(divisor) || Math.Abs(divisor) < 1e-24) return new double[n];
                for (int k = i; k < n; k++) matrix[i, k] /= divisor;
                rhs[i] /= divisor;
                for (int j = 0; j < n; j++) if (j != i)
                {
                    double factor = matrix[j, i];
                    for (int k = i; k < n; k++) matrix[j, k] -= factor * matrix[i, k];
                    rhs[j] -= factor * rhs[i];
                }
            }
            return rhs;
        }
    }
}
