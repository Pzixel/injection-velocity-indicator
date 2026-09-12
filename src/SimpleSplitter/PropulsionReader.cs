using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimpleSplitter.Engineer.VesselSimulator;

namespace SimpleSplitter
{
    internal static class PropulsionReader
    {
        private static Task<StagedPropulsion>? pending;
        private static Vessel? preparedVessel;
        private static PropulsionFingerprint? fingerprint;
        private static StagedPropulsion prepared = new StagedPropulsion(new BurnStage[0]);
        private static string failure = "Calculate propulsion before comparing plans.";

        // Snapshot Unity/KSP objects on the main thread, then run KER's own
        // fuel-flow and staging simulator in the background. Never read VesselDeltaV.
        internal static IEnumerator Refresh(Vessel vessel)
        {
            Clear();
            while (pending != null && !pending.IsCompleted)
            {
                if (FlightGlobals.ActiveVessel != vessel) yield break;
                yield return null;
            }
            if (FlightGlobals.ActiveVessel != vessel) yield break;
            var cancellation = new CancellationTokenSource();
            var simulation = new Simulation { cancellation = cancellation.Token };
            PropulsionFingerprint? before = null;
            Task<StagedPropulsion>? computation = null;
            try
            {
                before = PropulsionFingerprint.Capture(vessel);
                SimManager.UpdateModSettings();
                if (!simulation.PrepareSimulation(null, vessel.parts, PhysicsGlobals.GravitationalAcceleration,
                    theAtmosphere: 0, theMach: 0, vectoredThrust: true, fullThrust: true))
                    throw new InvalidOperationException("Could not snapshot the vessel for KER's simulator.");
                int currentStage = vessel.currentStage;
                cancellation.CancelAfter(TimeSpan.FromSeconds(10));
                computation = pending = Task.Run(() =>
                {
                    try { return EngineerStageMapper.Map(simulation.RunSimulation(null), currentStage); }
                    finally { simulation.FreePooledObject(); }
                });
            }
            catch (Exception exception)
            {
                simulation.FreePooledObject();
                failure = "Propulsion snapshot failed: " + exception.Message;
                UnityEngine.Debug.LogError("[SimpleSplitter] " + failure + "\n" + exception);
            }
            try
            {
                if (computation == null) yield break;
                while (!computation.IsCompleted)
                {
                    if (FlightGlobals.ActiveVessel != vessel) yield break;
                    yield return null;
                }
                if (computation.IsFaulted || computation.IsCanceled)
                {
                    Exception? exception = computation.Exception?.GetBaseException();
                    failure = exception is OperationCanceledException || computation.IsCanceled
                        ? "The embedded KER propulsion calculation timed out. The maneuver is unchanged."
                        : "The embedded KER propulsion calculation failed: " + exception?.Message;
                    UnityEngine.Debug.LogError("[SimpleSplitter] " + failure + "\n" + exception);
                    yield break;
                }
                if (FlightGlobals.ActiveVessel != vessel || before == null || !StillMatches(before, vessel))
                {
                    failure = "Fuel, engines or staging changed during the propulsion calculation. Compare again.";
                    yield break;
                }
                prepared = computation.Result;
                preparedVessel = vessel;
                fingerprint = before;
                failure = prepared.IsUsable ? "" : "The embedded KER simulator reports no usable remaining engine delta-v.";
            }
            finally
            {
                cancellation.Cancel();
                // An abandoned job owns its snapshot until cleanup has finished.
                if (computation == null || computation.IsCompleted) cancellation.Dispose();
                else _ = computation.ContinueWith(done => { _ = done.Exception; cancellation.Dispose(); });
            }
        }

        internal static StagedPropulsion Read(Vessel vessel, out string error)
        {
            error = failure;
            if (preparedVessel != vessel || fingerprint == null) return new StagedPropulsion(new BurnStage[0]);
            if (!StillMatches(fingerprint, vessel))
            {
                error = "Fuel, engines or staging changed. Compare plans again.";
                return new StagedPropulsion(new BurnStage[0]);
            }
            return prepared;
        }

        private static bool StillMatches(PropulsionFingerprint before, Vessel vessel)
        {
            try { return before.Matches(PropulsionFingerprint.Capture(vessel)); }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError("[SimpleSplitter] Cannot verify propulsion snapshot: " + exception);
                return false;
            }
        }

        internal static void Clear()
        {
            preparedVessel = null;
            fingerprint = null;
            prepared = new StagedPropulsion(new BurnStage[0]);
            failure = "Waiting for the embedded KER propulsion calculation.";
        }
    }
}
