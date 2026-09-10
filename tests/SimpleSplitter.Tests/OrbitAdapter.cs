// Headless analytic two-body adapter. Production uses KSP's Orbit/Vector3d;
// this independent Kepler implementation lets the actual planner run in CI.
using System;
using System.Collections.Generic;

internal static class PhysicsGlobals { internal const double GravitationalAcceleration = 9.80665; }

internal sealed class Vessel
{
    internal double Mass { get; set; }
    internal int currentStage { get; set; }
    internal VesselDeltaV VesselDeltaV { get; set; } = new VesselDeltaV();
    internal List<Part> parts { get; } = new List<Part>();
    internal double GetTotalMass() => Mass;
}
internal sealed class VesselDeltaV
{
    internal bool IsReady { get; set; } = true;
    internal bool SimulationRunning { get; set; }
    internal List<DeltaVStageInfo> OperatingStageInfo { get; } = new List<DeltaVStageInfo>();
}
internal sealed class DeltaVStageInfo
{
    internal int stage { get; set; }
    internal List<DeltaVCalc> deltaVCalcs { get; } = new List<DeltaVCalc>();
}
internal sealed class DeltaVCalc
{
    internal double dVinVac { get; set; }
    internal double startMass { get; set; }
    internal double endMass { get; set; }
    internal double thrustVac { get; set; }
    internal double ispVAC { get; set; }
    internal List<DeltaVEngineInfo> activeEngines { get; } = new List<DeltaVEngineInfo>();
}
internal sealed class DeltaVEngineInfo
{
    internal ModuleEngines engine { get; set; } = new ModuleEngines();
}
internal sealed class AvailablePart
{
    internal string name { get; set; } = "";
    internal string title { get; set; } = "";
}
internal sealed class Part
{
    internal int inverseStage { get; set; }
    internal AvailablePart partInfo { get; set; } = new AvailablePart();
    internal List<PartModule> Modules { get; } = new List<PartModule>();
}
internal class PartModule { internal bool isEnabled { get; set; } = true; }
internal sealed class ModuleEngines : PartModule
{
    internal bool EngineIgnited { get; set; }
    internal bool throttleLocked { get; set; }
    internal bool atmChangeFlow { get; set; }
    internal bool useThrustCurve { get; set; }
    internal double maxThrust { get; set; }
    internal double thrustPercentage { get; set; } = 100;
    internal double g { get; set; } = 9.80665;
    internal string engineID { get; set; } = "engine";
    internal FloatCurve atmosphereCurve { get; set; } = new FloatCurve(800);
}
internal sealed class FloatCurve
{
    private readonly double vacuumIsp;
    internal FloatCurve(double vacuumIsp) { this.vacuumIsp = vacuumIsp; }
    internal double Evaluate(float pressure) => vacuumIsp;
}

internal struct Vector3d
{
    internal double x, y, z;
    internal Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    internal double sqrMagnitude => x * x + y * y + z * z;
    internal double magnitude => Math.Sqrt(sqrMagnitude);
    internal Vector3d normalized => magnitude > 0 ? this / magnitude : default;
    internal Vector3d xzy => new Vector3d(x, z, y);
    internal static double Dot(Vector3d a, Vector3d b) => a.x * b.x + a.y * b.y + a.z * b.z;
    internal static Vector3d Cross(Vector3d a, Vector3d b) => new Vector3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vector3d operator -(Vector3d a, Vector3d b) => a + -b;
    public static Vector3d operator -(Vector3d a) => new Vector3d(-a.x, -a.y, -a.z);
    public static Vector3d operator *(Vector3d a, double b) => new Vector3d(a.x * b, a.y * b, a.z * b);
    public static Vector3d operator *(double b, Vector3d a) => a * b;
    public static Vector3d operator /(Vector3d a, double b) => a * (1.0 / b);
}

namespace UnityEngine
{
    internal struct QuaternionD
    {
        private Vector3d right, up, forward;
        internal static QuaternionD LookRotation(Vector3d forward, Vector3d up)
        {
            Vector3d f = forward.normalized;
            Vector3d r = Vector3d.Cross(up, f).normalized;
            return new QuaternionD { right = r, up = Vector3d.Cross(f, r), forward = f };
        }
        internal static QuaternionD Inverse(QuaternionD q) => new QuaternionD
        {
            right = new Vector3d(q.right.x, q.up.x, q.forward.x),
            up = new Vector3d(q.right.y, q.up.y, q.forward.y),
            forward = new Vector3d(q.right.z, q.up.z, q.forward.z)
        };
        public static Vector3d operator *(QuaternionD q, Vector3d v) => q.right * v.x + q.up * v.y + q.forward * v.z;
    }
}

internal sealed class CelestialBody
{
    internal double gravParameter, Radius, atmosphereDepth, sphereOfInfluence;
    internal bool atmosphere;
}

internal sealed class ManeuverNode
{
    internal Orbit patch = null!;
    internal double UT;
    internal Vector3d DeltaV;
}

internal sealed class Orbit
{
    internal enum PatchTransitionType { INITIAL, FINAL, MANEUVER, ENCOUNTER, ESCAPE, IMPACT }
    internal PatchTransitionType patchEndTransition { get; set; } = PatchTransitionType.FINAL;
    internal double EndUT { get; set; } = double.PositiveInfinity;
    internal CelestialBody referenceBody = null!;
    internal double period, eccentricity, semiMajorAxis, PeR, ApR;
    private double epoch, meanAtEpoch;
    private Vector3d p, q;

    internal void UpdateFromStateVectors(Vector3d r, Vector3d v, CelestialBody body, double ut)
    {
        referenceBody = body;
        epoch = ut;
        double mu = body.gravParameter;
        Vector3d h = Vector3d.Cross(r, v);
        Vector3d e = Vector3d.Cross(v, h) / mu - r.normalized;
        eccentricity = e.magnitude;
        semiMajorAxis = 1.0 / (2.0 / r.magnitude - v.sqrMagnitude / mu);
        p = eccentricity > 1e-10 ? e.normalized : r.normalized;
        q = Vector3d.Cross(h.normalized, p);
        double f = Math.Atan2(Vector3d.Dot(r, q), Vector3d.Dot(r, p));
        meanAtEpoch = MeanFromTrue(f);
        period = semiMajorAxis > 0.0 ? 2 * Math.PI * Math.Sqrt(Math.Pow(semiMajorAxis, 3) / mu) : double.PositiveInfinity;
        PeR = semiMajorAxis * (1.0 - eccentricity);
        ApR = semiMajorAxis * (1.0 + eccentricity);
    }

    private double MeanFromTrue(double f)
    {
        double e = eccentricity;
        if (e < 1.0)
        {
            double anomaly = Math.Atan2(Math.Sqrt(1 - e * e) * Math.Sin(f), e + Math.Cos(f));
            return anomaly - e * Math.Sin(anomaly);
        }
        double h = Math.Asinh(Math.Sqrt(e * e - 1) * Math.Sin(f) / (1 + e * Math.Cos(f)));
        return e * Math.Sinh(h) - h;
    }

    internal void GetOrbitalStateVectorsAtUT(double ut, out Vector3d r, out Vector3d v)
    {
        double a = semiMajorAxis, e = eccentricity;
        double n = Math.Sqrt(referenceBody.gravParameter / Math.Pow(Math.Abs(a), 3));
        double m = meanAtEpoch + (ut - epoch) * n;
        if (e < 1.0)
        {
            m = Math.IEEERemainder(m, 2 * Math.PI);
            double anomaly = e < 0.8 ? m : Math.CopySign(Math.PI, m);
            for (int i = 0; i < 60; i++)
            {
                double step = (anomaly - e * Math.Sin(anomaly) - m) / (1 - e * Math.Cos(anomaly));
                anomaly -= step;
                if (Math.Abs(step) < 1e-14) break;
            }
            double c = Math.Cos(anomaly), s = Math.Sin(anomaly), b = Math.Sqrt(1 - e * e);
            r = p * (a * (c - e)) + q * (a * b * s);
            v = (p * -s + q * (b * c)) * (a * n / (1 - e * c));
        }
        else
        {
            double anomaly = Math.Asinh(m / e);
            for (int i = 0; i < 60; i++)
            {
                double step = (e * Math.Sinh(anomaly) - anomaly - m) / (e * Math.Cosh(anomaly) - 1);
                anomaly -= step;
                if (Math.Abs(step) < 1e-14) break;
            }
            double c = Math.Cosh(anomaly), s = Math.Sinh(anomaly), b = Math.Sqrt(e * e - 1);
            r = p * (a * (c - e)) + q * (-a * b * s);
            v = (p * (a * s) + q * (-a * b * c)) * (n / (e * c - 1));
        }
    }

    internal double TrueAnomalyAtUT(double ut)
    {
        GetOrbitalStateVectorsAtUT(ut, out Vector3d r, out _);
        return (Math.Atan2(Vector3d.Dot(r, q), Vector3d.Dot(r, p)) + 2 * Math.PI) % (2 * Math.PI);
    }
    internal double GetDTforTrueAnomalyAtUT(double anomaly, double ut)
    {
        double n = 2 * Math.PI / period;
        return Math.IEEERemainder((MeanFromTrue(anomaly) - meanAtEpoch) / n - (ut - epoch), period);
    }
}
