using System;

namespace SimpleSplitter
{
    // The original impulsive maneuver is immutable. All candidates target the
    // same position AND velocity at the same epoch on its outgoing trajectory.
    // This is the pseudo-rendezvous boundary condition in Fogel et al. (2020),
    // Figures 3-4; v-infinity alone omits position and time of flight.
    internal sealed class TrajectoryReference
    {
        internal TrajectoryReference(Orbit target, double maneuverUt, double arrivalUt)
        {
            Target = target;
            ManeuverUt = maneuverUt;
            target.GetFixedState(maneuverUt, out Vector3d origin, out Vector3d velocity);
            double radius = origin.magnitude;
            double boundary = (radius + target.referenceBody.sphereOfInfluence) * 0.5;
            double limit = (arrivalUt - maneuverUt) * 0.25;
            if (target.eccentricity < 1) limit = Math.Min(limit, target.period * 0.25);
            double upper = Math.Min(limit, Math.Max(1, (boundary - radius) / velocity.magnitude));
            double lower = 0;
            if (target.eccentricity >= 1)
            {
                // Stay within the departure body's patched-conic domain. The
                // target determines this epoch, never a candidate's burn timer.
                while (upper < limit && RadiusAt(upper) < boundary) upper = Math.Min(limit, upper * 2);
                if (RadiusAt(upper) >= boundary)
                {
                    for (int i = 0; i < 40; i++)
                    {
                        double middle = (lower + upper) * 0.5;
                        if (RadiusAt(middle) < boundary) lower = middle; else upper = middle;
                    }
                }
            }
            else upper = limit;
            Horizon = upper;
            Epoch = maneuverUt + Horizon;
            target.GetFixedState(Epoch, out Vector3d r, out Vector3d v);
            Position = r;
            Velocity = v;
            // Express both residuals as separation in metres over this common
            // coast horizon. Scaling does not change the six zero constraints.
            // Unlike local nondimensional r/v errors it retains the mission's
            // sensitivity to a small velocity error accumulating during coast.
            Scale = Math.Max(1, radius);
            IsUsable = CandidateRules.IsFinite(Horizon) && Horizon > 0 &&
                CandidateRules.IsFinite(r.magnitude) && CandidateRules.IsFinite(v.magnitude);

            double RadiusAt(double offset)
            {
                target.GetFixedState(maneuverUt + offset, out Vector3d r0, out _);
                return r0.magnitude;
            }
        }

        internal Orbit Target { get; }
        internal double ManeuverUt { get; }
        internal double Epoch { get; }
        internal double Horizon { get; }
        internal double Scale { get; }
        internal Vector3d Position { get; }
        internal Vector3d Velocity { get; }
        internal bool IsUsable { get; }

        internal TrajectoryError Measure(Orbit executed)
        {
            if (!IsUsable) return TrajectoryError.Invalid;
            executed.GetFixedState(Epoch, out Vector3d r, out Vector3d v);
            return new TrajectoryError(r - Position, v - Velocity, Horizon);
        }

        internal double[] Residual(Orbit executed)
        {
            TrajectoryError error = Measure(executed);
            Vector3d r = error.Position / Scale;
            Vector3d v = error.Velocity * (Horizon / Scale);
            return new[] { r.x, r.y, r.z, v.x, v.y, v.z };
        }
    }

    internal readonly struct TrajectoryError
    {
        internal const double PositionTolerance = 1;
        internal const double VelocityTolerance = .001;
        internal TrajectoryError(Vector3d position, Vector3d velocity, double horizon)
        { Position = position; Velocity = velocity; Horizon = horizon; }
        internal Vector3d Position { get; }
        internal Vector3d Velocity { get; }
        internal double Horizon { get; }
        internal double PositionMeters => Position.magnitude;
        internal double VelocityMetersPerSecond => Velocity.magnitude;
        internal double SeparationMeters => Math.Sqrt(Position.sqrMagnitude + Horizon * Horizon * Velocity.sqrMagnitude);
        internal bool IsFinite => CandidateRules.IsFinite(SeparationMeters);
        internal bool MatchesReference => IsFinite && PositionMeters <= PositionTolerance &&
            VelocityMetersPerSecond <= VelocityTolerance;
        internal static TrajectoryError Invalid => new TrajectoryError(new Vector3d(double.NaN, 0, 0), default, 1);
    }
}
