using System;
using DuckovSharedBike.Domain;
using UnityEngine;

namespace DuckovSharedBike
{
    public sealed class BicycleVisual : MonoBehaviour
    {
        private Transform[] _wheels=Array.Empty<Transform>();
        private Transform? _crank;
        private Transform[] _pedals=Array.Empty<Transform>();
        private readonly RollingKinematics _rolling=new RollingKinematics();
        private Vector3 _previous;
        private bool _tracking;
        internal Transform? Seat {get;private set;}
        internal Transform[] Pedals=>_pedals;
        internal Transform[] Grips {get;private set;}=Array.Empty<Transform>();

        internal void Configure(Transform[] wheels,Transform crank,Transform[] pedals,Transform seat,Transform[] grips)
        { _wheels=wheels; _crank=crank; _pedals=pedals; Seat=seat; Grips=grips; }

        internal void Track(bool riding)
        {
            var current=transform.position;
            if (_tracking && riding && Time.timeScale>0)
            {
                var delta=current-_previous; delta.y=0;
                var signed=delta.magnitude*(Vector3.Dot(delta,transform.forward)<0 ? -1 : 1);
                _rolling.Advance(signed,true);
            }
            _previous=current; _tracking=riding;
            var wheelRotation=Quaternion.AngleAxis((float)_rolling.WheelDegrees,Vector3.right);
            foreach (var wheel in _wheels) if (wheel!=null) wheel.localRotation=wheelRotation;
            var crankRotation=Quaternion.AngleAxis((float)_rolling.PedalDegrees,Vector3.right);
            if (_crank!=null) _crank.localRotation=crankRotation;
            foreach (var pedal in _pedals) if (pedal!=null) pedal.localRotation=Quaternion.Inverse(crankRotation);
        }
    }
}
