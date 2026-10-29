using DuckovSharedBike.Domain;

internal static class RidingPresentationTests
{
    internal static void Register(Action<string,Action> add)
    {
        add("one wheel circumference produces one revolution and the correct pedal ratio",()=>
        {
            var motion=new RollingKinematics(); motion.Advance(2*Math.PI*BicycleGeometry.WheelRadius,true);
            Angle(0,motion.WheelDegrees); Angle(360d/BicycleGeometry.PedalGearRatio,motion.PedalDegrees);
        });
        add("wheel animation depends on displacement rather than frame count",()=>
        {
            var coarse=new RollingKinematics(); var fine=new RollingKinematics(); coarse.Advance(2,true);
            for (var i=0;i<1000;i++) fine.Advance(0.002,true);
            Angle(coarse.WheelDegrees,fine.WheelDegrees); Angle(coarse.PedalDegrees,fine.PedalDegrees);
        });
        add("reverse motion reverses wheel and pedal rotation",()=>
        {
            var motion=new RollingKinematics(); motion.Advance(0.5,true); motion.Advance(-0.5,true);
            Angle(0,motion.WheelDegrees); Angle(0,motion.PedalDegrees);
        });
        add("stationary and paused riding cannot spin wheels",()=>
        {
            var motion=new RollingKinematics(); motion.Advance(0.5,true);
            var wheel=motion.WheelDegrees; var pedal=motion.PedalDegrees;
            motion.Advance(0,true); motion.Advance(1,false);
            Angle(wheel,motion.WheelDegrees); Angle(pedal,motion.PedalDegrees);
        });
        add("teleport-like displacement is excluded from rolling animation",()=>
        {
            var motion=new RollingKinematics(); motion.Advance(0.5,true); var original=motion.WheelDegrees;
            motion.Advance(30,true); motion.Advance(-30,true); Angle(original,motion.WheelDegrees);
        });
        add("pedal phase stays continuous when wheels cross multiple revolutions",()=>
        {
            var motion=new RollingKinematics();
            for (var i=0;i<20;i++) motion.Advance(2*Math.PI*BicycleGeometry.WheelRadius,true);
            Angle(0,motion.WheelDegrees); Angle(20d*360/BicycleGeometry.PedalGearRatio,motion.PedalDegrees);
        });
        add("invalid displacement cannot poison future wheel animation",()=>
        {
            var motion=new RollingKinematics(); motion.Advance(1,true); var before=motion.WheelDegrees;
            Throws<ArgumentException>(()=>motion.Advance(double.NaN,true));
            Throws<ArgumentException>(()=>motion.Advance(double.PositiveInfinity,true)); Angle(before,motion.WheelDegrees);
        });
        add("bicycle heading takes the short turn across zero degrees",()=>Angle(4,RollingKinematics.TurnYaw(350,10,0.1,140)));
        add("turning respects the configured angular speed",()=>Angle(27,RollingKinematics.TurnYaw(0,90,0.1,270)));
        add("turning arrives at its target without overshoot",()=>Angle(20,RollingKinematics.TurnYaw(10,20,1,540)));
        add("zero delta time freezes the bicycle heading",()=>Angle(42,RollingKinematics.TurnYaw(42,200,0,540)));
        add("splitting time samples preserves a constant-target heading",()=>
        {
            var heading=350d; for (var i=0;i<10;i++) heading=RollingKinematics.TurnYaw(heading,90,0.01,180);
            Angle(RollingKinematics.TurnYaw(350,90,0.1,180),heading);
        });
        add("large heading inputs normalize to a finite wrapped heading",()=>
            Angle(15,RollingKinematics.TurnYaw(725,15,1,540)));
        add("finite extreme headings cannot overflow angle subtraction",()=>
        {
            var result=RollingKinematics.TurnYaw(double.MaxValue,-double.MaxValue,0.1,540);
            if (!double.IsFinite(result) || result<0 || result>=360) throw new Exception("Heading was not normalized.");
        });
        add("invalid turn timing and angular rate are rejected",()=>
        {
            Throws<ArgumentException>(()=>RollingKinematics.TurnYaw(0,20,-1,540));
            Throws<ArgumentException>(()=>RollingKinematics.TurnYaw(0,20,1,0));
            Throws<ArgumentException>(()=>RollingKinematics.TurnYaw(0,double.NaN,1,540));
        });
        add("legacy map config gains safe riding defaults without changing its map scope",()=>
        {
            var settings=DuckovSharedBike.StationSettingsFile.Parse("{\"TargetDisplayKeys\":[\"Level_GroundZero\"]}");
            settings.Validate(); Equal(1,settings.TargetDisplayKeys.Count); Equal(0,settings.Riding.NativeRidePoseValue);
            Equal(true,settings.Riding.VehicleClearance); Equal(540f,settings.Riding.TurnDegreesPerSecond);
            Equal(1.5f,settings.Riding.RideSpeedMultiplier);
        });
        add("ride speed multiplier defaults to the zero-weight bonus and accepts an explicit override",()=>
        {
            var settings=DuckovSharedBike.StationSettingsFile.Parse("{\"Riding\":{\"RideSpeedMultiplier\":1.15}}");
            Equal(1.15f,settings.Riding.RideSpeedMultiplier);
            Throws<InvalidOperationException>(()=>new RidingPresentationSettings { RideSpeedMultiplier=0.4f }.Validate());
            Throws<InvalidOperationException>(()=>new RidingPresentationSettings { RideSpeedMultiplier=2.5f }.Validate());
            Throws<InvalidOperationException>(()=>new RidingPresentationSettings { RideSpeedMultiplier=float.NaN }.Validate());
        });
        add("ride decoration and clearance can be disabled independently",()=>
        {
            var settings=DuckovSharedBike.StationSettingsFile.Parse("{\"Riding\":{\"NativeRidePose\":false,\"VehicleClearance\":false}}");
            Equal(false,settings.Riding.NativeRidePose); Equal(false,settings.Riding.VehicleClearance);
            Equal(true,settings.Enabled);
        });
        add("invalid visual heights and unknown ride pose values are rejected",()=>
        {
            Throws<InvalidOperationException>(()=>new RidingPresentationSettings { RiderVisualHeight=float.NaN }.Validate());
            Throws<InvalidOperationException>(()=>new RidingPresentationSettings { RiderVisualHeight=2 }.Validate());
            Throws<InvalidOperationException>(()=>new RidingPresentationSettings { NativeRidePoseValue=-1 }.Validate());
        });
        add("invalid turning settings and explicit null ride config fail before game activation",()=>
        {
            Throws<InvalidOperationException>(()=>new RidingPresentationSettings { TurnDegreesPerSecond=0 }.Validate());
            Throws<InvalidOperationException>(()=>new RidingPresentationSettings { TurnDegreesPerSecond=float.PositiveInfinity }.Validate());
            Throws<InvalidOperationException>(()=>DuckovSharedBike.StationSettingsFile.Parse("{\"Riding\":null}"));
        });
    }

    private static void Equal<T>(T expected,T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void Angle(double expected,double actual)
    {
        var difference=Math.Abs((expected-actual)%360); difference=Math.Min(difference,360-difference);
        if (difference>0.00001) throw new Exception($"Expected angle {expected}, got {actual}");
    }
    private static void Throws<T>(Action action) where T:Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected "+typeof(T).Name); }
}
