using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using WaterSlides;
using Random = System.Random;

public static class NightCitySetup
{
    const string ScenePath = "Assets/Scenes/NightCity.unity";
    const string ArtPath = "Assets/WaterSlides/Environment/NightCityArt.asset";
    const string PanoramaPath = "Assets/WaterSlides/Environment/NightCityPanorama.png";
    const string CityName = "Night City | Glass District";

    [MenuItem("Water Slides/Open Night City")]
    static void Open()
    {
        if (!EditorApplication.isPlaying && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(ScenePath);
    }

    // Лагуна (стартовая сцена). Мост агента сохраняет открытые сцены после пункта
    // меню, поэтому несохранённая ночная сцена здесь просто сохраняется без диалога.
    [MenuItem("Water Slides/Open Lagoon")]
    static void OpenLagoon()
    {
        if (EditorApplication.isPlaying)
            return;
        EditorSceneManager.SaveOpenScenes();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
    }

    [MenuItem("Water Slides/Apply Night City Panorama")]
    static void ApplyPanorama()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != ScenePath)
            return;

        var sky = RenderSettings.skybox;
        if (sky == null || sky.shader.name != "WaterSlides/Night City Sky")
            throw new InvalidOperationException("The night scene needs its Night City Sky material.");
        if (!SetPanorama(sky))
            throw new InvalidOperationException("Missing night city panorama: " + PanoramaPath);

        EditorUtility.SetDirty(sky);
        AssetDatabase.SaveAssets();
    }

    static bool SetPanorama(Material sky)
    {
        var panorama = AssetDatabase.LoadAssetAtPath<Texture2D>(PanoramaPath);
        if (panorama == null)
            return false;
        sky.SetTexture("_CityPanorama", panorama);
        sky.SetFloat("_PanoramaWeight", 1f);
        return true;
    }

    [MenuItem("Water Slides/Build Night City Environment")]
    static void Build()
    {
        if (EditorApplication.isPlaying)
            return;

        var scene = EditorSceneManager.OpenScene(ScenePath);
        var roots = scene.GetRootGameObjects();
        var course = roots.Select(r => r.GetComponentInChildren<WaterSlideCourse>()).First(c => c != null);
        var slides = course.GetComponentsInChildren<WaterSlideGenerator>().OrderBy(s => s.name).ToArray();
        if (slides.Length == 0)
            throw new InvalidOperationException("Night City needs the saved slide layout.");

        var route = new List<Vector3>();
        foreach (var slide in slides)
        {
            var frames = slide.RideFrames;
            if (route.Count > 0)
            {
                // Also reserve the air gap between slides.
                var end = route[^1];
                for (int j = 1; j < 10; j++)
                    route.Add(Vector3.Lerp(end, frames[0].Position, j / 10f));
            }
            for (int i = 0; i < frames.Count; i++)
                if (i == frames.Count - 1 || route.Count == 0 ||
                    (route[^1] - frames[i].Position).sqrMagnitude >= 4f)
                    route.Add(frames[i].Position);
        }

        foreach (var go in roots)
            if (go.name == CityName || go.name == "Night City | Fill")
                UnityEngine.Object.DestroyImmediate(go);
            else if (go.name == "Tropical Archipelago" || go.name == "Endless Ground")
                UnityEngine.Object.DestroyImmediate(go);

        var facade = NewMaterial("WaterSlides/Night City Facade", "City glass");
        var sky = NewMaterial("WaterSlides/Night City Sky", "Night sky");
        SetPanorama(sky);
        if (AssetDatabase.LoadMainAssetAtPath(ArtPath) != null)
            AssetDatabase.DeleteAsset(ArtPath);
        AssetDatabase.CreateAsset(facade, ArtPath);
        AssetDatabase.AddObjectToAsset(sky, facade);

        var city = new GameObject(CityName);
        float streetY = Mathf.Floor(route.Min(p => p.y) / 10f) * 10f - 32f;
        Vector2 start = XZ(route[0]);
        var layout = new District(route, streetY, start);
        layout.Generate();
        foreach (var pair in layout.Blocks)
        {
            var mesh = pair.Value.ToMesh($"District {pair.Key.x}, {pair.Key.y}");
            AssetDatabase.AddObjectToAsset(mesh, facade);
            var block = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
            block.transform.SetParent(city.transform, false);
            block.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = block.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = facade;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        RenderSettings.skybox = sky;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(.061f, .087f, .163f);
        RenderSettings.fogStartDistance = 80f;
        RenderSettings.fogEndDistance = 585f;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.43f, .52f, .69f);
        RenderSettings.ambientEquatorColor = new Color(.36f, .43f, .59f);
        RenderSettings.ambientGroundColor = new Color(.26f, .30f, .43f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = .66f;
        if (RenderSettings.sun != null)
        {
            RenderSettings.sun.color = new Color(.78f, .88f, 1f);
            RenderSettings.sun.intensity = 1.15f;
            RenderSettings.sun.shadowStrength = .48f;
            RenderSettings.sun.transform.rotation = Quaternion.Euler(43f, -128f, 0f);
        }
        var fill = new GameObject("Night City | Fill").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(1f, .70f, .50f);
        fill.intensity = .63f;
        fill.transform.rotation = Quaternion.Euler(25f, 63f, 0f);
        fill.shadows = LightShadows.None;

        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Night City: {layout.Count} towers, {slides.Length} original slides, street height {streetY}, route {route[0]} to {route[^1]}.");
    }

    static Material NewMaterial(string shaderName, string name)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null)
            throw new InvalidOperationException("Missing shader: " + shaderName);
        return new Material(shader) { name = name };
    }

    static Vector2 XZ(Vector3 p) => new(p.x, p.z);
    static Vector3 Point(Vector2 p, float y) => new(p.x, y, p.y);

    sealed class District
    {
        readonly List<Vector3> route;
        readonly float street;
        readonly Vector2 start;
        readonly Random rng = new(24181);
        readonly List<(Vector2 p, float radius)> occupied = new();
        public readonly Dictionary<Vector2Int, MeshWriter> Blocks = new();
        public int Count { get; private set; }

        public District(List<Vector3> route, float street, Vector2 start)
        {
            this.route = route;
            this.street = street;
            this.start = start;
        }

        float Range(float a, float b) => a + (b - a) * (float)rng.NextDouble();

        MeshWriter Block(Vector2 p)
        {
            var key = new Vector2Int(Mathf.FloorToInt((p.x-start.x)/135f), Mathf.FloorToInt((p.y-start.y)/135f));
            if (!Blocks.TryGetValue(key, out var result))
                Blocks.Add(key, result = new MeshWriter());
            return result;
        }

        public void Generate()
        {
            // Put a staggered row of towers on each side of the ride, then fill in the skyline.
            for (int i = 5; i < route.Count - 4; i += 10)
            {
                Vector2 p = XZ(route[i]);
                Vector2 tangent = (XZ(route[i+4]) - XZ(route[i-4])).normalized;
                if (tangent == Vector2.zero)
                    continue;
                var across = new Vector2(-tangent.y, tangent.x);
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int retry = 0; retry < 4; retry++)
                    {
                        Vector2 pos = p + across * side * (Range(22f, 32f) + retry*9f) + tangent*Range(-9f,9f);
                        if (Tower(pos, Range(13f,24f), Range(12f,22f), route[i].y + Range(14f,65f),
                                Mathf.Atan2(tangent.x, tangent.y)*Mathf.Rad2Deg + Range(-13f,13f), true))
                            break;
                    }
                }
            }

            const float step = 43f;
            int xMin = Mathf.FloorToInt((route.Min(p => p.x) - start.x - 360f)/step);
            int xMax = Mathf.CeilToInt((route.Max(p => p.x) - start.x + 360f)/step);
            int zMin = Mathf.FloorToInt((route.Min(p => p.z) - start.y - 360f)/step);
            int zMax = Mathf.CeilToInt((route.Max(p => p.z) - start.y + 360f)/step);
            for (int x = xMin; x <= xMax; x++)
                for (int z = zMin; z <= zMax; z++)
                {
                    Vector2 p = start + new Vector2(x*step,z*step);
                    var block = Block(p);
                    block.Road(Point(p, street-.15f), step, step, false, Style(1, .17f));
                    block.Road(Point(p+new Vector2(step*.5f,0), street), 7f, step, false, Style(3, .25f));
                    block.Road(Point(p+new Vector2(0,step*.5f), street+.025f), 7f, step, true, Style(3, .32f));
                    if ((x + 2*z) % 4 == 0)
                    {
                        Vector2 car = p + new Vector2(step*.5f+1.5f, Range(-14f,14f));
                        block.Box(Point(car,street+.22f), 1.55f, 3.7f, .65f, 0f, Style(1,.8f));
                        block.Box(Point(car+new Vector2(0,1.81f),street+.4f), 1.4f, .22f, .23f, 0f, Style(4,.05f));
                    }
                    float d = route.Min(q => (XZ(q)-p).sqrMagnitude);
                    if (d > 420f*420f)
                        continue;
                    float height = d < 75f*75f ? Range(25f,190f) : Range(65f,325f);
                    bool landmark = d > 105f*105f && Range(0f,1f) < .17f;
                    if (landmark)
                        height = Range(350f,550f);
                    Tower(p+new Vector2(Range(-5f,5f),Range(-5f,5f)), landmark ? Range(32f,45f) : Range(16f,31f),
                        landmark ? Range(30f,43f) : Range(14f,29f),
                        street+height, Range(-10f,10f), false);
                }
        }

        bool Tower(Vector2 p, float w, float d, float top, float angle, bool hero)
        {
            float radius = Mathf.Sqrt(w*w+d*d)*.5f;
            if (occupied.Any(v => (v.p-p).sqrMagnitude < Mathf.Pow(v.radius+radius+2.5f,2f)))
                return false;
            // A cylindrical bound covers corners, setbacks, and decorative roof trim.
            float clearance = radius + 9f;
            foreach (var q in route)
                if ((XZ(q)-p).sqrMagnitude < clearance*clearance)
                    top = Mathf.Min(top, q.y-10f);
            if (top < street+22f || (hero && top < street+55f))
                return false;

            occupied.Add((p,radius));
            Count++;
            var b = Block(p);
            float h = top-street;
            float hue = Range(0f,1f);
            b.Box(Point(p,street),w,d,Mathf.Min(h*.12f,8f),angle,Style(1,hue));
            float baseH = Mathf.Min(h*.12f,8f);
            float body = h-baseH;
            // Разнообразие силуэтов (10.11.2026, «небоскрёбы поразнообразнее»):
            // кроме прежней коробки с уступом — круглые, шести/восьмигранные,
            // ступенчатые, закрученные и тонкие иглы. Высота и зазор до трассы
            // посчитаны выше одинаково для всех форм.
            float shape = Range(0f,1f);
            if (shape < .16f)
                return RoundTower(b,p,w,d,body,street+baseH,angle,hue,hero);
            if (shape < .30f)
                return SteppedTower(b,p,w,d,body,street+baseH,angle,hue,hero);
            if (shape < .44f)
                return TwistedTower(b,p,w,d,body,street+baseH,angle,hue,hero);
            if (shape < .56f)
                return PrismTower(b,p,w,d,body,street+baseH,angle,hue,hero, Range(0f,1f) < .5f ? 6 : 8);
            if (shape < .64f && h > 60f)
                return NeedleTower(b,p,w,d,body,street+baseH,angle,hue);
            float style = Range(0f,1f);
            float waist = style < .42f ? .66f : (style < .72f ? .82f : 1f);
            float tier = waist < .99f ? Range(.19f,.38f)*body : 0f;
            float cut = style > .55f ? Range(.10f,.24f) : .045f;
            float taper = style > .72f ? .72f : 1f;
            float edge = hero ? Range(.6f,1f) : Range(.15f,.7f);
            b.Tower(Point(p,street+baseH), w*.96f,d*.96f, body-tier, angle, cut, taper, hue, edge);
            if (tier > 0f)
            {
                b.Box(Point(p,top-tier-.75f),w*.97f,d*.97f,.8f,angle,Style(1,hue));
                b.Tower(Point(p,top-tier),w*waist,d*waist,tier,angle+.5f,cut,1f,hue,edge);
            }
            float roof = waist*taper;
            b.Box(Point(p,top-.25f),w*roof*.91f,d*roof*.91f,.40f,angle,Style(2,hue,edge));
            b.Box(Point(p,top+.15f),w*roof*.55f,d*roof*.54f,2.2f,angle,Style(1,hue));
            if (h > 80f && style > .56f)
            {
                b.Box(Point(p,top+2.1f),.55f,.55f,5f,angle,Style(1,.8f));
                b.Box(Point(p,top+7f),.82f,.82f,.8f,angle,Style(4,.96f));
            }
            return true;
        }

        float Edge(bool hero) => hero ? Range(.7f,1f) : Range(.35f,.85f);

        // Круглая башня (16 граней, без неона по рёбрам — грани узкие), светящиеся
        // пояса этажей, корона-кольцо из неона.
        bool RoundTower(MeshWriter b, Vector2 p, float w, float d, float body, float y0, float angle, float hue, bool hero)
        {
            float r = Mathf.Min(w,d);
            float taper = Range(0f,1f) < .5f ? Range(.7f,.9f) : 1f;
            b.Prism(Point(p,y0), r, r, body, angle, 16, taper, Style(0,hue,0f));
            float top = y0+body;
            b.Prism(Point(p,top-.3f), r*taper*1.02f, r*taper*1.02f, .9f, angle, 16, 1f, Style(2,hue,Edge(hero)));
            b.Prism(Point(p,top+.6f), r*taper*.6f, r*taper*.6f, 3f, angle, 16, .8f, Style(1,hue));
            return true;
        }

        // Ступенчатая башня: 3–4 яруса, каждый уже предыдущего, между ярусами
        // неоновые карнизы.
        bool SteppedTower(MeshWriter b, Vector2 p, float w, float d, float body, float y0, float angle, float hue, bool hero)
        {
            int tiers = Range(0f,1f) < .5f ? 3 : 4;
            float edge = Edge(hero);
            float y = y0, scale = 1f;
            for (int t = 0; t < tiers; t++)
            {
                float th = body * (t == 0 ? .42f : .58f/(tiers-1));
                b.Tower(Point(p,y), w*scale, d*scale, th, angle, .06f, 1f, hue, edge);
                y += th;
                b.Box(Point(p,y-.5f), w*scale*1.03f, d*scale*1.03f, .7f, angle, Style(2,hue,edge));
                scale *= Range(.68f,.8f);
            }
            b.Box(Point(p,y), w*scale, d*scale, 2.5f, angle, Style(1,hue));
            return true;
        }

        // Закрученная башня: 8 сегментов, каждый повёрнут на несколько градусов.
        bool TwistedTower(MeshWriter b, Vector2 p, float w, float d, float body, float y0, float angle, float hue, bool hero)
        {
            const int segments = 8;
            float twist = Range(4f,8f) * (Range(0f,1f) < .5f ? -1f : 1f);
            float side = Mathf.Min(w,d);
            float edge = Edge(hero);
            float sh = body/segments;
            for (int k = 0; k < segments; k++)
            {
                float taper = Mathf.Lerp(1f,.82f,k/(float)segments);
                b.Tower(Point(p,y0+k*sh), side*taper, side*taper, sh, angle+twist*k, .14f, 1f, hue, edge);
            }
            b.Box(Point(p,y0+body), side*.7f, side*.7f, 1.2f, angle+twist*segments, Style(2,hue,edge));
            return true;
        }

        // Шести/восьмигранная башня с неоном по рёбрам и шпилем.
        bool PrismTower(MeshWriter b, Vector2 p, float w, float d, float body, float y0, float angle, float hue, bool hero, int sides)
        {
            float r = Mathf.Min(w,d);
            float edge = Edge(hero);
            b.Prism(Point(p,y0), r, r, body, angle, sides, .9f, Style(0,hue,edge));
            float top = y0+body;
            b.Prism(Point(p,top), r*.9f, r*.9f, 1f, angle, sides, .7f, Style(2,hue,edge));
            if (body > 70f)
            {
                b.Box(Point(p,top+1f),.7f,.7f,Range(10f,22f),angle,Style(2,hue,1f));
            }
            return true;
        }

        // Тонкая игла: узкий высокий ствол и длинный светящийся шпиль.
        bool NeedleTower(MeshWriter b, Vector2 p, float w, float d, float body, float y0, float angle, float hue)
        {
            float r = Mathf.Min(w,d)*.7f;
            b.Prism(Point(p,y0), r, r, body*.8f, angle, 8, .75f, Style(0,hue,.8f));
            float y = y0+body*.8f;
            b.Prism(Point(p,y), r*.75f, r*.75f, body*.2f, angle, 8, .25f, Style(0,hue,.9f));
            y += body*.2f;
            b.Box(Point(p,y),.5f,.5f,Range(18f,34f),angle,Style(2,hue,1f));
            return true;
        }
    }

    static Color Style(int kind, float hue, float strength = 0f) => new(hue, kind*.25f, 0f, strength);

    sealed class MeshWriter
    {
        readonly List<Vector3> positions = new(), normals = new();
        readonly List<Vector2> uv = new();
        readonly List<Color> colors = new();
        readonly List<int> triangles = new();

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u, float v, Color style)
        {
            int i = positions.Count;
            var n = Vector3.Cross(b-a,c-a).normalized;
            positions.Add(a); positions.Add(b); positions.Add(c); positions.Add(d);
            for (int j = 0; j < 4; j++) { normals.Add(n); colors.Add(style); }
            uv.Add(Vector2.zero); uv.Add(new Vector2(u,0)); uv.Add(new Vector2(u,v)); uv.Add(new Vector2(0,v));
            triangles.Add(i); triangles.Add(i+1); triangles.Add(i+2);
            triangles.Add(i); triangles.Add(i+2); triangles.Add(i+3);
        }

        public void Road(Vector3 p, float width, float length, bool turn, Color style)
        {
            var q = Quaternion.Euler(0f,turn?90f:0f,0f);
            Vector3 u = q*Vector3.right*width*.5f, v = q*Vector3.back*length*.5f;
            Quad(p-u-v,p+u-v,p+u+v,p-u+v,1f,length,style);
        }

        public void Box(Vector3 p,float w,float d,float h,float angle,Color style) =>
            Shell(p,w,d,h,angle,0f,1f,style,false);

        public void Tower(Vector3 p,float w,float d,float h,float angle,float corner,float taper,float hue,float edge) =>
            Shell(p,w,d,h,angle,corner,taper,Style(0,hue,edge),true);

        void Shell(Vector3 p,float w,float d,float h,float angle,float corner,float taper,Color style,bool glass)
        {
            float x=w*.5f, z=d*.5f, bevel=Mathf.Min(w,d)*corner;
            var ring = corner == 0f
                ? new[] { new Vector3(-x,0,-z), new Vector3(x,0,-z), new Vector3(x,0,z), new Vector3(-x,0,z) }
                : new[] { new Vector3(-x+bevel,0,-z), new Vector3(x-bevel,0,-z), new Vector3(x,0,-z+bevel),
                    new Vector3(x,0,z-bevel), new Vector3(x-bevel,0,z), new Vector3(-x+bevel,0,z),
                    new Vector3(-x,0,z-bevel), new Vector3(-x,0,-z+bevel) };
            var q = Quaternion.Euler(0f,angle,0f);
            var top = p+Vector3.up*h;
            for (int i=0;i<ring.Length;i++)
            {
                int next=(i+1)%ring.Length;
                var a=q*ring[i]; var b=q*ring[next];
                float u = Mathf.Max(1f,Mathf.Round(Vector3.Distance(a,b)/2.2f));
                float v = Mathf.Max(1f,Mathf.Round(h/3.35f));
                var tint=style;
                tint.b=u/64f;
                // Counterclockwise footprint viewed from below, so traverse the wall in reverse.
                Quad(p+b,p+a,top+a*taper,top+b*taper,u,v,tint);
                Quad(top+b*taper,top+a*taper,top,top,1f,1f,glass?Style(1,style.r):style);
            }
        }

        // Многогранная призма (круглая при 16 гранях) по эллипсу w×d.
        public void Prism(Vector3 p, float w, float d, float h, float angle, int sides, float taper, Color style)
        {
            var ring = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (i + .5f) / sides * Mathf.PI * 2f;
                ring[i] = new Vector3(Mathf.Cos(a)*w*.5f, 0f, Mathf.Sin(a)*d*.5f);
            }
            var q = Quaternion.Euler(0f,angle,0f);
            var top = p+Vector3.up*h;
            bool glass = Mathf.Approximately(style.g, 0f);
            for (int i = 0; i < sides; i++)
            {
                int next = (i+1)%sides;
                // Обход в обратную сторону — как у Shell (нормали наружу).
                var a = q*ring[i]; var bb = q*ring[next];
                float u = Mathf.Max(1f,Mathf.Round(Vector3.Distance(a,bb)/2.2f));
                float v = Mathf.Max(1f,Mathf.Round(h/3.35f));
                var tint = style;
                tint.b = u/64f;
                Quad(p+bb,p+a,top+a*taper,top+bb*taper,u,v,tint);
                Quad(top+bb*taper,top+a*taper,top,top,1f,1f,glass?Style(1,style.r):style);
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name=name };
            if (positions.Count > ushort.MaxValue)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0,uv);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles,0);
            return mesh;
        }
    }
}
