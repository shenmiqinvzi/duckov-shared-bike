using DuckovSharedBike;
using DuckovSharedBike.Domain;
using Newtonsoft.Json;

internal static class CompleteReleaseTests
{
    internal static void Register(Action<string,Action> add)
    {
        add("release lays out a denser station network ordered farm > warehouse > ground zero",()=>
        {
            var layout=Layout(); Equal(15,layout.Sites.Count);
            var farm=layout.Sites.Count(s=>s.MapId=="Level_Farm_Main" && s.SceneName=="Level_Farm_01");
            var warehouse=layout.Sites.Count(s=>s.MapId=="Level_HiddenWarehouse_Main" && s.SceneName=="Level_HiddenWarehouse");
            var groundZero=layout.Sites.Count(s=>s.MapId=="Level_GroundZero_Main" && s.SceneName=="Level_GroundZero_1");
            Equal(6,farm); Equal(5,warehouse); Equal(4,groundZero);
            True(farm>warehouse && warehouse>groundZero);
            Equal(layout.Sites.Count,farm+warehouse+groundZero);
            True(layout.Sites.All(s=>s.Anchor.IsFinite && s.SearchRadius>=6 && s.SearchRadius<=40));
            Equal(15,layout.Sites.Select(s=>s.Id).Distinct().Count());
        });
        add("native one-way warehouse route cannot strand a parked rented bicycle",()=>
        {
            var layout=Layout(); True(layout.Routes.Any(r=>r.SourceMap=="Level_GroundZero_Main" && r.TargetMap=="Level_HiddenWarehouse_Main" && !r.Returnable));
            True(!layout.CanLeaveAndReturn("Level_GroundZero_Main","Level_HiddenWarehouse_Main"));
            Equal(TravelDecision.NoReturnRoute,TravelPolicy.Evaluate(true,false,false,false,false));
        });
        add("returnable travel requires verified routes in both directions",()=>
        {
            var layout=new StationBlueprints { Routes=new() { new MapRoute { SourceMap="A",TargetMap="B",Returnable=true } } };
            layout.Validate(); True(!layout.CanLeaveAndReturn("A","B"));
            layout.Routes.Add(new MapRoute { SourceMap="B",TargetMap="A",Returnable=true });
            layout.Validate(); True(layout.CanLeaveAndReturn("A","B")); True(!layout.CanLeaveAndReturn("A","C"));
            Equal(TravelDecision.Allowed,TravelPolicy.Evaluate(true,false,false,false,true));
        });
        add("invalid station identities coordinates and search radii are rejected",()=>
        {
            foreach (var mutate in new Action<StationSite>[] {
                s=>s.Id="",s=>s.SceneName="",s=>s.Name="",s=>s.MapId="",s=>s.Anchor.X=float.NaN,
                s=>s.Yaw=float.PositiveInfinity,s=>s.SearchRadius=5,s=>s.SearchRadius=41 })
            {
                var layout=Layout(); mutate(layout.Sites[0]); Throws<InvalidOperationException>(layout.Validate);
            }
        });
        add("duplicate station and route identities are rejected before scene creation",()=>
        {
            var layout=Layout(); layout.Sites.Add(layout.Sites[0]); Throws<InvalidOperationException>(layout.Validate);
            layout=Layout(); layout.Routes.Add(layout.Routes[0]); Throws<InvalidOperationException>(layout.Validate);
        });
        add("release disables temporary test stations while retaining the full planned network",()=>
        {
            var settings=StationSettingsFile.Read(Path.Combine(ProjectRoot(),"mod","prototype.json"));
            True(settings.Enabled); True(!settings.AutomaticTestStations); Equal(3,settings.StationsPerScene);
            True(settings.Riding.NativeRidePose && settings.Riding.PedalAndGripPose && settings.Riding.VehicleClearance);
        });
        add("saved station direction survives copy and JSON reload",()=>
        {
            var station=new StationRecord { Id="s",Name="station",MapId="A",SceneName="a",Yaw=270,Position=new() { X=2 } };
            var copy=JsonConvert.DeserializeObject<StationRecord>(JsonConvert.SerializeObject(station.Copy()))!;
            Equal(270f,copy.Yaw); copy.Position.X=8; Equal(2f,station.Position.X);
        });
        add("ordinary subscene travel requires dismount and preserves the parked rental",()=>
        {
            Equal(TravelDecision.Dismount,TravelPolicy.Evaluate(true,true,false,true,true));
            Equal(TravelDecision.Allowed,TravelPolicy.Evaluate(true,false,false,true,false));
        });
        add("normal extraction is blocked for riding and parked vehicles even on returnable routes",()=>
        {
            foreach (var riding in new[] { false,true })
                Equal(TravelDecision.ReturnVehicle,TravelPolicy.Evaluate(true,riding,true,true,true));
        });
        add("no vehicle including unpaid-only orders does not interfere with any travel",()=>
        {
            var engine=Rented(); engine.Close(engine.CurrentRental!.RentalId,CloseReason.Returned,"B");
            True(engine.HasUnpaidOrders && engine.CanEvacuate);
            Equal(TravelDecision.Allowed,TravelPolicy.Evaluate(engine.CurrentRental!=null,false,true,false,false));
        });
        add("two-bone pose preserves native limb lengths on 1000 seeded reachable targets",()=>
        {
            var random=new Random(20261014);
            for (var i=0;i<1000;i++)
            {
                var upper=0.06+random.NextDouble()*0.15; var lower=0.06+random.NextDouble()*0.15;
                var radius=Math.Abs(upper-lower)+0.005+random.NextDouble()*(2*Math.Min(upper,lower)-0.01);
                var root=new WorldPoint { X=10,Y=2,Z=5 }; var target=new WorldPoint { X=10+(float)radius,Y=2,Z=5 };
                var result=TwoBoneGeometry.Solve(root,target,new() { X=10,Y=3,Z=6 },upper,lower);
                Near(upper,Distance(root,result.Joint)); Near(lower,Distance(result.Joint,result.End)); Near(0,Distance(target,result.End));
            }
        });
        add("unreachable pedal and grip targets clamp reach without stretching duck bones",()=>
        {
            var root=new WorldPoint(); var solution=TwoBoneGeometry.Solve(root,new() { Y=3 },new() { Z=1 },0.095,0.099);
            True(solution.Joint.IsFinite && solution.End.IsFinite); True(Distance(root,solution.End)<0.194);
            Near(0.095,Distance(root,solution.Joint)); Near(0.099,Distance(solution.Joint,solution.End));
        });
        add("coincident targets and collinear bend hints produce finite poses",()=>
        {
            foreach (var target in new[] { new WorldPoint(),new WorldPoint { Y=-0.1f } })
            {
                var root=new WorldPoint(); var solution=TwoBoneGeometry.Solve(root,target,new() { Y=-1 },0.095,0.099);
                True(solution.Joint.IsFinite && solution.End.IsFinite);
                Near(0.095,Distance(root,solution.Joint)); Near(0.099,Distance(solution.Joint,solution.End));
            }
        });
        add("bend hint chooses the expected side of a symmetric knee",()=>
        {
            var root=new WorldPoint(); var target=new WorldPoint { Y=-0.1f };
            var forward=TwoBoneGeometry.Solve(root,target,new() { Z=1 },0.095,0.099);
            var backward=TwoBoneGeometry.Solve(root,target,new() { Z=-1 },0.095,0.099);
            True(forward.Joint.Z>0 && backward.Joint.Z<0); Near(0,Distance(forward.End,backward.End));
        });
        add("nonfinite coordinates and invalid bone lengths cannot reach Unity transforms",()=>
        {
            foreach (var length in new[] { 0d,-1d,double.NaN,double.PositiveInfinity,101d })
                Throws<ArgumentException>(()=>TwoBoneGeometry.Solve(new(),new() { Y=1 },new() { Z=1 },length,0.1));
            Throws<ArgumentException>(()=>TwoBoneGeometry.Solve(new() { X=float.NaN },new(),new(),0.1,0.1));
        });
        add("rental closing and payment display dates survive reload and duplicate callbacks",()=>
        {
            var engine=Rented(); var rental=engine.CurrentRental!;
            True(DateTimeOffset.TryParse(rental.StartedUtc,out _));
            var order=engine.Close(rental.RentalId,CloseReason.Returned,"B").Id!;
            var closed=engine.Snapshot().Rentals[0].ClosedUtc; True(DateTimeOffset.TryParse(closed,out _));
            engine.Close(rental.RentalId,CloseReason.DeathRecovery); Equal(closed,engine.Snapshot().Rentals[0].ClosedUtc);
            var operation=engine.BeginPayment(order,200).Id!; engine.CompletePayment(order,operation,true);
            var paid=engine.FindOrder(order)!.PaidUtc; True(DateTimeOffset.TryParse(paid,out _));
            Equal(RuleCode.AlreadyPaid,engine.CompletePayment(order,operation,true).Code); Equal(paid,engine.FindOrder(order)!.PaidUtc);
            var restored=new RentalEngine(engine.SaveId); restored.Restore(JsonConvert.DeserializeObject<RentalState>(JsonConvert.SerializeObject(engine.Snapshot()))!);
            Equal(rental.StartedUtc,restored.Snapshot().Rentals[0].StartedUtc); Equal(closed,restored.Snapshot().Rentals[0].ClosedUtc); Equal(paid,restored.FindOrder(order)!.PaidUtc);
        });
        add("wall-clock display dates cannot add offline rental hours",()=>
        {
            var engine=Rented(); var state=engine.Snapshot(); state.Rentals[0].StartedUtc="2000-01-01T00:00:00Z";
            engine.Restore(state); engine.AdvanceTime(1,PlayContext.Active);
            Equal(200L,engine.FindOrder(engine.Close(engine.CurrentRental!.RentalId,CloseReason.Returned,"B").Id!)!.Total);
        });
        add("old records without display timestamps remain valid and payable",()=>
        {
            var engine=Rented(); var order=engine.Close(engine.CurrentRental!.RentalId,CloseReason.Returned,"B").Id!;
            var old=engine.Snapshot(); old.Rentals[0].StartedUtc=null; old.Rentals[0].ClosedUtc=null;
            var restored=new RentalEngine(engine.SaveId); restored.Restore(old);
            var operation=restored.BeginPayment(order,200).Id!; Equal(RuleCode.Success,restored.CompletePayment(order,operation,true).Code);
            Equal(RuleCode.Success,restored.RentalEligibility);
        });
    }

    private static StationBlueprints Layout()=>StationSettingsFile.ReadBlueprints(Path.Combine(ProjectRoot(),"mod","station-layout.json"));
    private static string ProjectRoot()
    {
        for (var directory=new DirectoryInfo(AppContext.BaseDirectory); directory!=null; directory=directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName,"mod","station-layout.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("Cannot locate the actual release station layout.");
    }
    private static RentalEngine Rented() { var e=new RentalEngine("test-release"); e.Rent("A","bike",new RatePlan(),true); return e; }
    private static double Distance(WorldPoint a,WorldPoint b)=>Math.Sqrt(Math.Pow((double)a.X-b.X,2)+Math.Pow((double)a.Y-b.Y,2)+Math.Pow((double)a.Z-b.Z,2));
    private static void Near(double expected,double actual) { if (Math.Abs(expected-actual)>0.000005) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void True(bool value) { if (!value) throw new Exception("Expected true"); }
    private static void Equal<T>(T expected,T actual) { if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void Throws<T>(Action action) where T:Exception { try { action(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }
}
