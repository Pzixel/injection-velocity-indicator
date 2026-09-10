using System;

namespace SimpleSplitter
{
    // SI for distance/time; thrust in kN and mass in tonnes (KSP's units).
    internal sealed class BurnPhysics
    {
        internal BurnPhysics(double mass, double thrust, double exhaustVelocity)
        {
            Mass = mass;
            Thrust = thrust;
            ExhaustVelocity = exhaustVelocity;
        }

        internal double Mass { get; }
        internal double Thrust { get; }
        internal double ExhaustVelocity { get; }
        internal bool IsUsable => CandidateRules.IsFinite(Mass) && Mass > 0.0 &&
            CandidateRules.IsFinite(Thrust) && Thrust > 0.0 &&
            CandidateRules.IsFinite(ExhaustVelocity) && ExhaustVelocity > 0.0;

        internal double MassAfter(double deltaV) => Mass * Math.Exp(-deltaV / ExhaustVelocity);

        internal double Duration(double deltaV, double spentDeltaV = 0.0)
        {
            if (!IsUsable || !CandidateRules.IsFinite(deltaV) || deltaV < 0.0 ||
                !CandidateRules.IsFinite(spentDeltaV) || spentDeltaV < 0.0)
                return double.NaN;
            double x = deltaV / ExhaustVelocity;
            // Avoid cancellation for very short burns.
            double consumed = x < 1e-5 ? x * (1.0 - x * 0.5 + x * x / 6.0) : 1.0 - Math.Exp(-x);
            return MassAfter(spentDeltaV) * ExhaustVelocity / Thrust * consumed;
        }

        // Centre the acceleration-weighted impulse at node UT, not elapsed time / 2.
        internal double StartOffset(double deltaV, double spentDeltaV = 0.0)
        {
            if (deltaV <= 0.0) return 0.0;
            double x = deltaV / ExhaustVelocity;
            double fraction = x < 1e-4
                ? x * (0.5 - x / 6.0 + x * x / 24.0)
                : 1.0 - (1.0 - Math.Exp(-x)) / x;
            return MassAfter(spentDeltaV) * ExhaustVelocity / Thrust * fraction;
        }

        // Maximum angular rate through a periapsis burn. Bounding both radial
        // sweep and velocity-direction sweep is conservative for eccentric orbits.
        internal bool KickFits(double mu, double radius, double initialSpeed,
            double tangentialFraction, double spent, double kick, double loss)
        {
            double speed = initialSpeed + spent + kick;
            if (!OrbitScalars.TryCreate(mu, radius, speed, tangentialFraction, out OrbitScalars orbit))
                return false;
            double omega = orbit.AngularMomentum / (orbit.Periapsis * orbit.Periapsis);
            double endpoint = Math.Max(StartOffset(kick, spent), Duration(kick, spent) - StartOffset(kick, spent));
            return CandidateRules.IsFinite(endpoint) && omega * endpoint <= Math.Acos(1.0 - loss);
        }
    }

    internal readonly struct OrbitScalars
    {
        private OrbitScalars(double a, double e, double h, double mu)
        {
            Periapsis = a * (1.0 - e);
            Apoapsis = a * (1.0 + e);
            Period = 2.0 * Math.PI * Math.Sqrt(a * a * a / mu);
            AngularMomentum = h;
        }

        internal double Periapsis { get; }
        internal double Apoapsis { get; }
        internal double Period { get; }
        internal double AngularMomentum { get; }

        internal static bool TryCreate(double mu, double radius, double speed,
            double tangentialFraction, out OrbitScalars orbit)
        {
            orbit = default;
            if (!CandidateRules.IsFinite(mu) || !CandidateRules.IsFinite(radius) ||
                !CandidateRules.IsFinite(speed) || !CandidateRules.IsFinite(tangentialFraction) ||
                mu <= 0.0 || radius <= 0.0 || speed <= 0.0 ||
                tangentialFraction <= 0.0 || tangentialFraction > 1.0) return false;
            double energy = speed * speed * 0.5 - mu / radius;
            if (energy >= 0.0) return false;
            double a = -mu / (2.0 * energy);
            double h = radius * speed * tangentialFraction;
            double eSquared = 1.0 - h * h / (mu * a);
            if (eSquared < -1e-12) return false;
            orbit = new OrbitScalars(a, Math.Sqrt(Math.Max(0.0, eSquared)), h, mu);
            return CandidateRules.IsFinite(orbit.Period) && orbit.Periapsis > 0.0;
        }
    }
}
