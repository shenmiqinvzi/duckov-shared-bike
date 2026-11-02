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
        // 共享单车专用材质：与站点配色分离，车身改黑时站点保持绿/黄识别色。
        private Material? _bikeFrame;
        private Material? _bikeDark;
        private Material? _bikeMetal;
        private Material? _bikeEmber;
        private Material? _bikeEmberDim;
        private Material? _bikeCold;
        private Material? _bikePlate;
        private Texture2D? _wornMetal;
        private Texture2D? _plateTexture;
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
            var shader = template != null ? template.shader : FindShader();
            if (shader == null) { _log("Visuals unavailable: no compatible shader found."); return; }
            // 站点配色（不变）：绿色识别色 + 黄色描边，和黑化的车辆形成区分。
            _frame = Create(template, shader, new Color(0.2f, 0.86f, 0.58f));
            _dark = Create(template, shader, new Color(0.08f, 0.1f, 0.12f));
            _metal = Create(template, shader, new Color(0.63f, 0.69f, 0.72f));
            _sign = Create(template, shader, new Color(0.96f, 0.8f, 0.24f));
            _glow = Emissive(Create(template, shader, new Color(0.55f, 0.95f, 1f)), 1.6f);
            _glowRed = Emissive(Create(template, shader, new Color(1f, 0.18f, 0.14f)), 1.6f);
            CreateBikeMaterials(template, shader);
            _log(template != null
                ? "Visuals use game material template shader=" + template.shader.name + " template=" + template.name
                : "Visuals use shader=" + shader.name + " (no game material template found)");
        }

        private static Material Create(Material? template, Shader shader, Color color)
            => template != null ? Tint(template, color) : MakeMaterial(shader, color);

        private static Material Emissive(Material material, float intensity)
        {
            if (!material.HasProperty("_EmissionColor")) return material;
            var tint = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor")
                : material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(tint.r * intensity, tint.g * intensity, tint.b * intensity, 1f));
            return material;
        }

        private void CreateBikeMaterials(Material? template, Shader shader)
        {
            _wornMetal = BuildWornMetalTexture();
            _bikeFrame = Create(template, shader, new Color(0.14f, 0.145f, 0.155f));
            ApplyTexture(_bikeFrame, _wornMetal, 0.8f, 0.22f);

            _bikeDark = Create(template, shader, new Color(0.045f, 0.048f, 0.055f));
            SetSurface(_bikeDark, 0.5f, 0.2f);
            RemoveEmission(_bikeDark);

            _bikeMetal = Create(template, shader, new Color(0.45f, 0.46f, 0.49f));
            SetSurface(_bikeMetal, 0.9f, 0.34f);
            RemoveEmission(_bikeMetal);

            _bikeEmber = Emissive(Create(template, shader, new Color(1f, 0.4f, 0.12f)), 2.1f);
            _bikeEmberDim = Emissive(Create(template, shader, new Color(0.72f, 0.18f, 0.05f)), 1f);
            _bikeCold = Emissive(Create(template, shader, new Color(0.28f, 0.62f, 1f)), 1.3f);

            _plateTexture = BuildPlateTexture();
            _bikePlate = Create(template, shader, new Color(0.09f, 0.095f, 0.105f));
            ApplyTexture(_bikePlate, _plateTexture, 0.25f, 0.35f);
        }

        private static void RemoveEmission(Material material)
        {
            if (!material.HasProperty("_EmissionColor")) return;
            material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
        }

        private static void ApplyTexture(Material material, Texture2D texture, float metallic, float smoothness)
        {
            SetSurface(material, metallic, smoothness);
            var applied = false;
            if (material.HasProperty("_BaseMap")) { material.SetTexture("_BaseMap", texture); applied = true; }
            if (material.HasProperty("_MainTex")) { material.SetTexture("_MainTex", texture); applied = true; }
            material.mainTexture = texture;
            // 贴图自带明暗与锈迹，基色置白才能让纹理原样显示（否则相乘后接近纯黑）。
            if (!applied) return;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            // 克隆模板时带过来的微弱自发光要关掉，车身靠余烬件发光，不靠车架。
            RemoveEmission(material);
        }

        private static void SetSurface(Material material, float metallic, float smoothness)
        {
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            else if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
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
                // 烧红的轮圈：24 段全是余烬材质，转起来像两圈缓慢流动的火。
                for (var segment=0;segment<24;segment++)
                {
                    var a=segment*Mathf.PI*2/24; var b=(segment+1)*Mathf.PI*2/24;
                    Bar(assembly,new Vector3(0,Mathf.Sin(a)*0.235f,Mathf.Cos(a)*0.235f),
                        new Vector3(0,Mathf.Sin(b)*0.235f,Mathf.Cos(b)*0.235f),_bikeEmberDim,0.05f);
                }
                for (var spoke=0;spoke<8;spoke++)
                {
                    var angle=spoke*Mathf.PI*2/8;
                    Bar(assembly,Vector3.zero,new Vector3(0,Mathf.Sin(angle)*0.22f,Mathf.Cos(angle)*0.22f),_bikeMetal,0.012f);
                }
                var wheel = Shape(assembly, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.14f, 0.06f, 0.14f), _bikeMetal);
                wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
            }
            // 车架：磨损黑铁，三角拓扑与坐标保持原样（坐姿锚点与净空检查依赖它）。
            Bar(root,new Vector3(0,0.26f,-0.42f),new Vector3(0,0.55f,-0.11f),_bikeFrame);
            Bar(root,new Vector3(0,0.55f,-0.11f),new Vector3(0,0.32f,0.12f),_bikeFrame);
            Bar(root,new Vector3(0,0.32f,0.12f),new Vector3(0,0.26f,-0.42f),_bikeFrame);
            Bar(root,new Vector3(0,0.55f,-0.11f),new Vector3(0,0.72f,0.28f),_bikeFrame);
            Bar(root,new Vector3(0,0.72f,0.28f),new Vector3(0,0.32f,0.12f),_bikeFrame);
            Bar(root,new Vector3(0,0.26f,0.42f),new Vector3(0,0.94f,0.22f),_bikeMetal);
            Bar(root,new Vector3(-0.19f,0.94f,0.22f),new Vector3(0.19f,0.94f,0.22f),_bikeDark);
            Bar(root,new Vector3(0,0.55f,-0.11f),new Vector3(0,0.66f,-0.11f),_bikeMetal);
            Shape(root,PrimitiveType.Cube,new Vector3(0,0.66f,-0.11f),new Vector3(0.26f,0.065f,0.22f),_bikeDark);
            var crank=new GameObject("PedalCrank"); crank.transform.SetParent(root.transform,false);
            crank.transform.localPosition=new Vector3(0,0.59f,-0.12f);
            Bar(crank,new Vector3(-0.14f,-0.05f,0),new Vector3(0.14f,0.05f,0),_bikeMetal);
            var pedals=new[] {
                Shape(crank,PrimitiveType.Cube,new Vector3(-0.17f,-0.05f,0),new Vector3(0.14f,0.035f,0.13f),_bikeDark).transform,
                Shape(crank,PrimitiveType.Cube,new Vector3(0.17f,0.05f,0),new Vector3(0.14f,0.035f,0.13f),_bikeDark).transform
            };
            // 车筐：金属网筐，共享单车最强的轮廓识别点，必须保留。
            Shape(root,PrimitiveType.Cube,new Vector3(0,0.73f,0.43f),new Vector3(0.34f,0.03f,0.22f),_bikeMetal);
            foreach (var x in new[] { -0.16f,0.16f })
            {
                Bar(root,new Vector3(x,0.73f,0.33f),new Vector3(x,0.87f,0.33f),_bikeMetal);
                Bar(root,new Vector3(x,0.73f,0.53f),new Vector3(x,0.87f,0.53f),_bikeMetal);
                Bar(root,new Vector3(x,0.87f,0.33f),new Vector3(x,0.87f,0.53f),_bikeMetal);
            }
            Bar(root,new Vector3(-0.16f,0.87f,0.53f),new Vector3(0.16f,0.87f,0.53f),_bikeMetal);
            // 后反光片改成幽蓝冷光，和暖色余烬形成冷暖对比。
            Shape(root,PrimitiveType.Cube,new Vector3(0,0.48f,-0.52f),new Vector3(0.11f,0.065f,0.03f),_bikeCold);
            Decorate(root);
            Emblem(root);
            Plate(root);
            var emberLight=CreateEmberLight(root);
            var seat=Anchor(root,"RiderSeat",new Vector3(0,0.60f,-0.11f));
            var grips=new[] { Anchor(root,"Grip.Left",new Vector3(-0.19f,0.94f,0.22f)),Anchor(root,"Grip.Right",new Vector3(0.19f,0.94f,0.22f)) };
            var visual=root.AddComponent<BicycleVisual>();
            visual.Configure(wheels.ToArray(),crank.transform,pedals,seat,grips);
            visual.ConfigureFire(_bikeEmber,_bikeEmberDim,emberLight,new Color(1f,0.42f,0.14f));
            ApplyVisibleLayer(root, position);
            return root;
        }

        // 地狱火废土风点缀：挡泥板、烧红的车灯、冷光尾灯、余烬条纹与残旗。
        private void Decorate(GameObject root)
        {
            if (_bikeFrame==null) return;
            // Curved mudguards arcing over the top of both wheels.
            foreach (var z in new[] { -BicycleGeometry.HalfWheelbase, BicycleGeometry.HalfWheelbase })
                for (var segment=0;segment<5;segment++)
                {
                    var a=0.25f+segment*0.32f; var b=0.25f+(segment+1)*0.32f;
                    Bar(root,new Vector3(0,0.26f+Mathf.Sin(a)*0.31f,z+Mathf.Cos(a)*0.31f),
                        new Vector3(0,0.26f+Mathf.Sin(b)*0.31f,z+Mathf.Cos(b)*0.31f),_bikeFrame,0.06f);
                }
            // 前灯：一颗烧红的机械眼，配暗铁灯座。
            Shape(root,PrimitiveType.Sphere,new Vector3(0,0.99f,0.32f),new Vector3(0.1f,0.1f,0.1f),_bikeEmber);
            Shape(root,PrimitiveType.Cylinder,new Vector3(0,0.94f,0.24f),new Vector3(0.05f,0.05f,0.05f),_bikeMetal)
                .transform.localRotation=Quaternion.Euler(90,0,0);
            // 尾灯：幽蓝冷光，和暖色轮圈形成冷暖对比。
            Shape(root,PrimitiveType.Cube,new Vector3(0,0.56f,-0.55f),new Vector3(0.16f,0.09f,0.03f),_bikeCold);
            // 残旗：保留共享单车的旗杆识别，旗面换成暗红余烬。
            Bar(root,new Vector3(0,0.55f,-0.44f),new Vector3(0,1.35f,-0.58f),_bikeMetal,0.012f);
            Shape(root,PrimitiveType.Cube,new Vector3(0,1.26f,-0.66f),new Vector3(0.02f,0.18f,0.3f),_bikeEmberDim);
            // 车架余烬条纹只留两道，避免整辆车变成霓虹灯。
            Bar(root,new Vector3(0,0.28f,-0.4f),new Vector3(0,0.53f,-0.13f),_bikeEmberDim,0.02f);
            Bar(root,new Vector3(0,0.57f,-0.09f),new Vector3(0,0.7f,0.26f),_bikeEmberDim,0.02f);
        }

        // 车头机械骷髅徽记：方块和球堆出轮廓，不碰写实人头。
        private void Emblem(GameObject root)
        {
            var plate=_bikeMetal??_bikeDark; if (plate==null) return;
            var glow=_bikeEmber??plate; var dim=_bikeEmberDim??plate;
            Shape(root,PrimitiveType.Cube,new Vector3(0,0.83f,0.55f),new Vector3(0.26f,0.17f,0.02f),plate);
            Shape(root,PrimitiveType.Sphere,new Vector3(-0.055f,0.855f,0.562f),new Vector3(0.045f,0.04f,0.02f),glow);
            Shape(root,PrimitiveType.Sphere,new Vector3(0.055f,0.855f,0.562f),new Vector3(0.045f,0.04f,0.02f),glow);
            Shape(root,PrimitiveType.Cube,new Vector3(0,0.832f,0.562f),new Vector3(0.014f,0.03f,0.012f),dim);
            for (var i=0;i<4;i++)
                Shape(root,PrimitiveType.Cube,new Vector3(-0.045f+i*0.03f,0.795f,0.562f),new Vector3(0.012f,0.022f,0.012f),dim);
            for (var i=0;i<3;i++)
            {
                var angle=Mathf.PI*(0.15f+0.35f*i);
                Shape(root,PrimitiveType.Cube,new Vector3(Mathf.Cos(angle)*0.085f,0.915f+Mathf.Sin(angle)*0.03f,0.562f),
                    new Vector3(0.05f,0.012f,0.012f),dim).transform.localRotation=Quaternion.Euler(0,0,Mathf.Cos(angle)<0f?-18f:18f);
            }
        }

        // 后挡泥板尾部的牌照，像真车那样挂一块牌子。
        private void Plate(GameObject root)
        {
            if (_bikePlate==null) return;
            Bar(root,new Vector3(0,0.53f,-0.52f),new Vector3(0,0.48f,-0.62f),_bikeMetal,0.012f);
            Shape(root,PrimitiveType.Cube,new Vector3(0,0.42f,-0.63f),new Vector3(0.22f,0.09f,0.02f),_bikePlate);
        }

        // 只加一盏点光：URP 附加光数量有限，多一盏就可能挤掉场景灯。
        private static Light? CreateEmberLight(GameObject root)
        {
            var go=new GameObject("SharedBike.EmberLight");
            go.transform.SetParent(root.transform,false);
            go.transform.localPosition=new Vector3(0,0.34f,-0.4f);
            var light=go.AddComponent<Light>();
            light.type=LightType.Point;
            light.color=new Color(1f,0.44f,0.15f);
            light.range=2.3f;
            light.intensity=0.85f;
            light.shadows=LightShadows.None;
            return light;
        }

        // 磨损黑铁：值噪声打底 + 锈斑 + 随机划痕，192² 一次性生成后复用。
        private static Texture2D BuildWornMetalTexture()
        {
            const int size=192;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,true){
                name="sbk-worn-metal",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear};
            var pixels=new Color[size*size];
            var baseColor=new Color(0.115f,0.12f,0.13f);
            var rust=new Color(0.31f,0.13f,0.055f);
            var steel=new Color(0.28f,0.29f,0.31f);
            for (var y=0;y<size;y++)
                for (var x=0;x<size;x++)
                {
                    var grain=Fbm(x*0.09f,y*0.09f,4);
                    var blot=Fbm(x*0.035f+17f,y*0.035f+41f,3);
                    var color=baseColor*(0.62f+0.72f*grain);
                    if (blot>0.58f) color=Color.Lerp(color,rust,Mathf.Clamp01((blot-0.58f)*3.4f)*0.6f);
                    else if (blot<0.34f) color=Color.Lerp(color,steel,Mathf.Clamp01((0.34f-blot)*3f)*0.35f);
                    pixels[y*size+x]=color;
                }
            PaintScratches(pixels,size,size,new Color(0.55f,0.55f,0.58f));
            texture.SetPixels(pixels);
            texture.Apply(true,true);
            return texture;
        }

        private static void PaintScratches(Color[] pixels,int width,int height,Color color)
        {
            var random=new System.Random(7);
            for (var i=0;i<16;i++)
            {
                var originX=random.Next(0,width); var originY=random.Next(0,height);
                var length=8+random.Next(44);
                // 圆柱 UV 的 v 轴是管长方向，划痕顺着它才像金属刮擦而不是噪点。
                var direction=Math.PI*0.5+(random.NextDouble()-0.5)*0.9;
                var dx=(float)Math.Cos(direction); var dy=(float)Math.Sin(direction);
                for (var step=0;step<length;step++)
                    Blend(pixels,width,height,(int)(originX+dx*step),(int)(originY+dy*step),color,0.5f);
            }
        }

        private static void Blend(Color[] pixels,int width,int height,int x,int y,Color color,float amount)
        {
            for (var offsetY=0;offsetY<2;offsetY++)
                for (var offsetX=0;offsetX<2;offsetX++)
                {
                    var px=x+offsetX; var py=y+offsetY;
                    if (px<0||py<0||px>=width||py>=height) continue;
                    var index=py*width+px;
                    var weight=offsetX+offsetY==0?1f:0.45f;
                    pixels[index]=Color.Lerp(pixels[index],color,amount*weight);
                }
        }

        // 车牌：160×64 点阵，中央 A-0427，四周一圈暗金边框。
        private static Texture2D BuildPlateTexture()
        {
            const int width=160,height=64;
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false){
                name="sbk-plate",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var background=new Color(0.08f,0.085f,0.095f);
            var border=new Color(0.42f,0.3f,0.14f);
            var ink=new Color(0.92f,0.62f,0.24f);
            var pixels=new Color[width*height];
            for (var i=0;i<pixels.Length;i++) pixels[i]=background;
            for (var x=0;x<width;x++){ pixels[x]=border; pixels[(height-1)*width+x]=border; }
            for (var y=0;y<height;y++){ pixels[y*width]=border; pixels[y*width+width-1]=border; }
            PaintText(pixels,width,height,"A-0427",ink);
            texture.SetPixels(pixels);
            texture.Apply(false,true);
            return texture;
        }

        private static readonly Dictionary<char,string[]> Glyphs=new Dictionary<char,string[]>
        {
            {'0',new[]{"01110","10001","10011","10101","11001","10001","01110"}},
            {'1',new[]{"00100","01100","00100","00100","00100","00100","01110"}},
            {'2',new[]{"01110","10001","00001","00010","00100","01000","11111"}},
            {'3',new[]{"11110","00001","00001","01110","00001","00001","11110"}},
            {'4',new[]{"00010","00110","01010","10010","11111","00010","00010"}},
            {'5',new[]{"11111","10000","11110","00001","00001","10001","01110"}},
            {'6',new[]{"00110","01000","10000","11110","10001","10001","01110"}},
            {'7',new[]{"11111","00001","00010","00100","01000","01000","01000"}},
            {'8',new[]{"01110","10001","10001","01110","10001","10001","01110"}},
            {'9',new[]{"01110","10001","10001","01111","00001","00010","01100"}},
            {'A',new[]{"01110","10001","10001","11111","10001","10001","10001"}},
            {'-',new[]{"00000","00000","00000","11111","00000","00000","00000"}}
        };

        private static void PaintText(Color[] pixels,int width,int height,string text,Color color)
        {
            const int scale=3;
            var advance=5*scale+scale;
            var total=text.Length*advance-scale;
            var originX=Mathf.Max(2,(width-total)/2);
            var originY=Mathf.Max(2,(height-7*scale)/2);
            for (var i=0;i<text.Length;i++)
            {
                if (!Glyphs.TryGetValue(text[i],out var rows)) continue;
                for (var row=0;row<7;row++)
                    for (var column=0;column<5;column++)
                    {
                        if (rows[row][column]!='1') continue;
                        var startX=originX+i*advance+column*scale;
                        var startY=originY+(6-row)*scale;
                        for (var dy=0;dy<scale;dy++)
                            for (var dx=0;dx<scale;dx++)
                            {
                                var x=startX+dx; var y=startY+dy;
                                if (x<0||y<0||x>=width||y>=height) continue;
                                pixels[y*width+x]=color;
                            }
                    }
            }
        }

        private static float Hash01(int x,int y)
        {
            unchecked
            {
                var hash=x*374761393+y*668265263;
                hash=(hash^(hash>>13))*1274126177;
                return ((hash^(hash>>16))&0x7fffffff)/(float)0x7fffffff;
            }
        }

        private static float ValueNoise(float x,float y)
        {
            var xi=Mathf.FloorToInt(x); var yi=Mathf.FloorToInt(y);
            var fx=x-xi; var fy=y-yi;
            var u=fx*fx*(3f-2f*fx); var v=fy*fy*(3f-2f*fy);
            return Mathf.Lerp(Mathf.Lerp(Hash01(xi,yi),Hash01(xi+1,yi),u),
                              Mathf.Lerp(Hash01(xi,yi+1),Hash01(xi+1,yi+1),u),v);
        }

        private static float Fbm(float x,float y,int octaves)
        {
            var sum=0f; var amplitude=0.5f; var frequency=1f;
            for (var i=0;i<octaves;i++)
            {
                sum+=ValueNoise(x*frequency,y*frequency)*amplitude;
                frequency*=2f; amplitude*=0.5f;
            }
            return sum;
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
            foreach (var material in new[] { _bikeFrame,_bikeDark,_bikeMetal,_bikeEmber,_bikeEmberDim,_bikeCold,_bikePlate }) if (material != null) UnityEngine.Object.Destroy(material);
            if (_wornMetal != null) UnityEngine.Object.Destroy(_wornMetal);
            if (_plateTexture != null) UnityEngine.Object.Destroy(_plateTexture);
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
