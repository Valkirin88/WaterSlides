using System.Collections.Generic;
using UnityEngine;

namespace WaterSlides
{
    // Рисунок неона на стекле (просьба 27.10.2026 — вариативность стеклянных горок).
    public enum GlassLineStyle
    {
        EdgesAndRings, // продольные линии + кольца поперёк (как было)
        EdgesOnly,     // только продольные линии, без колец
        RingsOnly,     // только кольца поперёк, без продольных линий
        Grid           // частая решётка: тонкие кольца и продольные линии (как «сетка» 26.10)
    }

    [ExecuteAlways]
    public class WaterSlideGenerator : MonoBehaviour
    {
        [Header("Path")]
        [Tooltip("Опорные точки пути горки (в локальных координатах); по ним строится центральная линия трубы/жёлоба.")]
        public List<Vector3> ControlPoints = new()
        {
            new(0f, 20f, 0f),
            new(10f, 18f, 15f),
            new(25f, 14f, 10f),
            new(35f, 10f, 30f),
            new(20f, 6f, 45f),
            new(5f, 2f, 55f),
            new(-5f, 0f, 70f)
        };

        [Header("Type")]
        [Tooltip("Тип сечения горки: открытый жёлоб или закрытая труба.")]
        public SlideEnclosure Enclosure = SlideEnclosure.Open;

        [Header("Closed Tube (32\" commercial scale)")]
        [Tooltip("Радиус закрытой трубы (метры).")]
        [Min(0.3f)] public float TubeRadius = 0.52f;

        [Tooltip("Толщина стенки закрытой трубы (метры).")]
        [Range(0.03f, 1.5f)] public float WallThickness = 0.08f;

        [Header("Open Flume (42\" commercial scale)")]
        [Tooltip("Форма сечения открытого жёлоба: прямоугольная или скруглённая.")]
        public OpenFlumeShape OpenShape = OpenFlumeShape.Square;
        [Tooltip("Ширина открытого жёлоба (метры); задаёт и боковой предел для катания игрока.")]
        [Min(0.6f)] public float FlumeWidth = 1.07f;
        [Tooltip("Глубина открытого жёлоба (метры).")]
        [Min(0.15f)] public float FlumeDepth = 0.32f;
        [Tooltip("Высота бортика по краям открытого жёлоба (метры).")]
        [Min(0.1f)] public float FlumeLipHeight = 0.28f;
        [Tooltip("Радиус скругления углов сечения открытого жёлоба (метры).")]
        [Min(0.05f)] public float FlumeCornerRadius = 0.14f;
        [Tooltip("Вынос (свес) верхней кромки борта наружу открытого жёлоба (метры).")]
        [Min(0f)] public float CopingOverhang = 0f;

        [Tooltip("Толщина дна, бортов и бортика открытого жёлоба (метры).")]
        [Range(0.03f, 1.5f)] public float FlumeWallThickness = 0.1f;

        [Header("Mesh Quality")]
        [Tooltip("Число сегментов в кольце сечения; больше — глаже стенки, но тяжелее меш.")]
        [Range(8, 40)] public int RingSegments = 24;
        [Tooltip("Число выборок пути между опорными точками; больше — плотнее и глаже кольца вдоль горки.")]
        [Range(4, 32)] public int SamplesPerSegment = 14;
        [Tooltip("Сила наклона (виража) трубы в поворотах; 0 — без наклона.")]
        [Range(0f, 1.5f)] public float BankStrength = 1f;

        [Header("Appearance")]
        [Tooltip("Цвет материала горки.")]
        public Color SlideColor = new(0.15f, 0.75f, 0.95f, 1f);
        [Tooltip("Материал горки; если не задан, создаётся стандартный материал автоматически.")]
        public Material SlideMaterial;

        [Header("Stripes")]
        [Tooltip("Красить горку поперечными цветными полосами (как у больших парковых горок) вместо одного цвета. Материал при этом всегда свой (SlideMaterial не трогается).")]
        public bool Striped;

        [Tooltip("Палитра полос; последовательность вдоль горки выбирается случайно из этих цветов (две соседние полосы разного цвета).")]
        public Color[] StripeColors =
        {
            new(0.10f, 0.35f, 0.85f, 1f),
            new(0.40f, 0.72f, 0.95f, 1f),
            new(0.98f, 0.82f, 0.12f, 1f),
            new(0.96f, 0.96f, 0.94f, 1f),
            new(0.20f, 0.78f, 0.72f, 1f),
            new(0.06f, 0.14f, 0.48f, 1f)
        };

        [Tooltip("Длина одной полосы вдоль горки, м.")]
        [Min(0.5f)] public float StripeLength = 5f;

        [Tooltip("Полосы ВДОЛЬ горки (цветные дорожки по ширине жёлоба, как флаг) вместо поперечных секций.")]
        public bool StripesAlong;

        [Tooltip("Число продольных дорожек по ширине жёлоба (только для полос вдоль).")]
        [Range(2, 12)] public int StripeLanes = 5;

        [Tooltip("Зерно случайной последовательности полос этой горки.")]
        public int StripeSeed;

        [Tooltip("Собственное свечение непрозрачной горки в её же цвете (доля цвета полосы/горки, добавляется к освещению). Окружение — картинка без освещения и выглядит сочно, а горки под солнцем 1.35 выходили тусклыми (06.11.2026). 0 — только освещение. Стеклянных горок и общего материала Slide Material не касается.")]
        [Range(0f, 1f)] public float SelfIllumination = 0.85f;

        [Tooltip("Доля непрозрачных горок, которые рисуются ОДНОТОННЫМИ из старого поцарапанного пластика (шейдер WaterSlides/SlidePlastic + процедурная карта износа WaterSlidePlasticWear). Царапины бывают только на однотонных горках — полосатые всегда новые (просьба 06.11.2026). Цвет — самый насыщенный из палитры полос горки. Какая горка однотонная, решает её Stripe Seed — геометрия не пересобирается, выбор стабилен между запусками. 0 — все горки полосатые и новые.")]
        [Range(0f, 1f)] public float WornPlasticChance = 0.35f;
        [Tooltip("Сила износа изношенной горки: заметность царапин, потёртостей и их матовости. 0 — как новая.")]
        [Range(0f, 1f)] public float WornPlasticStrength = 1f;
        [Tooltip("Длина одной плитки карты ПЯТЕН износа (потёртости, грязь) вдоль горки, м; поперёк плитка всегда на всю ширину жёлоба, вдоль шейдер растягивает её ещё в ~3.4 раза. Царапин не касается — они разбросаны случайно, см. Scratch Density.")]
        [Min(0.5f)] public float WearTileLength = 3f;
        [Tooltip("Количество царапин на изношенной горке: доля клеток (~1.3 × 2 м, два слоя), в которых лежит штамп с 1–2 длинными и парой коротких царапин. Расположение случайное по зерну Stripe Seed и не повторяется вдоль горки (плитка с царапинами заметно повторялась — 10.11.2026). У стенок царапин примерно втрое меньше, чем на дне. 0 — без царапин, только потёртости.")]
        [Range(0f, 1f)] public float ScratchDensity = 0.5f;

        // Изношенная ли эта горка: детерминированно по зерну полос.
        public bool IsWornPlastic
        {
            get
            {
                if (WornPlasticChance <= 0f || WornPlasticStrength <= 0f || GlassTube)
                    return false;
                unchecked
                {
                    uint h = (uint)StripeSeed * 2654435761u ^ 0x9E3779B9u;
                    h ^= h >> 15;
                    h *= 2246822519u;
                    h ^= h >> 13;
                    return (h % 1000u) / 1000f < WornPlasticChance;
                }
            }
        }

        // Цвет однотонной изношенной горки: из палитры полос — по зерну среди
        // насыщенных цветов (белёсые и тёмные оттенки палитры не берутся); без
        // палитры — Slide Color.
        Color PickSolidWornColor()
        {
            if (StripeColors == null || StripeColors.Length == 0)
                return SlideColor;
            int count = 0;
            foreach (var c in StripeColors)
                if (IsVivid(c)) count++;
            if (count == 0)
                return StripeColors[0];
            int pick = (int)(((uint)StripeSeed >> 7) % (uint)count);
            foreach (var c in StripeColors)
                if (IsVivid(c) && pick-- == 0)
                    return c;
            return StripeColors[0];
        }

        static bool IsVivid(Color c)
        {
            Color.RGBToHSV(c, out _, out float s, out float v);
            return s > 0.45f && v > 0.55f;
        }

        [Header("Boost Strips")]
        [Tooltip("Число ускоряющих полос на горке. Расставляются случайно по длине (Boost Seed), не ближе 6 м друг к другу, не в первых 12 % и последних 12 % пути.")]
        [Min(0)] public int BoostStripCount;
        [Tooltip("Длина каждой ускоряющей полосы, м.")]
        [Min(1f)] public float BoostStripLength = 6f;
        [Tooltip("Зерно расстановки ускоряющих полос.")]
        public int BoostSeed;
        [Tooltip("Материал ускоряющих полос (шейдер WaterSlides/BoostStrip). Пусто — полосы работают, но не рисуются.")]
        public Material BoostStripMaterial;
        // Полосы: x — начало по длине пути (м), y — длина (м). Считает Rebuild.
        [SerializeField, HideInInspector] List<Vector2> _boostStrips = new();

        [Header("Glass Tube")]
        [Tooltip("Прозрачное «стекло» со светящимися кольцами подсветки (цвета — из палитры полос): и для закрытых труб, и для открытых жёлобов.")]
        public bool GlassTube;

        [Tooltip("Цвет и прозрачность стекла (альфа — насколько труба непрозрачна).")]
        public Color GlassTint = new(0.75f, 0.9f, 1f, 0.28f);

        [Tooltip("Цвет неона этой горки (кромки, линия по дну, кольца). Прозрачный (альфа 0) — выбрать из палитры полос.")]
        public Color GlassNeon = Color.clear;

        [Tooltip("Шаг колец подсветки вдоль трубы, м.")]
        [Min(0.5f)] public float TubeRingSpacing = 3f;

        [Tooltip("Ширина кольца подсветки, м.")]
        [Min(0.1f)] public float TubeRingWidth = 0.5f;

        [Tooltip("Яркость свечения колец (множитель эмиссии).")]
        [Min(0f)] public float TubeRingGlow = 1.6f;

        [Tooltip("Свечение всего стекла трубы изнутри неоновым цветом (как у ночных горок с подсветкой): 0 — стекло не светится, только кольца.")]
        [Min(0f)] public float GlassGlow = 0.45f;

        [Tooltip("Продольные неоновые линии стекла: светящиеся кромки открытого жёлоба и линия по дну (у закрытой трубы — четыре линии по окружности). Как у прозрачных горок в рилсах.")]
        public bool GlassEdgeLines = true;

        [Tooltip("Ширина продольной линии по дну, доля профиля сечения (0..1).")]
        [Range(0.01f, 0.2f)] public float GlassFloorLineWidth = 0.05f;

        [Tooltip("Рисунок неона: линии вдоль + кольца поперёк; только вдоль; только кольца; частая решётка (сетка). Продольные линии у скруглённого жёлоба — кромки и дно, у U-образного (Square) — две линии по уголкам (стык дна и стенок).")]
        public GlassLineStyle GlassLines = GlassLineStyle.EdgesAndRings;

        [Tooltip("Шаг ячейки решётки (стиль Grid), м — и колец поперёк, и продольных линий (по длине профиля).")]
        [Min(0.2f)] public float GlassGridSpacing = 0.6f;

        [Header("Runtime")]
        [Tooltip("Создавать MeshCollider поверхности катания для физики игрока.")]
        public bool GenerateCollider = true;

        [Tooltip("Keep up/over vertical motion (vertical loops). Disables the 'always descend' clamp on the mesh path.")]
        public bool PreserveVerticalMotion;

        [Tooltip("Set when this slide contains a loop segment (disables the 'always descend' clamp on the mesh path).")]
        public bool HasLoop;

        MeshFilter _meshFilter;
        MeshRenderer _meshRenderer;
        MeshCollider _meshCollider;
        Material _runtimeMaterial;
        Texture2D _stripeTexture;
        Texture2D _glowTexture;
        List<WaterSlideMeshBuilder.RideFrame> _rideFrames;

        // Число полос в текстуре-палитре; дальше последовательность повторяется.
        const int StripeBands = 64;

        // Cached centreline frames of the rendered slide, in world space. Rebuilt lazily
        // after every mesh rebuild; used by the player to ride the slide smoothly.
        public IReadOnlyList<WaterSlideMeshBuilder.RideFrame> RideFrames
        {
            get
            {
                if (_rideFrames == null || _rideFrames.Count == 0)
                    _rideFrames = WaterSlideMeshBuilder.BuildRideFrames(GetWorldControlPoints(), BuildMeshSettings());
                return _rideFrames;
            }
        }

        // Half of the usable cross-section width: how far the rider may drift sideways
        // from the centreline before being pushed back.
        public float RideHalfWidth => Enclosure == SlideEnclosure.Open ? FlumeWidth * 0.5f : TubeRadius;

        public Vector3 EndPosition => ControlPoints.Count > 0 ? ControlPoints[^1] : transform.position;
        public Vector3 EndTangent => WaterSlideMeshBuilder.GetPathEndTangent(ControlPoints);

        // World-space end of the ACTUAL rendered centreline (after smoothing + descent
        // clamping). The mesh can push the end lower than the raw control point, so connections
        // must use these to stay aligned with what is drawn.
        public Vector3 ProcessedWorldEnd
        {
            get
            {
                var p = WaterSlideMeshBuilder.BuildProcessedPath(GetWorldControlPoints(), BuildMeshSettings());
                return p.Count > 0 ? p[^1] : transform.TransformPoint(EndPosition);
            }
        }

        public Vector3 ProcessedWorldEndTangent
        {
            get
            {
                var p = WaterSlideMeshBuilder.BuildProcessedPath(GetWorldControlPoints(), BuildMeshSettings());
                if (p.Count < 2)
                    return transform.TransformDirection(EndTangent);
                var dir = p[^1] - p[^2];
                return dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            }
        }

        public bool IsRideColliderReady =>
            _meshCollider != null
            && _meshCollider.enabled
            && _meshCollider.sharedMesh != null
            && _meshCollider.sharedMesh.vertexCount > 0;

        public bool TryGetRideSpawnPose(
            float heightAboveSurface,
            float bodyClearance,
            out Vector3 position,
            out Vector3 forward)
        {
            position = default;
            forward = Vector3.forward;

            var settings = BuildMeshSettings();
            var worldPoints = GetWorldControlPoints();
            if (!WaterSlideMeshBuilder.TryGetRideStartPose(
                    worldPoints, settings, heightAboveSurface, bodyClearance, out position, out forward))
                return false;

            if (TryRefineSpawnWithRaycast(ref position, forward, heightAboveSurface, bodyClearance))
                return true;

            return true;
        }

        bool TryRefineSpawnWithRaycast(
            ref Vector3 position,
            Vector3 forward,
            float heightAboveSurface,
            float bodyClearance)
        {
            if (_meshCollider == null || !_meshCollider.enabled || _meshCollider.sharedMesh == null)
                return false;

            float clearance = Mathf.Max(heightAboveSurface, 0.5f) + Mathf.Max(bodyClearance, 0f);
            Vector3 probeOrigin = position + Vector3.up * 12f;
            var hits = Physics.RaycastAll(probeOrigin, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);

            float bestDist = float.MaxValue;
            bool found = false;
            Vector3 bestPoint = position;

            foreach (var hit in hits)
            {
                if (hit.collider != _meshCollider)
                    continue;

                if (hit.distance >= bestDist)
                    continue;

                bestDist = hit.distance;
                // World-up clearance: along the surface normal the point drifts off the
                // slide on tilted entry ramps, and the vertical drop then misses the flume.
                bestPoint = hit.point + Vector3.up * clearance + forward.normalized * 0.15f;
                found = true;
            }

            if (!found)
                return false;

            position = bestPoint;
            return true;
        }

        void OnEnable()
        {
            EnsureComponents();
            Rebuild();
        }

        void OnValidate()
        {
            if (ControlPoints == null || ControlPoints.Count < 2)
            {
                ControlPoints ??= new List<Vector3>();
                if (ControlPoints.Count < 2)
                {
                    ControlPoints.Clear();
                    ControlPoints.Add(new Vector3(0f, 10f, 0f));
                    ControlPoints.Add(new Vector3(0f, 0f, 20f));
                }
            }

            EnsureComponents();

#if UNITY_EDITOR
            // Rebuild() cascades into water prefab instantiation; deferred out of
            // OnValidate to avoid "SendMessage cannot be called during OnValidate" spam.
            // OnValidate also fires while ENTERING play mode — defer unconditionally.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                    Rebuild();
            };
#else
            Rebuild();
#endif
        }

        public void Rebuild()
        {
            if (ControlPoints == null || ControlPoints.Count < 2)
                return;

            EnsureComponents();
            _rideFrames = null;

            var settings = BuildMeshSettings();
            var worldPoints = GetWorldControlPoints();
            var visualMesh = WaterSlideMeshBuilder.BuildTube(worldPoints, settings);
            var rideMesh = WaterSlideMeshBuilder.BuildRideSurface(worldPoints, settings);

            if (visualMesh == null)
                return;

            KeepDerivedMeshTransient(visualMesh);
            KeepDerivedMeshTransient(rideMesh);
            _meshFilter.sharedMesh = visualMesh;

            // Стекло и для открытых жёлобов (прозрачные горки из рилсов), не только труб.
            bool glass = GlassTube;
            // Изношенная горка всегда однотонная (царапины — только на однотонных).
            bool solidWorn = !glass && IsWornPlastic;
            bool striped = !glass && !solidWorn && Striped && StripeColors != null && StripeColors.Length > 0;
            var mat = SlideMaterial != null && !striped && !glass && !solidWorn ? SlideMaterial : (_runtimeMaterial ??= CreateDefaultMaterial());
            // Стекло живёт на Unlit/Transparent (см. ApplyGlassTube); при смене горки
            // со стеклянной на полосатую тот же runtime-материал возвращаем на Standard.
            // Непрозрачный runtime-материал — пластик (WaterSlides/SlidePlastic, свечение
            // и износ); если шейдера нет — Standard, как раньше.
            if (!glass && mat == _runtimeMaterial)
            {
                var opaque = WaterSlidesRendering.GetPlasticShader() ?? WaterSlidesRendering.GetSlideShader();
                if (mat.shader != opaque)
                    mat.shader = opaque;
            }
            WaterSlidesRendering.ConfigureSlideMaterial(mat);
            if (glass)
                ApplyGlassTube(mat);
            else if (striped)
                ApplyStripes(mat);
            else
            {
                if (mat == _runtimeMaterial)
                    mat.mainTexture = null;
                // Однотонная — чуть светлее палитры: насыщенные базовые цвета в один тон
                // читались тёмными (06.11.2026, «горки ярче»).
                WaterSlidesRendering.ApplySlideColor(mat, solidWorn ? Color.Lerp(PickSolidWornColor(), Color.white, 0.15f) : SlideColor);
            }
            if (!glass && mat == _runtimeMaterial)
                ApplySelfIllumination(mat, striped);
            _meshRenderer.sharedMaterial = mat;
            // Стекло не принимает тени: прозрачный Standard берёт экранную маску теней,
            // а она посчитана по глубине ЗЕМЛИ за стеклом (260 м ниже) — тень горки на
            // земле с муаром карты теней рисовалась на стекле решёткой прямоугольников.
            _meshRenderer.receiveShadows = !glass;

            if (GenerateCollider && rideMesh != null)
            {
                _meshCollider.sharedMesh = rideMesh;
                _meshCollider.convex = false;
                _meshCollider.material = WaterSlidePhysicsMaterials.Slide;
                _meshCollider.enabled = true;
                Physics.SyncTransforms();
            }
            else
            {
                _meshCollider.enabled = false;
            }

            RebuildWater(settings, worldPoints);
            RebuildBoostStrips();
        }

        internal void KeepDerivedMeshTransient(Mesh mesh)
        {
            // OnEnable recreates these meshes from the saved path.
            if (mesh != null && GetComponentInParent<WaterSlideCourse>() is { PreservesSceneLayout: true })
                mesh.hideFlags |= HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        // ================= УСКОРЯЮЩИЕ ПОЛОСЫ =================

        public IReadOnlyList<Vector2> BoostStrips => _boostStrips;

        // Лежит ли дистанция s (м вдоль пути) на ускоряющей полосе.
        public bool IsInBoostStrip(float s)
        {
            for (int i = 0; i < _boostStrips.Count; i++)
            {
                var strip = _boostStrips[i];
                if (s >= strip.x && s <= strip.x + strip.y)
                    return true;
            }
            return false;
        }

        GameObject _boostRoot;
        Mesh _boostMesh;

        void RebuildBoostStrips()
        {
            _boostStrips.Clear();
            var frames = RideFrames;
            if (frames == null || frames.Count < 2)
            {
                ClearBoostMesh();
                return;
            }

            var cum = new float[frames.Count];
            for (int i = 1; i < frames.Count; i++)
                cum[i] = cum[i - 1] + Vector3.Distance(frames[i - 1].Position, frames[i].Position);
            float total = cum[^1];
            float len = Mathf.Max(BoostStripLength, 1f);

            if (BoostStripCount > 0 && total > len * 3f)
            {
                var rng = new System.Random(BoostSeed);
                float lo = total * 0.12f;
                float hi = total * 0.88f - len;
                for (int n = 0; n < BoostStripCount && hi > lo; n++)
                {
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        float start = Mathf.Lerp(lo, hi, (float)rng.NextDouble());
                        bool clash = false;
                        for (int k = 0; k < _boostStrips.Count; k++)
                            if (Mathf.Abs(_boostStrips[k].x - start) < len + 6f) { clash = true; break; }
                        if (clash)
                            continue;
                        _boostStrips.Add(new Vector2(start, len));
                        break;
                    }
                }
                _boostStrips.Sort((a, b) => a.x.CompareTo(b.x));
            }

            BuildBoostMesh(frames, cum);
        }

        void ClearBoostMesh()
        {
            if (_boostMesh != null)
            {
                if (Application.isPlaying) Destroy(_boostMesh); else DestroyImmediate(_boostMesh);
                _boostMesh = null;
            }
            if (_boostRoot == null)
            {
                var existing = transform.Find("BoostStrips");
                if (existing != null)
                    _boostRoot = existing.gameObject;
            }
            if (_boostRoot != null && _boostRoot.TryGetComponent(out MeshRenderer r))
                r.enabled = false;
        }

        // Лента на дне жёлоба (в трубе — на нижней дуге сечения) по каждой полосе:
        // поперёк 7 точек, вдоль шаг 0.5 м; UV.x — поперёк 0..1, UV.y — метры от
        // начала полосы (шейдер гонит по ней шевроны).
        void BuildBoostMesh(IReadOnlyList<WaterSlideMeshBuilder.RideFrame> frames, float[] cum)
        {
            ClearBoostMesh();
            if (_boostStrips.Count == 0)
                return;

            if (_boostRoot == null)
            {
                var existing = transform.Find("BoostStrips");
                _boostRoot = existing != null ? existing.gameObject : new GameObject("BoostStrips");
                _boostRoot.transform.SetParent(transform, false);
                _boostRoot.transform.localPosition = Vector3.zero;
                _boostRoot.transform.localRotation = Quaternion.identity;
                _boostRoot.transform.localScale = Vector3.one;
            }
            if (!_boostRoot.TryGetComponent(out MeshFilter filter))
                filter = _boostRoot.AddComponent<MeshFilter>();
            if (!_boostRoot.TryGetComponent(out MeshRenderer renderer))
                renderer = _boostRoot.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = BoostStripMaterial;
            renderer.enabled = BoostStripMaterial != null;

            const int columns = 7;
            const float step = 0.5f;
            bool open = Enclosure == SlideEnclosure.Open;
            bool rounded = open && OpenShape == OpenFlumeShape.Rounded;
            float halfW = open ? FlumeWidth * 0.5f : TubeRadius;
            float stripHalf = open ? halfW * 0.55f : TubeRadius * 0.45f;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            foreach (var strip in _boostStrips)
            {
                int rows = Mathf.Max(Mathf.CeilToInt(strip.y / step), 1) + 1;
                int baseIndex = verts.Count;
                for (int r = 0; r < rows; r++)
                {
                    float s = Mathf.Min(strip.x + r * step, strip.x + strip.y);
                    SampleFrameAt(frames, cum, s, out Vector3 center, out Vector3 tangent, out Vector3 up);
                    Vector3 right = Vector3.Cross(up, tangent).normalized;
                    for (int c = 0; c < columns; c++)
                    {
                        float x = Mathf.Lerp(-stripHalf, stripHalf, c / (float)(columns - 1));
                        Vector3 p;
                        if (open)
                        {
                            float y = rounded
                                ? -FlumeDepth * Mathf.Sqrt(Mathf.Max(1f - (x / halfW) * (x / halfW), 0f))
                                : -FlumeDepth;
                            p = center + right * x + up * (y + 0.025f);
                        }
                        else
                        {
                            float angle = x / Mathf.Max(TubeRadius, 0.05f);
                            float rr = TubeRadius - 0.025f;
                            p = center + (right * Mathf.Sin(angle) - up * Mathf.Cos(angle)) * rr;
                        }
                        verts.Add(p);
                        uvs.Add(new Vector2(c / (float)(columns - 1), s - strip.x));
                    }
                }
                for (int r = 0; r < rows - 1; r++)
                {
                    for (int c = 0; c < columns - 1; c++)
                    {
                        int a = baseIndex + r * columns + c;
                        int b = a + 1;
                        int d = a + columns;
                        int e = d + 1;
                        tris.Add(a); tris.Add(d); tris.Add(b);
                        tris.Add(b); tris.Add(d); tris.Add(e);
                    }
                }
            }

            _boostMesh = new Mesh { name = "BoostStrips" };
            _boostMesh.SetVertices(verts);
            _boostMesh.SetUVs(0, uvs);
            _boostMesh.SetTriangles(tris, 0);
            _boostMesh.RecalculateNormals();
            _boostMesh.RecalculateBounds();
            filter.sharedMesh = _boostMesh;
        }

        static void SampleFrameAt(IReadOnlyList<WaterSlideMeshBuilder.RideFrame> frames, float[] cum, float s,
            out Vector3 center, out Vector3 tangent, out Vector3 up)
        {
            s = Mathf.Clamp(s, 0f, cum[^1]);
            int lo = 0, hi = cum.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (cum[mid] <= s) lo = mid; else hi = mid;
            }
            float t = (s - cum[lo]) / Mathf.Max(cum[hi] - cum[lo], 1e-4f);
            center = Vector3.LerpUnclamped(frames[lo].Position, frames[hi].Position, t);
            Vector3 tb = Vector3.Slerp(frames[lo].Forward, frames[hi].Forward, t);
            tangent = tb.sqrMagnitude > 1e-6f ? tb.normalized : frames[lo].Forward.normalized;
            Vector3 ub = Vector3.Slerp(frames[lo].Up, frames[hi].Up, t);
            up = ub.sqrMagnitude > 1e-6f ? ub.normalized : Vector3.up;
        }

        void RebuildWater(WaterSlideMeshBuilder.Settings settings, List<Vector3> worldPoints)
        {
            var water = GetComponent<WaterSlideWater>();
            if (water == null)
                return;

            water.Rebuild(settings, worldPoints, this);
        }

        public WaterSlideMeshBuilder.Settings BuildMeshSettings() => new()
        {
            Enclosure = Enclosure,
            WallThickness = Enclosure == SlideEnclosure.Open ? FlumeWallThickness : WallThickness,
            RingSegments = RingSegments,
            SamplesPerSegment = SamplesPerSegment,
            BankStrength = BankStrength,
            PreserveVerticalMotion = PreserveVerticalMotion,
            HasLoop = HasLoop,
            ClosedTube = new SlideCrossSection.ClosedTube { Radius = TubeRadius },
            OpenFlume = new SlideCrossSection.OpenFlume
            {
                Width = FlumeWidth,
                Depth = FlumeDepth,
                LipHeight = FlumeLipHeight,
                CornerRadius = FlumeCornerRadius,
                CopingOverhang = CopingOverhang,
                Shape = OpenShape
            }
        };

        public void SetControlPointsLocal(IReadOnlyList<Vector3> localPoints)
        {
            ControlPoints = new List<Vector3>(localPoints);
            Rebuild();
        }

        public void OffsetControlPoints(Vector3 offset)
        {
            for (int i = 0; i < ControlPoints.Count; i++)
                ControlPoints[i] += offset;
            Rebuild();
        }

        public List<Vector3> GetWorldControlPoints()
        {
            var world = new List<Vector3>(ControlPoints.Count);
            foreach (var p in ControlPoints)
                world.Add(transform.TransformPoint(p));
            return world;
        }

        void EnsureComponents()
        {
            _meshFilter = GetOrAdd<MeshFilter>();
            _meshRenderer = GetOrAdd<MeshRenderer>();
            _meshCollider = GetOrAdd<MeshCollider>();
        }

        T GetOrAdd<T>() where T : Component
        {
            if (!TryGetComponent(out T component))
                component = gameObject.AddComponent<T>();
            return component;
        }

        Material CreateDefaultMaterial()
        {
            var mat = new Material(WaterSlidesRendering.GetSlideShader());
            WaterSlidesRendering.ConfigureSlideMaterial(mat);
            ApplySlideColor(mat);
            return mat;
        }

        // Свечение Standard: _EmissionMap использует тот же тайлинг, что и основная
        // текстура, поэтому полосы светятся каждая своим цветом.
        void ApplySelfIllumination(Material mat, bool striped)
        {
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (mat.HasProperty("_WearTex"))
            {
                // Пластик: свечение — параметр шейдера (итоговый цвет × доля, включая
                // износ); износ — общая процедурная карта со своим тайлингом.
                mat.SetFloat("_EmissionStrength", Mathf.Max(SelfIllumination, 0f));
                bool worn = IsWornPlastic;
                mat.SetFloat("_WearStrength", worn ? WornPlasticStrength : 0f);
                // Старый пластик и в целом глянцует меньше нового.
                mat.SetFloat("_Glossiness", worn ? Mathf.Lerp(0.92f, 0.7f, WornPlasticStrength) : 0.92f);
                if (worn)
                {
                    mat.SetTexture("_WearTex", WaterSlidePlasticWear.WearTexture);
                    mat.SetTexture("_ScratchTex", WaterSlidePlasticWear.ScratchAtlas);
                    // Поперёк: одна плитка на всю ширину профиля (середина плитки — дно,
                    // там царапин больше); вдоль: WearTileLength метров, сдвиг по зерну —
                    // у соседних изношенных горок рисунок не совпадает.
                    float across = Enclosure == SlideEnclosure.Open ? 1f / SlideCrossSection.OpenProfileMaxT : 1f;
                    mat.SetTextureScale("_WearTex", new Vector2(across, 1f / Mathf.Max(WearTileLength, 0.5f)));
                    mat.SetTextureOffset("_WearTex", new Vector2(0f, ((StripeSeed & 0xFFFF) % 997) / 997f));
                    // Царапины: клетки ~ScratchCellWidth × ScratchCellLength м по развёртке
                    // профиля; зерно горки сдвигает хэш — у каждой горки свой разброс.
                    float unrolled = Enclosure == SlideEnclosure.Open
                        ? FlumeWidth + 2f * (FlumeDepth + FlumeLipHeight)
                        : 2f * Mathf.PI * TubeRadius;
                    float columns = Mathf.Max(2f, Mathf.Round(unrolled / WaterSlidePlasticWear.ScratchCellWidth));
                    mat.SetVector("_ScratchCells", new Vector4(
                        columns,
                        Mathf.Max(WearTileLength, 0.5f) / WaterSlidePlasticWear.ScratchCellLength,
                        ScratchDensity,
                        (StripeSeed & 0xFFFF) % 911));
                }
                return;
            }
            if (SelfIllumination <= 0f)
            {
                mat.SetTexture("_EmissionMap", null);
                mat.DisableKeyword("_EMISSION");
                return;
            }
            Color tint = striped ? Color.white : SlideColor;
            mat.SetTexture("_EmissionMap", striped ? mat.mainTexture : null);
            mat.SetColor("_EmissionColor", new Color(tint.r, tint.g, tint.b, 1f) * SelfIllumination);
            mat.EnableKeyword("_EMISSION");
        }

        void ApplySlideColor(Material mat) => WaterSlidesRendering.ApplySlideColor(mat, SlideColor);

        // Полосы — текстура-палитра 1×N точек (одна точка = одна полоса), натянутая на
        // UV.y меша, где UV.y — длина дуги в метрах: тайлинг 1/(N·длина полосы) даёт
        // полосы ровно заданной длины на любой горке. Фильтрация точечная — границы
        // полос резкие, как у настоящих секционных горок.
        void ApplyStripes(Material mat)
        {
            DestroyStripeTexture();

            // Поперечные секции: палитра по UV.y (метры вдоль). Продольные дорожки:
            // палитра по UV.x (параметр профиля 0..OpenProfileMaxT по ширине жёлоба).
            bool along = StripesAlong && Enclosure == SlideEnclosure.Open;
            int bands = along ? Mathf.Clamp(StripeLanes, 2, 12) : StripeBands;
            _stripeTexture = new Texture2D(along ? bands : 1, along ? 1 : bands, TextureFormat.RGBA32, false)
            {
                name = "SlideStripes",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };

            var rng = new System.Random(StripeSeed);
            int prev = -1;
            for (int i = 0; i < bands; i++)
            {
                int pick = rng.Next(StripeColors.Length);
                if (StripeColors.Length > 1 && pick == prev)
                    pick = (pick + 1 + rng.Next(StripeColors.Length - 1)) % StripeColors.Length;
                prev = pick;
                var c = StripeColors[pick];
                c.a = 1f;
                if (along)
                    _stripeTexture.SetPixel(i, 0, c);
                else
                    _stripeTexture.SetPixel(0, i, c);
            }
            _stripeTexture.Apply(false, false);

            mat.mainTexture = _stripeTexture;
            mat.mainTextureScale = along
                ? new Vector2(1f / SlideCrossSection.OpenProfileMaxT, 1f)
                : new Vector2(1f, 1f / (StripeBands * Mathf.Max(StripeLength, 0.5f)));
            mat.mainTextureOffset = Vector2.zero;
            WaterSlidesRendering.ApplySlideColor(mat, Color.white);
        }

        // Стеклянная труба: основная текстура — прозрачное стекло с полупрозрачными
        // цветными кольцами, карта эмиссии — те же кольца (светятся), стекло чёрное.
        // Текстура 1×N, точка = 0.125 м вдоль трубы (UV.y — метры), повтор каждые N·0.125 м.
        void ApplyGlassTube(Material mat)
        {
            DestroyStripeTexture();

            bool grid = GlassLines == GlassLineStyle.Grid;
            bool rings = GlassLines != GlassLineStyle.EdgesOnly;
            bool lines = GlassEdgeLines && GlassLines != GlassLineStyle.RingsOnly;
            // Решётка: тонкие кольца через GlassGridSpacing — нужен мелкий тексель.
            float texel = grid ? 0.05f : 0.125f;
            const int N = 256;
            // По X — поперёк сечения (UV.x = параметр профиля): продольные неоновые линии.
            const int W = 256;
            _stripeTexture = new Texture2D(W, N, TextureFormat.RGBA32, false)
            {
                name = "GlassTube", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat
            };
            _glowTexture = new Texture2D(W, N, TextureFormat.RGBA32, false)
            {
                name = "GlassTubeGlow", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat
            };

            int period = Mathf.Max(Mathf.RoundToInt((grid ? GlassGridSpacing : TubeRingSpacing) / texel), 2);
            int ringWidth = grid ? 1 : Mathf.Clamp(Mathf.RoundToInt(TubeRingWidth / texel), 1, period - 1);
            var palette = StripeColors != null && StripeColors.Length > 0
                ? StripeColors
                : new[] { new Color(0.2f, 0.6f, 1f), new Color(1f, 0.3f, 0.3f), new Color(0.3f, 1f, 0.5f) };
            // Один неоновый цвет на трубу: им подсвечено всё стекло, кольца — ярче.
            var rng = new System.Random(StripeSeed);
            Color neon = GlassNeon.a > 0.01f ? GlassNeon : palette[rng.Next(palette.Length)];
            // Белый/тёмный из палитры полос — не неон; берём насыщенный оттенок.
            float h, s, v;
            Color.RGBToHSV(neon, out h, out s, out v);
            neon = Color.HSVToRGB(h, Mathf.Max(s, 0.75f), 1f);
            // Бесцветное стекло (малая альфа) неоном почти не красится — светятся кромки.
            var glassC = Color.Lerp(GlassTint, neon, GlassTint.a < 0.15f ? 0.15f : 0.5f);
            glassC.a = GlassTint.a;
            // Неон почти непрозрачный: без эмиссии (unlit) яркость линий — только их цвет и альфа.
            var ringC = new Color(neon.r, neon.g, neon.b, Mathf.Min(GlassTint.a + 0.65f, 0.95f));
            bool open = Enclosure == SlideEnclosure.Open;
            // Открытый жёлоб: нормированный параметр профиля 0..1 — левая кромка
            // (вертикальная губа) 0..0.12, дно 0.5, правая кромка 0.88..1
            // (SlideCrossSection.SampleOpenRounded). Закрытая труба: четыре линии
            // по окружности.
            const float lipFrac = 0.12f;
            float half = GlassFloorLineWidth * 0.5f;
            // U-образный (Square) жёлоб: продольный неон — две линии по уголкам, дуги
            // скругления на нормированном t 0.11..0.19 и 0.50..0.58 (SampleOpenSquare).
            bool square = open && OpenShape == OpenFlumeShape.Square;
            // Решётка: продольные линии через GlassGridSpacing по длине профиля.
            float perimeter = open
                ? FlumeWidth + 2f * (FlumeDepth + FlumeLipHeight)
                : 2f * Mathf.PI * TubeRadius;
            float gridStep = Mathf.Clamp(GlassGridSpacing / Mathf.Max(perimeter, 0.5f), 0.05f, 0.5f);
            const float gridHalf = 0.006f;
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                bool isLine = false;
                if (lines)
                {
                    if (square)
                        isLine = (u >= 0.11f && u <= 0.19f) || (u >= 0.50f && u <= 0.58f);
                    else if (open)
                        isLine = u <= lipFrac || u >= 1f - lipFrac || Mathf.Abs(u - 0.5f) <= half;
                    else
                    {
                        float q = Mathf.Repeat(u, 0.25f);
                        isLine = q <= half || q >= 0.25f - half;
                    }
                    if (grid)
                    {
                        float q = Mathf.Repeat(u, gridStep);
                        isLine |= q <= gridHalf || q >= gridStep - gridHalf;
                    }
                }

                for (int i = 0; i < N; i++)
                {
                    bool isRing = rings && i % period < ringWidth;
                    bool lit = isRing || isLine;
                    _stripeTexture.SetPixel(x, i, lit ? ringC : glassC);
                    var glow = neon * (lit ? TubeRingGlow : GlassGlow);
                    glow.a = 1f;
                    _glowTexture.SetPixel(x, i, glow);
                }
            }
            _stripeTexture.Apply(false, false);
            _glowTexture.Apply(false, false);

            // Стекло — Unlit/Transparent, НЕ Standard Fade: Standard на этом
            // двухслойном двустороннем меше рисовал тёмную решётку вдоль всех рёбер
            // полигонов (видна сквозь прозрачный жёлоб, на воде читалась «сеткой»);
            // причина в самом Standard не найдена — проверено заменой шейдера
            // (26.10.2026). Unlit заодно ближе к референсу: ровное свечение без
            // бликов и без «молочности» под скользящим углом.
            WaterSlidesRendering.ConfigureGlassMaterial(mat);
            mat.mainTexture = _stripeTexture;
            // X: у открытого жёлоба UV.x идёт 0..OpenProfileMaxT — растягиваем на 0..1.
            mat.mainTextureScale = new Vector2(open ? 1f / SlideCrossSection.OpenProfileMaxT : 1f, 1f / (N * texel));
            mat.mainTextureOffset = Vector2.zero;
            mat.color = Color.white;
            mat.SetTexture("_EmissionMap", _glowTexture);
            mat.SetColor("_EmissionColor", Color.white);
        }

        void DestroyStripeTexture()
        {
            if (_stripeTexture != null)
            {
                if (Application.isPlaying) Destroy(_stripeTexture); else DestroyImmediate(_stripeTexture);
                _stripeTexture = null;
            }
            if (_glowTexture != null)
            {
                if (Application.isPlaying) Destroy(_glowTexture); else DestroyImmediate(_glowTexture);
                _glowTexture = null;
            }
        }

        void OnDestroy()
        {
            DestroyStripeTexture();
        }
    }
}
