using System;
using System.Globalization;
using System.Collections.Generic;
using System.Reflection;
using DuckovSharedBike.Domain;
using Newtonsoft.Json;
using Saves;
using UnityEngine;

namespace DuckovSharedBike
{
    // Domain, world and cash-receipt keys share the game's native cached save file.
    internal sealed class NativeRentalRuntime
    {
        private const string SaveKey = "DuckovSharedBike.State.v1";
        private const string WorldKey = "DuckovSharedBike.World.v1";
        private const string ReceiptsKey = "DuckovSharedBike.Payments.v1";
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            Culture = CultureInfo.InvariantCulture,
            MaxDepth = 64
        };
        private readonly Action<string> _log;
        private readonly ActivePlayClock _clock = new ActivePlayClock();
        private RentalEngine? _engine;
        private bool _enabled;
        private bool _loading;
        private bool _faulted;
        private string? _lastCached;
        private float _nextCache;
        private int _fullSaveSeconds = 20;
        private WorldState? _world;
        private ReceiptState? _receipts;
        private bool _paying;
        private bool _persisting;
        private bool _savePending;
        private Exception? _collectFailure;
        private string? _lastSaveError;
        private bool _paymentFault;
        internal event Action? BeforeCache;
        internal event Action? SlotChanged;
        internal event Action? RentalClosed;
        internal RentalEngine? Engine => _faulted ? null : _engine;
        internal WorldState? World => _faulted ? null : _world;
        internal bool HasVehicleDebt => _engine?.CurrentRental != null;
        internal bool IsReady => _enabled && !_faulted && !_loading && _engine != null && LevelManager.LevelInited;
        internal bool PaymentUnavailable=>_paymentFault;
        internal bool HasBoundSlot=>_enabled && !_faulted && _engine!=null
            && _engine.SaveId=="slot-"+SavesSystem.CurrentSlot.ToString(CultureInfo.InvariantCulture);
        internal static string CurrentDiskPath()=>System.IO.Path.GetFullPath(DiskSettings(SavesSystem.CurrentFilePath).FullPath);
        internal bool SavePending => _savePending;
        internal int FullSaveSeconds => _fullSaveSeconds;
        internal void SetFullSaveSeconds(int seconds)
        {
            _fullSaveSeconds = Math.Max(5, Math.Min(300, seconds));
            _log("Periodic full save interval=" + _fullSaveSeconds + "s (key events still save immediately).");
        }
        internal void RentalStarted()
        {
            _clock.Reset();
            _clock.Sample((decimal)(Time.realtimeSinceStartupAsDouble*1000),CurrentContext());
        }
        internal void AccumulateToNow()
        {
            if (!_enabled || _faulted || _engine==null) return;
            var context=CurrentContext();
            _engine.AdvanceTime(_clock.Sample((decimal)(Time.realtimeSinceStartupAsDouble*1000),context),context);
        }

        internal NativeRentalRuntime(Action<string> log) { _log = log; }

        internal string Summary
        {
            get
            {
                if (_faulted) return "业务资料读取失败，见日志";
                if (_savePending) return "保存暂未完成，保留当前租赁并自动重试";
                if (_engine == null) return "业务规则已就绪，等待进入存档";
                var rental = _engine.CurrentRental;
                if (rental != null) return "租赁中｜当前租赁费 " + _engine.CurrentFee!.Value.Total + "元";
                return _engine.HasUnpaidOrders ? "车辆已归还｜存在待付款订单" : "当前存档：无租赁、无欠费";
            }
        }

        internal void Enable()
        {
            if (_enabled) return;
            _enabled = true;
            LevelManager.OnAfterLevelInitialized += OnLevelReady;
            LevelManager.OnMainCharacterDead += OnDeath;
            SavesSystem.OnSetFile += OnSlotChanged;
            SavesSystem.OnSaveDeleted += OnSlotChanged;
            SavesSystem.OnCollectSaveData += OnCollectSaveData;
            SceneLoader.onStartedLoadingScene += OnLoading;
            SceneLoader.onFinishedLoadingScene += OnLoaded;
            Duckov.UI.ManagedUIElement.onOpen += OnUiOpened;
            _loading = LevelManager.LevelInitializing;
            try { TryLoadCurrentSlot(); } catch (Exception ex) { Fail(ex); }
        }

        internal void Disable()
        {
            if (!_enabled) return;
            OnCollectSaveData();
            var actor=CharacterMainControl.Main;
            if (!_faulted && _engine!=null && !_loading && LevelManager.LevelInited
                && actor!=null && actor.Health!=null && !actor.Health.IsDead)
            {
                try { Persist(true); } catch (Exception ex) { RecordSaveFailure(ex); }
            }
            LevelManager.OnAfterLevelInitialized -= OnLevelReady;
            LevelManager.OnMainCharacterDead -= OnDeath;
            SavesSystem.OnSetFile -= OnSlotChanged;
            SavesSystem.OnSaveDeleted -= OnSlotChanged;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData;
            SceneLoader.onStartedLoadingScene -= OnLoading;
            SceneLoader.onFinishedLoadingScene -= OnLoaded;
            Duckov.UI.ManagedUIElement.onOpen -= OnUiOpened;
            _clock.Reset();
            _enabled = false;
        }

        internal void Tick()
        {
            if (!_enabled || _faulted) return;
            try
            {
                try { TryLoadCurrentSlot(); }
                catch (Exception ex) { Fail(ex); return; }
                var context = CurrentContext();
                var elapsed = _clock.Sample((decimal)(Time.realtimeSinceStartupAsDouble * 1000), context);
                _engine?.AdvanceTime(elapsed, context);
                if (context!=PlayContext.Loading && _engine!=null && Time.unscaledTime >= _nextCache)
                {
                    // A failed save should be retried sooner than the normal cadence.
                    var interval = _savePending ? Math.Min(5, _fullSaveSeconds) : _fullSaveSeconds;
                    _nextCache = Time.unscaledTime + interval;
                    try
                    {
                        var actor=CharacterMainControl.Main;
                        if ((_savePending || _engine.CurrentRental != null) && actor!=null && actor.Health!=null && !actor.Health.IsDead)
                            Persist(true);
                        else CacheState();
                    }
                    catch (Exception ex) { RecordSaveFailure(ex); }
                }
            }
            catch (Exception ex) { Fail(ex); }
        }

        private PlayContext CurrentContext()
        {
            if (_loading || LevelManager.LevelInitializing || SceneLoader.IsSceneLoading
                || (Duckov.Scenes.MultiSceneCore.Instance!=null && Duckov.Scenes.MultiSceneCore.Instance.IsLoading)) return PlayContext.Loading;
            if (_engine?.CurrentRental == null || !LevelManager.LevelInited || CharacterMainControl.Main == null)
                return PlayContext.Offline;
            return Time.timeScale > 0 ? PlayContext.Active : PlayContext.Paused;
        }

        private void TryLoadCurrentSlot()
        {
            if (_faulted || _loading || LevelManager.LevelInitializing || !LevelManager.LevelInited || CharacterMainControl.Main == null) return;
            var saveId = "slot-" + SavesSystem.CurrentSlot.ToString(CultureInfo.InvariantCulture);
            if (_engine != null && _engine.SaveId == saveId) return;
            var engine = new RentalEngine(saveId);
            string? json = null;
            if (SavesSystem.KeyExisits(SaveKey))
            {
                json = SavesSystem.Load<string>(SaveKey);
                if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("Rental save key contains empty data.");
                var state = JsonConvert.DeserializeObject<RentalState>(json, JsonSettings)
                    ?? throw new InvalidOperationException("Rental save key contains no state.");
                engine.Restore(state);
            }
            _engine = engine;
            try
            {
                _world = SavesSystem.KeyExisits(WorldKey)
                    ? JsonConvert.DeserializeObject<WorldState>(SavesSystem.Load<string>(WorldKey), JsonSettings)
                    : new WorldState { SaveId = saveId };
                WorldValidation.Validate(_world!,saveId);
            }
            catch (Exception ex)
            {
                // Preserve the damaged source inside this save before repairing only world placement data.
                _world=new WorldState { SaveId=saveId };
                if (engine.CurrentRental!=null) engine.Close(engine.CurrentRental.RentalId,CloseReason.SystemRecovery);
                Quarantine(WorldKey);
                _savePending=true;
                _log("World placement data repaired; active vehicle recovered without penalty: "+ex);
            }
            try
            {
                _receipts = SavesSystem.KeyExisits(ReceiptsKey)
                    ? JsonConvert.DeserializeObject<ReceiptState>(SavesSystem.Load<string>(ReceiptsKey), JsonSettings)
                    : new ReceiptState { SaveId = saveId };
                if (_receipts==null || _receipts.SaveId!=saveId) throw new InvalidOperationException("Cash receipts belong to another save.");
                CashJournalValidation.Validate(_receipts.Receipts,engine.Snapshot());
                _paymentFault=_receipts.Faulted;
            }
            catch (Exception ex)
            {
                _receipts=new ReceiptState { SaveId=saveId,Faulted=true }; _paymentFault=true; _savePending=true;
                if (engine.CurrentRental!=null)
                {
                    engine.Close(engine.CurrentRental.RentalId,CloseReason.SystemRecovery);
                    _world!.Vehicle=null;
                }
                Quarantine(ReceiptsKey);
                _log("Payment receipt data quarantined; no new cash mutation permitted: "+ex);
            }
            if (_world == null || _receipts == null || _receipts.SaveId != saveId)
                throw new InvalidOperationException("World/receipt state belongs to another save or schema.");
            WorldValidation.Validate(_world,saveId);
            CashJournalValidation.Validate(_receipts.Receipts,engine.Snapshot());
            _lastCached = json;
            _clock.Reset();
            _log("Rental state loaded save=" + saveId + " hasRental=" + (engine.CurrentRental != null) + " hasDebt=" + engine.HasUnpaidOrders);
        }

        private void Quarantine(string key)
        {
            if (!SavesSystem.KeyExisits(key)) return;
            try
            {
                var original=SavesSystem.Load<object>(key);
                SavesSystem.Save(key+".quarantine."+Guid.NewGuid().ToString("N"),original);
            }
            catch (Exception ex) { _log("Original damaged save key retained in native backup; quarantine copy unavailable: "+ex); }
        }

        private void CacheState()
        {
            if (_faulted || _engine == null) return;
            BeforeCache?.Invoke();
            var snapshot = _engine.Snapshot();
            var actualId = "slot-" + SavesSystem.CurrentSlot.ToString(CultureInfo.InvariantCulture);
            if (actualId != _engine.SaveId) throw new InvalidOperationException("Slot changed before caching rental data.");
            if (_world==null || _receipts==null || _receipts.SaveId!=actualId)
                throw new InvalidOperationException("Incomplete world/receipt cache.");
            WorldValidation.Validate(_world,actualId);
            CashJournalValidation.Validate(_receipts.Receipts,snapshot);
            var json = JsonConvert.SerializeObject(snapshot, JsonSettings);
            if ((snapshot.Rentals.Count != 0 || snapshot.Orders.Count != 0 || _lastCached != null)
                && (json != _lastCached || !SavesSystem.KeyExisits(SaveKey) || SavesSystem.Load<string>(SaveKey)!=json))
            {
                SaveChecked(SaveKey, json);
                _lastCached = json;
            }
            if (_world != null && (_world.Stations.Count != 0 || _world.Vehicle != null || SavesSystem.KeyExisits(WorldKey)))
                SaveChecked(WorldKey, JsonConvert.SerializeObject(_world, JsonSettings));
            if (_receipts != null && (_receipts.Receipts.Count != 0 || _receipts.Faulted))
                SaveChecked(ReceiptsKey, JsonConvert.SerializeObject(_receipts, JsonSettings));
        }

        private static void SaveChecked(string key, string json)
        {
            SavesSystem.Save(key, json);
            if (!SavesSystem.KeyExisits(key) || SavesSystem.Load<string>(key) != json)
                throw new InvalidOperationException("Game cache verification failed: " + key);
        }

        internal void Persist(bool flush, RentalState? expectedState=null)
        {
            if (_faulted) throw new InvalidOperationException("Rental runtime is suspended.");
            if (_persisting) throw new InvalidOperationException("A native save is already in progress.");
            _persisting=true;
            try
            {
                var engine=_engine ?? throw new InvalidOperationException("Rental state is unavailable.");
                var saveId=engine.SaveId;
                var filePath=SavesSystem.CurrentFilePath;
                var expectedJson=expectedState==null ? null : JsonConvert.SerializeObject(expectedState,JsonSettings);
                EnsureSaveSession(engine,saveId,filePath,expectedJson);
                CacheState();
                if (!flush) { _savePending=true; return; }
                // Respect raid scenes that deliberately suppress character saving. Payment requires a saveable inventory.
                var level = LevelManager.Instance;
                if (_loading || SceneLoader.IsSceneLoading
                    || (Duckov.Scenes.MultiSceneCore.Instance!=null && Duckov.Scenes.MultiSceneCore.Instance.IsLoading)
                    || level == null || level.MainCharacter == null || (_paying && !LevelConfig.SaveCharacter))
                    throw new InvalidOperationException("Player inventory is not ready to save.");
                var actor=level.MainCharacter;
                var savePlayer = typeof(LevelManager).GetMethod("SaveMainCharacter", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException("LevelManager.SaveMainCharacter");
                savePlayer.Invoke(level, null);
                _collectFailure=null;
                SavesSystem.CollectSaveData();
                if (_collectFailure!=null) throw new InvalidOperationException("Rental cache collection failed.",_collectFailure);
                EnsureSaveSession(engine,saveId,filePath,expectedJson);
                if (_loading || LevelManager.Instance!=level || level.MainCharacter!=actor)
                    throw new InvalidOperationException("Scene/player changed during save collection.");
                // Capture expected cache strings before native callbacks can change runtime state.
                var rentalJson=_lastCached;
                var worldJson=_world!=null && SavesSystem.KeyExisits(WorldKey) ? JsonConvert.SerializeObject(_world,JsonSettings) : null;
                var receiptJson=_receipts!=null && (_receipts.Receipts.Count!=0 || _receipts.Faulted) ? JsonConvert.SerializeObject(_receipts,JsonSettings) : null;
                var cashCheckpoint=_paying ? CaptureCashInventory(null) : null;
                var previousCommit=NativePatches.PaymentCommit;
                var previousAtomic=NativePatches.AtomicSavePath;
                NativePatches.AtomicSavePath=System.IO.Path.GetFullPath(DiskSettings(filePath).FullPath);
                NativePatches.PaymentCommit = true;
                try { SavesSystem.SaveFile(false); }
                finally { NativePatches.PaymentCommit = previousCommit; NativePatches.AtomicSavePath=previousAtomic; }
                EnsureSaveSession(engine,saveId,filePath,expectedJson);
                VerifyFileState(filePath,rentalJson,worldJson,receiptJson,cashCheckpoint);
                if (_savePending) _log("Native save retry succeeded save="+saveId);
                _savePending=false; _lastSaveError=null;
            }
            catch (Exception ex) { RecordSaveFailure(ex); throw; }
            finally { _persisting=false; }
        }

        private void EnsureSaveSession(RentalEngine engine,string saveId,string filePath,string? expectedJson)
        {
            if (_faulted || _engine!=engine || engine.SaveId!=saveId
                || saveId!="slot-"+SavesSystem.CurrentSlot.ToString(CultureInfo.InvariantCulture)
                || filePath!=SavesSystem.CurrentFilePath)
                throw new InvalidOperationException("Save slot changed during native commit.");
            if (expectedJson!=null && expectedJson!=JsonConvert.SerializeObject(engine.Snapshot(),JsonSettings))
                throw new InvalidOperationException("Live payment state does not match the requested snapshot.");
        }

        private void VerifyFileState(string filePath,string? rentalJson,string? worldJson,string? receiptJson,CashInventorySnapshot? cashCheckpoint)
        {
            var disk=DiskSettings(filePath);
            // Read the actual file, not the cache that was just modified.
            if (rentalJson != null && ES3.Load<string>(SaveKey, disk) != rentalJson)
                throw new InvalidOperationException("Rental state was not committed to the native save file.");
            if (worldJson != null)
                if (ES3.Load<string>(WorldKey, disk) != worldJson)
                    throw new InvalidOperationException("World state was not committed to the native save file.");
            if (receiptJson != null)
                if (ES3.Load<string>(ReceiptsKey, disk) != receiptJson)
                    throw new InvalidOperationException("Cash receipts were not committed to the native save file.");
            cashCheckpoint?.Verify(CaptureCashInventory(disk));
        }

        private static ES3Settings DiskSettings(string filePath)
        {
            var field = typeof(SavesSystem).GetField("settings", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingFieldException("SavesSystem.settings");
            var native = field.GetValue(null) as ES3Settings ?? throw new InvalidOperationException("Native save settings unavailable.");
            var disk = (ES3Settings)native.Clone();
            disk.path = filePath;
            disk.location = ES3.Location.File;
            return disk;
        }

        private static CashInventorySnapshot CaptureCashInventory(ES3Settings? disk)
        {
            var result=new CashInventorySnapshot();
            const string playerKey="Item/MainCharacterItemData";
            var player=disk==null ? SavesSystem.Load<ItemStatsSystem.Data.ItemTreeData>(playerKey)
                : ES3.Load<ItemStatsSystem.Data.ItemTreeData>(playerKey,disk);
            result.Add(playerKey,CashStacks(player));
            if (PlayerStorage.Inventory!=null)
                CaptureInventory(result,"Inventory/PlayerStorage",disk);
            var pet=LevelManager.Instance?.PetProxy;
            if (pet!=null && pet.Inventory!=null)
            {
                if (!LevelConfig.SavePet) throw new InvalidOperationException("Pet cash inventory cannot be saved in this scene.");
                CaptureInventory(result,"Inventory/Inventory_Safe",disk);
            }
            return result;
        }

        private static void CaptureInventory(CashInventorySnapshot result,string key,ES3Settings? disk)
        {
            var data=disk==null ? SavesSystem.Load<ItemStatsSystem.Data.InventoryData>(key)
                : ES3.Load<ItemStatsSystem.Data.InventoryData>(key,disk);
            if (data==null || data.entries==null) throw new InvalidOperationException("Inventory data unavailable: "+key);
            var stacks=new List<CashStack>();
            foreach (var entry in data.entries)
            {
                if (entry==null) throw new InvalidOperationException("Invalid inventory entry: "+key);
                stacks.AddRange(CashStacks(entry.itemTreeData));
            }
            result.Add(key,stacks);
        }

        private static IEnumerable<CashStack> CashStacks(ItemStatsSystem.Data.ItemTreeData data)
        {
            if (data==null || data.entries==null) throw new InvalidOperationException("Player cash data unavailable.");
            foreach (var entry in data.entries)
            {
                if (entry==null) throw new InvalidOperationException("Invalid item tree entry.");
                if (entry.typeID==451) yield return new CashStack(entry.instanceID,entry.StackCount);
            }
        }

        internal RuleResult Pay(string orderId)
        {
            if (!IsReady || _engine == null || _paying || _paymentFault) return new RuleResult(RuleCode.PaymentBusy);
            if (LevelManager.Instance == null
                || !LevelConfig.SaveCharacter
                || PlayerStorage.Loading
                || (LevelManager.Instance.PetProxy!=null && LevelManager.Instance.PetProxy.Inventory!=null && !LevelConfig.SavePet))
                return new RuleResult(RuleCode.PaymentBusy);
            _paying = true;
            var engine=_engine;
            try { return PaymentService.Attempt(engine, orderId,
                new JournalCashGateway(new NativeCashPort(this,engine), new NativeJournal(this,engine), ex => _log("Cash API interrupted: " + ex)),
                new NativeStore(this,engine)); }
            finally { _paying = false; }
        }

        // Auto-settlement runs right after Return/CloseRecovery, so it cannot require the base level
        // or an open StorageDock; only savable character/pet cash inventories gate the transaction.
        internal RuleResult SettleOrder(string orderId)
        {
            if (!IsReady || _engine == null || _paying || _paymentFault) return new RuleResult(RuleCode.PaymentBusy);
            if (LevelManager.Instance == null
                || !LevelConfig.SaveCharacter
                || PlayerStorage.Loading
                || SceneLoader.IsSceneLoading
                || (LevelManager.Instance.PetProxy!=null && LevelManager.Instance.PetProxy.Inventory!=null && !LevelConfig.SavePet))
                return new RuleResult(RuleCode.PaymentBusy);
            _paying = true;
            var engine=_engine;
            try { return PaymentService.Attempt(engine, orderId,
                new JournalCashGateway(new NativeCashPort(this,engine), new NativeJournal(this,engine), ex => _log("Cash API interrupted: " + ex)),
                new NativeStore(this,engine)); }
            finally { _paying = false; }
        }

        private void EnsurePaymentSession(RentalEngine engine)
        {
            if (_engine!=engine || !IsReady || engine.SaveId!="slot-"+SavesSystem.CurrentSlot.ToString(CultureInfo.InvariantCulture)
                || LevelManager.Instance==null || !LevelConfig.SaveCharacter
                || SceneLoader.IsSceneLoading || PlayerStorage.Loading)
                throw new InvalidOperationException("Payment session changed; refusing cash mutation or cross-slot receipt write.");
        }

        // Direct wallet mutation for the paid summon feature. Same atomicity guard as the payment
        // pipeline (MutationScope + _paying) but no order involved: charge up-front, refund on failure.
        internal bool DirectPay(long amount)
        {
            if (!IsReady || _paying || _paymentFault || amount<=0) return false;
            if (LevelManager.Instance==null || !LevelConfig.SaveCharacter || PlayerStorage.Loading
                || SceneLoader.IsSceneLoading) return false;
            _paying = true;
            try
            {
                using (new MutationScopeStatic())
                {
                    var cost = new Duckov.Economy.Cost { money = amount, items = Array.Empty<Duckov.Economy.Cost.ItemEntry>() };
                    if (!Duckov.Economy.EconomyManager.Pay(cost, false, true)) return false;
                }
                Persist(true);
                return true;
            }
            catch (Exception ex) { _log("DirectPay failed: " + ex); return false; }
            finally { _paying = false; }
        }

        internal bool DirectRefund(long amount)
        {
            if (amount<=0) return false;
            try
            {
                using (new MutationScopeStatic())
                {
                    return Duckov.Economy.EconomyManager.Add(amount);
                }
            }
            catch (Exception ex) { _log("DirectRefund failed: " + ex); return false; }
        }

        private sealed class MutationScopeStatic : IDisposable
        {
            private readonly bool _previous = NativePatches.PaymentMutation;
            internal MutationScopeStatic() { NativePatches.PaymentMutation = true; }
            public void Dispose() { NativePatches.PaymentMutation = _previous; }
        }

        private sealed class NativeStore : IRentalStateStore
        {
            private readonly NativeRentalRuntime _runtime;
            private readonly RentalEngine _engine;
            internal NativeStore(NativeRentalRuntime runtime,RentalEngine engine) { _runtime = runtime; _engine=engine; }
            public void Save(RentalState snapshot)
            {
                _runtime.EnsurePaymentSession(_engine);
                if (snapshot.SaveId != _engine.SaveId)
                    throw new InvalidOperationException("Payment save slot mismatch.");
                _runtime.Persist(true,snapshot);
            }
        }

        private sealed class NativeCashPort : ICashMutationPort
        {
            private readonly NativeRentalRuntime _runtime;
            private readonly RentalEngine _engine;
            internal NativeCashPort(NativeRentalRuntime runtime,RentalEngine engine) { _runtime=runtime; _engine=engine; }
            public long Balance { get { _runtime.EnsurePaymentSession(_engine); return Duckov.Economy.EconomyManager.Cash; } }
            public bool Debit(long amount)
            {
                _runtime.EnsurePaymentSession(_engine);
                var cost = new Duckov.Economy.Cost { money = amount, items = Array.Empty<Duckov.Economy.Cost.ItemEntry>() };
                return Duckov.Economy.EconomyManager.Pay(cost, false, true);
            }
            public IDisposable EnterMutation() { _runtime.EnsurePaymentSession(_engine); return new MutationScope(); }
            private sealed class MutationScope : IDisposable
            {
                private readonly bool _previous = NativePatches.PaymentMutation;
                internal MutationScope() { NativePatches.PaymentMutation = true; }
                public void Dispose() { NativePatches.PaymentMutation = _previous; }
            }
        }

        private sealed class NativeJournal : ICashJournal
        {
            private readonly NativeRentalRuntime _runtime;
            private readonly RentalEngine _engine;
            internal NativeJournal(NativeRentalRuntime runtime,RentalEngine engine) { _runtime=runtime; _engine=engine; }
            public CashJournalEntry? Find(string operationId)
            {
                _runtime.EnsurePaymentSession(_engine);
                return _runtime._receipts?.Receipts.Find(r => r.OperationId==operationId);
            }
            public void Add(CashJournalEntry entry)
            {
                if (_runtime._receipts==null) throw new InvalidOperationException("Cash receipts unavailable.");
                if (Find(entry.OperationId)!=null) throw new InvalidOperationException("Duplicate cash receipt.");
                _runtime._receipts.Receipts.Add(entry);
            }
            public void Flush() { _runtime.EnsurePaymentSession(_engine); _runtime.Persist(true); }
        }

        private sealed class ReceiptState
        {
            public string SaveId { get; set; } = "";
            public List<CashJournalEntry> Receipts { get; set; } = new List<CashJournalEntry>();
            public bool Faulted {get;set;}
        }

        private void OnCollectSaveData()
        {
            if (!_enabled || _faulted) return;
            try { CacheState(); }
            catch (Exception ex) { _collectFailure=ex; RecordSaveFailure(ex); }
        }

        private void OnLevelReady()
        {
            _loading = false;
            _clock.Reset();
            try { TryLoadCurrentSlot(); } catch (Exception ex) { Fail(ex); }
        }

        private void OnSlotChanged()
        {
            // Native OnSetFile occurs after its slot change. Never write old-slot state here.
            _engine = null;
            _world = null;
            _receipts = null;
            _lastCached = null;
            _faulted = false;
            _paymentFault=false;
            _savePending=false; _lastSaveError=null; _collectFailure=null;
            _loading = true;
            _clock.Reset();
            SlotChanged?.Invoke();
        }

        private void OnLoading(SceneLoadingContext context) { _loading = true; _clock.Reset(); }
        private void OnLoaded(SceneLoadingContext context) { _loading = false; _clock.Reset(); }

        private void OnDeath(DamageInfo damage)
        {
            if (_faulted || _engine?.CurrentRental == null) return;
            try
            {
                AccumulateToNow();
                var rental = _engine.CurrentRental;
                var closed = _engine.Close(rental!.RentalId, CloseReason.DeathRecovery);
                if (_world != null) _world.Vehicle = null;
                RentalClosed?.Invoke();
                _clock.Reset();
                _savePending=true;
                CacheState();
                _log("Death settlement created order=" + closed.Id + " total=" + _engine.FindOrder(closed.Id!)!.Total);
            }
            catch (Exception ex) { RecordSaveFailure(ex); }
        }

        internal void RecoverSystemRental()
        {
            if (_faulted || _engine?.CurrentRental==null) return;
            AccumulateToNow(); _engine.Close(_engine.CurrentRental.RentalId,CloseReason.SystemRecovery);
            if (_world!=null) _world.Vehicle=null;
            RentalClosed?.Invoke(); _savePending=true; CacheState();
        }

        private void OnUiOpened(Duckov.UI.ManagedUIElement element)
        {
            if (element is Duckov.UI.StorageDock)
                _log("StorageDock opened; base=" + (LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel));
        }

        private void Fail(Exception ex)
        {
            // A readable, previously validated lease must not strand the player when the runtime stops.
            if (_engine?.CurrentRental!=null)
            {
                try
                {
                    _engine.Close(_engine.CurrentRental.RentalId,CloseReason.SystemRecovery);
                    if (_world!=null) _world.Vehicle=null;
                    RentalClosed?.Invoke(); _savePending=true; CacheState();
                }
                catch (Exception recovery) { _log("Runtime recovery could not be cached; original disk data retained: "+recovery); }
            }
            _faulted = true;
            _clock.Reset();
            _log("Rental runtime suspended; existing data retained: " + ex);
        }

        private void RecordSaveFailure(Exception ex)
        {
            _savePending=true;
            var message=ex.GetType().Name+": "+ex.Message;
            if (message==_lastSaveError) return;
            _lastSaveError=message;
            _log("Save pending; current rental retained for retry: "+ex);
        }
    }
}
