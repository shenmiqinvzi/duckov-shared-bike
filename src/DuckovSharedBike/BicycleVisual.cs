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
        // 地狱火余烬：共享自发光材质 + 一盏点光，用两条低频正弦做呼吸。
        private Material? _emberMaterial;
        private Material? _emberDim;
        private Light? _emberLight;
        private Color _emberColor=new Color(1f,0.42f,0.14f);
        private bool _emberHasEmission;
        private bool _emberDimHasEmission;
        internal Transform? Seat {get;private set;}
        internal Transform[] Pedals=>_pedals;
        internal Transform[] Grips {get;private set;}=Array.Empty<Transform>();

        internal void Configure(Transform[] wheels,Transform crank,Transform[] pedals,Transform seat,Transform[] grips)
        { _wheels=wheels; _crank=crank; _pedals=pedals; Seat=seat; Grips=grips; }

        internal void ConfigureFire(Material? ember,Material? emberDim,Light? light,Color color)
        {
            _emberMaterial=ember;
            _emberDim=emberDim;
            _emberLight=light;
            _emberColor=color;
            _emberHasEmission=ember!=null && ember.HasProperty("_EmissionColor");
            _emberDimHasEmission=emberDim!=null && emberDim.HasProperty("_EmissionColor");
        }

        private void Update()
        {
            var pump=0.78f+0.16f*Mathf.Sin(Time.unscaledTime*2.7f)+0.06f*Mathf.Sin(Time.unscaledTime*6.9f+0.7f);
            if (_emberHasEmission && _emberMaterial!=null)
                _emberMaterial.SetColor("_EmissionColor",_emberColor*(1.6f+pump*1.1f));
            if (_emberDimHasEmission && _emberDim!=null)
                _emberDim.SetColor("_EmissionColor",new Color(0.72f,0.18f,0.05f,1f)*(1.5f+pump*0.9f));
            if (_emberLight!=null) _emberLight.intensity=0.45f+pump*0.5f;
        }

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
