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
        [Min(0.03f)] public float WallThickness = 0.08f;

        [Header("Open Flume (42\" commercial scale)")]
        public OpenFlumeShape OpenShape = OpenFlumeShape.Square;
        [Min(0.6f)] public float FlumeWidth = 1.07f;
        [Min(0.15f)] public float FlumeDepth = 0.32f;
        [Min(0.1f)] public float FlumeLipHeight = 0.28f;
        [Min(0.05f)] public float FlumeCornerRadius = 0.14f;
        [Min(0f)] public float CopingOverhang = 0f;

        [Header("Mesh Quality")]
        [Range(8, 40)] public int RingSegments = 24;
        [Range(4, 32)] public int SamplesPerSegment = 14;
        [Range(0f, 1.5f)] public float BankStrength = 1f;

        [Header("Appearance")]
        public Color SlideColor = new(0.15f, 0.75f, 0.95f, 1f);
        public Material SlideMaterial;

        [Header("Runtime")]
        public bool GenerateCollider = true;

        MeshFilter _meshFilter;
        MeshRenderer _meshRenderer;
        MeshCollider _meshCollider;
        Material _runtimeMaterial;

        public Vector3 StartPosition => ControlPoints.Count > 0 ? ControlPoints[0] : transform.position;
        public Vector3 EndPosition => ControlPoints.Count > 0 ? ControlPoints[^1] : transform.position;
        public Vector3 EndTangent => WaterSlideMeshBuilder.GetPathEndTangent(ControlPoints);

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

            var settings = BuildSettings();
            var worldPoints = GetWorldControlPoints();
            var visualMesh = WaterSlideMeshBuilder.BuildTube(worldPoints, settings);
            var rideMesh = WaterSlideMeshBuilder.BuildRideSurface(worldPoints, settings);

            if (visualMesh == null)
                return;

            _meshFilter.sharedMesh = visualMesh;

            if (SlideMaterial != null)
                _meshRenderer.sharedMaterial = SlideMaterial;
            else
            {
                _runtimeMaterial ??= CreateDefaultMaterial();
                ApplySlideColor(_runtimeMaterial);
                _meshRenderer.sharedMaterial = _runtimeMaterial;
            }

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
        }

        WaterSlideMeshBuilder.Settings BuildSettings() => new()
        {
            Enclosure = Enclosure,
            WallThickness = WallThickness,
            RingSegments = RingSegments,
            SamplesPerSegment = SamplesPerSegment,
            BankStrength = BankStrength,
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

        List<Vector3> GetWorldControlPoints()
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
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            var mat = new Material(shader);
            mat.SetFloat("_Smoothness", 0.92f);
            mat.SetFloat("_Metallic", 0.02f);
            ApplySlideColor(mat);
            return mat;
        }

        void ApplySlideColor(Material mat)
        {
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", SlideColor);
            else
                mat.color = SlideColor;
        }

        void OnDrawGizmosSelected()
        {
            if (ControlPoints == null || ControlPoints.Count < 2)
                return;

            Gizmos.color = Color.cyan;
            for (int i = 0; i < ControlPoints.Count; i++)
            {
                var world = transform.TransformPoint(ControlPoints[i]);
                Gizmos.DrawSphere(world, 0.4f);
                if (i > 0)
                {
                    var prev = transform.TransformPoint(ControlPoints[i - 1]);
                    Gizmos.DrawLine(prev, world);
                }
            }

            var sampled = WaterSlideMeshBuilder.SamplePath(GetWorldControlPoints(), SamplesPerSegment);
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            for (int i = 1; i < sampled.Count; i++)
                Gizmos.DrawLine(sampled[i - 1], sampled[i]);
        }
    }
}
