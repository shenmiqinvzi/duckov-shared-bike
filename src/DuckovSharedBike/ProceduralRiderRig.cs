using System;
using System.Collections.Generic;
using System.Linq;
using DuckovSharedBike.Domain;
using UnityEngine;

namespace DuckovSharedBike
{
    internal sealed class ProceduralRiderRig : IDisposable
    {
        private readonly CharacterModel _model;
        private readonly Transform _hip,_spine,_chest;
        private readonly Limb[] _legs,_arms;
        private readonly Dictionary<Transform,BoneState> _bones=new Dictionary<Transform,BoneState>();
        private readonly Vector3 _basePosition;
        private Vector3 _writtenPosition;
        private bool _positionWritten;
        private DuckovItemAgent? _agent;
        private readonly List<KeyValuePair<Renderer,bool>> _hidden=new List<KeyValuePair<Renderer,bool>>();

        private readonly struct BoneState
        {
            internal readonly Quaternion Before,Written;
            internal BoneState(Quaternion before,Quaternion written) { Before=before; Written=written; }
        }
        private readonly struct Limb
        {
            internal readonly Transform Upper,Lower,End;
            internal Limb(Transform upper,Transform lower,Transform end) { Upper=upper; Lower=lower; End=end; }
        }

        private ProceduralRiderRig(CharacterModel model,Dictionary<string,Transform> bones)
        {
            _model=model; _basePosition=model.transform.localPosition;
            _hip=bones["Hip"]; _spine=bones["Spine.001"]; _chest=bones["Spine.003"];
            _legs=new[] { new Limb(bones["Leg.Upper.L"],bones["Leg.Lower.L"],bones["Foot.L.001"]),
                new Limb(bones["Leg.Upper.R"],bones["Leg.Lower.R"],bones["Foot.R.001"]) };
            _arms=new[] { new Limb(bones["Arm.Upper.L"],bones["Arm.Fore.L"],bones["Hand.L"]),
                new Limb(bones["Arm.Upper.R"],bones["Arm.Fore.R"],bones["Hand.R"]) };
        }

        internal static ProceduralRiderRig? TryBind(CharacterModel model)
        {
            var required=new[] { "Hip","Spine.001","Spine.003","Leg.Upper.L","Leg.Lower.L","Foot.L.001",
                "Leg.Upper.R","Leg.Lower.R","Foot.R.001","Arm.Upper.L","Arm.Fore.L","Hand.L","Arm.Upper.R","Arm.Fore.R","Hand.R" };
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones=renderer.bones.Where(b=>b!=null).GroupBy(b=>b.name).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);
                if (!required.All(bones.ContainsKey)) continue;
                return new ProceduralRiderRig(model,bones);
            }
            return null;
        }

        internal void Apply(CharacterMainControl actor,BicycleVisual bike,float heightOffset)
        {
            if (_model==null || _hip==null || bike.Seat==null || bike.Pedals.Length!=2 || bike.Grips.Length!=2) return;
            RestoreFrame();
            var controlled=new[] { _spine,_legs[0].Upper,_legs[0].Lower,_legs[0].End,_legs[1].Upper,_legs[1].Lower,_legs[1].End,
                _arms[0].Upper,_arms[0].Lower,_arms[0].End,_arms[1].Upper,_arms[1].Lower,_arms[1].End };
            foreach (var bone in controlled) _bones[bone]=new BoneState(bone.localRotation,bone.localRotation);
            try
            {
            var desired=bike.Seat.position+Vector3.up*heightOffset;
            _model.transform.position+=desired-_hip.position;
            _writtenPosition=_model.transform.localPosition; _positionWritten=true;
            var torso=_chest.position-_hip.position;
            var lean=(Vector3.up*Mathf.Cos(20*Mathf.Deg2Rad)+bike.transform.forward*Mathf.Sin(20*Mathf.Deg2Rad)).normalized;
            if (torso.sqrMagnitude>0.001f) _spine.rotation=Quaternion.FromToRotation(torso,lean)*_spine.rotation;
            for (var i=0;i<2;i++)
            {
                var side=i==0 ? -1f : 1f;
                var footRotation=_legs[i].End.rotation;
                if (_legs[i].End.childCount>0)
                {
                    var toe=_legs[i].End.GetChild(0).position-_legs[i].End.position;
                    if (toe.sqrMagnitude>0.0001f) footRotation=Quaternion.FromToRotation(toe,bike.transform.forward)*footRotation;
                }
                Solve(_legs[i],bike.Pedals[i].position+bike.transform.up*0.035f,
                    _legs[i].Upper.position+bike.transform.forward*0.4f,footRotation);
                var handRotation=_arms[i].End.rotation;
                Solve(_arms[i],bike.Grips[i].position,_arms[i].Upper.position+bike.transform.right*side*0.3f-Vector3.up*0.2f,handRotation);
            }
            HideHeldWeapon(actor.CurrentHoldItemAgent);
            }
            finally
            {
                foreach (var bone in controlled) if (bone!=null) _bones[bone]=new BoneState(_bones[bone].Before,bone.localRotation);
            }
        }

        private static void Solve(Limb limb,Vector3 target,Vector3 hint,Quaternion endRotation)
        {
            var upper=Vector3.Distance(limb.Upper.position,limb.Lower.position);
            var lower=Vector3.Distance(limb.Lower.position,limb.End.position);
            var solution=TwoBoneGeometry.Solve(SafeGround.Point(limb.Upper.position),SafeGround.Point(target),SafeGround.Point(hint),upper,lower);
            limb.Upper.rotation=Quaternion.FromToRotation(limb.Lower.position-limb.Upper.position,SafeGround.Vector(solution.Joint)-limb.Upper.position)*limb.Upper.rotation;
            limb.Lower.rotation=Quaternion.FromToRotation(limb.End.position-limb.Lower.position,SafeGround.Vector(solution.End)-limb.Lower.position)*limb.Lower.rotation;
            limb.End.rotation=endRotation;
        }

        private void HideHeldWeapon(DuckovItemAgent? agent)
        {
            if (_agent==agent) return;
            RestoreWeapon(); _agent=agent;
            if (agent==null) return;
            foreach (var renderer in agent.GetComponentsInChildren<Renderer>(true))
            {
                _hidden.Add(new KeyValuePair<Renderer,bool>(renderer,renderer.forceRenderingOff)); renderer.forceRenderingOff=true;
            }
        }
        private void RestoreWeapon()
        {
            foreach (var entry in _hidden) if (entry.Key!=null && entry.Key.forceRenderingOff) entry.Key.forceRenderingOff=entry.Value;
            _hidden.Clear(); _agent=null;
        }
        private void RestoreFrame()
        {
            foreach (var entry in _bones)
                if (entry.Key!=null && Quaternion.Angle(entry.Key.localRotation,entry.Value.Written)<0.001f) entry.Key.localRotation=entry.Value.Before;
            _bones.Clear();
            if (_positionWritten && _model!=null && (_model.transform.localPosition-_writtenPosition).sqrMagnitude<0.000001f)
                _model.transform.localPosition=_basePosition;
            _positionWritten=false;
        }
        public void Dispose() { RestoreFrame(); RestoreWeapon(); }
    }
}
