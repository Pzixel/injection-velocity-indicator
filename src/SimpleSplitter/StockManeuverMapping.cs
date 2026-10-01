using System;
using System.Collections.Generic;

namespace SimpleSplitter
{
    // An effective impulse need not equal the integrated thrust impulse.
    // Fit stock times/magnitudes while preserving ignition, duration, fuel and
    // the direction obtained from each node on the actual pre-burn orbit.
    internal static class StockManeuverMapping
    {
        internal static List<NodeSpec> Create(SplitRequest request, SplitCandidate candidate)
            => Create(request, candidate.Nodes, candidate.ConversionSeed?.Nodes, request.SourceOrbit, 0);

        internal static List<NodeSpec> Create(SplitRequest request, IList<NodeSpec> commands,
            IList<NodeSpec>? seeds, Orbit source, int completed)
        {
            if (completed < 0 || completed >= commands.Count || source.referenceBody != request.SourceOrbit.referenceBody)
                throw new InvalidOperationException("The remaining maneuvers cannot be mapped from this orbit.");
            var problem = new Mapping(request, commands, seeds, source, completed);
            return problem.Run();
        }

        private sealed class Mapping
        {
            private readonly SplitRequest request;
            private readonly IList<NodeSpec> commands;
            private readonly IList<NodeSpec>? seeds;
            private readonly List<Orbit> before = new List<Orbit>();
            private readonly List<Vector3d> directions = new List<Vector3d>();
            private readonly Vector3d targetR, targetV;
            private readonly double scale;
            private readonly Orbit source;
            private readonly int completed;

            internal Mapping(SplitRequest request, IList<NodeSpec> commands, IList<NodeSpec>? seeds, Orbit source, int completed)
            {
                this.request = request; this.commands = commands; this.seeds = seeds;
                this.source = source; this.completed = completed;
                scale = request.Reference.Scale;
                Orbit actual = source;
                StageCursor fuel = request.Propulsion.Cursor();
                foreach (NodeSpec node in commands)
                {
                    before.Add(actual);
                    directions.Add(node.InertialDirection ?? (SplitPlanner.NodeRotation(actual, node.Ut) * node.DeltaV).xzy.normalized);
                    if (before.Count <= completed)
                    {
                        if (!fuel.Consume(node.BurnDeltaV)) throw new InvalidOperationException("Invalid completed-burn budget.");
                        continue;
                    }
                    var estimate = FiniteBurnEstimate.Measure(actual, request.Reference.Target, node, fuel.Engine, 0, actual, out actual);
                    if (!estimate.IsFinite || !fuel.Consume(node.BurnDeltaV)) throw new InvalidOperationException("Unable to map an invalid finite burn.");
                }
                actual.GetFixedState(request.Reference.Epoch, out targetR, out targetV);
            }

            internal List<NodeSpec> Run()
            {
                var result = new List<NodeSpec>();
                Orbit stock = source;
                int tail = Math.Max(completed, commands.Count - 2);
                for (int i = completed; i < tail; i++)
                {
                    double until = commands[i + 1].Ut - commands[i + 1].StartOffset;
                    before[i + 1].GetFixedState(until, out Vector3d target, out _);
                    Fit(stock, i, 1, orbit => {
                        orbit.GetFixedState(until, out Vector3d r, out _);
                        Vector3d dr = (r - target) / scale;
                        return new[] { dr.x, dr.y, dr.z };
                    }, result, out stock);
                }
                double[] Boundary(Orbit orbit) {
                    orbit.GetFixedState(request.Reference.Epoch, out Vector3d r, out Vector3d v);
                    Vector3d dr = (r - targetR) / scale;
                    Vector3d dv = (v - targetV) * (request.Reference.Horizon / scale);
                    return new[] { dr.x, dr.y, dr.z, dv.x, dv.y, dv.z };
                }
                double Error(Orbit orbit) { double sum = 0; foreach (double value in Boundary(orbit)) sum += value * value; return sum; }
                Fit(stock, tail, commands.Count - tail, Boundary, result, out stock);
                // Two effective impulses suffice for the planar boundary. A
                // three-dimensional departure can need earlier
                // mapping variables too. Reuse the already fitted map instead
                // of starting a new physical optimization.
                double error = Error(stock);
                while (tail > completed && error * scale * scale > 1)
                {
                    tail--;
                    Orbit prefix = source;
                    var next = new List<NodeSpec>();
                    for (int i = completed; i < tail; i++)
                    {
                        NodeSpec n = result[i - completed];
                        prefix.GetFixedState(n.Ut, out Vector3d r, out Vector3d v);
                        prefix = SplitPlanner.OrbitFromState(r, v + (SplitPlanner.NodeRotation(prefix, n.Ut) * n.DeltaV).xzy, prefix.referenceBody, n.Ut);
                        next.Add(n);
                    }
                    Fit(prefix, tail, commands.Count - tail, Boundary, next, out Orbit fitted, result);
                    double trialError = Error(fitted);
                    if (trialError < error) { result = next; stock = fitted; error = trialError; }
                }
                return result;
            }

            private NodeSpec? Node(int i, double time, double magnitude)
            {
                NodeSpec command = commands[i];
                double ignition = command.Ut - command.StartOffset;
                double ut = command.Ut + time * Math.Max(1, command.Duration * .1);
                double dv = command.BurnDeltaV * (1 + .1 * magnitude);
                if (!CandidateRules.IsFinite(ut) || ut < ignition || ut > ignition + command.Duration || dv <= 0 || !CandidateRules.IsFinite(dv)) return null;
                return new NodeSpec(ut, SplitPlanner.ToNodeCoordinates(before[i], ut, directions[i] * dv),
                    command.Duration, ut - ignition, command.Purpose, command.BurnDeltaV, directions[i]);
            }

            private void Fit(Orbit source, int first, int count, Func<Orbit, double[]> residual,
                List<NodeSpec> result, out Orbit after, IList<NodeSpec>? initial = null)
            {
                double[] x = new double[2 * count];
                for (int i = 0; i < count; i++)
                {
                    int index = first + i;
                    NodeSpec command = commands[index];
                    if (command.StartOffset < 0 || command.StartOffset > command.Duration)
                        x[2 * i] = (command.Duration * .5 - command.StartOffset) / Math.Max(1, command.Duration * .1);
                    if (seeds != null && index < seeds.Count - 1)
                        x[2 * i + 1] = 10 * (seeds[index].DeltaV.magnitude / commands[index].BurnDeltaV - 1);
                    if (initial != null)
                    {
                        NodeSpec fitted = initial[index - completed];
                        x[2 * i] = (fitted.Ut - command.Ut) / Math.Max(1, command.Duration * .1);
                        x[2 * i + 1] = 10 * (fitted.DeltaV.magnitude / command.BurnDeltaV - 1);
                    }
                }
                bool Trial(double[] parameters, out Orbit orbit, out List<NodeSpec> nodes)
                {
                    orbit = source; nodes = new List<NodeSpec>();
                    for (int i = 0; i < count; i++)
                    {
                        NodeSpec? n = Node(first + i, parameters[2 * i], parameters[2 * i + 1]);
                        if (n == null) return false;
                        orbit.GetFixedState(n.Ut, out Vector3d r, out Vector3d v);
                        orbit = SplitPlanner.OrbitFromState(r, v + (SplitPlanner.NodeRotation(orbit, n.Ut) * n.DeltaV).xzy, orbit.referenceBody, n.Ut);
                        if (!CandidateRules.IsFinite(orbit.eccentricity)) return false;
                        nodes.Add(n);
                    }
                    return true;
                }
                if (!Trial(x, out after, out List<NodeSpec> best)) throw new InvalidOperationException("Unable to initialize stock maneuver mapping.");
                double[] current = residual(after);
                double Norm(double[] values) { double sum = 0; foreach (double v in values) sum += v * v; return sum; }
                double damping = 1e-6;
                for (int iteration = 0; iteration < 60 && Norm(current) * scale * scale > .01; iteration++)
                {
                    var jacobian = new double[current.Length, x.Length];
                    for (int j = 0; j < x.Length; j++)
                    {
                        double epsilon = Norm(current) * scale * scale < 1e6 ? 1e-6 : 1e-4;
                        var xx = (double[])x.Clone(); xx[j] += epsilon;
                        if (!Trial(xx, out Orbit probe, out _)) continue;
                        double[] rr = residual(probe);
                        for (int k = 0; k < current.Length; k++) jacobian[k, j] = (rr[k] - current[k]) / epsilon;
                    }
                    double[] step = FiniteConversion.Step(jacobian, current, damping);
                    double length = 1;
                    foreach (double v in step) if (Math.Abs(v) > 2) length = Math.Min(length, 2 / Math.Abs(v));
                    bool improved = false;
                    for (int line = 0; line < 10; line++)
                    {
                        var xx = (double[])x.Clone(); for (int j = 0; j < x.Length; j++) xx[j] += length * step[j];
                        if (Trial(xx, out Orbit next, out List<NodeSpec> nodes))
                        {
                            var rr = residual(next);
                            if (Norm(rr) < Norm(current))
                            { x = xx; after = next; best = nodes; current = rr; improved = true; damping = Math.Max(1e-12, damping * .3); break; }
                        }
                        length *= .5;
                    }
                    if (!improved) damping *= 10;
                    if (damping > 1e8) break;
                }
                if (!CandidateRules.IsFinite(Norm(current))) throw new InvalidOperationException("Stock maneuver mapping produced a non-finite trajectory.");
                result.AddRange(best);
            }
        }
    }
}
