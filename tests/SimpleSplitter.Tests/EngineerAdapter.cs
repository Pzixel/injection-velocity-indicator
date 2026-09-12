// Test doubles for Unity's snapshot boundary, not KER's fuel-flow algorithm.
// The production reader, fingerprint, stage type and interval mapper are linked.
using System;
using System.Collections.Generic;
using System.Threading;

internal static class FlightGlobals { internal static Vessel? ActiveVessel { get; set; } }
internal sealed class PartResourceDefinition
{
    internal int id { get; set; }
    internal double density { get; set; } = 1;
}
internal sealed class PartResource
{
    internal PartResourceDefinition info { get; set; } = new PartResourceDefinition();
    internal double amount { get; set; }
    internal double maxAmount { get; set; }
    internal bool flowState { get; set; } = true;
}
internal sealed class Propellant
{
    internal int id { get; set; }
    internal double ratio { get; set; }
    internal int GetFlowMode() => 0;
}
namespace UnityEngine
{
    internal static class Debug
    {
        internal static void Log(string text) { }
        internal static void LogError(string text) { }
    }
}
namespace SimpleSplitter.Engineer.VesselSimulator
{
    internal static class SimManager { internal static void UpdateModSettings() { } }
    internal sealed class Simulation
    {
        internal static Stage[] Result { get; set; } = Array.Empty<Stage>();
        internal static Action<Simulation>? Run { get; set; }
        internal static bool UsedVacuumFullThrust { get; private set; }
        internal static int CleanupCount;
        internal CancellationToken cancellation;
        internal bool PrepareSimulation(object? log, List<Part> parts, double gravity, double theAtmosphere,
            double theMach, bool vectoredThrust, bool fullThrust)
        {
            UsedVacuumFullThrust = theAtmosphere == 0 && theMach == 0 && fullThrust && vectoredThrust;
            return true;
        }
        internal Stage[] RunSimulation(object? log) { Run?.Invoke(this); return Result; }
        internal void FreePooledObject() => Interlocked.Increment(ref CleanupCount);
    }
}
