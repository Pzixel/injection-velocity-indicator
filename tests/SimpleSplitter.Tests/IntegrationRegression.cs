using System;

namespace SimpleSplitter
{
    internal static partial class Program
    {
        private static void IntegrationRegression()
        {
            var body = new CelestialBody { gravParameter = 3.5316e12, Radius = 600000,
                atmosphere = true, atmosphereDepth = 70000, sphereOfInfluence = 84159286 };
            double radius = 686780;
            var position = new Vector3d(radius, 0, 0);
            var velocity = new Vector3d(0, Math.Sqrt(body.gravParameter / radius), 0);
            Orbit before = SplitPlanner.OrbitFromState(position, velocity, body, 0);
            var engine = new BurnPhysics(161.54, 300, 8041.45);
            foreach (double dv in new[] { 20.0, 370.0, 560.0, 1478.0 })
            {
                var node = new NodeSpec(1000, new Vector3d(0, 0, dv), engine.Duration(dv), engine.StartOffset(dv));
                before.GetFixedState(node.Ut, out Vector3d center, out Vector3d speed);
                Vector3d direction = (SplitPlanner.NodeRotation(before, node.Ut) * node.DeltaV).xzy.normalized;
                Orbit after = SplitPlanner.OrbitFromState(center, speed + direction * dv, body, node.Ut);
                FiniteBurnEstimate actual = FiniteBurnEstimate.Measure(before, after, node, engine, 0, before, out Orbit executed);
                double start = node.Ut - node.StartOffset;
                before.GetFixedState(start, out Vector3d r, out Vector3d v);
                int steps = Math.Max(64, Math.Min(8192, (int)Math.Ceiling(node.Duration / 0.5)));
                double h = node.Duration / steps, flow = engine.Thrust / engine.ExhaustVelocity, loss = 0;
                Vector3d Acceleration(Vector3d p, double mass) =>
                    p * (-body.gravParameter / (p.magnitude * p.sqrMagnitude)) + direction * (engine.Thrust / mass);
                // Independent vector-form RK4 oracle retained from the original
                // implementation. Check long nuclear burns, not just a short step.
                bool safe = true;
                for (int i = 0; i <= steps; i++)
                {
                    loss = Math.Max(loss, 1 - Vector3d.Dot(center.normalized, r.normalized));
                    if (r.magnitude <= body.Radius + body.atmosphereDepth) { safe = false; break; }
                    if (i == steps) break;
                    double mass = engine.Mass - flow * i * h;
                    Vector3d a1 = Acceleration(r, mass);
                    Vector3d v2 = v + a1 * (h * 0.5);
                    Vector3d a2 = Acceleration(r + v * (h * 0.5), mass - flow * h * 0.5);
                    Vector3d v3 = v + a2 * (h * 0.5);
                    Vector3d a3 = Acceleration(r + v2 * (h * 0.5), mass - flow * h * 0.5);
                    Vector3d v4 = v + a3 * h;
                    Vector3d a4 = Acceleration(r + v3 * h, mass - flow * h);
                    r += (v + 2 * v2 + 2 * v3 + v4) * (h / 6);
                    v += (a1 + 2 * a2 + 2 * a3 + a4) * (h / 6);
                }
                True(actual.IsFinite == safe, "scalar RK4 retains atmospheric rejection");
                if (!safe) continue;
                executed.GetFixedState(start + node.Duration, out Vector3d actualR, out Vector3d actualV);
                Nearly(0, (r - actualR).magnitude, 1e-5, "scalar RK4 matches reference position");
                Nearly(0, (v - actualV).magnitude, 1e-7, "scalar RK4 matches reference velocity");
                Nearly(loss, actual.CosineLoss, 1e-10, "scalar RK4 retains cosine diagnostic");
            }
        }
    }
}
