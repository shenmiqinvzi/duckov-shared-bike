using DuckovSharedBike.Domain;
using UnityEngine;

namespace DuckovSharedBike
{
    internal static class BicycleClearance
    {
        private static readonly float[] Heights={0.3f,0.73f};
        private static readonly Collider[] Colliders=new Collider[64];
        private static readonly RaycastHit[] Hits=new RaycastHit[64];
        internal static bool Fits(Vector3 position,Quaternion rotation,CharacterMainControl actor)
        {
            foreach (var height in Heights)
            {
                Endpoints(position,rotation,height,out var back,out var front);
                var count=Physics.OverlapCapsuleNonAlloc(back,front,BicycleGeometry.BodyRadius,Colliders,~0,QueryTriggerInteraction.Ignore);
                if (count==Colliders.Length) return false;
                for (var i=0;i<count;i++) if (!Ignore(Colliders[i],actor)) return false;
            }
            return true;
        }

        internal static bool CanTravel(Vector3 from,Quaternion fromRotation,Vector3 to,Quaternion toRotation,CharacterMainControl actor)
        {
            if (!Fits(to,toRotation,actor)) return false;
            var delta=to-from; var distance=delta.magnitude;
            if (distance<0.0001f) return true;
            foreach (var height in Heights)
            {
                Endpoints(from,fromRotation,height,out var back,out var front);
                var count=Physics.CapsuleCastNonAlloc(back,front,BicycleGeometry.BodyRadius,delta/distance,Hits,distance,~0,QueryTriggerInteraction.Ignore);
                if (count==Hits.Length) return false;
                for (var i=0;i<count;i++) if (!Ignore(Hits[i].collider,actor)) return false;
            }
            // Rotation is limited to a small per-frame turn. Samples cover its arc between clear endpoints.
            var steps=Mathf.CeilToInt(Quaternion.Angle(fromRotation,toRotation)/10);
            for (var i=1;i<steps;i++)
                if (!Fits(Vector3.Lerp(from,to,(float)i/steps),Quaternion.Slerp(fromRotation,toRotation,(float)i/steps),actor)) return false;
            return true;
        }

        private static bool Ignore(Collider collider,CharacterMainControl actor)
            => collider==null || collider.transform.IsChildOf(actor.transform);
        private static void Endpoints(Vector3 position,Quaternion rotation,float height,out Vector3 back,out Vector3 front)
        {
            var halfAxis=BicycleGeometry.HalfWheelbase+BicycleGeometry.WheelRadius-BicycleGeometry.BodyRadius;
            var forward=rotation*Vector3.forward*halfAxis;
            var center=position+rotation*Vector3.up*height;
            back=center-forward; front=center+forward;
        }
    }
}
