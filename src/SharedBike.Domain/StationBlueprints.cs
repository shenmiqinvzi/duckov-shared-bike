using System;
using System.Collections.Generic;
using System.Linq;

namespace DuckovSharedBike.Domain
{
    public sealed class StationSite
    {
        public string Id {get;set;}="";
        public string Name {get;set;}="";
        public string MapId {get;set;}="";
        public string SceneName {get;set;}="";
        public WorldPoint Anchor {get;set;}=new WorldPoint();
        public float SearchRadius {get;set;}=22;
        public float Yaw {get;set;}
    }
    public sealed class MapRoute
    {
        public string SourceMap {get;set;}="";
        public string TargetMap {get;set;}="";
        public bool Returnable {get;set;}
    }
    public sealed class StationBlueprints
    {
        public int SchemaVersion {get;set;}=1;
        public List<StationSite> Sites {get;set;}=new List<StationSite>();
        public List<MapRoute> Routes {get;set;}=new List<MapRoute>();
        public void Validate()
        {
            if (SchemaVersion!=1 || Sites==null || Routes==null || Sites.Select(s=>s?.Id).Distinct().Count()!=Sites.Count)
                throw new InvalidOperationException("Invalid station blueprints.");
            foreach (var site in Sites)
                if (site==null || string.IsNullOrWhiteSpace(site.Id) || string.IsNullOrWhiteSpace(site.Name)
                    || string.IsNullOrWhiteSpace(site.MapId) || string.IsNullOrWhiteSpace(site.SceneName)
                    || site.Anchor==null || !site.Anchor.IsFinite || !Finite(site.SearchRadius) || site.SearchRadius<6 || site.SearchRadius>40 || !Finite(site.Yaw))
                    throw new InvalidOperationException("Invalid station site.");
            foreach (var route in Routes)
                if (route==null || string.IsNullOrWhiteSpace(route.SourceMap) || string.IsNullOrWhiteSpace(route.TargetMap)
                    || route.SourceMap==route.TargetMap) throw new InvalidOperationException("Invalid map route.");
            if (Routes.Select(r=>r.SourceMap+"\n"+r.TargetMap).Distinct().Count()!=Routes.Count)
                throw new InvalidOperationException("Duplicate map routes.");
        }
        public bool CanLeaveAndReturn(string source,string target)=>Routes.Any(r=>r.SourceMap==source && r.TargetMap==target && r.Returnable)
            && Routes.Any(r=>r.SourceMap==target && r.TargetMap==source && r.Returnable);
        private static bool Finite(float value)=>!float.IsNaN(value) && !float.IsInfinity(value);
    }
}
