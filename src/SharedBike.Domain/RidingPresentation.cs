using System;

namespace DuckovSharedBike.Domain
{
    public sealed class RidingPresentationSettings
    {
        public bool NativeRidePose { get; set; } = true;
        // Zero resolves the value from an already-loaded native motorbike resource.
        public int NativeRidePoseValue { get; set; }
        public float RiderVisualHeight { get; set; }
        public bool VehicleClearance { get; set; } = true;
        public float TurnDegreesPerSecond { get; set; } = 540;
        public bool PedalAndGripPose {get;set;}=true;
        // >1 gives the shared bike an explicit base speed bonus on top of zero-weight travel.
        public float RideSpeedMultiplier { get; set; } = 1.5f;

        public void Validate()
        {
            if (NativeRidePoseValue < 0 || NativeRidePoseValue > 32
                || float.IsNaN(RiderVisualHeight) || float.IsInfinity(RiderVisualHeight) || RiderVisualHeight < -0.2f || RiderVisualHeight > 1
                || float.IsNaN(TurnDegreesPerSecond) || float.IsInfinity(TurnDegreesPerSecond)
                || TurnDegreesPerSecond < 90 || TurnDegreesPerSecond > 1080
                || float.IsNaN(RideSpeedMultiplier) || float.IsInfinity(RideSpeedMultiplier)
                || RideSpeedMultiplier < 0.5f || RideSpeedMultiplier > 2f)
                throw new InvalidOperationException("Invalid riding presentation settings.");
        }
    }

    public static class BicycleGeometry
    {
        public const float WheelRadius = 0.26f;
        public const float HalfWheelbase = 0.42f;
        public const float BodyRadius = 0.22f;
        public const float MaxContinuousStep = 3;
        public const float PedalGearRatio = 2.4f;
    }

    public sealed class RollingKinematics
    {
        public double WheelDegrees { get; private set; }
        public double PedalDegrees { get; private set; }
        public void Advance(double signedDistance, bool active)
        {
            if (double.IsNaN(signedDistance) || double.IsInfinity(signedDistance))
                throw new ArgumentException("Invalid wheel displacement.");
            // Position resets/teleports are not pedalling; a fresh bike starts a fresh visual phase.
            if (!active || Math.Abs(signedDistance)>BicycleGeometry.MaxContinuousStep) return;
            var rotation=signedDistance / BicycleGeometry.WheelRadius * 180 / Math.PI;
            WheelDegrees=Wrap(WheelDegrees+rotation);
            PedalDegrees=Wrap(PedalDegrees+rotation / BicycleGeometry.PedalGearRatio);
        }
        public static double TurnYaw(double current,double target,double elapsedSeconds,double rate)
        {
            if (!Finite(current) || !Finite(target) || !Finite(elapsedSeconds) || !Finite(rate) || elapsedSeconds<0 || rate<=0)
                throw new ArgumentException("Invalid bicycle heading.");
            current=Wrap(current); target=Wrap(target);
            var delta=Wrap(target-current+180)-180;
            var maximum=elapsedSeconds*rate;
            return Wrap(current+Math.Max(-maximum,Math.Min(maximum,delta)));
        }
        private static bool Finite(double value)=>!double.IsNaN(value) && !double.IsInfinity(value);
        private static double Wrap(double value)=>(value%360+360)%360;
    }
}
