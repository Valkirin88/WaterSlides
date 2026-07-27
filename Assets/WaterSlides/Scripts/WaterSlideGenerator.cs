using System.Collections.Generic;
using UnityEngine;

namespace WaterSlides
{
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
        List<WaterSlideMeshBuilder.RideFrame> _rideFrames;

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

            _meshFilter.sharedMesh = visualMesh;

            var mat = SlideMaterial != null ? SlideMaterial : (_runtimeMaterial ??= CreateDefaultMaterial());
            WaterSlidesRendering.ConfigureSlideMaterial(mat);
            WaterSlidesRendering.ApplySlideColor(mat, SlideColor);
            _meshRenderer.sharedMaterial = mat;

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

        void ApplySlideColor(Material mat) => WaterSlidesRendering.ApplySlideColor(mat, SlideColor);
    }
}
