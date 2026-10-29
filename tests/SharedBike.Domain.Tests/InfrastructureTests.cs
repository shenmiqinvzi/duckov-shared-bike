using DuckovSharedBike.Domain;
using Newtonsoft.Json;

internal static class InfrastructureTests
{
    internal static void Register(Action<string,Action> add)
    {
        add("packaged config routes all three requested maps and farm south",()=>
        {
            var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../mod/prototype.json"));
            var settings=DuckovSharedBike.StationSettingsFile.Read(path);
            foreach (var key in new[] { "Level_GroundZero","HiddenWarehouse","Level_Farm","Level_Farm_South" })
                Equal(key,StationRouting.TargetKey(settings,key,key,true));
        });
        add("native config reader replaces default targets instead of appending duplicates",()=>
        {
            var settings=DuckovSharedBike.StationSettingsFile.Parse("{\"TargetDisplayKeys\":[\"Level_GroundZero\"]}");
            Equal(1,settings.TargetDisplayKeys.Count); Equal(2,settings.StationsPerScene);
            Equal<string?>(null,StationRouting.TargetKey(settings,"Level_Farm","Level_Farm",true));
        });
        add("missing config uses three-map defaults while explicit null config is rejected",()=>
        {
            Equal(4,DuckovSharedBike.StationSettingsFile.Parse(null).TargetDisplayKeys.Count);
            Reject(()=>DuckovSharedBike.StationSettingsFile.Parse("null"));
        });
        add("outdoor map settings never create stations in its underground subscene",()=>
            Equal<string?>(null,StationRouting.TargetKey(new StationLayoutSettings(),"Level_GroundZero","Level_GroundZero_Cave",false)));
        add("farm south remains eligible when loaded as a subscene",()=>
            Equal("Level_Farm_South",StationRouting.TargetKey(new StationLayoutSettings(),"Level_Farm","Level_Farm_South",false)));
        add("unknown main scene may use its confirmed outdoor parent key",()=>
            Equal("HiddenWarehouse",StationRouting.TargetKey(new StationLayoutSettings(),"HiddenWarehouse","",true)));
        add("disabled station config does not enable automatic placements",()=>
            Equal<string?>(null,StationRouting.TargetKey(new StationLayoutSettings { Enabled=false },"Level_Farm","Level_Farm",true)));
        add("invalid station spacing and retry interval are rejected before generation",()=>
        {
            Reject(()=>new StationLayoutSettings { MinimumStationSeparation=float.NaN }.Validate());
            Reject(()=>new StationLayoutSettings { PlacementRetrySeconds=0 }.Validate());
            Reject(()=>new StationLayoutSettings { StationsPerScene=99 }.Validate());
        });
        add("duplicate configured fixed station IDs are rejected across scenes",()=>
        {
            var settings=new StationLayoutSettings();
            settings.FixedStations.Add(Station("same","zero","main"));
            settings.FixedStations.Add(Station("same","farm","south"));
            Reject(settings.Validate);
        });
        add("configured station copies cannot mutate persisted coordinates",()=>
        {
            var station=Station("one","zero","main"); var copy=station.Copy(); copy.Position.X=123;
            Equal(0f,station.Position.X);
        });
        add("three-map layout round trip preserves old zero-zone station IDs and positions",()=>
        {
            var state=World();
            state.Stations.Add(Station("prototype-zero-1","zero","main")); state.Stations[0].Position.Z=19;
            state.Stations.Add(Station("farm-1","farm","main"));
            state.Stations.Add(Station("warehouse-1","warehouse","main"));
            var restored=JsonConvert.DeserializeObject<WorldState>(JsonConvert.SerializeObject(state))!;
            WorldValidation.Validate(restored,"slot-1");
            Equal(3,restored.Stations.Count); Equal("prototype-zero-1",restored.Stations[0].Id); Equal(19f,restored.Stations[0].Position.Z);
        });
        add("world from a different player slot cannot be accepted",()=>Reject(()=>WorldValidation.Validate(World(),"slot-2")));
        add("duplicate saved station identities fail without changing the input",()=>
        {
            var state=World(); state.Stations.Add(Station("A","zero","main")); state.Stations.Add(Station("A","farm","main"));
            Reject(()=>WorldValidation.Validate(state,"slot-1")); Equal(2,state.Stations.Count);
        });
        add("unknown world schema and nonfinite stations are rejected",()=>
        {
            var state=World(); state.SchemaVersion=2; Reject(()=>WorldValidation.Validate(state,"slot-1"));
            state.SchemaVersion=1; state.Stations.Add(Station("A","zero","main")); state.Stations[0].Position.Y=float.PositiveInfinity;
            Reject(()=>WorldValidation.Validate(state,"slot-1"));
        });
        add("incomplete vehicle origin and invalid yaw are rejected",()=>
        {
            var state=World(); state.Vehicle=new VehiclePose { VehicleId="bike",MapId="zero",SceneName="main" };
            WorldValidation.Validate(state,"slot-1"); state.Vehicle.SceneName="";
            Reject(()=>WorldValidation.Validate(state,"slot-1")); state.Vehicle.SceneName="main"; state.Vehicle.Yaw=float.NaN;
            Reject(()=>WorldValidation.Validate(state,"slot-1"));
        });
        add("legacy business-only world can load before missing-vehicle system recovery",()=>
            WorldValidation.Validate(World(),"slot-1"));

        add("charged receipt can confirm a pending or completed order",()=>
        {
            var (engine,order,receipt)=Pending();
            CashJournalValidation.Validate(new[] { receipt },engine.Snapshot());
            engine.CompletePayment(order,receipt.OperationId,true);
            CashJournalValidation.Validate(new[] { receipt },engine.Snapshot());
        });
        add("receipt with a partial debit cannot masquerade as charged",()=>
        {
            var (engine,_,receipt)=Pending(); receipt.CashAfter=150;
            Reject(()=>CashJournalValidation.Validate(new[] { receipt },engine.Snapshot()));
        });
        add("a charged receipt cannot be attached to a different payment",()=>
        {
            var (engine,_,receipt)=Pending(); receipt.OperationId="foreign";
            Reject(()=>CashJournalValidation.Validate(new[] { receipt },engine.Snapshot()));
        });
        add("payment amount mismatch and duplicate receipt IDs are rejected",()=>
        {
            var (engine,_,receipt)=Pending(); receipt.Amount=100; receipt.CashAfter=200;
            Reject(()=>CashJournalValidation.Validate(new[] { receipt },engine.Snapshot()));
            (engine,_,receipt)=Pending(); Reject(()=>CashJournalValidation.Validate(new[] { receipt,receipt },engine.Snapshot()));
        });
        add("uncertain debit stays pending and cannot confirm a paid order",()=>
        {
            var (engine,order,receipt)=Pending(); receipt.Decision=CashDecision.Uncertain; receipt.CashAfter=150;
            CashJournalValidation.Validate(new[] { receipt },engine.Snapshot());
            engine.CompletePayment(order,receipt.OperationId,true);
            Reject(()=>CashJournalValidation.Validate(new[] { receipt },engine.Snapshot()));
        });
        add("declined historical operation may remain after successful retry with a new operation",()=>
        {
            var (engine,order,declined)=Pending(); declined.Decision=CashDecision.Declined; declined.CashAfter=declined.CashBefore;
            engine.CompletePayment(order,declined.OperationId,false);
            var next=engine.BeginPayment(order,300);
            var charged=new CashJournalEntry { OperationId=next.Id!,Amount=200,CashBefore=300,CashAfter=100,Decision=CashDecision.Charged };
            engine.CompletePayment(order,next.Id!,true);
            CashJournalValidation.Validate(new[] { declined,charged },engine.Snapshot());
        });
        add("negative balances and false declined receipt are rejected",()=>
        {
            var (engine,_,receipt)=Pending(); receipt.CashBefore=-1;
            Reject(()=>CashJournalValidation.Validate(new[] { receipt },engine.Snapshot()));
            (engine,_,receipt)=Pending(); receipt.Decision=CashDecision.Declined;
            Reject(()=>CashJournalValidation.Validate(new[] { receipt },engine.Snapshot()));
        });

        add("cash disk verification accepts reordered stacks in all three sources",()=>
        {
            var expected=new CashInventorySnapshot(); var actual=new CashInventorySnapshot();
            foreach (var source in new[] { "backpack","storage","pet" })
            {
                expected.Add(source,new[] { new CashStack(1,100),new CashStack(2,200) });
                actual.Add(source,new[] { new CashStack(2,200),new CashStack(1,100) });
            }
            expected.Verify(actual);
        });
        foreach (var staleSource in new[] { "backpack","storage","pet" })
            add("cash disk verification rejects stale debit in "+staleSource,()=>
            {
                var expected=new CashInventorySnapshot(); var actual=new CashInventorySnapshot();
                foreach (var source in new[] { "backpack","storage","pet" })
                {
                    expected.Add(source,new[] { new CashStack(1,100) });
                    actual.Add(source,new[] { new CashStack(1,source==staleSource ? 300 : 100) });
                }
                Reject(()=>expected.Verify(actual));
            });
        add("cash verification cannot ignore a missing empty inventory",()=>
        {
            var expected=new CashInventorySnapshot(); expected.Add("storage",Array.Empty<CashStack>());
            Reject(()=>expected.Verify(new CashInventorySnapshot()));
        });
        add("duplicate cash stack identities and negative amounts fail before save verification",()=>
        {
            Reject(()=>new CashInventorySnapshot().Add("storage",new[] { new CashStack(1,2),new CashStack(1,2) }));
            Reject(()=>new CashInventorySnapshot().Add("pet",new[] { new CashStack(1,-1) }));
        });
    }

    private static WorldState World()=>new WorldState { SaveId="slot-1" };
    private static StationRecord Station(string id,string map,string scene)=>new StationRecord { Id=id,MapId=map,SceneName=scene,Name=id };
    private static (RentalEngine Engine,string Order,CashJournalEntry Receipt) Pending()
    {
        var engine=new RentalEngine("slot-1"); engine.Rent("station","bike",new RatePlan(),true);
        var order=engine.Close(engine.CurrentRental!.RentalId,CloseReason.Returned,"station").Id!;
        var payment=engine.BeginPayment(order,300);
        return (engine,order,new CashJournalEntry { OperationId=payment.Id!,Amount=200,CashBefore=300,CashAfter=100,Decision=CashDecision.Charged });
    }
    private static void Equal<T>(T expected,T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Expected {expected}, got {actual}");
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Expected invalid data to be rejected.");
    }
}
