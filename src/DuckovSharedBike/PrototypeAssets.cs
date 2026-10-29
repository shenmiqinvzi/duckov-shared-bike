using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DuckovSharedBike.Domain;
using UnityEngine;
using UnityEngine.AI;

namespace DuckovSharedBike
{
    internal sealed class PrototypeAssets : IDisposable
    {
        private readonly Action<string> _log;
        private Material? _frame;
        private Material? _dark;
        private Material? _metal;
        private Material? _sign;
        private Material? _glow;
        private Material? _glowRed;
        private bool _materialsReady;
        private Texture2D? _iconTexture;
        private Sprite? _iconFallback;
        internal Sprite? Icon { get; private set; }

        internal PrototypeAssets(string modPath, Action<string> log)
        {
            _log = log;
            var path = Path.Combine(modPath, "preview.png");
            if (File.Exists(path))
            {
                try
                {
                    _iconTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (ImageConversion.LoadImage(_iconTexture, File.ReadAllBytes(path)))
                        Icon = Sprite.Create(_iconTexture, new Rect(0, 0, _iconTexture.width, _iconTexture.height), new Vector2(0.5f, 0.5f));
                }
                catch (Exception ex) { _log("Preview icon unavailable: " + ex.Message); }
            }
            // A tiny generated icon keeps map markers visible even if preview.png is unreadable.
            _iconFallback = CreateFallbackIcon();
            if (Icon == null) Icon = _iconFallback;
        }

        private void EnsureMaterials()
        {
            if (_materialsReady) return;
            _materialsReady = true;
            var template = FindTemplateMaterial();
            if (template != null)
            {
                _frame = Tint(template, new Color(0.2f, 0.86f, 0.58f));
                _dark = Tint(template, new Color(0.08f, 0.1f, 0.12f));
                _metal = Tint(template, new Color(0.63f, 0.69f, 0.72f));
                _sign = Tint(template, new Color(0.96f, 0.8f, 0.24f));
                _glow = Glow(template, new Color(0.55f, 0.95f, 1f), 1.6f);
                _glowRed = Glow(template, new Color(1f, 0.18f, 0.14f), 1.6f);
                _log("Visuals use game material template shader=" + template.shader.name + " template=" + template.name);
                return;
            }
            var shader = FindShader();
            if (shader == null) { _log("Visuals unavailable: no compatible shader found."); return; }
            _frame = MakeMaterial(shader, new Color(0.2f, 0.86f, 0.58f));
            _dark = MakeMaterial(shader, new Color(0.08f, 0.1f, 0.12f));
            _metal = MakeMaterial(shader, new Color(0.63f, 0.69f, 0.72f));
            _sign = MakeMaterial(shader, new Color(0.96f, 0.8f, 0.24f));
            _glow = MakeGlow(shader, new Color(0.55f, 0.95f, 1f), 1.6f);
            _glowRed = MakeGlow(shader, new Color(1f, 0.18f, 0.14f), 1.6f);
            _log("Visuals use shader=" + shader.name + " (no game material template found)");
        }

        private static Shader? FindShader()
        {
            foreach (var name in new[]
            {
                "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Simple Lit",
                "Standard", "Unlit/Color", "Sprites/Default", "UI/Default"
            })
            {
                var shader = Shader.Find(name);
                if (shader != null) return shader;
            }
            return null;
        }

        private static Material? FindTemplateMaterial()
        {
            // Cloning a material the game already renders is safer than guessing a shader name in a built player.
            var actor = CharacterMainControl.Main;
            if (actor != null && actor.characterModel != null)
            {
                foreach (var renderer in actor.characterModel.GetComponentsInChildren<Renderer>(true))
                    if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null)
                        return renderer.sharedMaterial;
            }
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null) continue;
                if (!renderer.gameObject.scene.IsValid() || !renderer.gameObject.scene.isLoaded) continue;
                return renderer.sharedMaterial;
            }
            return null;
        }

        private static Material Tint(Material template, Color color)
        {
            var material = new Material(template) { hideFlags = HideFlags.DontSave };
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", null);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 0.35f);
            }
            return material;
        }

        private static Material MakeMaterial(Shader shader, Color color)
        {
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            return material;
        }

        // Strong emissive material for lights/beacons; falls back to a flat bright color.
        private static Material Glow(Material template, Color color, float intensity)
        {
            var material = Tint(template, color);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * intensity);
            }
            return material;
        }

        private static Material MakeGlow(Shader shader, Color color, float intensity)
        {
            var material = MakeMaterial(shader, color);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * intensity);
            }
            return material;
        }

        internal GameObject Bike(Vector3 position, float yaw)
        {
            EnsureMaterials();
            var root = new GameObject("SharedBike.Vehicle");
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            var wheels=new List<Transform>();
            foreach (var z in new[] { -BicycleGeometry.HalfWheelbase, BicycleGeometry.HalfWheelbase })
            {
                var assembly=new GameObject("Wheel"); assembly.transform.SetParent(root.transform,false);
                assembly.transform.localPosition=new Vector3(0,BicycleGeometry.WheelRadius,z);
                wheels.Add(assembly.transform);
                // Thin rim segments leave the wheel open and make rolling visible.
                for (var segment=0;segment<24;segment++)
                {
                    var a=segment*Mathf.PI*2/24; var b=(segment+1)*Mathf.PI*2/24;
                    Bar(assembly,new Vector3(0,Mathf.Sin(a)*0.235f,Mathf.Cos(a)*0.235f),
                        new Vector3(0,Mathf.Sin(b)*0.235f,Mathf.Cos(b)*0.235f),_dark,0.05f);
                }
                for (var spoke=0;spoke<8;spoke++)
                {
                    var angle=spoke*Mathf.PI*2/8;
                    Bar(assembly,Vector3.zero,new Vector3(0,Mathf.Sin(angle)*0.22f,Mathf.Cos(angle)*0.22f),_metal,0.012f);
                }
                var wheel = Shape(assembly, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.14f, 0.06f, 0.14f), _metal);
                wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
            }
            Bar(root,new Vector3(0,0.26f,-0.42f),new Vector3(0,0.55f,-0.11f),_frame);
            Bar(root,new Vector3(0,0.55f,-0.11f),new Vector3(0,0.32f,0.12f),_frame);
            Bar(root,new Vector3(0,0.32f,0.12f),new Vector3(0,0.26f,-0.42f),_frame);
            Bar(root,new Vector3(0,0.55f,-0.11f),new Vector3(0,0.72f,0.28f),_frame);
            Bar(root,new Vector3(0,0.72f,0.28f),new Vector3(0,0.32f,0.12f),_frame);
            Bar(root,new Vector3(0,0.26f,0.42f),new Vector3(0,0.94f,0.22f),_metal);
            Bar(root,new Vector3(-0.19f,0.94f,0.22f),new Vector3(0.19f,0.94f,0.22f),_dark);
            Bar(root,new Vector3(0,0.55f,-0.11f),new Vector3(0,0.66f,-0.11f),_metal);
            Shape(root,PrimitiveType.Cube,new Vector3(0,0.66f,-0.11f),new Vector3(0.26f,0.065f,0.22f),_dark);
            var crank=new GameObject("PedalCrank"); crank.transform.SetParent(root.transform,false);
            crank.transform.localPosition=new Vector3(0,0.59f,-0.12f);
            Bar(crank,new Vector3(-0.14f,-0.05f,0),new Vector3(0.14f,0.05f,0),_metal);
            var pedals=new[] {
                Shape(crank,PrimitiveType.Cube,new Vector3(-0.17f,-0.05f,0),new Vector3(0.14f,0.035f,0.13f),_dark).transform,
                Shape(crank,PrimitiveType.Cube,new Vector3(0.17f,0.05f,0),new Vector3(0.14f,0.035f,0.13f),_dark).transform
            };
            // A small basket and rear reflector distinguish the shared bicycle silhouette.
            Shape(root,PrimitiveType.Cube,new Vector3(0,0.73f,0.43f),new Vector3(0.34f,0.03f,0.22f),_frame);
            foreach (var x in new[] { -0.16f,0.16f })
            {
                Bar(root,new Vector3(x,0.73f,0.33f),new Vector3(x,0.87f,0.33f),_metal);
                Bar(root,new Vector3(x,0.73f,0.53f),new Vector3(x,0.87f,0.53f),_metal);
                Bar(root,new Vector3(x,0.87f,0.33f),new Vector3(x,0.87f,0.53f),_frame);
            }
            Bar(root,new Vector3(-0.16f,0.87f,0.53f),new Vector3(0.16f,0.87f,0.53f),_frame);
            Shape(root,PrimitiveType.Cube,new Vector3(0,0.48f,-0.52f),new Vector3(0.11f,0.065f,0.03f),_sign);
            Decorate(root);
            var seat=Anchor(root,"RiderSeat",new Vector3(0,0.60f,-0.11f));
            var grips=new[] { Anchor(root,"Grip.Left",new Vector3(-0.19f,0.94f,0.22f)),Anchor(root,"Grip.Right",new Vector3(0.19f,0.94f,0.22f)) };
            root.AddComponent<BicycleVisual>().Configure(wheels.ToArray(),crank.transform,pedals,seat,grips);
            ApplyVisibleLayer(root, position);
            return root;
        }

        // Bold accents (mudguards, headlight, flag, glow stripes) make the bike pop at distance.
        private void Decorate(GameObject root)
        {
            if (_glow==null) return;
            // Curved mudguards arcing over the top of both wheels.
            foreach (var z in new[] { -BicycleGeometry.HalfWheelbase, BicycleGeometry.HalfWheelbase })
                for (var segment=0;segment<5;segment++)
                {
                    var a=0.25f+segment*0.32f; var b=0.25f+(segment+1)*0.32f;
                    Bar(root,new Vector3(0,0.26f+Mathf.Sin(a)*0.31f,z+Mathf.Cos(a)*0.31f),
                        new Vector3(0,0.26f+Mathf.Sin(b)*0.31f,z+Mathf.Cos(b)*0.31f),_frame,0.06f);
                }
            // Front headlight and a white ring so the front is unmistakable.
            Shape(root,PrimitiveType.Sphere,new Vector3(0,0.99f,0.32f),new Vector3(0.1f,0.1f,0.1f),_glow);
            Shape(root,PrimitiveType.Cylinder,new Vector3(0,0.94f,0.24f),new Vector3(0.05f,0.05f,0.05f),_metal)
                .transform.localRotation=Quaternion.Euler(90,0,0);
            // Tail light: bright red, larger than the reflector.
            Shape(root,PrimitiveType.Cube,new Vector3(0,0.56f,-0.55f),new Vector3(0.16f,0.09f,0.03f),_glowRed);
            // High-visibility flag on a whip antenna.
            Bar(root,new Vector3(0,0.55f,-0.44f),new Vector3(0,1.35f,-0.58f),_metal,0.012f);
            Shape(root,PrimitiveType.Cube,new Vector3(0,1.26f,-0.66f),new Vector3(0.02f,0.18f,0.3f),_sign);
            Shape(root,PrimitiveType.Cube,new Vector3(0,1.08f,-0.66f),new Vector3(0.02f,0.18f,0.3f),_glow);
            // Glow stripes along the frame tubes.
            Bar(root,new Vector3(0,0.28f,-0.4f),new Vector3(0,0.53f,-0.13f),_glow,0.02f);
            Bar(root,new Vector3(0,0.57f,-0.09f),new Vector3(0,0.7f,0.26f),_glow,0.02f);
        }

        internal GameObject Station(Vector3 position)
        {
            EnsureMaterials();
            var root = new GameObject("SharedBike.Station");
            root.transform.position = position;
            // Wide landing pad with a raised bright border (4 segments per side, no overlap).
            Shape(root, PrimitiveType.Cube, new Vector3(0,0.025f,0),new Vector3(2.2f,0.05f,2.2f),_frame);
            foreach (var (dx,dz) in new[] { (-1f,-1f),(1f,-1f),(1f,1f),(-1f,1f) })
                for (var step=0;step<4;step++)
                {
                    var t0=step*0.5f-0.75f; var t1=step*0.5f-0.25f;
                    Bar(root,new Vector3(dx*1.05f,0.06f,dz*t0),new Vector3(dx*1.05f,0.06f,dz*t1),_sign,0.05f);
                    Bar(root,new Vector3(t0*1.05f,0.06f,dz*1.05f),new Vector3(t1*1.05f,0.06f,dz*1.05f),_sign,0.05f);
                }
            // Corner light posts with glowing tops.
            foreach (var (dx,dz) in new[] { (-1f,-1f),(1f,-1f),(1f,1f),(-1f,1f) })
            {
                Bar(root,new Vector3(dx*1.0f,0f,dz*1.0f),new Vector3(dx*1.0f,0.9f,dz*1.0f),_metal,0.06f);
                Shape(root,PrimitiveType.Sphere,new Vector3(dx*1.0f,0.97f,dz*1.0f),new Vector3(0.14f,0.14f,0.14f),_glow);
            }
            // A tall pole and a large lit sign are visible from a distance even when the ground is cluttered.
            Shape(root, PrimitiveType.Cylinder, new Vector3(0,1.3f,0),new Vector3(0.09f,1.3f,0.09f),_metal);
            Shape(root, PrimitiveType.Cube, new Vector3(0,2.6f,0),new Vector3(1.3f,1.3f,0.12f),_dark);
            // Giant "P" mark: glow frame + sign strokes (stem, top bowl, bowl bottom) on the board.
            Shape(root, PrimitiveType.Cube, new Vector3(0,2.6f,0.075f),new Vector3(0.9f,0.9f,0.02f),_glow);
            Shape(root, PrimitiveType.Cube, new Vector3(-0.22f,2.6f,0.09f),new Vector3(0.24f,0.62f,0.02f),_sign);
            Shape(root, PrimitiveType.Cube, new Vector3(0.05f,2.82f,0.09f),new Vector3(0.36f,0.2f,0.02f),_sign);
            Shape(root, PrimitiveType.Cube, new Vector3(0.19f,2.7f,0.09f),new Vector3(0.09f,0.24f,0.02f),_sign);
            Shape(root, PrimitiveType.Cube, new Vector3(0.05f,2.58f,0.09f),new Vector3(0.36f,0.18f,0.02f),_sign);
            Shape(root, PrimitiveType.Cube, new Vector3(0,0.5f,0),new Vector3(0.5f,0.12f,0.5f),_sign);
            // Rotating hover beacon on top for long-range visibility.
            var beacon=new GameObject("SharedBike.StationBeacon");
            beacon.transform.SetParent(root.transform,false);
            beacon.transform.localPosition=new Vector3(0,3.4f,0);
            Shape(beacon,PrimitiveType.Sphere,new Vector3(0,0,0),new Vector3(0.22f,0.22f,0.22f),_glow);
            Bar(beacon,new Vector3(0,-0.32f,0),new Vector3(0,-0.05f,0),_metal,0.03f);
            var halo=new GameObject("SharedBike.StationHalo");
            halo.transform.SetParent(beacon.transform,false);
            foreach (var corner in new[] { new Vector3(-0.3f,0,-0.3f),new Vector3(0.3f,0,-0.3f),new Vector3(0.3f,0,0.3f),new Vector3(-0.3f,0,0.3f) })
                Shape(halo,PrimitiveType.Sphere,corner,new Vector3(0.1f,0.1f,0.1f),_glow);
            halo.AddComponent<StationBeaconSpin>();
            ApplyVisibleLayer(root, position);
            return root;
        }

        // Slow self-spinning hover beacon: cheap, always-visible station landmark.
        private sealed class StationBeaconSpin : MonoBehaviour
        {
            private void Update() { transform.Rotate(0,45f*Time.deltaTime,0,Space.Self); }
        }

        internal static int VisibleLayer(Vector3 position)
        {
            var camera = Camera.main;
            var mask = camera != null ? camera.cullingMask : ~0;
            var layer = -1;
            if ((mask & 1) != 0) layer = 0;
            if (layer < 0 && Physics.Raycast(position + Vector3.up * 1.5f, Vector3.down, out var hit, 5, ~0, QueryTriggerInteraction.Ignore))
            {
                var ground = hit.collider.gameObject.layer;
                if ((mask & (1 << ground)) != 0 && ground != 8) layer = ground;
            }
            if (layer < 0)
            {
                var actor = CharacterMainControl.Main;
                if (actor != null && actor.characterModel != null)
                {
                    foreach (var renderer in actor.characterModel.GetComponentsInChildren<Renderer>(true))
                        if (renderer != null && (mask & (1 << renderer.gameObject.layer)) != 0) { layer = renderer.gameObject.layer; break; }
                }
            }
            if (layer < 0)
                for (var i = 0; i < 32; i++)
                    if ((mask & (1 << i)) != 0 && i != 5 && i != 8) { layer = i; break; }
            return layer < 0 ? 0 : layer;
        }

        private static void ApplyVisibleLayer(GameObject root, Vector3 position)
        {
            var layer = VisibleLayer(position);
            foreach (var transform in root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = layer;
        }

        private Sprite CreateFallbackIcon()
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var background = new Color(0.16f, 0.22f, 0.26f, 1f);
            var accent = new Color(0.2f, 0.86f, 0.58f, 1f);
            for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                {
                    var ring = Mathf.Abs(Mathf.Sqrt((x - 16) * (x - 16) + (y - 16) * (y - 16)) - 10f) < 2.2f;
                    texture.SetPixel(x, y, ring ? accent : background);
                }
            texture.Apply(false);
            return Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f));
        }

        private static Transform Anchor(GameObject root,string name,Vector3 position)
        {
            var anchor=new GameObject(name).transform; anchor.SetParent(root.transform,false); anchor.localPosition=position; return anchor;
        }

        private static GameObject Shape(GameObject root, PrimitiveType type, Vector3 position, Vector3 scale, Material? material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            var collider = part.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
            if (material != null) part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }
        private static void Bar(GameObject root, Vector3 a, Vector3 b, Material? material,float diameter=0.045f)
        {
            var part = Shape(root, PrimitiveType.Cylinder, (a+b)*0.5f, new Vector3(diameter,(b-a).magnitude*0.5f,diameter),material);
            part.transform.localRotation = Quaternion.FromToRotation(Vector3.up,b-a);
        }
        public void Dispose()
        {
            foreach (var material in new[] { _frame,_dark,_metal,_sign,_glow,_glowRed }) if (material != null) UnityEngine.Object.Destroy(material);
            if (Icon != null && Icon != _iconFallback) UnityEngine.Object.Destroy(Icon);
            if (_iconFallback != null) UnityEngine.Object.Destroy(_iconFallback);
            if (_iconTexture != null) UnityEngine.Object.Destroy(_iconTexture);
        }
    }

    internal static class SafeGround
    {
        internal static WorldPoint Point(Vector3 value) => new WorldPoint { X=value.x,Y=value.y,Z=value.z };
        internal static Vector3 Vector(WorldPoint value) => new Vector3(value.X,value.Y,value.Z);
        internal static Quaternion GroundRotation(Vector3 position,float yaw,CharacterMainControl actor)
        {
            var normal=Vector3.up;
            var hits=Physics.RaycastAll(position+Vector3.up*0.6f,Vector3.down,2,~0,QueryTriggerInteraction.Ignore);
            var best=float.PositiveInfinity;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(actor.transform) || hit.distance>=best || Vector3.Angle(hit.normal,Vector3.up)>35) continue;
                best=hit.distance; normal=hit.normal;
            }
            var forward=Vector3.ProjectOnPlane(Quaternion.Euler(0,yaw,0)*Vector3.forward,normal).normalized;
            return Quaternion.LookRotation(forward,normal);
        }
        internal static bool Find(Vector3 origin, Vector3 candidate, CharacterMainControl actor, out Vector3 result)
        {
            result=default;
            var navMeshFound = NavMesh.SamplePosition(candidate,out var nav,2,NavMesh.AllAreas);
            if (navMeshFound) candidate=nav.position;
            if (!Physics.Raycast(candidate+Vector3.up*1.5f,Vector3.down,out var hit,4,~0,QueryTriggerInteraction.Ignore)) return false;
            if (Vector3.Angle(hit.normal,Vector3.up)>35 || (navMeshFound && Mathf.Abs(hit.point.y-nav.position.y)>0.8f)) return false;
            candidate=hit.point+Vector3.up*0.025f;
            foreach (var collider in Physics.OverlapCapsule(candidate+Vector3.up*0.55f,candidate+Vector3.up*1.25f,0.4f,~0,QueryTriggerInteraction.Ignore))
                if (!collider.transform.IsChildOf(actor.transform)) return false;
            if (navMeshFound)
            {
                var path=new NavMeshPath();
                if (!NavMesh.CalculatePath(origin,candidate,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete) return false;
            }
            else if (!Clear(origin,candidate)) return false;
            result=candidate; return true;
        }
        internal static bool Clear(Vector3 a,Vector3 b)
        {
            var start=a+Vector3.up*0.75f; var offset=b-a;
            if (offset.sqrMagnitude<0.0001f) return true;
            foreach (var hit in Physics.RaycastAll(start,offset.normalized,offset.magnitude,~0,QueryTriggerInteraction.Ignore))
                if (CharacterMainControl.Main==null || !hit.collider.transform.IsChildOf(CharacterMainControl.Main.transform)) return false;
            return true;
        }
    }

    public sealed class SharedBikeInteractable : InteractableBase
    {
        internal BikeWorldController? World;
        internal string? StationId;
        private static readonly FieldInfo? GroupField = typeof(InteractableBase).GetField("otherInterablesInGroup", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? ListField = typeof(InteractableBase).GetField("_interactbleList", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? TimeField = typeof(InteractableBase).GetField("interactTime", BindingFlags.NonPublic | BindingFlags.Instance);

        protected override void Awake()
        {
            // A runtime-created InteractableBase has serialized lists equal to null. The native Awake enumerates
            // otherInterablesInGroup and throws before it can initialise _interactbleList, so seed them first.
            try { if (GroupField != null && GroupField.GetValue(this) == null) GroupField.SetValue(this, new List<InteractableBase>()); }
            catch (Exception ex) { Debug.LogWarning("[DuckovSharedBike] Interactable group init failed: " + ex.Message); }
            try { base.Awake(); }
            catch (Exception ex) { Debug.LogWarning("[DuckovSharedBike] Interactable base wake failed: " + ex.Message); }
            try
            {
                if (ListField != null && ListField.GetValue(this) == null) ListField.SetValue(this, new List<InteractableBase>());
                finishWhenTimeOut = true;
                interactMarkerOffset = Vector3.up * 1.35f;
                InteractName = "共享单车";
                TimeField?.SetValue(this, 0.2f);
            }
            catch (Exception ex) { Debug.LogWarning("[DuckovSharedBike] Interactable setup failed: " + ex.Message); }
        }

        protected override bool IsInteractable()
        {
            if (World?.Ready != true) return false;
            return StationId == null ? World.CanMountInteract : World.CanUseStation;
        }

        protected override void OnInteractFinished()
        {
            if (StationId == null) World?.TryMountOrDismount(); else World?.OpenStation(StationId);
            StopInteract();
        }
    }
}
