using DuckovSharedBike.Domain;
using Newtonsoft.Json;

internal static class AtomicCheckpointTests
{
    internal static void Register(Action<string,Action> add)
    {
        add("atomic initial save renames a flushed temporary file",()=>
        {
            var path=NewPath(); File.WriteAllText(path+".tmp","new"); AtomicFileCommit.Commit(path,new SystemAtomicFileOperations());
            Equal("new",File.ReadAllText(path)); Equal(false,File.Exists(path+".tmp"));
        });
        add("atomic replacement retains the whole prior image as backup",()=>
        {
            var path=NewPath(); File.WriteAllText(path,"old"); File.WriteAllText(path+".tmp","new");
            AtomicFileCommit.Commit(path,new SystemAtomicFileOperations());
            Equal("new",File.ReadAllText(path)); Equal("old",File.ReadAllText(path+".sharedbike.bak"));
        });
        add("failed atomic replacement leaves the live save and temporary image intact",()=>
        {
            var path=NewPath(); File.WriteAllText(path,"old"); File.WriteAllText(path+".tmp","new");
            Reject(()=>AtomicFileCommit.Commit(path,new FaultOperations { BeforeReplace=true }));
            Equal("old",File.ReadAllText(path)); Equal("new",File.ReadAllText(path+".tmp"));
        });
        add("failed disk flush cannot replace a live save",()=>
        {
            var path=NewPath(); File.WriteAllText(path,"old"); File.WriteAllText(path+".tmp","new");
            Reject(()=>AtomicFileCommit.Commit(path,new FaultOperations { FailFlush=true })); Equal("old",File.ReadAllText(path));
        });
        add("missing native temporary file never deletes the current save",()=>
        {
            var path=NewPath(); File.WriteAllText(path,"old");
            Reject(()=>AtomicFileCommit.Commit(path,new SystemAtomicFileOperations())); Equal("old",File.ReadAllText(path));
        });
        add("atomic commit preserves opaque binary and encrypted save bytes",()=>
        {
            var path=NewPath(); var old=new byte[40000]; var next=new byte[45000];
            new Random(19).NextBytes(old); new Random(23).NextBytes(next);
            File.WriteAllBytes(path,old); File.WriteAllBytes(path+".tmp",next); AtomicFileCommit.Commit(path,new SystemAtomicFileOperations());
            Equal(true,next.SequenceEqual(File.ReadAllBytes(path))); Equal(true,old.SequenceEqual(File.ReadAllBytes(path+".sharedbike.bak")));
        });
        foreach (var phase in new[] {1,2,3})
            foreach (var after in new[] {false,true})
                add($"payment checkpoint interruption {(after ? "after" : "before")} replacement phase {phase} recovers cash and receipt together",()=>
                {
                    var fixture=PaymentFile.Create(); var order=fixture.Engine.Snapshot().Orders.Single().OrderId;
                    fixture.FailureCommit=phase; fixture.FailAfter=after;
                    var failed=fixture.Pay(order);
                    if (failed.Succeeded) throw new Exception("The injected interruption did not occur.");
                    // Simulate a fresh process: discard live balances, engine and in-memory receipts.
                    var recovered=PaymentFile.Reload(fixture.Path);
                    var expectedDebits=recovered.Cash==100 ? 0 : 1;
                    var result=recovered.Pay(order);
                    if (result.Code!=RuleCode.Success && result.Code!=RuleCode.AlreadyPaid) throw new Exception("Payment was not recovered: "+result.Code);
                    Equal(100L,recovered.Cash); Equal(expectedDebits,recovered.Debits);
                    Equal(PaymentStatus.Paid,recovered.Engine.FindOrder(order)!.PaymentStatus);
                    var again=PaymentFile.Reload(fixture.Path); Equal(100L,again.Cash);
                    Equal(RuleCode.AlreadyPaid,again.Pay(order).Code); Equal(0,again.Debits);
                    CashJournalValidation.Validate(again.Receipts,again.Engine.Snapshot());
                });
    }

    private static string NewPath()
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/verification/atomic-checkpoints",Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root); return System.IO.Path.Combine(root,"save.bin");
    }
    private static void Equal<T>(T expected,T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void Reject(Action action) { try { action(); } catch (IOException) { return; } throw new Exception("Expected IO failure."); }

    private sealed class FaultOperations:IAtomicFileOperations
    {
        private readonly SystemAtomicFileOperations _files=new();
        internal bool FailFlush,BeforeReplace,AfterReplace;
        public bool Exists(string path)=>_files.Exists(path);
        public void Flush(string path) { if (FailFlush) throw new IOException("Flush interruption"); _files.Flush(path); }
        public void Replace(string source,string target,string backup)
        {
            if (BeforeReplace) throw new IOException("Before replacement");
            _files.Replace(source,target,backup);
            if (AfterReplace) throw new IOException("After replacement");
        }
        public void Move(string source,string target)=>_files.Move(source,target);
    }

    private sealed class Image
    {
        public RentalState State {get;set;}=new();
        public long Cash {get;set;}
        public List<CashJournalEntry> Receipts {get;set;}=new();
    }
    private sealed class PaymentFile:ICashMutationPort,ICashJournal,IRentalStateStore
    {
        internal readonly RentalEngine Engine;
        internal readonly string Path;
        internal long Cash;
        internal int Debits,FailureCommit;
        internal bool FailAfter;
        internal List<CashJournalEntry> Receipts=new();
        private int _commits;
        private bool _mutating;
        private PaymentFile(string path,RentalEngine engine,long cash) { Path=path; Engine=engine; Cash=cash; }
        internal static PaymentFile Create()
        {
            var engine=new RentalEngine("slot-atomic"); engine.Rent("station-A","bike",new RatePlan(),true);
            engine.Close(engine.CurrentRental!.RentalId,CloseReason.Returned,"station-B");
            var result=new PaymentFile(NewPath(),engine,300); result.Write(new SystemAtomicFileOperations()); return result;
        }
        internal static PaymentFile Reload(string path)
        {
            var data=JsonConvert.DeserializeObject<Image>(File.ReadAllText(path))!;
            var engine=new RentalEngine(data.State.SaveId); engine.Restore(data.State);
            CashJournalValidation.Validate(data.Receipts,engine.Snapshot());
            return new PaymentFile(path,engine,data.Cash) { Receipts=data.Receipts };
        }
        internal RuleResult Pay(string order)=>PaymentService.Attempt(Engine,order,new JournalCashGateway(this,this),this);
        public long Balance=>Cash;
        public bool Debit(long amount) { if (!_mutating) throw new Exception("Cash mutation scope missing"); if (Cash<amount) return false; Cash-=amount; Debits++; return true; }
        public IDisposable EnterMutation() { _mutating=true; return new Scope(()=>_mutating=false); }
        public CashJournalEntry? Find(string id)=>Receipts.Find(r=>r.OperationId==id);
        public void Add(CashJournalEntry entry)=>Receipts.Add(entry);
        public void Flush()
        {
            _commits++; Write(new FaultOperations { BeforeReplace=_commits==FailureCommit && !FailAfter,AfterReplace=_commits==FailureCommit && FailAfter });
        }
        public void Save(RentalState snapshot)
        {
            Equal(JsonConvert.SerializeObject(snapshot),JsonConvert.SerializeObject(Engine.Snapshot())); Flush();
        }
        private void Write(IAtomicFileOperations operations)
        {
            var snapshot=Engine.Snapshot(); CashJournalValidation.Validate(Receipts,snapshot);
            File.WriteAllText(Path+".tmp",JsonConvert.SerializeObject(new Image { State=snapshot,Cash=Cash,Receipts=Receipts }));
            AtomicFileCommit.Commit(Path,operations);
        }
        private sealed class Scope(Action close):IDisposable { public void Dispose()=>close(); }
    }
}
