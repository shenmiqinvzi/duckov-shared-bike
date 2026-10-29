using System.Text.Json;
using DuckovSharedBike.Domain;

var cases = new List<(string Name, Action Run)>();
var expectedFees = new (long Milliseconds, long Fee)[]
{
    (0,200),(1,200),(10*60_000,200),(3_599_999,200),(3_600_000,200),
    (3_600_001,400),(7_200_000,400),(7_200_001,600)
};
foreach (var input in expectedFees)
    Add($"hour boundary {input.Milliseconds}ms => {input.Fee}", () =>
        Equal(input.Fee, Billing.Quote(input.Milliseconds, 0, new RatePlan(), CloseReason.Returned).Total));
Add("sub-millisecond after exact hour starts second billing hour", () =>
    Equal(400L, Billing.Quote(3_600_000, 0.01m, new RatePlan(), CloseReason.Returned).Total));
Add("death at 10 minutes costs 2200", () => Equal(2200L, Closed(600_000, CloseReason.DeathRecovery).Total));
Add("death at 70 minutes costs 2400", () => Equal(2400L, Closed(4_200_000, CloseReason.DeathRecovery).Total));
Add("reported loss adds configured recovery fee", () => Equal(2200L, Closed(1, CloseReason.ReportedLost).Total));
Add("system recovery does not add a penalty", () => Equal(200L, Closed(1, CloseReason.SystemRecovery).Total));
Add("vehicle creation failure creates neither rental nor minimum charge", () =>
{
    var e = new RentalEngine("save-A");
    Equal(RuleCode.VehicleCreationFailed, e.Rent("station-A", "bike-A", new RatePlan(), false).Code);
    Equal(0, e.Snapshot().Rentals.Count); Equal(0, e.Snapshot().Orders.Count); True(e.CanEvacuate);
});
Add("only one active rental, even after dismounting", () =>
{
    var e = Rented(); e.SetRiding("bike-A", true); e.SetRiding("bike-A", false);
    Equal(RuleCode.ActiveRentalExists, e.Rent("station-B", "bike-B", new RatePlan(), true).Code);
    Equal(1, e.Snapshot().Rentals.Count); False(e.CanEvacuate);
});
Add("mounting someone else's vehicle fails", () =>
{
    var e = Rented(); Equal(RuleCode.WrongVehicle, e.SetRiding("foreign-bike", true).Code);
    Equal(RideState.Parked, e.CurrentRental!.RideState);
});
Add("parked time counts as much as ridden time", () =>
{
    var e = Rented(); e.AdvanceTime(1_800_000, PlayContext.Active);
    e.SetRiding("bike-A", true); e.AdvanceTime(1_800_001, PlayContext.Active);
    Equal(400L, e.CurrentFee!.Value.Total);
});
Add("pause loading and offline time do not count", () =>
{
    var e = Rented(); e.AdvanceTime(100, PlayContext.Active);
    e.AdvanceTime(86_400_000, PlayContext.Paused); e.AdvanceTime(86_400_000, PlayContext.Loading);
    e.AdvanceTime(86_400_000, PlayContext.Offline); Equal(100L, e.CurrentRental!.ElapsedActiveMs);
});
Add("fractional frames accumulate instead of being truncated", () =>
{
    var e = Rented(); for (var i = 0; i < 10; i++) e.AdvanceTime(0.1m, PlayContext.Active);
    Equal(1L, e.CurrentRental!.ElapsedActiveMs); Equal(0m, e.CurrentRental.FractionalMs);
});
Add("snapshot reload preserves partial milliseconds without offline accrual", () =>
{
    var e = Rented(); e.AdvanceTime(0.75m, PlayContext.Active);
    var restored = Reload(e); restored.AdvanceTime(0.25m, PlayContext.Active);
    Equal(1L, restored.CurrentRental!.ElapsedActiveMs);
});
Add("riding blocks all combat consumables and world interactions", () =>
{
    var e = Rented(); e.SetRiding("bike-A", true);
    foreach (var action in new[] { PlayerAction.Sprint, PlayerAction.Aim, PlayerAction.Shoot, PlayerAction.Melee,
        PlayerAction.Throw, PlayerAction.AttackSkill, PlayerAction.UseConsumable, PlayerAction.Loot,
        PlayerAction.Pickup, PlayerAction.Drop, PlayerAction.Talk, PlayerAction.WorldInteract }) False(e.Allows(action));
    foreach (var action in new[] { PlayerAction.Move, PlayerAction.InspectMap, PlayerAction.InspectBackpack,
        PlayerAction.ReturnBike, PlayerAction.Dismount }) True(e.Allows(action));
});
Add("dismount restores normal action eligibility but not evacuation", () =>
{
    var e = Rented(); e.SetRiding("bike-A", true); e.SetRiding("bike-A", false);
    foreach (var action in Enum.GetValues<PlayerAction>()) True(e.Allows(action)); False(e.CanEvacuate);
});
Add("returning at another station ends debt to vehicle but retains payment debt", () =>
{
    var e = Rented(); var id = e.CurrentRental!.RentalId;
    Equal(RuleCode.Success, e.Close(id, CloseReason.Returned, "station-B").Code);
    True(e.CanEvacuate); Equal(RuleCode.UnpaidOrders, e.RentalEligibility);
    Equal("station-B", e.Snapshot().Rentals.Single().ReturnStationId);
});
Add("normal return requires a real station identity", () =>
{
    var e = Rented(); Throws<ArgumentException>(() => e.Close(e.CurrentRental!.RentalId, CloseReason.Returned));
    False(e.CanEvacuate); Equal(0, e.Snapshot().Orders.Count);
});
Add("repeated death callback creates one order and one penalty", () =>
{
    var e = Rented(); var id = e.CurrentRental!.RentalId;
    var first = e.Close(id, CloseReason.DeathRecovery);
    Equal(first.Id, e.Close(id, CloseReason.DeathRecovery).Id);
    Equal(1, e.Snapshot().Orders.Count); Equal(2200L, e.FindOrder(first.Id!)!.Total);
});
Add("death after return cannot append a penalty", () =>
{
    var e = Rented(); var id = e.CurrentRental!.RentalId;
    var order = e.Close(id, CloseReason.Returned, "station-B").Id!;
    e.Close(id, CloseReason.DeathRecovery); Equal(200L, e.FindOrder(order)!.Total);
});
Add("death wins the race only if it closes the rental first", () =>
{
    var e = Rented(); var id = e.CurrentRental!.RentalId;
    var order = e.Close(id, CloseReason.DeathRecovery).Id!;
    e.Close(id, CloseReason.Returned, "station-B"); Equal(2200L, e.FindOrder(order)!.Total);
});
Add("settled amount stays fixed during later play and offline time", () =>
{
    var e = Rented(); var order = e.Close(e.CurrentRental!.RentalId, CloseReason.Returned, "B").Id!;
    e.AdvanceTime(86_400_000, PlayContext.Active); Equal(200L, e.FindOrder(order)!.Total);
});
Add("changing config cannot change the existing contract", () =>
{
    var e = new RentalEngine("A"); var rates = new RatePlan(); e.Rent("A", "bike-A", rates, true);
    rates.HourlyRate = 1; rates.DeathPenalty = 0;
    Equal(2200L, e.FindOrder(e.Close(e.CurrentRental!.RentalId, CloseReason.DeathRecovery).Id!)!.Total);
});
Add("restored riding posture becomes parked while preserving ownership and debt", () =>
{
    var e = Rented(); e.SetRiding("bike-A", true); e.AdvanceTime(1234, PlayContext.Active);
    var loaded = Reload(e); Equal(RideState.Parked, loaded.CurrentRental!.RideState);
    Equal("bike-A", loaded.CurrentRental.VehicleId); Equal(1234L, loaded.CurrentRental.ElapsedActiveMs);
    False(loaded.CanEvacuate);
});
Add("one save cannot load another save's debt", () =>
{
    var e = Rented(); var other = new RentalEngine("save-B");
    Throws<InvalidOperationException>(() => other.Restore(e.Snapshot())); True(other.CanEvacuate);
});
Add("returned snapshots and records cannot mutate live business state", () =>
{
    var e = Rented(); e.Snapshot().Rentals.Clear(); e.CurrentRental!.Rates.HourlyRate = 1;
    False(e.CanEvacuate); Equal(200L, e.CurrentFee!.Value.Total);
});
Add("invalid persisted amounts are rejected without erasing original debt", () =>
{
    var e = Rented(); var order = e.Close(e.CurrentRental!.RentalId, CloseReason.Returned, "B").Id!;
    var bad = e.Snapshot(); bad.Orders[0].Total = 0;
    Throws<InvalidOperationException>(() => e.Restore(bad)); Equal(200L, e.FindOrder(order)!.Total);
});
Add("duplicate active rentals are rejected on restore", () =>
{
    var e = Rented(); var bad = e.Snapshot(); var duplicate = bad.Rentals[0].Copy();
    duplicate.RentalId = "other"; duplicate.VehicleId = "other-bike"; bad.Rentals.Add(duplicate);
    Throws<InvalidOperationException>(() => e.Restore(bad)); Equal(1, e.Snapshot().Rentals.Count);
});
Add("negative time and overflowing money cannot mutate accumulated duration", () =>
{
    var e = Rented(); Throws<ArgumentOutOfRangeException>(() => e.AdvanceTime(-1, PlayContext.Active));
    Throws<OverflowException>(() => e.AdvanceTime(decimal.MaxValue, PlayContext.Active));
    Equal(0L, e.CurrentRental!.ElapsedActiveMs);
    Throws<OverflowException>(() => Billing.Quote(long.MaxValue, 0, new RatePlan { HourlyRate = int.MaxValue }, CloseReason.Returned));
});
Add("unknown rental does not close the actual rented vehicle", () =>
{
    var e = Rented(); Equal(RuleCode.UnknownRental, e.Close("missing", CloseReason.DeathRecovery).Code);
    False(e.CanEvacuate); Equal(0, e.Snapshot().Orders.Count);
});
Add("insufficient cash never partially debits or removes the order", () =>
{
    var (e,id) = Unpaid(); var cash = new FakeCash(199); var store = new FakeStore(e.Snapshot());
    Equal(RuleCode.InsufficientCash, PaymentService.Attempt(e,id,cash,store).Code);
    Equal(199L,cash.AvailableCash); Equal(0,cash.Debits); Equal(0,store.Calls);
    Equal(PaymentStatus.Unpaid,e.FindOrder(id)!.PaymentStatus); False(e.RentalEligibility == RuleCode.Success);
});
Add("exact cash balance pays once and restores rental eligibility", () =>
{
    var (e,id) = Unpaid(); var cash = new FakeCash(200); var store = new FakeStore(e.Snapshot());
    Equal(RuleCode.Success,PaymentService.Attempt(e,id,cash,store).Code); Equal(0L,cash.AvailableCash);
    Equal(1,cash.Debits); Equal(RuleCode.Success,e.RentalEligibility);
    Equal(PaymentStatus.Paid,store.Current.Orders.Single().PaymentStatus);
});
Add("double clicking an already paid order does not debit twice", () =>
{
    var (e,id) = Unpaid(); var cash = new FakeCash(500); var store = new FakeStore(e.Snapshot());
    PaymentService.Attempt(e,id,cash,store); Equal(RuleCode.AlreadyPaid,PaymentService.Attempt(e,id,cash,store).Code);
    Equal(300L,cash.AvailableCash); Equal(1,cash.Debits);
});
Add("death compensation and rent are paid together in one debit", () =>
{
    var e = Rented(); var id = e.Close(e.CurrentRental!.RentalId,CloseReason.DeathRecovery).Id!;
    var cash = new FakeCash(2200); var store = new FakeStore(e.Snapshot());
    Equal(RuleCode.Success,PaymentService.Attempt(e,id,cash,store).Code); Equal(1,cash.Debits); Equal(0L,cash.AvailableCash);
});
Add("failed preparation save never reaches the cash gateway", () =>
{
    var (e,id) = Unpaid(); var cash = new FakeCash(200); var store = new FakeStore(e.Snapshot()) { FailCall = 1 };
    Equal(RuleCode.PersistenceFailed,PaymentService.Attempt(e,id,cash,store).Code);
    Equal(0,cash.Debits); Equal(0,cash.Calls); Equal(PaymentStatus.Unpaid,e.FindOrder(id)!.PaymentStatus);
});
Add("crash after debit and before order commit resumes same payment without double charge", () =>
{
    var (e,id) = Unpaid(); var cash = new FakeCash(200); var store = new FakeStore(e.Snapshot()) { FailCall = 2 };
    Equal(RuleCode.PersistenceFailed,PaymentService.Attempt(e,id,cash,store).Code);
    Equal(0L,cash.AvailableCash); Equal(PaymentStatus.Pending,store.Current.Orders.Single().PaymentStatus);
    var restarted = new RentalEngine("save-A"); restarted.Restore(JsonRoundTrip(store.Current));
    Equal(RuleCode.Success,PaymentService.Attempt(restarted,id,cash,store).Code);
    Equal(1,cash.Debits); Equal(0L,cash.AvailableCash); Equal(RuleCode.Success,restarted.RentalEligibility);
});
Add("exception after cash debit leaves a recoverable pending operation", () =>
{
    var (e,id) = Unpaid(); var cash = new FakeCash(200) { ThrowAfterDebit = true }; var store = new FakeStore(e.Snapshot());
    Equal(RuleCode.PaymentUncertain,PaymentService.Attempt(e,id,cash,store).Code);
    Equal(PaymentStatus.Pending,e.FindOrder(id)!.PaymentStatus);
    Equal(RuleCode.Success,PaymentService.Attempt(e,id,cash,store).Code); Equal(1,cash.Debits);
});
Add("ambiguous cash result never marks an order paid", () =>
{
    var (e,id) = Unpaid(); var cash = new FakeCash(200) { Uncertain = true }; var store = new FakeStore(e.Snapshot());
    Equal(RuleCode.PaymentUncertain,PaymentService.Attempt(e,id,cash,store).Code);
    Equal(PaymentStatus.Pending,e.FindOrder(id)!.PaymentStatus); Equal(RuleCode.UnpaidOrders,e.RentalEligibility);
});
Add("receipt with wrong amount is not trusted as payment", () =>
{
    var (e,id) = Unpaid(); var cash = new FakeCash(200) { WrongReceipt = true }; var store = new FakeStore(e.Snapshot());
    Equal(RuleCode.PaymentUncertain,PaymentService.Attempt(e,id,cash,store).Code);
    Equal(PaymentStatus.Pending,e.FindOrder(id)!.PaymentStatus);
});
Add("cash declined after balance check leaves an unpaid order", () =>
{
    var (e,id) = Unpaid(); var cash = new FakeCash(200) { Decline = true }; var store = new FakeStore(e.Snapshot());
    Equal(RuleCode.PaymentDeclined,PaymentService.Attempt(e,id,cash,store).Code);
    Equal(200L,cash.AvailableCash); Equal(PaymentStatus.Unpaid,e.FindOrder(id)!.PaymentStatus);
});
Add("paying one of multiple historical orders does not restore eligibility", () =>
{
    var (e,first) = Unpaid(); var cash = new FakeCash(600); var store = new FakeStore(e.Snapshot());
    PaymentService.Attempt(e,first,cash,store); e.Rent("B","bike-B",new RatePlan(),true);
    var second = e.Close(e.CurrentRental!.RentalId,CloseReason.Returned,"C").Id!;
    PaymentService.Attempt(e,second,cash,store);
    var historical = e.Snapshot(); foreach (var order in historical.Orders) { order.PaymentStatus = PaymentStatus.Unpaid; order.PaymentOperationId = null; }
    e.Restore(historical); cash = new FakeCash(400); store = new FakeStore(e.Snapshot());
    PaymentService.Attempt(e,first,cash,store); Equal(RuleCode.UnpaidOrders,e.RentalEligibility);
    PaymentService.Attempt(e,second,cash,store); Equal(RuleCode.Success,e.RentalEligibility);
});
Add("unknown payment order never reaches cash gateway", () =>
{
    var (e,_) = Unpaid(); var cash = new FakeCash(500); var store = new FakeStore(e.Snapshot());
    Equal(RuleCode.UnknownOrder,PaymentService.Attempt(e,"missing",cash,store).Code); Equal(0,cash.Calls);
});
Add("monotonic clock excludes pause and resume boundaries", () =>
{
    var clock=new ActivePlayClock(); Equal(0m,clock.Sample(100,PlayContext.Active));
    Equal(50m,clock.Sample(150,PlayContext.Active)); Equal(0m,clock.Sample(200,PlayContext.Paused));
    Equal(0m,clock.Sample(100_000,PlayContext.Paused)); Equal(0m,clock.Sample(100_100,PlayContext.Active));
    Equal(50m,clock.Sample(100_150,PlayContext.Active));
});
Add("clock reset never bills the gap between game sessions", () =>
{
    var clock=new ActivePlayClock(); clock.Sample(100,PlayContext.Active); clock.Reset();
    Equal(0m,clock.Sample(86_400_000,PlayContext.Active)); Equal(20m,clock.Sample(86_400_020,PlayContext.Active));
});
Add("loading context and non-monotonic input do not create charges", () =>
{
    var clock=new ActivePlayClock(); clock.Sample(100,PlayContext.Active);
    Equal(0m,clock.Sample(300,PlayContext.Loading)); Equal(0m,clock.Sample(10_000,PlayContext.Active));
    Throws<ArgumentOutOfRangeException>(() => clock.Sample(9999,PlayContext.Active));
    Equal(1m,clock.Sample(10_001,PlayContext.Active));
});
Add("native Newtonsoft serializer preserves contract and partial milliseconds", () =>
{
    var e=Rented(); e.AdvanceTime(3_600_000.125m,PlayContext.Active);
    var data=Newtonsoft.Json.JsonConvert.SerializeObject(e.Snapshot());
    var recovered=Newtonsoft.Json.JsonConvert.DeserializeObject<RentalState>(data)!;
    var engine=new RentalEngine(e.SaveId); engine.Restore(recovered);
    Equal(400L,engine.CurrentFee!.Value.Total); Equal(0.125m,engine.CurrentRental!.FractionalMs);
});
Add("native Newtonsoft serializer preserves pending payment operation", () =>
{
    var (e,id)=Unpaid(); var cash=new FakeCash(200); var store=new FakeStore(e.Snapshot()) { FailCall=2 };
    PaymentService.Attempt(e,id,cash,store);
    var data=Newtonsoft.Json.JsonConvert.SerializeObject(store.Current);
    var recovered=Newtonsoft.Json.JsonConvert.DeserializeObject<RentalState>(data)!;
    var engine=new RentalEngine(e.SaveId); engine.Restore(recovered);
    Equal(store.Current.Orders[0].PaymentOperationId,engine.FindOrder(id)!.PaymentOperationId);
    Equal(RuleCode.Success,PaymentService.Attempt(engine,id,cash,store).Code); Equal(1,cash.Debits);
});
Add("unknown schema version is rejected without resetting debt", () =>
{
    var (e,id)=Unpaid(); var bad=e.Snapshot(); bad.SchemaVersion=999;
    Throws<InvalidOperationException>(() => e.Restore(bad)); Equal(200L,e.FindOrder(id)!.Total);
});
Add("recovered vehicle cannot retain a return-station claim", () =>
{
    var e=Rented(); var id=e.Close(e.CurrentRental!.RentalId,CloseReason.DeathRecovery).Id!;
    var bad=e.Snapshot(); bad.Rentals[0].ReturnStationId="A";
    Throws<InvalidOperationException>(() => e.Restore(bad)); Equal(2200L,e.FindOrder(id)!.Total);
});

Add("zero weight has no riding speed bonus", () =>
    Near(3f,MovementMath.WithoutWeight(3f,Array.Empty<StatAdjustment>())));
Add("weight slowdown disappears while injury and equipment remain", () =>
    Near(3f*0.75f*0.9f,MovementMath.WithoutWeight(3f,new[] {
        new StatAdjustment(AdjustmentKind.PercentageMultiply,-0.5f,200,true),
        new StatAdjustment(AdjustmentKind.PercentageMultiply,-0.25f,200,false),
        new StatAdjustment(AdjustmentKind.PercentageMultiply,-0.1f,200,false) })));
Add("extreme overweight zero multiplier is removed without dividing by zero", () =>
    Near(2.4f,MovementMath.WithoutWeight(3f,new[] {
        new StatAdjustment(AdjustmentKind.PercentageMultiply,-1,200,true),
        new StatAdjustment(AdjustmentKind.PercentageMultiply,-0.2f,200,false) })));
Add("non-weight immobilization stays effective", () =>
    Near(0f,MovementMath.WithoutWeight(3f,new[] {
        new StatAdjustment(AdjustmentKind.PercentageMultiply,-1,200,true),
        new StatAdjustment(AdjustmentKind.PercentageMultiply,-1,200,false) })));
Add("same-order percentage additions form one native group", () =>
    Near(2.4f,MovementMath.WithoutWeight(3,new[] {
        new StatAdjustment(AdjustmentKind.PercentageAdd,-0.1f,100,false),
        new StatAdjustment(AdjustmentKind.PercentageAdd,-0.1f,100,false),
        new StatAdjustment(AdjustmentKind.PercentageAdd,-0.5f,100,true) })));
Add("different-order percentage additions are applied separately", () =>
    Near(2.43f,MovementMath.WithoutWeight(3,new[] {
        new StatAdjustment(AdjustmentKind.PercentageAdd,-0.1f,100,false),
        new StatAdjustment(AdjustmentKind.PercentageAdd,-0.1f,110,false) })));
Add("additive modifiers stay before percentage multipliers", () =>
    Near(3f,MovementMath.WithoutWeight(3,new[] {
        new StatAdjustment(AdjustmentKind.Add,1,0,false),
        new StatAdjustment(AdjustmentKind.PercentageMultiply,-0.25f,200,false),
        new StatAdjustment(AdjustmentKind.Add,-3,0,true) })));
Add("player at station cannot return a bike parked elsewhere", () =>
    False(SpatialRules.CanReturn(new WorldPoint(),new WorldPoint { X=10 },new WorldPoint(),4,true)));
Add("bike at station cannot be remotely returned by distant player", () =>
    False(SpatialRules.CanReturn(new WorldPoint { X=10 },new WorldPoint(),new WorldPoint(),4,true)));
Add("return range is inclusive but line of sight is mandatory", () =>
{
    True(SpatialRules.CanReturn(new WorldPoint { X=4 },new WorldPoint { Z=4 },new WorldPoint(),4,true));
    False(SpatialRules.CanReturn(new WorldPoint(),new WorldPoint(),new WorldPoint(),4,false));
});
Add("invalid world coordinates never qualify for return", () =>
    False(SpatialRules.Within(new WorldPoint { X=float.NaN },new WorldPoint(),4)));
Add("vehicle pose JSON preserves original map and parked position", () =>
{
    var state=new WorldState { SaveId="A",Vehicle=new VehiclePose { VehicleId="bike",MapId="GroundZero",SceneName="Main",Position=new WorldPoint { X=1.5f,Y=0.2f,Z=-3 },Yaw=90 } };
    var recovered=Newtonsoft.Json.JsonConvert.DeserializeObject<WorldState>(Newtonsoft.Json.JsonConvert.SerializeObject(state))!;
    Equal("GroundZero",recovered.Vehicle!.MapId); Equal(-3f,recovered.Vehicle.Position.Z); Equal(90f,recovered.Vehicle.Yaw);
});
Add("movement formula matches actual game Stat calculation on 500 seeded combinations", () =>
{
    var random=new Random(20261013);
    for (var iteration=0;iteration<500;iteration++)
    {
        var baseValue=(float)(1+random.NextDouble()*5);
        var inputs=new List<StatAdjustment>();
        var native=new ItemStatsSystem.Stat("test-speed",baseValue,false);
        for (var i=0;i<8;i++)
        {
            var kind=(AdjustmentKind)(random.Next(3)*100); var amount=(float)(random.NextDouble()*0.8-0.4);
            var weight=random.Next(4)==0; var order=i*10;
            inputs.Add(new StatAdjustment(kind,amount,order,weight));
            if (!weight) native.AddModifier(new ItemStatsSystem.Stats.Modifier((ItemStatsSystem.Stats.ModifierType)kind,amount,true,order,new object()));
        }
        Near(native.Value,MovementMath.WithoutWeight(baseValue,inputs));
    }
});

Add("production journal gateway records exact debit and balances", () =>
{
    var cash=new TestCashPort(300); var journal=new TestJournal(); var gateway=new JournalCashGateway(cash,journal);
    Equal(CashDecision.Charged,gateway.Charge("op-A",200).Decision); Equal(100L,cash.Balance);
    Equal(300L,journal.Find("op-A")!.CashBefore); Equal(100L,journal.Find("op-A")!.CashAfter); False(cash.InMutation);
});
Add("production journal gateway reuses persisted receipt without debiting twice", () =>
{
    var cash=new TestCashPort(200); var journal=new TestJournal(); var gateway=new JournalCashGateway(cash,journal);
    gateway.Charge("op-A",200); gateway.Charge("op-A",200); Equal(1,cash.Calls); Equal(0L,cash.Balance);
});
Add("production journal gateway retries failed flush without repeating debit", () =>
{
    var cash=new TestCashPort(200); var journal=new TestJournal { FailFlushCall=1 }; var gateway=new JournalCashGateway(cash,journal);
    Throws<IOException>(() => gateway.Charge("op-A",200)); False(cash.InMutation);
    Equal(CashDecision.Charged,gateway.Charge("op-A",200).Decision); Equal(1,cash.Calls); Equal(0L,cash.Balance);
});
Add("production journal gateway handles native API throwing after debit", () =>
{
    var cash=new TestCashPort(200) { ThrowAfter=true }; var journal=new TestJournal();
    Equal(CashDecision.Charged,new JournalCashGateway(cash,journal).Charge("op-A",200).Decision); Equal(0L,cash.Balance);
});
Add("production journal gateway does not mark API failure before debit paid", () =>
{
    var cash=new TestCashPort(200) { ThrowBefore=true }; var journal=new TestJournal();
    Equal(CashDecision.Declined,new JournalCashGateway(cash,journal).Charge("op-A",200).Decision); Equal(200L,cash.Balance);
});
Add("unexpected native balance delta stays uncertain and cannot debit again", () =>
{
    var cash=new TestCashPort(200) { Partial=true }; var journal=new TestJournal(); var gateway=new JournalCashGateway(cash,journal);
    Equal(CashDecision.Uncertain,gateway.Charge("op-A",200).Decision);
    Equal(CashDecision.Uncertain,gateway.Charge("op-A",200).Decision); Equal(1,cash.Calls); Equal(100L,cash.Balance);
});
Add("production receipt operation cannot be reused for a different amount", () =>
{
    var cash=new TestCashPort(400); var journal=new TestJournal(); var gateway=new JournalCashGateway(cash,journal);
    gateway.Charge("op-A",200); Throws<InvalidOperationException>(() => gateway.Charge("op-A",100)); Equal(1,cash.Calls);
});
Add("business service plus production cash journal survives order commit failure", () =>
{
    var (e,id)=Unpaid(); var cash=new TestCashPort(200); var journal=new TestJournal();
    var store=new FakeStore(e.Snapshot()) { FailCall=2 }; var gateway=new JournalCashGateway(cash,journal);
    Equal(RuleCode.PersistenceFailed,PaymentService.Attempt(e,id,gateway,store).Code);
    var recovered=new RentalEngine(e.SaveId); recovered.Restore(JsonRoundTrip(store.Current));
    var persisted=Newtonsoft.Json.JsonConvert.DeserializeObject<List<CashJournalEntry>>(Newtonsoft.Json.JsonConvert.SerializeObject(journal.Entries.Values))!;
    var restoredJournal=new TestJournal(); foreach (var receipt in persisted) restoredJournal.Add(receipt);
    var restoredCash=new TestCashPort(cash.Balance);
    Equal(RuleCode.Success,PaymentService.Attempt(recovered,id,new JournalCashGateway(restoredCash,restoredJournal),store).Code);
    Equal(0,restoredCash.Calls); Equal(0L,restoredCash.Balance); Equal(RuleCode.Success,recovered.RentalEligibility);
});
Add("production journal validates operation before invoking native cash API", () =>
{
    var cash=new TestCashPort(200); var journal=new TestJournal(); var gateway=new JournalCashGateway(cash,journal);
    Throws<ArgumentException>(() => gateway.Charge("",200)); Throws<ArgumentException>(() => gateway.Charge("op",0)); Equal(0,cash.Calls);
});

InfrastructureTests.Register(Add);
RidingPresentationTests.Register(Add);
AtomicCheckpointTests.Register(Add);
CompleteReleaseTests.Register(Add);
RuntimeSettingsTests.Register(Add);

var results = new List<object>();
var failures = 0;
foreach (var test in cases)
{
    try { test.Run(); results.Add(new { name = test.Name, passed = true }); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; results.Add(new { name = test.Name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
}
Console.WriteLine($"RESULT {cases.Count-failures}/{cases.Count} passed");
if (args.Length == 2 && args[0] == "--report")
{
    var report = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(report)!);
    File.WriteAllText(report,JsonSerializer.Serialize(new { total = cases.Count, passed = cases.Count-failures, failed = failures, tests = results },new JsonSerializerOptions { WriteIndented = true }));
}
return failures == 0 ? 0 : 1;

void Add(string name, Action test) => cases.Add((name,test));
static void Equal<T>(T expected,T actual) { if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Expected {expected}, got {actual}"); }
static void True(bool condition) { if (!condition) throw new Exception("Expected true"); }
static void Near(float expected,float actual) { if (Math.Abs(expected-actual)>0.0001f*Math.Max(1,Math.Abs(expected))) throw new Exception($"Expected {expected}, got {actual}"); }
static void False(bool condition) => True(!condition);
static void Throws<T>(Action action) where T:Exception { try { action(); } catch(T) { return; } throw new Exception("Expected " + typeof(T).Name); }
static RentalEngine Rented() { var e=new RentalEngine("save-A"); e.Rent("station-A","bike-A",new RatePlan(),true); return e; }
static (RentalEngine,string) Unpaid() { var e=Rented(); return (e,e.Close(e.CurrentRental!.RentalId,CloseReason.Returned,"station-B").Id!); }
static OrderRecord Closed(long duration,CloseReason reason) { var e=Rented(); e.AdvanceTime(duration,PlayContext.Active); return e.FindOrder(e.Close(e.CurrentRental!.RentalId,reason,"station-B").Id!)!; }
static RentalState JsonRoundTrip(RentalState state) => JsonSerializer.Deserialize<RentalState>(JsonSerializer.Serialize(state))!;
static RentalEngine Reload(RentalEngine e) { var result=new RentalEngine(e.SaveId); result.Restore(JsonRoundTrip(e.Snapshot())); return result; }

sealed class FakeStore(RentalState original):IRentalStateStore
{
    public RentalState Current { get; private set; } = original.Copy();
    public int Calls { get; private set; }
    public int FailCall { get; set; }
    public void Save(RentalState snapshot) { Calls++; if (Calls == FailCall) throw new IOException("Simulated atomic save failure"); Current=snapshot.Copy(); }
}
sealed class FakeCash(long initial):ICashGateway
{
    private readonly Dictionary<string,CashReceipt> _receipts=new();
    public long AvailableCash { get; private set; }=initial;
    public int Debits { get; private set; }
    public int Calls { get; private set; }
    public bool ThrowAfterDebit { get; set; }
    public bool Decline { get; set; }
    public bool Uncertain { get; set; }
    public bool WrongReceipt { get; set; }
    public CashReceipt Charge(string operationId,long amount)
    {
        Calls++; if (_receipts.TryGetValue(operationId,out var prior)) return prior;
        if (Uncertain) return new CashReceipt(operationId,amount,CashDecision.Uncertain);
        var decision=Decline || AvailableCash<amount ? CashDecision.Declined : CashDecision.Charged;
        var receipt=new CashReceipt(operationId,WrongReceipt ? amount+1 : amount,decision);
        _receipts.Add(operationId,receipt);
        if (decision==CashDecision.Charged) { AvailableCash-=amount; Debits++; }
        if (ThrowAfterDebit) { ThrowAfterDebit=false; throw new IOException("Simulated interruption after debit"); }
        return receipt;
    }
}
sealed class TestCashPort(long initial):ICashMutationPort
{
    public long Balance { get; private set; }=initial;
    public int Calls { get; private set; }
    public bool InMutation { get; private set; }
    public bool ThrowBefore { get; set; }
    public bool ThrowAfter { get; set; }
    public bool Partial { get; set; }
    public bool Debit(long amount)
    {
        TrueGuard(InMutation); Calls++;
        if (ThrowBefore) throw new IOException("Before debit");
        if (Balance<amount) return false;
        Balance-=Partial ? amount/2 : amount;
        if (ThrowAfter) throw new IOException("After debit");
        return true;
    }
    public IDisposable EnterMutation() { InMutation=true; return new Scope(() => InMutation=false); }
    private static void TrueGuard(bool value) { if (!value) throw new InvalidOperationException("Mutation guard is absent"); }
    private sealed class Scope(Action release):IDisposable { public void Dispose() => release(); }
}
sealed class TestJournal:ICashJournal
{
    public Dictionary<string,CashJournalEntry> Entries { get; }=new();
    public int Flushes { get; private set; }
    public int FailFlushCall { get; set; }
    public CashJournalEntry? Find(string id) => Entries.GetValueOrDefault(id);
    public void Add(CashJournalEntry entry) => Entries.Add(entry.OperationId,entry);
    public void Flush() { Flushes++; if (Flushes==FailFlushCall) throw new IOException("Checkpoint flush failed"); }
}
