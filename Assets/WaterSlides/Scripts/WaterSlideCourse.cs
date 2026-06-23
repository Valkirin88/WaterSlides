using System.Collections.Generic;
using UnityEngine;

namespace WaterSlides
{
    public enum OpenShapeMode
    {
        Square,
        Rounded,
        Mixed
    }

    [ExecuteAlways]
    public class WaterSlideCourse : MonoBehaviour
    {
        [Header("Course")]
        [Tooltip("Сколько горок создать в цепочке. Первая всегда открытая.")]
        [Min(1)] public int SlideCount = 3;

        [Tooltip("Зерно случайности. При Live Update траектория остаётся той же, меняются только параметры.")]
        public int RandomSeed = 42;

        [Tooltip("Если включено, кнопка Generate Course каждый раз даёт новую случайную трассу.")]
        public bool RandomizeEachGeneration = true;

        [Tooltip("Добавлять MeshCollider на внутреннюю поверхность каждой горки.")]
        public bool GenerateColliders = true;

        [Tooltip("Автоматически перестраивать курс при изменении параметров в Inspector.")]
        public bool LiveUpdate = true;

        [Header("Connection Between Slides")]
        [Tooltip("Дистанция прыжка вдоль направления вылета с конца предыдущей горки (метры).")]
        public FloatRange JumpGap = new() { Min = 4f, Max = 9f };

        [Tooltip("Просадка точки приземления вниз за время прыжка (гравитация). Вход следующей трубы целится по хорде прыжка.")]
        public FloatRange JumpDrop = new() { Min = 0.5f, Max = 2.5f };

        [Tooltip("Диапазон смещения начала следующей трубы по оси X (мировые координаты, метры).")]
        public FloatRange JumpOffsetX = new() { Min = -3f, Max = 3f };

        [Tooltip("Диапазон смещения начала следующей трубы по оси Z (мировые координаты, метры).")]
        public FloatRange JumpOffsetZ = new() { Min = -3f, Max = 3f };

        [Tooltip("Макс. отклонение старта первой горки от вертикали (градусы). Следующие зеркалят угол выхода.")]
        [Range(0f, 55f)] public float MaxStartTiltAngle = 22f;

        [Tooltip("Длина восходящего старта у каждой горки (включая после прыжка).")]
        public FloatRange StartLaunchLength = new() { Min = 2f, Max = 5f };

        [Tooltip("Короткий восходящий участок после прыжка (доля от StartLaunchLength).")]
        [Range(0.25f, 0.85f)] public float ChainedRampScale = 0.55f;

        [Header("Path — Per Slide Ranges")]
        [Tooltip("Длина одной горки по пути.")]
        public FloatRange SlideLength = new() { Min = 45f, Max = 65f };

        [Tooltip("Перепад высоты одной горки.")]
        public FloatRange SlideDrop = new() { Min = 14f, Max = 22f };

        [Tooltip("Число фигур на горке (повороты, спирали…).")]
        public IntRange FeaturesPerSlide = new() { Min = 4, Max = 7 };

        [Tooltip("Радиус поворотов и спиралей.")]
        public FloatRange TurnRadius = new() { Min = 5f, Max = 14f };

        [Tooltip("Разрешить спирали на закрытых трубах (экспериментально, может давать петли).")]
        public bool AllowLoopsAndSpirals = false;

        [Header("Closed Tube — Per Slide Ranges")]
        public FloatRange TubeRadius = new() { Min = 0.45f, Max = 0.6f };
        public FloatRange WallThickness = new() { Min = 0.06f, Max = 0.1f };

        [Header("Open Flume — Per Slide Ranges")]
        public FloatRange FlumeWidth = new() { Min = 0.95f, Max = 1.2f };
        public FloatRange FlumeDepth = new() { Min = 0.26f, Max = 0.38f };
        public FloatRange FlumeLipHeight = new() { Min = 0.22f, Max = 0.34f };
        public FloatRange FlumeCornerRadius = new() { Min = 0.1f, Max = 0.18f };

        [Header("Mesh Quality — Per Slide Ranges")]
        public IntRange RingSegments = new() { Min = 20, Max = 28 };
        public IntRange SamplesPerSegment = new() { Min = 12, Max = 18 };
        public FloatRange BankStrength = new() { Min = 0.7f, Max = 1.2f };

        [Header("Slide Types")]
        [Tooltip("Чередовать открытые и закрытые горки (первая всё равно открытая).")]
        public bool MixOpenAndClosed = true;

        [Tooltip("Форма открытых горок: квадратный желоб, округлый или случайный выбор.")]
        public OpenShapeMode OpenFlumeShapeMode = OpenShapeMode.Mixed;

        [Header("Colors — Open Square")]
        [Tooltip("Палитра цветов для открытых горок с квадратным (U-образным) сечением.")]
        public Color[] OpenSquareColors =
        {
            new(0.95f, 0.45f, 0.12f, 1f),
            new(0.92f, 0.28f, 0.18f, 1f),
            new(0.98f, 0.62f, 0.15f, 1f)
        };

        [Header("Colors — Open Rounded")]
        [Tooltip("Палитра цветов для открытых горок с округлым сечением.")]
        public Color[] OpenRoundedColors =
        {
            new(0.18f, 0.82f, 0.42f, 1f),
            new(0.12f, 0.72f, 0.55f, 1f),
            new(0.35f, 0.88f, 0.28f, 1f)
        };

        [Header("Colors — Closed Tube")]
        [Tooltip("Палитра цветов для закрытых круглых труб.")]
        public Color[] ClosedColors =
        {
            new(0.12f, 0.62f, 0.95f, 1f),
            new(0.45f, 0.22f, 0.88f, 1f),
            new(0.08f, 0.48f, 0.78f, 1f)
        };

        readonly List<WaterSlideGenerator> _slides = new();

        public IReadOnlyList<WaterSlideGenerator> Slides => _slides;

        void OnEnable()
        {
            if (_slides.Count == 0)
                GenerateCourse();
        }

        [ContextMenu("Generate Course")]
        public void GenerateCourse()
        {
            Build(RandomizeEachGeneration);
        }

        public void RegenerateStable()
        {
            Build(useRandomSeed: false);
        }

        void Build(bool useRandomSeed)
        {
            ClearCourse();

            int seed = useRandomSeed
                ? RandomSeed ^ System.Environment.TickCount
                : RandomSeed;

            var rng = new System.Random(seed);
            Vector3 cursorPos = transform.position + Vector3.up * 25f;
            Vector3 pendingEntryDir = Vector3.zero;

            for (int i = 0; i < SlideCount; i++)
            {
                var slideRng = new System.Random(rng.Next());
                var enclosure = PickEnclosure(slideRng, i);
                var openShape = enclosure == SlideEnclosure.Open ? PickOpenShape(slideRng) : OpenFlumeShape.Square;

                Vector3 worldEntry = i == 0
                    ? ComputeFirstSlideEntryDirection(transform.forward, MaxStartTiltAngle, slideRng)
                    : pendingEntryDir;

                var slideGo = new GameObject(BuildSlideName(i, enclosure, openShape));
                slideGo.transform.SetParent(transform, false);
                // Identity transform: the path is built in world space, so the mesh must NOT be
                // transformed again by the GameObject (that would double-transform / scatter it).
                slideGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var worldPath = BuildSlidePathWorld(slideRng, enclosure, cursorPos, worldEntry, i > 0);

                var slide = slideGo.AddComponent<WaterSlideGenerator>();
                ApplyRandomSlideSettings(slide, enclosure, slideRng);
                slide.Enclosure = enclosure;
                slide.OpenShape = openShape;
                slide.SlideColor = PickSlideColorStable(enclosure, openShape, slideRng);
                slide.GenerateCollider = GenerateColliders;
                slide.SetControlPointsLocal(worldPath);

                _slides.Add(slide);

                if (i < SlideCount - 1)
                {
                    Vector3 worldEnd = worldPath[^1];
                    var exitTangent = WaterSlideMeshBuilder.GetPathEndTangent(worldPath);

                    var heading = Vector3.ProjectOnPlane(exitTangent, Vector3.up);
                    if (heading.sqrMagnitude < 1e-4f)
                        heading = Vector3.forward;
                    heading.Normalize();

                    // Jump forward along the exit heading, and ALWAYS step down a clear amount,
                    // so the next entrance sits below the previous exit (you fly DOWN into it).
                    float gap = Mathf.Max(JumpGap.Sample(slideRng), 1f);
                    float step = Mathf.Max(JumpDrop.Sample(slideRng), gap * 0.6f);

                    // Lateral offset of the next tube along world X / Z (configurable in inspector).
                    Vector3 offsetXZ = new Vector3(JumpOffsetX.Sample(slideRng), 0f, JumpOffsetZ.Sample(slideRng));

                    Vector3 landing = worldEnd + heading * gap + offsetXZ + Vector3.down * step;

                    // Entry aims along the actual jump chord, so it stays codirectional with the flight.
                    pendingEntryDir = (landing - worldEnd).normalized;
                    if (pendingEntryDir.y > -0.05f)
                    {
                        var chordHoriz = new Vector3(landing.x - worldEnd.x, 0f, landing.z - worldEnd.z);
                        if (chordHoriz.sqrMagnitude < 1e-4f)
                            chordHoriz = heading;
                        pendingEntryDir = (chordHoriz.normalized + Vector3.down * 0.5f).normalized;
                    }

                    cursorPos = landing;
                }
            }
        }

        static Vector3 ComputeFirstSlideEntryDirection(Vector3 courseForward, float maxTiltDegrees, System.Random rng)
        {
            var horizontal = Vector3.ProjectOnPlane(courseForward, Vector3.up);
            if (horizontal.sqrMagnitude < 0.0001f)
                horizontal = Vector3.forward;
            horizontal.Normalize();

            // Gentle ascending start (~20°). Never vertical, so no "stick" at the top.
            float tilt = Mathf.Clamp(maxTiltDegrees, 12f, 30f);
            float rise = Mathf.Tan(tilt * Mathf.Deg2Rad);
            return (horizontal + Vector3.up * rise).normalized;
        }

        void ApplyRandomSlideSettings(WaterSlideGenerator slide, SlideEnclosure enclosure, System.Random rng)
        {
            slide.WallThickness = WallThickness.Sample(rng);
            slide.RingSegments = RingSegments.Sample(rng);
            slide.SamplesPerSegment = SamplesPerSegment.Sample(rng);
            slide.BankStrength = enclosure == SlideEnclosure.Open ? 0f : BankStrength.Sample(rng);

            if (enclosure == SlideEnclosure.Closed)
            {
                slide.TubeRadius = TubeRadius.Sample(rng);
            }
            else
            {
                slide.FlumeWidth = FlumeWidth.Sample(rng);
                slide.FlumeDepth = FlumeDepth.Sample(rng);
                slide.FlumeLipHeight = FlumeLipHeight.Sample(rng);
                slide.FlumeCornerRadius = FlumeCornerRadius.Sample(rng);
            }
        }

        [ContextMenu("Clear Course")]
        public void ClearCourse()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }

            _slides.Clear();
        }

        List<Vector3> BuildSlidePathWorld(
            System.Random rng,
            SlideEnclosure enclosure,
            Vector3 worldStart,
            Vector3 worldEntry,
            bool isChainedSlide)
        {
            var settings = new WaterSlidePathSettings
            {
                TargetLength = SlideLength.Sample(rng),
                TotalDrop = SlideDrop.Sample(rng),
                TurnRadiusMin = TurnRadius.Min,
                TurnRadiusMax = TurnRadius.Max,
                MinFeatures = FeaturesPerSlide.Min,
                MaxFeatures = FeaturesPerSlide.Max,
                AllowLoops = AllowLoopsAndSpirals && enclosure == SlideEnclosure.Closed,
                Enclosure = enclosure,
                InitialLaunchLength = isChainedSlide
                    ? Mathf.Clamp(StartLaunchLength.Sample(rng) * ChainedRampScale, 1.5f, 3.5f)
                    : StartLaunchLength.Sample(rng),
                AllowEntryRamp = true,
                AscendingStart = !isChainedSlide,
                WorldStart = worldStart,
                WorldStartForward = worldEntry,
                UseWorldSpace = true
            };

            return WaterSlidePathGenerator.GenerateInWorldSpace(rng, settings, worldStart, worldEntry);
        }

        static string BuildSlideName(int index, SlideEnclosure enclosure, OpenFlumeShape shape)
        {
            if (enclosure == SlideEnclosure.Open)
                return $"Slide_{index + 1}_Open_{shape}";
            return $"Slide_{index + 1}_Closed";
        }

        Color PickSlideColorStable(SlideEnclosure enclosure, OpenFlumeShape shape, System.Random rng)
        {
            if (enclosure == SlideEnclosure.Closed)
                return PickFromPaletteStable(ClosedColors, rng, new Color(0.12f, 0.62f, 0.95f));

            return shape == OpenFlumeShape.Rounded
                ? PickFromPaletteStable(OpenRoundedColors, rng, new Color(0.18f, 0.82f, 0.42f))
                : PickFromPaletteStable(OpenSquareColors, rng, new Color(0.95f, 0.45f, 0.12f));
        }

        static Color PickFromPaletteStable(Color[] palette, System.Random rng, Color fallback)
        {
            if (palette == null || palette.Length == 0)
                return fallback;
            return palette[rng.Next(palette.Length)];
        }

        OpenFlumeShape PickOpenShape(System.Random rng)
        {
            return OpenFlumeShapeMode switch
            {
                OpenShapeMode.Square => OpenFlumeShape.Square,
                OpenShapeMode.Rounded => OpenFlumeShape.Rounded,
                _ => rng.NextDouble() > 0.5 ? OpenFlumeShape.Rounded : OpenFlumeShape.Square
            };
        }

        SlideEnclosure PickEnclosure(System.Random rng, int index)
        {
            if (index == 0)
                return SlideEnclosure.Open;

            if (!MixOpenAndClosed)
                return SlideEnclosure.Open;

            if (AllowLoopsAndSpirals && rng.NextDouble() > 0.55)
                return SlideEnclosure.Closed;

            return index % 2 == 0 ? SlideEnclosure.Open : SlideEnclosure.Closed;
        }

        void OnDrawGizmos()
        {
            if (transform.childCount < 2)
                return;

            for (int i = 0; i < transform.childCount - 1; i++)
            {
                var current = transform.GetChild(i).GetComponent<WaterSlideGenerator>();
                var next = transform.GetChild(i + 1).GetComponent<WaterSlideGenerator>();
                if (current == null || next == null)
                    continue;

                var exit = current.transform.TransformPoint(current.EndPosition);
                var entry = next.transform.TransformPoint(next.StartPosition);

                Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.9f);
                Gizmos.DrawLine(exit, entry);
                Gizmos.DrawWireSphere(exit, 0.45f);
                Gizmos.DrawWireSphere(entry, 0.35f);
            }
        }
    }
}
