using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using SimpleSplitter.Engineer.VesselSimulator;

namespace SimpleSplitter
{
    internal static partial class Program
    {
        private static EngineerSegment Interval(BurnPhysics physics, double dv, double spent = 0) =>
            new EngineerSegment(physics.MassAfter(spent), physics.MassAfter(spent + dv), dv, physics.Duration(dv, spent));

        private static void EngineerPropulsionTests()
        {
            var booster = new BurnPhysics(100, 9200, 3200);
            var nuclear = new BurnPhysics(80, 180, 8000);
            var current = new Stage { number = -1, deltaV = 500 };
            current.burnSegments.Add(Interval(booster, 200));
            current.burnSegments.Add(Interval(booster, 300, 200));
            var future = new Stage { number = 2, deltaV = 4000 };
            future.burnSegments.Add(Interval(nuclear, 4000));
            // A zero-dv decoupler stage may have meaningless aggregate masses.
            var empty = new Stage { number = 3, totalMass = double.NaN };
            Stage[] fixture = { future, empty, current };
            StagedPropulsion propulsion = EngineerStageMapper.Map(fixture, 4);
            True(propulsion.Stages.Count == 2 && propulsion.Stages[0].Stage == 4 && propulsion.Stages[1].Stage == 2,
                "KER current engines execute before future stages; empty staging events skipped");
            Nearly(500, propulsion.Stages[0].DeltaV, 1e-8, "tank drain steps do not split the short booster burn");
            Nearly(100, propulsion.Stages[0].Engine.Mass, 1e-8, "use wet vessel mass, not aggregate stage mass");
            Nearly(booster.Duration(500), propulsion.Stages[0].Engine.Duration(500), 1e-8, "preserve full-throttle burn duration");
            Nearly(80, propulsion.Stages[1].Engine.Mass, 1e-8, "retain KER's post-decoupling vessel mass");
            var cursor = propulsion.Cursor();
            True(cursor.Consume(500) && cursor.Stage == 2, "whole current stage fits one burn and advances to nuclear engine");
            Nearly(180, cursor.Engine.Thrust, 1e-7, "nuclear engine uses its own thrust");
            var noise = new StagedPropulsion(new[] {
                new BurnStage(4, new BurnPhysics(100, propulsion.Stages[0].Engine.Thrust * (1 + 5e-8), 3200), 500),
                propulsion.Stages[1]
            });
            True(PlanSearchCache.MatchesPropulsion(propulsion, noise), "float-vector noise preserves cache reuse");
            var consumed = new StagedPropulsion(new[] {
                new BurnStage(4, propulsion.Stages[0].Engine, 499), propulsion.Stages[1]
            });
            False(PlanSearchCache.MatchesPropulsion(propulsion, consumed), "a meaningful fuel change invalidates the cache");

            var segments = new List<BurnStage>();
            double remainingMass = booster.MassAfter(200);
            var reducedThrust = new BurnPhysics(remainingMass, 4600, 3200);
            EngineerSegmentMapper.AppendStage(segments, 4, new[] { Interval(booster, 200), Interval(reducedThrust, 300) });
            True(segments.Count == 2, "an engine shutting down inside one stage preserves its propulsion boundary");
            Nearly(reducedThrust.Duration(100), segments[1].Engine.Duration(100), 1e-8, "partial later burn is not modeled with stage average thrust");
            ExpectInvalid(() => EngineerSegmentMapper.AppendStage(new List<BurnStage>(), 4,
                new[] { new EngineerSegment(100, 100, 500, 5) }), "invalid mass interval is rejected");
            ExpectInvalid(() => EngineerSegmentMapper.AppendStage(new List<BurnStage>(), 4,
                new[] { new EngineerSegment(100, 90, 500, double.NaN) }), "invalid burn duration is rejected");
            ExpectInvalid(() => EngineerSegmentMapper.AppendStage(new List<BurnStage>(), 4,
                new[] { new EngineerSegment(100, 90, 500, 5, "solid engine") }), "unsupported engine cannot silently disappear");
            ExpectInvalid(() => EngineerStageMapper.Map(new[] { new Stage { number = 1, deltaV = 500 } }, 1),
                "nonempty stage without intervals is rejected");
            ExpectInvalid(() => EngineerStageMapper.Map(new[] { future, new Stage { number = -1, deltaV = double.NaN } }, 1),
                "invalid active stage cannot be skipped to use a future stage");

            var fuel = new PartResource { amount = 20, maxAmount = 20 };
            var engine = new ModuleEngines { propellants = { new Propellant { id = fuel.info.id } } };
            var part = new Part { flightID = 1, Resources = { fuel }, Modules = { engine } };
            var vessel = new Vessel { currentStage = 4, parts = { part } };
            FlightGlobals.ActiveVessel = vessel;
            Simulation.Result = fixture;
            Simulation.Run = null;
            Drain(PropulsionReader.Refresh(vessel));
            True(Simulation.UsedVacuumFullThrust, "request KER vacuum full-throttle simulation at configured limiter");
            True(PropulsionReader.Read(vessel, out string error).IsUsable && error == "", "works without any stock delta-v API or installed KER");
            var oxygen = new PartResource { info = new PartResourceDefinition { id = 123, density = 1.43e-6 },
                amount = 186.99758215838031, maxAmount = 200 };
            part.Resources.Add(oxygen);
            Drain(PropulsionReader.Refresh(vessel));
            oxygen.amount = 186.99757451062513;
            True(PropulsionReader.Read(vessel, out _).IsUsable, "measured Kerbalism cabin oxygen use preserves propulsion");
            oxygen.amount = 0;
            False(PropulsionReader.Read(vessel, out _).IsUsable, "significant non-propellant mass change invalidates propulsion");
            oxygen.amount = 186.99758215838031;
            engine.propellants.Add(new Propellant { id = oxygen.info.id });
            Drain(PropulsionReader.Refresh(vessel));
            oxygen.amount = 186.99757451062513;
            False(PropulsionReader.Read(vessel, out _).IsUsable, "the same oxygen change remains strict when a remaining engine uses it");
            engine.propellants.RemoveAt(1);
            Drain(PropulsionReader.Refresh(vessel));
            fuel.amount = 19;
            False(PropulsionReader.Read(vessel, out _).IsUsable, "fuel changes invalidate prepared propulsion");
            Drain(PropulsionReader.Refresh(vessel));
            True(PropulsionReader.Read(vessel, out _).IsUsable,
                "a fresh end-of-search snapshot replaces resource data aged during trajectory validation");
            StagedPropulsion beforeSearch = PropulsionReader.Read(vessel, out _);
            fuel.amount -= 0.000002;
            False(PropulsionReader.Read(vessel, out _).IsUsable,
                "the raw snapshot reader cannot serve as a long-search completion check");
            current.deltaV = 500 - 0.00005;
            current.burnSegments.Clear();
            current.burnSegments.Add(Interval(booster, current.deltaV));
            Drain(PropulsionReader.Refresh(vessel));
            True(PlanSearchCache.MatchesPropulsion(beforeSearch, PropulsionReader.Read(vessel, out _)),
                "fresh KER stages accept sub-ppm background resource changes without using stale snapshots");
            current.deltaV = 499;
            current.burnSegments.Clear();
            current.burnSegments.Add(Interval(booster, current.deltaV));
            Drain(PropulsionReader.Refresh(vessel));
            False(PlanSearchCache.MatchesPropulsion(beforeSearch, PropulsionReader.Read(vessel, out _)),
                "fresh KER stages reject meaningful propulsion loss at search completion");
            engine.thrustPercentage = 50;
            False(PropulsionReader.Read(vessel, out _).IsUsable, "engine limiter changes invalidate propulsion");
            engine.thrustPercentage = 100;
            Simulation.Run = _ => throw new InvalidOperationException("fixture failure");
            Drain(PropulsionReader.Refresh(vessel));
            False(PropulsionReader.Read(vessel, out error).IsUsable, "failed KER calculation cannot reuse previous success");
            True(error.Contains("fixture failure"), "the calculation failure is explained");
            Simulation.Run = _ => fuel.flowState = false;
            Drain(PropulsionReader.Refresh(vessel));
            False(PropulsionReader.Read(vessel, out error).IsUsable, "a resource lock change during simulation rejects the result");
            True(error.Contains("changed"), "snapshot mismatch is explained");
            Simulation.Run = null;
            Drain(PropulsionReader.Refresh(vessel));
            var other = new Vessel { currentStage = 4 };
            False(PropulsionReader.Read(other, out _).IsUsable, "another vessel never receives cached propulsion");

            using var started = new ManualResetEventSlim();
            Simulation.Run = sim => { started.Set(); sim.cancellation.WaitHandle.WaitOne(2000); sim.cancellation.ThrowIfCancellationRequested(); };
            IEnumerator waiting = PropulsionReader.Refresh(vessel);
            True(waiting.MoveNext(), "background calculation yields to the game");
            True(started.Wait(1000), "background cancellation fixture started");
            FlightGlobals.ActiveVessel = other;
            False(waiting.MoveNext(), "switching vessels abandons a running request");
            Simulation.Run = null;
            FlightGlobals.ActiveVessel = vessel;
            Drain(PropulsionReader.Refresh(vessel));
            True(PropulsionReader.Read(vessel, out _).IsUsable, "a new request recovers after cancellation and snapshot cleanup");
            PropulsionReader.Clear();
            False(PropulsionReader.Read(vessel, out _).IsUsable, "leaving flight releases prepared vessel data");
            FlightGlobals.ActiveVessel = null;
        }

        private static void Drain(IEnumerator iterator)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (iterator.MoveNext())
            {
                if (DateTime.UtcNow >= deadline) throw new Exception("Propulsion coroutine did not complete.");
                Thread.Sleep(1);
            }
        }

        private static void ExpectInvalid(Action action, string name)
        {
            try { action(); False(true, name); }
            catch (InvalidOperationException) { True(true, name); }
        }
    }
}
