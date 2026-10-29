using System;
using System.Linq;
using DuckovSharedBike.Domain;
using UnityEngine;

namespace DuckovSharedBike
{
    internal sealed class NativeRiderPose : IDisposable
    {
        private readonly RidingPresentationSettings _settings;
        private readonly Action<string> _log;
        private CharacterMainControl? _actor;
        private CharacterModel? _model;
        private Animator? _animator;
        private Vector3 _originalPosition;
        private Vector3 _lastPosition;
        private int _type;
        private int _lastType;
        private int _hash;
        private float _nextProbe;
        private bool _wrotePosition;
        private bool _wroteType;
        private bool _unavailableLogged;
        private bool _failed;
        private ProceduralRiderRig? _rig;

        internal NativeRiderPose(RidingPresentationSettings settings,Action<string> log) { _settings=settings; _log=log; }
        internal string Status => !_settings.NativeRidePose ? "骑行姿态适配已关闭"
            : _failed ? "骑行姿态适配异常，沿用原姿态"
            : _actor==null ? "骑行姿态：尚未上车"
            : _rig!=null ? "骑行姿态：坐姿、握把与踩踏已接入"
            : _type>0 ? "骑行姿态：原生坐姿（当前骨架未适配踩踏）"
            : "骑行姿态：资源未就绪，沿用原姿态";

        internal void Begin(CharacterMainControl actor)
        {
            Restore(); _actor=actor; _failed=false; _nextProbe=0; _unavailableLogged=false;
        }

        internal void ApplyAnimation(CharacterAnimationControl_MagicBlend control)
        {
            if (!_settings.NativeRidePose || _failed || _actor==null || control.characterMainControl!=_actor) return;
            try
            {
                if (_model!=_actor.characterModel || _animator!=control.animator)
                {
                    RestoreModel(); _model=_actor.characterModel; _animator=control.animator; _type=0; _nextProbe=0;
                    if (_model!=null) _originalPosition=_model.transform.localPosition;
                    if (_model!=null && _settings.PedalAndGripPose) _rig=ProceduralRiderRig.TryBind(_model);
                }
                if (_model==null || _animator==null || !_animator.isActiveAndEnabled) return;
                if (_type==0 && Time.unscaledTime>=_nextProbe)
                {
                    _nextProbe=Time.unscaledTime+5;
                    _hash=Animator.StringToHash("VehicleType");
                    if (_animator.parameters.Any(p=>p.nameHash==_hash && p.type==AnimatorControllerParameterType.Int))
                        _type=_settings.NativeRidePoseValue>0 ? _settings.NativeRidePoseValue : ResolveNativePose(_animator);
                    if (_type>0) _log("Native rider pose ready type="+_type+" model="+_model.name);
                    else if (!_unavailableLogged)
                    {
                        _unavailableLogged=true;
                        _log("Native rider pose unavailable; retaining original model and retrying loaded-resource discovery.");
                    }
                }
                if (_type==0) return;
                _animator.SetInteger(_hash,_type); _lastType=_type; _wroteType=true;
                var hand=Animator.StringToHash("HandState");
                if (_animator.parameters.Any(p=>p.nameHash==hand && p.type==AnimatorControllerParameterType.Int)) _animator.SetInteger(hand,0);
            }
            catch (Exception ex) { Disable(ex); }
        }

        internal void LateTick(BicycleVisual? bike)
        {
            if (_failed || _model==null || _actor==null || _model!=_actor.characterModel) return;
            try
            {
                if (_rig!=null && bike!=null) { _rig.Apply(_actor,bike,_settings.RiderVisualHeight); return; }
                if (_type==0 || !_wroteType) return;
                _lastPosition=_originalPosition+Vector3.up*_settings.RiderVisualHeight;
                _model.transform.localPosition=_lastPosition; _wrotePosition=true;
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static int ResolveNativePose(Animator animator)
        {
            // Verified in this game's serialized controller: Riding is entered when VehicleType > 0.
            if (animator.HasState(0,Animator.StringToHash("Base Layer.Riding"))
                && animator.runtimeAnimatorController!=null && animator.runtimeAnimatorController.animationClips.Any(c=>c!=null && c.name=="Armature_Riding")) return 1;
            // Read only assets already loaded by the game; never instantiate or switch native vehicles.
            foreach (var motor in Resources.FindObjectsOfTypeAll<CharacterAnimationControl_Moto>())
            {
                if (motor==null) continue;
                var owner=motor.characterMainControl;
                if (owner==null && motor.characterModel!=null) owner=motor.characterModel.characterMainControl;
                if (owner==null) owner=motor.GetComponentInParent<CharacterMainControl>();
                if (owner!=null && owner.isVehicle && owner.vehicleAnimationType>0) return owner.vehicleAnimationType;
            }
            return 0;
        }

        private void RestoreModel()
        {
            _rig?.Dispose(); _rig=null;
            // Only restore values still owned by this adapter, preserving later changes by other code.
            if (_wroteType && _animator!=null && _animator.GetInteger(_hash)==_lastType)
                _animator.SetInteger(_hash,_actor!=null ? _actor.ridingVehicleType : 0);
            if (_wrotePosition && _model!=null && (_model.transform.localPosition-_lastPosition).sqrMagnitude<0.000001f)
                _model.transform.localPosition=_originalPosition;
            _wroteType=false; _wrotePosition=false;
        }

        internal void Restore()
        {
            try { RestoreModel(); }
            catch (Exception ex) { _log("Rider pose restore interrupted: "+ex); }
            finally { _actor=null; _model=null; _animator=null; _type=0; }
        }

        private void Disable(Exception ex)
        {
            Restore(); _failed=true;
            _log("Rider decoration disabled; rental gameplay retained: "+ex);
        }
        public void Dispose()=>Restore();
    }
}
