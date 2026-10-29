namespace DuckovSharedBike.Domain
{
    public enum TravelDecision { Allowed, ReturnVehicle, Dismount, NoReturnRoute }
    public static class TravelPolicy
    {
        public static TravelDecision Evaluate(bool hasVehicle,bool riding,bool extraction,bool sameMap,bool returnable)
        {
            if (!hasVehicle) return TravelDecision.Allowed;
            if (extraction) return TravelDecision.ReturnVehicle;
            if (riding) return TravelDecision.Dismount;
            return sameMap || returnable ? TravelDecision.Allowed : TravelDecision.NoReturnRoute;
        }
    }
}
