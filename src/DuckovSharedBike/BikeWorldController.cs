using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DuckovSharedBike.Domain;
using Duckov.Scenes;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DuckovSharedBike
{
    internal sealed partial class BikeWorldController : IDisposable
    {
        private readonly NativeRentalRuntime _runtime;
        private readonly Action<string> _log;
        private readonly PrototypeAssets _assets;
        private readonly StationLayoutSettings _settings;
        private readonly NativeRiderPose _riderPose;
        private readonly StationBlueprints _blueprints;
        private readonly Dictionary<string,GameObject> _stations=new Dictionary<string,GameObject>();
        private readonly List<GameObject> _markers=new List<GameObject>();
        private GameObject? _bike;
        private GameObject? _bikeMarker;
        private CharacterMainControl? _rider;
        private string _mapId="";
        private string _sceneName="";
        private string? _dialogStation;
        private string? _returnAfterDismount;
        private float _transitionUntil;
        private bool _mounting;
        private Vector3 _landing;
        private float _nextSync;
        private float _nextPlacement;
        private float _nextNotice;
        private float _toastUntil;
        private string _toast="";
        private bool _worldFailed;
        private bool _confirmLoss;
        private Vector3 _lastRidePosition;
        private Quaternion _lastRideRotation;
        private bool _trackingRide;
        private bool _clearanceWarned;
        private float _mountGuardUntil;
        private bool _guardLogged;
        private float _nextGuardLog;
        private bool _markersLogged;
        private float _nextMarkerSync;
        private readonly List<InteractableBase> _nativeTravelPoints=new List<InteractableBase>();
        private readonly HashSet<int> _loggedTravelPoints=new HashSet<int>();
        private float _nextTravelScan;
        private float _nextNativePromptLog;
        private string _visualSummary="尚未创建模型";
        private string _speedSummary="尚无骑行速度数据";
        internal string SpeedSummary => _speedSummary;
        internal float RideSpeedMultiplier => _settings.Riding.RideSpeedMultiplier;
        private Key _stationKey=Key.F6;
        private Key _mountKey=Key.F7;
        private Key _lossKey=Key.F8;
        private Key _summonKey=Key.F5;
        internal Key DiagnosticsKey {get;private set;}=Key.F9;
        internal Key SnapshotKey {get;private set;}=Key.F10;
        private readonly BikeSounds _sounds;
        private bool _summoning;
        private Vector3 _summonFrom;
        private Vector3 _summonTo;
        private float _summonYaw;
        private float _summonUntil;
        private string? _summonStationId;
        private string? _summonVehicleId;
        internal string StationKeyText => KeyLabel(_stationKey);
        internal string MountKeyText => KeyLabel(_mountKey);
        internal string LossKeyText => KeyLabel(_lossKey);
        internal string SummonKeyText => KeyLabel(_summonKey);
        internal string DiagnosticsKeyText => KeyLabel(DiagnosticsKey);
        internal string SnapshotKeyText => KeyLabel(SnapshotKey);
        internal string ControlsHint => StationKeyText+" 站点面板   "+MountKeyText+" 上下车   "+LossKeyText+" 报失   "+SummonKeyText+" 召车(¥"+_settings.Summon.Cost+")";
        internal string VisualSummary => _visualSummary;
        internal string SaveIntervalSummary => _runtime.FullSaveSeconds+" 秒";
        internal bool Ready => !_worldFailed && _runtime.IsReady && !SceneLoader.IsSceneLoading
            && (MultiSceneCore.Instance==null || !MultiSceneCore.Instance.IsLoading);
        internal bool HasVehicleDebt => _runtime.HasVehicleDebt && Alive;
        internal string PresentationSummary => _riderPose.Status;
        internal bool HasBoundSave=>_runtime.HasBoundSlot;
        private static CharacterMainControl? Actor => CharacterMainControl.Main;
        private static bool Alive => Actor != null && Actor.Health != null && !Actor.Health.IsDead;
        private bool Transition => _transitionUntil>0;
        private Scene CurrentScene => MultiSceneCore.ActiveSubScene ?? MultiSceneCore.MainScene ?? SceneManager.GetActiveScene();
        private string CurrentMapId => MultiSceneCore.Instance == null || !MultiSceneCore.MainScene.HasValue ? "" : MultiSceneCore.MainSceneID;

        internal BikeWorldController(NativeRentalRuntime runtime,string modPath,Action<string> log)
        {
            _runtime=runtime; _log=log;
            var path=Path.Combine(modPath,"prototype.json");
            _settings=StationSettingsFile.Read(path);
            _runtime.SetFullSaveSeconds(_settings.Persistence.FullSaveSeconds);
            _stationKey=ParseKey(_settings.Keys.Station,Key.F6,"Station");
            _mountKey=ParseKey(_settings.Keys.Mount,Key.F7,"Mount");
            _lossKey=ParseKey(_settings.Keys.Loss,Key.F8,"Loss");
            _summonKey=ParseKey(_settings.Keys.Summon,Key.F5,"Summon");
            DiagnosticsKey=ParseKey(_settings.Keys.Diagnostics,Key.F9,"Diagnostics");
            SnapshotKey=ParseKey(_settings.Keys.Snapshot,Key.F10,"Snapshot");
            _sounds=new BikeSounds(log);
            _blueprints=StationSettingsFile.ReadBlueprints(Path.Combine(modPath,"station-layout.json"));
            _assets=new PrototypeAssets(modPath,log);
            _riderPose=new NativeRiderPose(_settings.Riding,log);
            _runtime.BeforeCache+=CapturePose;
            _runtime.SlotChanged+=ResetWorld;
            _runtime.RentalClosed+=OnRentalClosed;
            SceneLoader.onStartedLoadingScene+=BeforeLoading;
            MultiSceneCore.OnSubSceneWillBeUnloaded+=BeforeSubSceneUnload;
            Notify("共享单车已启用，零号区、仓库区和农场镇设有租还站点。",true);
        }

        internal bool IsRiding(CharacterMainControl actor) => _rider==actor && actor!=null
            && actor.Health!=null && !actor.Health.IsDead
            && _runtime.Engine?.CurrentRental?.RideState==RideState.Riding;
        internal bool IsTransitioning(CharacterMainControl actor) => Ready && Alive && actor==Actor && (Transition || _dialogStation!=null || _confirmLoss);
        internal bool BlocksActions(CharacterMainControl actor) => actor!=null && actor==Actor && (IsRiding(actor) || IsTransitioning(actor));
        internal bool CanMountInteract { get { var actor=Actor; return Ready && Alive && actor!=null && !IsRiding(actor) && !Transition && _dialogStation==null && !SuppressForNativeTravel(actor.transform.position); } }
        internal bool CanUseStation { get { var actor=Actor; return Ready && Alive && actor!=null && !IsRiding(actor) && !Transition && _dialogStation==null && !SuppressForNativeTravel(actor.transform.position); } }

        internal void Tick()
        {
            try
            {
                if (Transition && Time.unscaledTime>=_transitionUntil && Time.timeScale>0) CompleteTransition();
                if (_summoning && Time.unscaledTime>=_summonUntil && Time.timeScale>0) CompleteSummon();
                if (Time.unscaledTime>=_nextSync)
                {
                    _nextSync=Time.unscaledTime+1;
                    SyncWorld();
                }
                if (!Ready || !Alive || Time.timeScale<=0) return;
                if (_dialogStation!=null && !NearStation(_dialogStation,4)) _dialogStation=null;
                var keyboard=Keyboard.current;
                if (keyboard!=null)
                {
                    if (keyboard.escapeKey.wasPressedThisFrame) { _dialogStation=null; _confirmLoss=false; }
                    if (keyboard[_stationKey].wasPressedThisFrame)
                    {
                        var nearest=NearestStation(3);
                        if (nearest!=null) OpenStation(nearest.Id);
                        else Notify("请先靠近共享单车站点。");
                    }
                    if (keyboard[_mountKey].wasPressedThisFrame) TryMountOrDismount();
                    if (keyboard[_lossKey].wasPressedThisFrame && _runtime.HasVehicleDebt) _confirmLoss=true;
                    if (keyboard[_summonKey].wasPressedThisFrame) Summon();
                }
            }
            catch (Exception ex) { RecoverWorldFault(ex); }
        }

        internal void LateTick()
        {
            try
            {
                if (!Ready || !Alive || _bike==null || _rider==null || !IsRiding(_rider))
                { if (_trackingRide) EndRiderPresentation(); return; }
                if (Time.timeScale<=0) return;
                var actor=_rider;
                // A native action (especially the interact action) makes CharacterMainControl.CanMove() false
                // and freezes the rider. Riding blocks every action, so clearing one is always safe here.
                if (actor.CurrentAction!=null && !actor.CurrentAction.CanMove())
                {
                    if (Time.unscaledTime>=_nextGuardLog)
                    {
                        _nextGuardLog=Time.unscaledTime+2;
                        _log("Ride guard clearing native action "+actor.CurrentAction.GetType().Name);
                    }
                    actor.CurrentAction.StopAction();
                }
                if (Time.unscaledTime<_mountGuardUntil && !_guardLogged && !actor.CanMove())
                {
                    _guardLogged=true;
                    _log("Ride guard: CanMove=false speed="+actor.CharacterWalkSpeed.ToString("0.###")
                        +" moveability="+actor.CharacterMoveability.ToString("0.###")
                        +" movementEnabled="+actor.movementControl.MovementEnabled);
                }
                if (actor.HeadInWater)
                {
                    var rental=_runtime.Engine!.CurrentRental!;
                    _runtime.Engine.SetRiding(rental.VehicleId,false); _rider=null; _returnAfterDismount=null; _transitionUntil=0;
                    EndRiderPresentation(); TryCacheWorld();
                    Notify("水域无法骑行，已下车；车辆停在上次位置，租赁继续。",true); return;
                }
                var candidate=actor.transform.position;
                if (_trackingRide && Vector3.Distance(candidate,_lastRidePosition)>BicycleGeometry.MaxContinuousStep)
                {
                    var rental=_runtime.Engine!.CurrentRental!;
                    _runtime.Engine.SetRiding(rental.VehicleId,false); _rider=null; _returnAfterDismount=null; _transitionUntil=0;
                    EndRiderPresentation(); TryCacheWorld();
                    Notify("检测到位置跳转，已停止骑行；车辆停留原处并继续计费。",true); return;
                }
                var direction=actor.movementControl.MoveInput; direction.y=0;
                var rotation=_lastRideRotation;
                if (direction.sqrMagnitude>0.0004f)
                {
                    var target=Quaternion.LookRotation(direction,Vector3.up).eulerAngles.y;
                    var yaw=RollingKinematics.TurnYaw(rotation.eulerAngles.y,target,Mathf.Min(Time.deltaTime,0.1f),_settings.Riding.TurnDegreesPerSecond);
                    rotation=SafeGround.GroundRotation(candidate,(float)yaw,actor);
                }
                if (_settings.Riding.VehicleClearance
                    && !BicycleClearance.CanTravel(_lastRidePosition,_lastRideRotation,candidate,rotation,actor)
                    && !_clearanceWarned)
                {
                    // Never pin the player back: an over-strict clearance result used to freeze the whole ride.
                    // The check stays advisory and warns once per ride.
                    _clearanceWarned=true;
                    _log("Ride clearance warning x="+candidate.x.ToString("0.0")+" z="+candidate.z.ToString("0.0"));
                    Notify("自行车附近有障碍，已临时忽略车体碰撞。");
                }
                _bike.transform.SetPositionAndRotation(candidate,rotation);
                if (_bikeMarker!=null && Time.unscaledTime>=_nextMarkerSync)
                {
                    _nextMarkerSync=Time.unscaledTime+1;
                    _bikeMarker.transform.position=candidate;
                }
                _lastRidePosition=candidate; _lastRideRotation=rotation;
                actor.movementControl.ForceTurnTo(rotation*Vector3.forward);
                _bike.GetComponent<BicycleVisual>()?.Track(true);
                _riderPose.LateTick(_bike.GetComponent<BicycleVisual>());
                CapturePose();
            }
            catch (Exception ex) { RecoverWorldFault(ex); }
        }

        internal void ApplyRiderAnimation(CharacterAnimationControl_MagicBlend control)
        {
            if (control.characterMainControl!=null && IsRiding(control.characterMainControl)) _riderPose.ApplyAnimation(control);
        }
        internal void ApplyRiderRig(CharacterModel model)
        {
            if (Ready && Alive && Time.timeScale>0 && Actor!=null && IsRiding(Actor) && Actor.characterModel==model && _bike!=null)
                _riderPose.LateTick(_bike.GetComponent<BicycleVisual>());
        }

        private void EndRiderPresentation()
        {
            _riderPose.Restore(); _trackingRide=false;
            if (_bike!=null) _bike.GetComponent<BicycleVisual>()?.Track(false);
        }

        private void SyncWorld()
        {
            if (!Ready || !Alive || _runtime.World==null) return;
            var map=CurrentMapId;
            var scene=CurrentScene;
            if (string.IsNullOrWhiteSpace(map) || !scene.IsValid() || !scene.isLoaded) return;
            if (map!=_mapId || scene.name!=_sceneName)
            {
                CapturePose(); DestroyActors(); _mapId=map; _sceneName=scene.name;
                _rider=null; _transitionUntil=0; _dialogStation=null; _returnAfterDismount=null; _confirmLoss=false; _nextPlacement=0;
                _nativeTravelPoints.Clear(); _loggedTravelPoints.Clear(); _nextTravelScan=0;
                var rental=_runtime.Engine?.CurrentRental;
                if (rental!=null) _runtime.Engine!.SetRiding(rental.VehicleId,false);
            }
            if (LevelManager.Instance!=null && LevelManager.Instance.IsBaseLevel)
            {
                // Reaching base with an active lease through an external/debug path must not strand a missing vehicle.
                if (_runtime.Engine?.CurrentRental!=null) CloseRecovery(CloseReason.SystemRecovery);
                return;
            }
            RefreshNativeTravelPoints();
            var state=_runtime.World;
            var targetKey=TargetKey(scene);
            if (targetKey!=null && Time.unscaledTime>=_nextPlacement)
            {
                _nextPlacement=Time.unscaledTime+_settings.PlacementRetrySeconds;
                if (_blueprints.Sites.Any(s=>s.MapId==map && s.SceneName==scene.name)) CreatePlannedPlacements(state);
                else if (!state.Stations.Any(s=>s.MapId==map && s.SceneName==scene.name)) CreateTestPlacements(state,targetKey);
            }
            foreach (var station in state.Stations.Where(s=>s.MapId==map && s.SceneName==scene.name))
            {
                if (_stations.ContainsKey(station.Id)) continue;
                try
                {
                    var root=_assets.Station(SafeGround.Vector(station.Position));
                    root.transform.rotation=Quaternion.Euler(0,station.Yaw,0);
                    SceneManager.MoveGameObjectToScene(root,scene);
                    _stations.Add(station.Id,root);
                    AddInteraction(root,station.Id);
                    AddMarker(root,station.Name,SceneInfoCollection.GetSceneID(scene.buildIndex));
                    _log("Station visual created id="+station.Id+" layer="+root.layer+"("+LayerMask.LayerToName(root.layer)+") renderers="+root.GetComponentsInChildren<Renderer>().Length);
                }
                catch (Exception ex) { _log("Station visual failed id="+station.Id+": "+ex); }
            }
            var lease=_runtime.Engine?.CurrentRental;
            if (lease==null) { DestroyBike(); state.Vehicle=null; return; }
            if (_summoning) return; // The ghost ride is in flight; the real bike spawns at CompleteSummon.
            var pose=state.Vehicle;
            if (pose==null || pose.VehicleId!=lease.VehicleId)
            {
                CloseRecovery(CloseReason.SystemRecovery); return;
            }
            if (pose.MapId!=map || pose.SceneName!=scene.name) return;
            if (_bike==null)
            {
                if (!SafeGround.Find(SafeGround.Vector(pose.Position),SafeGround.Vector(pose.Position),Actor!,out var restored))
                { CloseRecovery(CloseReason.SystemRecovery); return; }
                var rotation=SafeGround.GroundRotation(restored,pose.Yaw,Actor!);
                if (_settings.Riding.VehicleClearance && !BicycleClearance.Fits(restored,rotation,Actor!))
                { CloseRecovery(CloseReason.SystemRecovery); return; }
                _bike=CreateBike(restored,pose.Yaw);
                _bike.transform.rotation=rotation;
                pose.Position=SafeGround.Point(restored);
            }
        }

        private string? TargetKey(Scene scene)
        {
            if (MultiSceneCore.Instance==null) return null;
            var sceneId=SceneInfoCollection.GetSceneID(scene.buildIndex);
            var sceneKey=SceneInfoCollection.GetSceneInfo(sceneId)?.DisplayNameRaw ?? "";
            var mainKey=MultiSceneCore.Instance.DisplaynameRaw;
            return StationRouting.TargetKey(_settings,mainKey,sceneKey,
                MultiSceneCore.MainScene.HasValue && MultiSceneCore.MainScene.Value==scene);
        }

        private void CreateTestPlacements(WorldState state,string targetKey)
        {
            var fixedStations=_settings.FixedStations.Where(s=>s.MapId==_mapId && s.SceneName==_sceneName).ToArray();
            if (fixedStations.Length>0)
            {
                if (fixedStations.Any(station=>state.Stations.Any(s=>s.Id==station.Id)))
                    throw new InvalidOperationException("Fixed station ID conflicts with saved layout.");
                foreach (var station in fixedStations)
                {
                    state.Stations.Add(station.Copy());
                }
                TryCacheWorld(); return;
            }
            if (!_settings.AutomaticTestStations) return;
            var actor=Actor!; var origin=actor.transform.position; var chosen=new List<Vector3>();
            foreach (var distance in new[] { 6f,14f,10f,20f })
            {
                for (var i=0;i<12 && chosen.Count<_settings.StationsPerScene;i++)
                {
                    var offset=Quaternion.Euler(0,i*30,0)*Vector3.forward*distance;
                    if (!SafeGround.Find(origin,origin+offset,actor,out var point)) continue;
                    if (chosen.Any(p=>Vector3.Distance(p,point)<_settings.MinimumStationSeparation)) continue;
                    chosen.Add(point);
                }
                if (chosen.Count==_settings.StationsPerScene) break;
            }
            if (chosen.Count!=_settings.StationsPerScene) { Notify("尚未找到足够的安全站点位置，稍后重试。"); return; }
            for (var i=0;i<chosen.Count;i++) state.Stations.Add(new StationRecord
            {
                Id="prototype-"+_mapId+"-"+_sceneName+"-"+(i+1),Name=StationRouting.DisplayName(targetKey)+"测试站"+(i+1),MapId=_mapId,
                SceneName=_sceneName,Position=SafeGround.Point(chosen[i])
            });
            TryCacheWorld();
            _log("Test stations placed count="+chosen.Count+" target="+targetKey+" map="+_mapId+" scene="+_sceneName);
        }

        private void CreatePlannedPlacements(WorldState state)
        {
            if (!_settings.Enabled) return;
            var changed=false;
            foreach (var site in _blueprints.Sites.Where(s=>s.MapId==_mapId && s.SceneName==_sceneName))
            {
                if (state.Stations.Any(s=>s.Id==site.Id)) continue;
                var anchor=SafeGround.Vector(site.Anchor); Vector3 chosen=default; var found=false;
                if (UnityEngine.AI.NavMesh.SamplePosition(anchor,out var origin,8,UnityEngine.AI.NavMesh.AllAreas)) anchor=origin.position;
                foreach (var radius in new[] { 10f,14f,18f,8f,site.SearchRadius })
                {
                    if (radius>site.SearchRadius) continue;
                    for (var i=0;i<24 && !found;i++)
                    {
                        var candidate=anchor+Quaternion.Euler(0,site.Yaw+i*15,0)*Vector3.forward*radius;
                        if (!SafeGround.Find(anchor,candidate,Actor!,out chosen) || !BicycleClearance.Fits(chosen,Quaternion.Euler(0,site.Yaw,0),Actor!)) continue;
                        if (NearestNativeTravel(chosen,6f)!=null) continue;
                        if (state.Stations.Any(s=>s.MapId==_mapId && s.SceneName==_sceneName && SpatialRules.Within(s.Position,SafeGround.Point(chosen),6))) continue;
                        if (UnityEngine.Object.FindObjectsOfType<CountDownArea>().Any(a=>a.gameObject.scene==CurrentScene
                            && Vector3.Distance(a.transform.position,chosen)<10)) continue;
                        found=true;
                    }
                    if (found) break;
                }
                if (!found) { _log("Station site pending safe placement id="+site.Id); continue; }
                state.Stations.Add(new StationRecord { Id=site.Id,Name=site.Name,MapId=site.MapId,SceneName=site.SceneName,
                    Position=SafeGround.Point(chosen),Yaw=site.Yaw }); changed=true;
                _log("Station site placed id="+site.Id+" position="+chosen);
            }
            if (changed) TryCacheWorld();
        }

        private void AddInteraction(GameObject root,string? stationId)
        {
            // The native base wakes by moving the collider object onto the "Interactable" layer, which the
            // camera does not render. Put the trigger on a dedicated child so the model keeps its visible layer.
            var point=new GameObject("InteractPoint");
            point.transform.SetParent(root.transform,false);
            point.transform.localPosition=Vector3.up*0.9f;
            var interactableLayer=LayerMask.NameToLayer("Interactable");
            if (interactableLayer>=0) point.layer=interactableLayer;
            var collider=point.AddComponent<BoxCollider>(); collider.isTrigger=true;
            collider.center=Vector3.zero; collider.size=new Vector3(1.1f,1.8f,1.4f);
            var interaction=point.AddComponent<SharedBikeInteractable>(); interaction.World=this; interaction.StationId=stationId;
            interaction.InteractName=stationId==null ? "共享单车" : "共享单车站点";
        }

        private GameObject? AddMarker(GameObject root,string name,string sceneId)
        {
            if (_assets.Icon==null) return null;
            // Keep the POI in the main scene the way the native factory intends; re-parenting it into the
            // sub-scene made the marker disappear when that sub-scene unloaded.
            var marker=Duckov.MiniMaps.SimplePointOfInterest.Create(root.transform.position,sceneId,name,_assets.Icon,false);
            marker.Color=new Color(0.2f,0.86f,0.58f); marker.ScaleFactor=0.65f;
            _markers.Add(marker.gameObject);
            if (!_markersLogged) { _markersLogged=true; _log("Map markers active scene="+sceneId+" icon="+_assets.Icon.name); }
            return marker.gameObject;
        }

        private void RefreshNativeTravelPoints()
        {
            if (Time.unscaledTime<_nextTravelScan) return;
            _nextTravelScan=Time.unscaledTime+2;
            _nativeTravelPoints.Clear();
            foreach (var teleporter in UnityEngine.Object.FindObjectsOfType<Duckov.Scenes.MultiSceneTeleporter>())
                if (teleporter!=null && teleporter.gameObject.scene.isLoaded) _nativeTravelPoints.Add(teleporter);
            foreach (var teleporter in UnityEngine.Object.FindObjectsOfType<SimpleTeleporter>())
                if (teleporter!=null && teleporter.gameObject.scene.isLoaded) _nativeTravelPoints.Add(teleporter);
            foreach (var teleporter in _nativeTravelPoints)
            {
                if (!_loggedTravelPoints.Add(teleporter.GetInstanceID())) continue;
                var position=teleporter.transform.position;
                _log("Native travel point "+teleporter.name+" type="+teleporter.GetType().Name
                    +" pos="+position.x.ToString("0.0")+","+position.z.ToString("0.0")
                    +" enabled="+teleporter.enabled
                    +" collider="+(teleporter.interactCollider!=null?teleporter.interactCollider.enabled.ToString():"none")
                    +" disableOnFinish="+teleporter.disableOnFinish);
            }
        }

        private InteractableBase? NearestNativeTravel(Vector3 position,float radius)
        {
            InteractableBase? best=null;
            var bestDistance=radius*radius;
            foreach (var teleporter in _nativeTravelPoints)
            {
                if (teleporter==null) continue;
                var point=teleporter.interactCollider!=null ? teleporter.interactCollider.ClosestPoint(position) : teleporter.transform.position;
                var distance=(point-position).sqrMagnitude;
                if (distance<=bestDistance) { bestDistance=distance; best=teleporter; }
            }
            return best;
        }

        private bool SuppressForNativeTravel(Vector3 position)
        {
            var nearest=NearestNativeTravel(position,3.5f);
            if (nearest==null) return false;
            if (Time.unscaledTime>=_nextNativePromptLog)
            {
                _nextNativePromptLog=Time.unscaledTime+5;
                _log("Shared-bike prompt suppressed near native travel point "+nearest.name
                    +" type="+nearest.GetType().Name+" dist="+Vector3.Distance(position,nearest.transform.position).ToString("0.0"));
            }
            return true;
        }

        private GameObject CreateBike(Vector3 position,float yaw)
        {
            var bike=_assets.Bike(position,yaw);
            try
            {
                SceneManager.MoveGameObjectToScene(bike,CurrentScene);
                AddInteraction(bike,null);
                try { _bikeMarker=AddMarker(bike,"你租赁的共享单车",SceneInfoCollection.GetSceneID(CurrentScene.buildIndex)); }
                catch (Exception ex) { _log("Bike marker failed: "+ex.Message); }
                _log("Bike visual created layer="+bike.layer+"("+LayerMask.LayerToName(bike.layer)+") renderers="
                    +bike.GetComponentsInChildren<Renderer>().Length+" position="+position.x.ToString("0.00")+","+position.z.ToString("0.00"));
                _visualSummary="层 "+LayerMask.LayerToName(bike.layer)+"，部件 "+bike.GetComponentsInChildren<Renderer>().Length+" 个";
                return bike;
            }
            catch { UnityEngine.Object.Destroy(bike); throw; }
        }

        internal void OpenStation(string id)
        {
            if (!Ready || Transition || !NearStation(id,4)) return;
            _dialogStation=id;
            Actor?.Trigger(false,false,true); Actor?.SetAdsInput(false);
        }

        private void Rent(string stationId)
        {
            if (!Ready || !Alive || Transition || !NearStation(stationId,4) || _runtime.Engine==null || _runtime.World==null) return;
            if (!_settings.Enabled) { Notify("配置已关闭新租赁，请先归还已有车辆。",true); return; }
            if (_runtime.PaymentUnavailable) { Notify("付款记录异常，已停止新租赁，请保留日志以便恢复。",true); return; }
            var engine=_runtime.Engine;
            if (engine.RentalEligibility!=RuleCode.Success) { Notify(RuleText(engine.RentalEligibility),true); return; }
            var station=_runtime.World.Stations.Single(s=>s.Id==stationId);
            var origin=SafeGround.Vector(station.Position);
            var actor=Actor!;
            var yaw=actor.modelRoot!=null ? actor.modelRoot.eulerAngles.y : actor.transform.eulerAngles.y;
            var rotation=Quaternion.Euler(0,yaw,0);
            Vector3 point=default; var found=false;
            for (var i=0;i<8 && !found;i++)
            {
                found=SafeGround.Find(actor.transform.position,origin+Quaternion.Euler(0,i*45,0)*Vector3.forward*1.6f,actor,out point);
                if (found) rotation=SafeGround.GroundRotation(point,yaw,actor);
                if (found && _settings.Riding.VehicleClearance && !BicycleClearance.Fits(point,rotation,actor)) found=false;
            }
            if (!found) { Notify("站点附近没有安全车辆生成位置，请稍后重试。",true); return; }
            var vehicleId=Guid.NewGuid().ToString("N");
            GameObject? created=null;
            try
            {
                created=CreateBike(point,yaw);
                created.transform.rotation=rotation;
                var result=engine.Rent(stationId,vehicleId,new RatePlan(),true);
                if (!result.Succeeded) { UnityEngine.Object.Destroy(created); Notify(RuleText(result.Code),true); return; }
                _bike=created;
                _runtime.World.Vehicle=new VehiclePose { VehicleId=vehicleId,MapId=_mapId,SceneName=_sceneName,
                    Position=SafeGround.Point(point),Yaw=created.transform.eulerAngles.y };
                _runtime.RentalStarted();
                _dialogStation=null;
                _runtime.Persist(true);
                _log("Rent started station="+stationId+" vehicle="+vehicleId+" position="+point.x.ToString("0.00")+","+point.z.ToString("0.00"));
                _sounds.RentTone();
                Notify("租赁成功，靠近自行车按F7上车；不足1小时收费200元。",true);
            }
            catch (Exception ex)
            {
                // If creation failed before the lease was committed there must be no minimum fee.
                if (engine.CurrentRental==null && created!=null) UnityEngine.Object.Destroy(created);
                _log("Rent operation interrupted: "+ex);
                Notify(engine.CurrentRental==null ? "车辆生成失败，未建立租赁。" : "租赁已建立，保存暂未完成，请稍后归还。",true);
            }
        }

        // Paid summon: charge up-front, then a ghost bike rides in from the nearest station
        // over Summon.Seconds. Any later failure refunds the fee.
        private void Summon()
        {
            if (!Ready || !Alive || Transition || _summoning || _runtime.Engine==null || _runtime.World==null) return;
            if (!_settings.Enabled) { Notify("配置已关闭新租赁，无法召车。",true); return; }
            if (_runtime.PaymentUnavailable) { Notify("付款记录异常，已停止召车，请保留日志以便恢复。",true); return; }
            var engine=_runtime.Engine;
            if (engine.CurrentRental!=null) { Notify("你已有一辆自行车在用，请先归还后再召车。",true); return; }
            if (engine.RentalEligibility!=RuleCode.Success) { Notify(RuleText(engine.RentalEligibility),true); return; }
            var station=NearestStationSummon();
            if (station==null) { Notify("本地图没有共享单车站点，无法召车。",true); return; }
            var actor=Actor!;
            var cost=_settings.Summon.Cost;
            if (cost>0 && !_runtime.DirectPay(cost))
            { _sounds.ShortChime(); Notify("召车需要"+cost+"元，现金不足或当前无法扣款。",true); return; }
            var yaw=actor.modelRoot!=null ? actor.modelRoot.eulerAngles.y : actor.transform.eulerAngles.y;
            Vector3 point=default; var found=false;
            var rotation=Quaternion.Euler(0,yaw,0);
            for (var i=0;i<8 && !found;i++)
            {
                found=SafeGround.Find(actor.transform.position,actor.transform.position+Quaternion.Euler(0,i*45,0)*Vector3.forward*1.6f,actor,out point);
                if (found) rotation=SafeGround.GroundRotation(point,yaw,actor);
                if (found && _settings.Riding.VehicleClearance && !BicycleClearance.Fits(point,rotation,actor)) found=false;
            }
            if (!found)
            {
                if (cost>0) _runtime.DirectRefund(cost);
                Notify("附近没有安全车辆落点，请换开阔位置再召车。",true); return;
            }
            var vehicleId=Guid.NewGuid().ToString("N");
            var result=engine.Rent(station.Id,vehicleId,new RatePlan(),true);
            if (!result.Succeeded)
            {
                if (cost>0) _runtime.DirectRefund(cost);
                Notify(RuleText(result.Code),true); return;
            }
            var origin=SafeGround.Vector(station.Position);
            var direction=origin-point; direction.y=0;
            var approachYaw=direction.sqrMagnitude>0.0001f ? Quaternion.LookRotation(direction,Vector3.up).eulerAngles.y : yaw;
            _summoning=true;
            _summonFrom=origin;
            _summonTo=point;
            _summonYaw=approachYaw;
            _summonUntil=Time.unscaledTime+Mathf.Max(1f,_settings.Summon.Seconds);
            _summonStationId=station.Id;
            _summonVehicleId=vehicleId;
            // The lease exists from this instant and SyncWorld validates the persisted pose
            // every second, so the pose must point at the summon destination immediately,
            // otherwise the very next sync closes the order as a missing vehicle.
            _runtime.World.Vehicle=new VehiclePose { VehicleId=vehicleId,MapId=_mapId,SceneName=_sceneName,
                Position=SafeGround.Point(_summonTo),Yaw=_summonYaw };
            _sounds.SummonTone();
            try { _runtime.Persist(true); }
            catch (Exception ex) { _log("Summon save pending: "+ex); TryCacheWorld(); }
            _log("Summon started station="+station.Id+" vehicle="+vehicleId+" cost="+cost
                +" from="+origin.x.ToString("0.0")+","+origin.z.ToString("0.0")+" to="+point.x.ToString("0.0")+","+point.z.ToString("0.0"));
            Notify("已支付召车费"+cost+"元，自行车正从"+station.Name+"赶来，约"+Mathf.CeilToInt(_settings.Summon.Seconds)+"秒。",true);
        }

        private void CompleteSummon()
        {
            _summoning=false;
            var engine=_runtime.Engine;
            var stationId=_summonStationId; var vehicleId=_summonVehicleId;
            _summonStationId=null; _summonVehicleId=null;
            if (engine==null || stationId==null || vehicleId==null) return;
            var rental=engine.CurrentRental;
            if (rental==null || rental.VehicleId!=vehicleId)
            {
                _log("Summon completed but lease vanished vehicle="+vehicleId);
                Notify("召车订单异常，费用已按订单状态处理。",true); return;
            }
            if (!Ready || !Alive)
            {
                // The lease stays valid; SyncWorld will restore the bike from the persisted pose.
                _runtime.World!.Vehicle=new VehiclePose { VehicleId=vehicleId,MapId=_mapId,SceneName=_sceneName,
                    Position=SafeGround.Point(_summonTo),Yaw=_summonYaw };
                TryCacheWorld();
                _sounds.RentTone();
                Notify("自行车已送达。",true); return;
            }
            var rotation=SafeGround.GroundRotation(_summonTo,_summonYaw,Actor!);
            GameObject? created=null;
            var world=_runtime.World!;
            try
            {
                created=CreateBike(_summonTo,_summonYaw);
                created.transform.rotation=rotation;
                _bike=created;
                world.Vehicle=new VehiclePose { VehicleId=vehicleId,MapId=_mapId,SceneName=_sceneName,
                    Position=SafeGround.Point(_summonTo),Yaw=created.transform.eulerAngles.y };
                _runtime.RentalStarted();
                _sounds.RentTone();
                try { _runtime.Persist(true); }
                catch (Exception ex) { _log("Summon arrival save pending: "+ex); TryCacheWorld(); }
                _log("Summon completed vehicle="+vehicleId+" at="+_summonTo.x.ToString("0.00")+","+_summonTo.z.ToString("0.00"));
                Notify("自行车已送达，靠近按F7上车；不足1小时收费200元。",true);
            }
            catch (Exception ex)
            {
                if (created==null && _bike==null)
                {
                    _log("Summon arrival failed: "+ex);
                    Notify("自行车送达失败，车辆数据已保留，稍后自动恢复。",true);
                }
                else _log("Summon arrival interrupted: "+ex);
            }
        }

        private StationRecord? NearestStationSummon()
        {
            if (_runtime.World==null || Actor==null) return null;
            return _runtime.World.Stations.Where(s=>s.MapId==_mapId && s.SceneName==_sceneName)
                .OrderBy(s=>Vector3.Distance(Actor.transform.position,SafeGround.Vector(s.Position))).FirstOrDefault();
        }

        internal void TryMountOrDismount()
        {
            if (!Ready || !Alive || Transition || _runtime.Engine==null) return;
            var actor=Actor!; var rental=_runtime.Engine.CurrentRental;
            if (rental==null || _bike==null) { Notify("请先租车并靠近你租赁的自行车。"); return; }
            if (_dialogStation!=null) return;
            if (IsRiding(actor))
            {
                if (!FindLanding(actor,out _landing)) { Notify("附近没有安全下车位置，请移动到开阔地带。"); return; }
                _mounting=false; _transitionUntil=Time.unscaledTime+1;
            }
            else
            {
                if (!SpatialRules.Within(SafeGround.Point(actor.transform.position),SafeGround.Point(_bike.transform.position),2)
                    || !SafeGround.Clear(actor.transform.position,_bike.transform.position)) { Notify("请先靠近你租赁的自行车。"); return; }
                if (Duckov.UI.View.ActiveView!=null) { Notify("请先关闭当前界面再上车。"); return; }
                if (actor.CurrentAction!=null && !actor.CurrentAction.StopAction()) { Notify("当前动作尚未结束，请稍后上车。"); return; }
                actor.CancleSkill(); actor.Trigger(false,false,true); actor.SetAdsInput(false); actor.SetRunInput(false);
                _mounting=true; _transitionUntil=Time.unscaledTime+1; _rider=actor;
            }
            Notify(_mounting ? "正在上车…" : "正在下车…",true);
        }

        private bool FindLanding(CharacterMainControl actor,out Vector3 result)
        {
            var center=_bike!=null ? _bike.transform.position : actor.transform.position;
            var right=_bike!=null ? _bike.transform.right : actor.transform.right;
            for (var i=0;i<8;i++)
                if (SafeGround.Find(actor.transform.position,center+Quaternion.Euler(0,i*45,0)*right*1.15f,actor,out result)) return true;
            result=default; return false;
        }

        private void CompleteTransition()
        {
            _transitionUntil=0;
            var rental=_runtime.Engine?.CurrentRental;
            if (!Ready || !Alive || _bike==null || rental==null)
            {
                if (rental!=null) _runtime.Engine!.SetRiding(rental.VehicleId,false);
                EndRiderPresentation(); _rider=null; _returnAfterDismount=null; return;
            }
            if (_mounting)
            {
                var actor=Actor!;
                if (Vector3.Distance(actor.transform.position,_bike.transform.position)>2.5f
                    || !SafeGround.Clear(actor.transform.position,_bike.transform.position)
                    || !SafeGround.Find(actor.transform.position,_bike.transform.position,actor,out var mountPosition)
                    || Vector3.Distance(mountPosition,_bike.transform.position)>0.2f
                    || (_settings.Riding.VehicleClearance && !BicycleClearance.Fits(mountPosition,_bike.transform.rotation,actor)))
                { _rider=null; _returnAfterDismount=null; Notify("车辆附近空间不足或地面已变化，请调整位置再上车。",true); return; }
                var engine=_runtime.Engine!;
                var expectedBike=_bike;
                actor.SetPosition(mountPosition);
                if (_runtime.Engine!=engine || _bike!=expectedBike || !Ready || !Alive || engine.CurrentRental?.RentalId!=rental.RentalId)
                { _rider=null; _returnAfterDismount=null; return; }
                engine.SetRiding(rental.VehicleId,true); _rider=actor;
                _bike.transform.position=mountPosition;
                _lastRidePosition=mountPosition; _lastRideRotation=_bike.transform.rotation; _trackingRide=true;
                _bike.GetComponent<BicycleVisual>()?.Track(false);
                _bike.GetComponent<BicycleVisual>()?.Track(true);
                _riderPose.Begin(actor); actor.movementControl.ForceTurnTo(_bike.transform.forward);
                actor.movementControl.MovementEnabled=true;
                _mountGuardUntil=Time.unscaledTime+3; _guardLogged=false; _clearanceWarned=false;
                var nativeSpeed=0f; var patchedSpeed=0f; var weight=-1f; var maxWeight=0f;
                var weightBuffs=Array.Empty<string>();
                try
                {
                    nativeSpeed=NativePatches.NativeWalkSpeed(actor);
                    patchedSpeed=actor.CharacterWalkSpeed;
                    if (actor.CharacterItem!=null) weight=actor.CharacterItem.TotalWeight;
                    maxWeight=actor.MaxWeight;
                    var manager=actor.GetBuffManager();
                    if (manager!=null && manager.Buffs!=null)
                        weightBuffs=manager.Buffs
                            .Where(buff=>buff!=null && buff.ExclusiveTag==Duckov.Buffs.Buff.BuffExclusiveTags.Weight)
                            .Select(buff=>buff.name).ToArray();
                    _speedSummary="步行 "+nativeSpeed.ToString("0.00")+"｜骑行 "+patchedSpeed.ToString("0.00")
                        +"｜负重 "+weight.ToString("0.0")+"/"+maxWeight.ToString("0.0");
                    _log("Mounted speed patched="+patchedSpeed.ToString("0.###")+" native="+nativeSpeed.ToString("0.###")
                        +" weight="+weight.ToString("0.0")+"/"+maxWeight.ToString("0.0")
                        +" buffs="+string.Join(",",weightBuffs)+" multiplier="+RideSpeedMultiplier
                        +" moveability="+actor.CharacterMoveability.ToString("0.###")
                        +" action="+(actor.CurrentAction!=null?actor.CurrentAction.GetType().Name:"none"));
                }
                catch (Exception ex)
                {
                    _speedSummary="读取失败，见日志";
                    _log("Ride speed diagnostics unavailable: "+ex);
                }
                var bonus="，无视负重并加速 x"+RideSpeedMultiplier.ToString("0.00");
                Notify("骑行中：无视负重并加速（负重 "+weight.ToString("0.0")+"/"+maxWeight.ToString("0.0")+bonus+"），不能战斗或使用食物药品。",true);
            }
            else
            {
                if (!SafeGround.Find(Actor!.transform.position,_landing,Actor,out var landing)) { _returnAfterDismount=null; Notify("下车位置已被占用，请重试。",true); return; }
                CapturePose(); _runtime.Engine!.SetRiding(rental.VehicleId,false); EndRiderPresentation(); _rider=null;
                Actor.SetPosition(landing);
                Notify("已下车，车辆未归还，继续计费。",true);
                var returnStation=_returnAfterDismount; _returnAfterDismount=null;
                if (returnStation!=null) Return(returnStation);
            }
        }

        private void Return(string stationId)
        {
            if (!Ready || !Alive || Transition || _runtime.Engine==null || _runtime.World==null || _bike==null) return;
            var station=_runtime.World.Stations.SingleOrDefault(s=>s.Id==stationId);
            var rental=_runtime.Engine.CurrentRental;
            if (station==null || rental==null || _runtime.World.Vehicle?.VehicleId!=rental.VehicleId) return;
            if (!SpatialRules.CanReturn(SafeGround.Point(Actor!.transform.position),SafeGround.Point(_bike.transform.position),station.Position,4,
                SafeGround.Clear(Actor.transform.position,SafeGround.Vector(station.Position)) && SafeGround.Clear(_bike.transform.position,SafeGround.Vector(station.Position))))
            { Notify("请将你和自行车一起带入本站还车范围。",true); return; }
            if (IsRiding(Actor))
            {
                _dialogStation=null; _returnAfterDismount=stationId; TryMountOrDismount();
                if (!Transition) _returnAfterDismount=null;
                return;
            }
            _runtime.AccumulateToNow();
            var closed=_runtime.Engine.Close(rental.RentalId,CloseReason.Returned,stationId);
            _runtime.World.Vehicle=null; DestroyBike(); _dialogStation=null;
            _runtime.Persist(true);
            var order=_runtime.Engine.FindOrder(closed.Id!)!;
            _log("Return closed order="+closed.Id+" total="+order.Total+" station="+stationId);
            Notify(SettleOrder(closed.Id!,order.Total,"还车成功"),true);
        }

        internal bool AllowTeleport(string target)
        {
            if (!HasVehicleDebt) return true;
            var core=MultiSceneCore.Instance;
            var same=target==CurrentMapId || (core!=null && core.SubScenes.Any(s=>s.sceneID==target));
            return AllowTravel(target,false,same);
        }

        internal void LogTeleportBlock(string target,string source)
            => _log("Teleport blocked source="+source+" target="+target+" debt="+HasVehicleDebt
                +" riding="+(Actor!=null && IsRiding(Actor))+" action="+(Actor?.CurrentAction!=null?Actor.CurrentAction.GetType().Name:"none"));

        internal string RepairNearbyTravelPoints()
        {
            var actor=Actor;
            if (actor==null) return "没有可用的玩家角色";
            RefreshNativeTravelPoints();
            var repaired=0;
            foreach (var teleporter in _nativeTravelPoints)
            {
                if (teleporter==null) continue;
                if (Vector3.Distance(teleporter.transform.position,actor.transform.position)>30) continue;
                if (!teleporter.enabled) { teleporter.enabled=true; repaired++; }
                if (teleporter.interactCollider!=null && !teleporter.interactCollider.enabled) { teleporter.interactCollider.enabled=true; repaired++; }
            }
            _log("Travel point repair repaired="+repaired+" nearby="+_nativeTravelPoints.Count);
            return repaired==0 ? "附近没有可修复的通道" : "已重新启用 "+repaired+" 项，请再靠近查看交互";
        }

        internal bool AllowPrimarySceneLoad(string target,bool notifyEvacuation=false)
        {
            if (!HasVehicleDebt) return true;
            var baseLevel=target==SceneInfoCollection.BaseSceneID || (!string.IsNullOrEmpty(target) && target.StartsWith("Base",StringComparison.Ordinal));
            var core=MultiSceneCore.Instance;
            var same=target==CurrentMapId || (core!=null && core.SubScenes.Any(s=>s.sceneID==target));
            return AllowTravel(target,baseLevel || notifyEvacuation,same);
        }

        private bool AllowTravel(string target,bool extraction,bool sameMap)
        {
            if (Transition) return false;
            var decision=TravelPolicy.Evaluate(HasVehicleDebt,Actor!=null && IsRiding(Actor),extraction,sameMap,
                _blueprints.CanLeaveAndReturn(CurrentMapId,target));
            if (decision==TravelDecision.Allowed) { CapturePose(); TryCacheWorld(); return true; }
            Notify(decision==TravelDecision.Dismount ? "请先下车再使用地图通道，车辆会停留原处。"
                : decision==TravelDecision.NoReturnRoute ? "此跨区通道不能保证返回取车，请先归还车辆再通行。"
                : "你还有一辆共享单车未归还，请先还车。"+NearestStationText(),true);
            return false;
        }

        internal void NotifyEvacuationBlocked()=>Notify("你还有一辆共享单车未归还，请先还车。"+NearestStationText(),true);
        private string NearestStationText()
        {
            if (_runtime.World==null || Actor==null) return "";
            var station=_runtime.World.Stations.Where(s=>s.MapId==CurrentMapId && s.SceneName==CurrentScene.name)
                .OrderBy(s=>Vector3.Distance(Actor.transform.position,SafeGround.Vector(s.Position))).FirstOrDefault();
            return station==null ? "" : "最近站点："+station.Name+"，约"+Mathf.CeilToInt(Vector3.Distance(Actor.transform.position,SafeGround.Vector(station.Position)))+"米。";
        }

        internal void PrepareSessionExit()
        {
            CapturePose(); CancelSummon("session exit"); var rental=_runtime.Engine?.CurrentRental;
            if (rental!=null) _runtime.Engine!.SetRiding(rental.VehicleId,false);
            EndRiderPresentation(); _rider=null; _transitionUntil=0; _dialogStation=null; _returnAfterDismount=null; _confirmLoss=false;
            try { if (_runtime.IsReady) _runtime.Persist(true); }
            catch (Exception ex) { _log("Session exit save pending: "+ex); }
        }

        private StationRecord? NearestStation(float radius)
        {
            if (_runtime.World==null || Actor==null) return null;
            return _runtime.World.Stations.Where(s=>s.MapId==_mapId && s.SceneName==_sceneName)
                .Where(s=>SpatialRules.Within(SafeGround.Point(Actor.transform.position),s.Position,radius)
                    && SafeGround.Clear(Actor.transform.position,SafeGround.Vector(s.Position)))
                .OrderBy(s=>Vector3.Distance(Actor.transform.position,SafeGround.Vector(s.Position))).FirstOrDefault();
        }
        private bool NearStation(string id,float radius) => _runtime.World?.Stations.Any(s=>s.Id==id && s.MapId==_mapId
            && s.SceneName==_sceneName && Actor!=null && SpatialRules.Within(SafeGround.Point(Actor.transform.position),s.Position,radius)
            && SafeGround.Clear(Actor.transform.position,SafeGround.Vector(s.Position)))==true;

        private void CapturePose()
        {
            var pose=_runtime.World?.Vehicle;
            if (pose==null || _bike==null) return;
            pose.Position=SafeGround.Point(_bike.transform.position); pose.Yaw=_bike.transform.eulerAngles.y;
        }
        private void BeforeLoading(SceneLoadingContext context)
        {
            CapturePose();
            CancelSummon("scene loading");
            var rental=_runtime.Engine?.CurrentRental;
            if (rental!=null) _runtime.Engine!.SetRiding(rental.VehicleId,false);
            _rider=null; _transitionUntil=0; _dialogStation=null; _returnAfterDismount=null; _confirmLoss=false; DestroyActors(); _sceneName="";
        }
        private void BeforeSubSceneUnload(MultiSceneCore core,Scene scene)
        {
            if (scene.name!=_sceneName) return;
            CapturePose();
            CancelSummon("sub scene unload");
            var rental=_runtime.Engine?.CurrentRental;
            if (rental!=null) _runtime.Engine!.SetRiding(rental.VehicleId,false);
            _rider=null; _transitionUntil=0; _dialogStation=null; _returnAfterDismount=null; _confirmLoss=false;
            TryCacheWorld(); DestroyActors(); _sceneName="";
        }
        private void OnRentalClosed() { _rider=null; _transitionUntil=0; _dialogStation=null; _returnAfterDismount=null; _confirmLoss=false; _summoning=false; DestroyBike(); }
        private void ResetWorld() { DestroyActors(); _rider=null; _transitionUntil=0; _mapId=""; _sceneName=""; _dialogStation=null; _returnAfterDismount=null; _confirmLoss=false; _worldFailed=false; _nextPlacement=0; _summoning=false; }

        private void CancelSummon(string reason)
        {
            if (!_summoning) return;
            _summoning=false; _summonStationId=null; _summonVehicleId=null;
            _log("Summon cancelled: "+reason);
            // The lease remains valid in the engine; its persisted pose points at the summon
            // destination, so SyncWorld/CreateBike will materialize the bike there later.
            var engine=_runtime.Engine;
            var vehicleId=engine?.CurrentRental?.VehicleId;
            if (engine!=null && vehicleId!=null && _runtime.World!=null)
                _runtime.World.Vehicle=new VehiclePose { VehicleId=vehicleId,MapId=_mapId,SceneName=_sceneName,
                    Position=SafeGround.Point(_summonTo),Yaw=_summonYaw };
            TryCacheWorld();
        }
        private void CloseRecovery(CloseReason reason)
        {
            var engine=_runtime.Engine; var rental=engine?.CurrentRental;
            if (rental==null || engine==null) return;
            _runtime.AccumulateToNow();
            var closed=engine.Close(rental.RentalId,reason);
            if (_runtime.World!=null) _runtime.World.Vehicle=null;
            OnRentalClosed(); TryCacheWorld();
            var order=engine.FindOrder(closed.Id!)!;
            _log("Recovery reason="+reason+" rental="+rental.RentalId+" order="+closed.Id+" total="+order.Total);
            Notify(reason==CloseReason.ReportedLost
                ? SettleOrder(closed.Id!,order.Total,"车辆已回收，账单含2000元回收赔偿")
                : SettleOrder(closed.Id!,order.Total,"车辆恢复失败，系统已回收；未追加赔偿"),true);
        }

        private string SettleOrder(string orderId,long total,string prefix)
        {
            try
            {
                var result=_runtime.SettleOrder(orderId);
                _log("Settle order="+orderId+" code="+result.Code);
                switch (result.Code)
                {
                    case RuleCode.Success:
                        if (_settings.Ui.Sounds) _sounds.PaidChime();
                        return prefix+"，本次费用"+total+"元已自动扣款。";
                    case RuleCode.InsufficientCash:
                        if (_settings.Ui.Sounds) _sounds.ShortChime();
                        return prefix+"，本次费用"+total+"元；现金不足，请在站点或马蜂自提点补付。";
                    case RuleCode.PaymentBusy:
                        return prefix+"，本次费用"+total+"元；当前无法立即扣款，请在站点或马蜂自提点补付。";
                    default:
                        return prefix+"，本次费用"+total+"元；扣款结果待确认，订单已保留，请稍后补付。";
                }
            }
            catch (Exception ex)
            {
                _log("Settle interrupted order="+orderId+": "+ex);
                return prefix+"，本次费用"+total+"元；扣款结果待确认，订单已保留，请稍后补付。";
            }
        }
        private void RecoverWorldFault(Exception ex)
        {
            if (_worldFailed) return;
            _log("World prototype failure: "+ex);
            try { CloseRecovery(CloseReason.SystemRecovery); } catch (Exception recovery) { _log("Recovery pending: "+recovery); }
            DestroyActors(); _rider=null; _transitionUntil=0; _worldFailed=true;
            Notify("单车场景发生异常，已停止骑行，请查看模组日志。",true);
        }
        private void TryCacheWorld()
        {
            // A transient save error must not turn a valid vehicle into a recovery invoice.
            try { _runtime.Persist(false); }
            catch (Exception ex) { _log("World cache pending: "+ex); Notify("保存暂未完成，将自动重试；当前车辆和订单已保留。",true); }
        }
        private void DestroyBike()
        {
            EndRiderPresentation();
            if (_bikeMarker!=null) { _markers.Remove(_bikeMarker); UnityEngine.Object.Destroy(_bikeMarker); _bikeMarker=null; }
            if (_bike!=null) UnityEngine.Object.Destroy(_bike); _bike=null;
        }
        private void DestroyActors()
        {
            DestroyBike(); _bikeMarker=null; foreach (var station in _stations.Values) if (station!=null) UnityEngine.Object.Destroy(station);
            _stations.Clear(); foreach (var marker in _markers) if (marker!=null) UnityEngine.Object.Destroy(marker); _markers.Clear();
        }
        private Key ParseKey(string name,Key fallback,string label)
        {
            if (Enum.TryParse<Key>((name??"").Trim(),true,out var parsed) && parsed!=Key.None) return parsed;
            _log("Unknown key binding "+label+"='"+name+"'; using "+fallback+".");
            return fallback;
        }
        private static string KeyLabel(Key key) => key.ToString();

        internal void Notify(string text,bool force=false)
        {
            if (!force && Time.unscaledTime<_nextNotice) return;
            _nextNotice=Time.unscaledTime+2; _toast=text; _toastUntil=Time.unscaledTime+5;
        }
        public void Dispose()
        {
            CapturePose(); var rental=_runtime.Engine?.CurrentRental;
            if (rental!=null) _runtime.Engine!.SetRiding(rental.VehicleId,false);
            _runtime.BeforeCache-=CapturePose; _runtime.SlotChanged-=ResetWorld; _runtime.RentalClosed-=OnRentalClosed;
            SceneLoader.onStartedLoadingScene-=BeforeLoading;
            MultiSceneCore.OnSubSceneWillBeUnloaded-=BeforeSubSceneUnload;
            _sounds.Dispose();
            DestroyActors(); _rider=null; _transitionUntil=0; _riderPose.Dispose(); _assets.Dispose();
        }

    }
}
