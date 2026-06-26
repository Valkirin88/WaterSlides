using System.Collections.Generic;
using UnityEngine;

namespace WaterSlides
{
    [ExecuteAlways]
    public class WaterSlideGenerator : MonoBehaviour
    {
        [Header("Path")]
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
        public SlideEnclosure Enclosure = SlideEnclosure.Open;

        [Header("Closed Tube (32\" commercial scale)")]
        [Min(0.3f)] public float TubeRadius = 0.52f;

        [Tooltip("Толщина стенки закрытой трубы (метры).")]
        [Range(0.03f, 1.5f)] public float WallThickness = 0.08f;

        [Header("Open Flume (42\" commercial scale)")]
        public OpenFlumeShape OpenShape = OpenFlumeShape.Square;
        [Min(0.6f)] public float FlumeWidth = 1.07f;
        [Min(0.15f)] public float FlumeDepth = 0.32f;
        [Min(0.1f)] public float FlumeLipHeight = 0.28f;
        [Min(0.05f)] public float FlumeCornerRadius = 0.14f;
        [Min(0f)] public float CopingOverhang = 0f;

        [Tooltip("Толщина дна, бортов и бортика открытого жёлоба (метры).")]
        [Range(0.03f, 1.5f)] public float FlumeWallThickness = 0.1f;

        [Header("Mesh Quality")]
        [Range(8, 40)] public int RingSegments = 24;
        [Range(4, 32)] public int SamplesPerSegment = 14;
        [Range(0f, 1.5f)] public float BankStrength = 1f;

        [Header("Appearance")]
        public Color SlideColor = new(0.15f, 0.75f, 0.95f, 1f);
        public Material SlideMaterial;

        [Header("Runtime")]
        public bool GenerateCollider = true;

        [Tooltip("Keep up/over vertical motion (vertical loops). Disables the 'always descend' clamp on the mesh path.")]
        public bool PreserveVerticalMotion;

        MeshFilter _meshFilter;
        MeshRenderer _meshRenderer;
        MeshCollider _meshCollider;
        Material _runtimeMaterial;

        public Vector3 StartPosition => ControlPoints.Count > 0 ? ControlPoints[0] : transform.position;
        public Vector3 EndPosition => ControlPoints.Count > 0 ? ControlPoints[^1] : transform.position;
        public Vector3 EndTangent => WaterSlideMeshBuilder.GetPathEndTangent(ControlPoints);

        // World-space endpoints of the ACTUAL rendered centreline (after smoothing + descent
        // clamping). The mesh can push the end lower than the raw control point, so connections
        // must use these to stay aligned with what is drawn.
        public Vector3 ProcessedWorldStart
        {
            get
            {
                var p = WaterSlideMeshBuilder.BuildProcessedPath(GetWorldControlPoints(), BuildMeshSettings());
                return p.Count > 0 ? p[0] : transform.TransformPoint(StartPosition);
            }
        }

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
            Rebuild();
        }

        public void Rebuild()
        {
            if (ControlPoints == null || ControlPoints.Count < 2)
                return;

            EnsureComponents();

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
                _meshCollider.enabled = true;
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
