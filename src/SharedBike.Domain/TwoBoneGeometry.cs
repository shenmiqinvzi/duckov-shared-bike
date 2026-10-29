using System;

namespace DuckovSharedBike.Domain
{
    public readonly struct LimbSolution
    {
        public WorldPoint Joint {get;}
        public WorldPoint End {get;}
        public LimbSolution(WorldPoint joint,WorldPoint end) { Joint=joint; End=end; }
    }
    public static class TwoBoneGeometry
    {
        public static LimbSolution Solve(WorldPoint root,WorldPoint target,WorldPoint bendHint,double upperLength,double lowerLength)
        {
            if (root==null || target==null || bendHint==null || !root.IsFinite || !target.IsFinite || !bendHint.IsFinite
                || double.IsNaN(upperLength) || double.IsInfinity(upperLength) || double.IsNaN(lowerLength) || double.IsInfinity(lowerLength)
                || upperLength<0.0001 || lowerLength<0.0001 || upperLength>100 || lowerLength>100) throw new ArgumentException("Invalid limb geometry.");
            var direction=new V((double)target.X-root.X,(double)target.Y-root.Y,(double)target.Z-root.Z);
            var distance=direction.Length;
            direction=distance>0.000001 ? direction*(1/distance) : new V(0,-1,0);
            var bend=new V((double)bendHint.X-root.X,(double)bendHint.Y-root.Y,(double)bendHint.Z-root.Z);
            bend=bend-direction*V.Dot(bend,direction);
            if (bend.Length<0.000001)
            {
                var fallback=Math.Abs(direction.X)<0.8 ? new V(1,0,0) : new V(0,0,1);
                bend=fallback-direction*V.Dot(fallback,direction);
            }
            bend=bend*(1/bend.Length);
            var epsilon=Math.Min(upperLength,lowerLength)*0.001;
            distance=Math.Max(Math.Abs(upperLength-lowerLength)+epsilon,Math.Min(upperLength+lowerLength-epsilon,distance));
            var along=(distance*distance+upperLength*upperLength-lowerLength*lowerLength)/(2*distance);
            var sideways=Math.Sqrt(Math.Max(0,upperLength*upperLength-along*along));
            var joint=direction*along+bend*sideways; var end=direction*distance;
            return new LimbSolution(joint.Point(root),end.Point(root));
        }
        private readonly struct V
        {
            private readonly double _x,_y,_z;
            internal V(double x,double y,double z) { _x=x; _y=y; _z=z; }
            internal double Length=>Math.Sqrt(_x*_x+_y*_y+_z*_z);
            internal double X=>_x;
            internal static double Dot(V a,V b)=>a._x*b._x+a._y*b._y+a._z*b._z;
            public static V operator +(V a,V b)=>new V(a._x+b._x,a._y+b._y,a._z+b._z);
            public static V operator -(V a,V b)=>new V(a._x-b._x,a._y-b._y,a._z-b._z);
            public static V operator *(V a,double scale)=>new V(a._x*scale,a._y*scale,a._z*scale);
            internal WorldPoint Point(WorldPoint origin)=>new WorldPoint { X=(float)(origin.X+_x),Y=(float)(origin.Y+_y),Z=(float)(origin.Z+_z) };
        }
    }
}
