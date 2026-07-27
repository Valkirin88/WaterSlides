using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Serialization;

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
        [Tooltip("Желаемая дистанция прыжка вдоль направления вылета с конца предыдущей горки (метры). Может быть урезана, если баллистика при расчётной скорости вылета не дотягивает (см. AssumedExitSpeed и MaxJumpDrop).")]
        public FloatRange JumpGap = new() { Min = 4f, Max = 9f };

        [Tooltip("Расчётная скорость вылета с горки, м/с. Вход следующей горки ставится на баллистической дуге под ЭТУ скорость и фактическую гравитацию игрока. ВАЖНО: держи близко к реальной скорости вылета (обычно у потолка _maxSlideSpeed ≈ 16). Слишком МАЛО — дуга крутая, вход ставится близко и низко, а реально быстрый игрок летит полого и ПЕРЕЛЕТАЕТ его. Слишком МНОГО — заторможенный на поворотах вылет не дотянет и упадёт в провал. 14 — компромисс с лёгким запасом на торможение.")]
        [Min(3f)] public float AssumedExitSpeed = 14f;

        [Tooltip("Минимальная просадка входа следующей горки вниз относительно конца предыдущей, м.")]
        [Min(0.5f)] public float MinJumpDrop = 1.5f;

        [Tooltip("Максимальная просадка входа следующей горки вниз, м. Если баллистике при расчётной скорости нужно упасть глубже — дистанция прыжка урезается, чтобы вписаться в этот предел. Это ГЛАВНЫЙ ограничитель того, как далеко разлетаются горки: дальше по горизонтали = физически ниже (баллистика), поэтому маленький предел жмёт горки близко. Больше — горки дальше и с более глубокими провалами между ними.")]
        [Min(1f)] public float MaxJumpDrop = 16f;

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

        [Header("Loops")]
        [Tooltip("Добавлять петли некоторым горкам (не всем).")]
        public bool EnableLoops = true;

        [Tooltip("Вероятность того, что у горки будут петли.")]
        [Range(0f, 1f)] public float LoopChancePerSlide = 0.4f;

        [Tooltip("Сколько петель на горке, когда петли выпали. Петли могут идти подряд, одна сразу за другой.")]
        public IntRange LoopsPerSlide = new() { Min = 1, Max = 2 };

        [Tooltip("Разрешить вертикальные петли (труба идёт вверх и через верх). Только закрытые трубы.")]
        public bool AllowVerticalLoops = true;

        [Tooltip("Разрешить горизонтальные петли (полный круг с плавным спуском). Открытые и закрытые.")]
        public bool AllowHorizontalLoops = true;

        [Tooltip("Радиус петель (метры).")]
        public FloatRange LoopRadius = new() { Min = 3.5f, Max = 6f };

        [Header("Exit Kick")]
        [Tooltip("Шанс загнуть конец горки ВВЕРХ (трамплин): игрока подбрасывает вверх, но в направлении входа следующей горки.")]
        [Range(0f, 1f)] public float ExitKickChance = 0.35f;

        [Tooltip("Шанс загнуть конец ВНИЗ под углом хорды прыжка — вылет идёт сразу по направлению на вход следующей горки. Оставшаяся вероятность — прямой конец.")]
        [Range(0f, 1f)] public float ExitMatchChance = 0.3f;

        [Tooltip("Угол загиба конца вверх, градусы.")]
        public FloatRange ExitKickAngle = new() { Min = 12f, Max = 24f };

        [Tooltip("Длина загнутого участка, метры.")]
        public FloatRange ExitKickLength = new() { Min = 2f, Max = 3.5f };

        [Header("Thickness")]
        [Tooltip("Общий множитель размеров сечения (радиус, ширина жёлоба). Не влияет на толщину стенок.")]
        [Range(0.25f, 5f)] public float ThicknessScale = 1f;

        [Header("Closed Tube — Per Slide Ranges")]
        [Tooltip("Внутренний радиус закрытой круглой трубы (метры). Множится на ThicknessScale.")]
        public FloatRange TubeRadius = new() { Min = 0.45f, Max = 0.6f };

        [Tooltip("Толщина стенки закрытой трубы (метры).")]
        [FormerlySerializedAs("WallThickness")]
        public FloatRange ClosedWallThickness = new() { Min = 0.08f, Max = 0.2f };

        [Header("Open Flume — Per Slide Ranges")]
        [Tooltip("Толщина дна, бортов и бортика открытого жёлоба (метры).")]
        public FloatRange OpenFlumeWallThickness = new() { Min = 0.1f, Max = 0.25f };

        [Tooltip("Ширина открытого жёлоба (метры). Множится на ThicknessScale.")]
        public FloatRange FlumeWidth = new() { Min = 0.95f, Max = 1.2f };

        [Tooltip("Глубина (высота стенок) открытого жёлоба от дна до начала бортика (метры). Множится на ThicknessScale.")]
        public FloatRange FlumeDepth = new() { Min = 0.26f, Max = 0.38f };

        [Tooltip("Высота загнутого наружу бортика по краям жёлоба (метры). Множится на ThicknessScale.")]
        public FloatRange FlumeLipHeight = new() { Min = 0.22f, Max = 0.34f };

        [Tooltip("Радиус скругления углов сечения жёлоба (метры). Множится на ThicknessScale.")]
        public FloatRange FlumeCornerRadius = new() { Min = 0.1f, Max = 0.18f };

        [Header("Mesh Quality — Per Slide Ranges")]
        [Tooltip("Число вершин по кольцу сечения. Больше — глаже округлость трубы, но тяжелее меш.")]
        public IntRange RingSegments = new() { Min = 20, Max = 28 };

        [Tooltip("Число сэмплов вдоль каждого сегмента пути. Больше — глаже изгибы вдоль трубы.")]
        public IntRange SamplesPerSegment = new() { Min = 12, Max = 18 };

        [Tooltip("Сила наклона (банкинга) трубы в поворотах. 0 — без наклона (для открытых жёлобов всегда 0).")]
        public FloatRange BankStrength = new() { Min = 0.7f, Max = 1.2f };

        [Header("Water — Course")]
        [Tooltip("Водопад из конца горки при переходе к следующей.")]
        public bool WaterfallAtExits = true;

        [Tooltip("Водопад с последней горки курса.")]
        public bool WaterfallOnLastSlide = true;

        [Header("Slide Types")]
        [Tooltip("Разрешить закрытые трубы. Выкл — весь курс из открытых жёлобов; вертикальные петли (им нужна труба) тоже отключаются. Первая горка всегда открытая.")]
        [FormerlySerializedAs("MixOpenAndClosed")]
        public bool AllowClosedTubes = true;

        [Tooltip("Вероятность закрытой горки (0 = только открытые, 1 = только закрытые). Первая горка всегда открытая.")]
        [Range(0f, 1f)] public float ClosedSlideChance = 0.25f;

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
        WaterSlideWater _waterPreset;

        public IReadOnlyList<WaterSlideGenerator> Slides => _slides;

        // Speed needed to clear ANY jump of this course, with a small margin. The jumps
        // are PLACED from ballistics for AssumedExitSpeed under the player's actual
        // gravity (see Build), so that speed is the guarantee by construction — no
        // geometry-derived estimate needed, and no extra gravity scaling on the consumer
        // side (the placement already used the real player gravity).
        public float RequiredJumpSpeed => Mathf.Max(AssumedExitSpeed, 3f) * 1.05f;

        // Gravity the PLAYER actually flies under: scene gravity times the extra
        // multiplier on PlayerView. The placement must use the same value, otherwise
        // arcs computed here undershoot the real (heavier) flight.
        static float PlayerGravity()
        {
            float g = Mathf.Max(Mathf.Abs(Physics.gravity.y), 0.1f);
            var player = FindFirstObjectByType<PlayerView>(FindObjectsInactive.Include);
            return player != null ? g * player.GravityMultiplier : g;
        }

        // The player's cornering physics for the path generator's speed-aware fairness
        // (corner limit √(grip·R), braking distance before sharp turns). Read from the
        // actual PlayerView so scene tweaks propagate; defaults match its script values.
        static void GetPlayerCornering(out float grip, out float brake, out float speedCap)
        {
            grip = 20f;
            brake = 12f;
            speedCap = 16f;
            var player = FindFirstObjectByType<PlayerView>(FindObjectsInactive.Include);
            if (player == null)
                return;

            grip = player.CorneringGrip;
            brake = player.BrakeDeceleration;
            speedCap = player.MaxRideSpeed;
        }

        // How far below the launch point the flight arc is at the given horizontal
        // distance. launchDownDeg — launch angle below horizontal (negative = upward).
        static float BallisticDropAt(float distance, float speed, float launchDownDeg, float gravity)
        {
            float rad = launchDownDeg * Mathf.Deg2Rad;
            float horizontalSpeed = Mathf.Max(speed * Mathf.Cos(rad), 0.5f);
            float t = distance / horizontalSpeed;
            return speed * Mathf.Sin(rad) * t + 0.5f * gravity * t * t;
        }

        // Horizontal distance at which the same arc has fallen by the given drop.
        static float BallisticDistanceAt(float drop, float speed, float launchDownDeg, float gravity)
        {
            float rad = launchDownDeg * Mathf.Deg2Rad;
            float downSpeed = speed * Mathf.Sin(rad);
            float t = (-downSpeed + Mathf.Sqrt(downSpeed * downSpeed + 2f * gravity * Mathf.Max(drop, 0.01f))) / gravity;
            return Mathf.Max(speed * Mathf.Cos(rad), 0.5f) * t;
        }

        public bool IsReadyForPlayerSpawn(float heightOffset, float bodyClearance)
        {
            var slide = GetFirstSlide();
            return slide != null
                   && slide.IsRideColliderReady
                   && slide.TryGetRideSpawnPose(heightOffset, bodyClearance, out _, out _);
        }

        public bool TryGetFirstSlideSpawn(
            out Vector3 position,
            out Vector3 forward,
            float heightOffset = 2.5f,
            float bodyClearance = 0.35f)
        {
            position = default;
            forward = transform.forward;

            var slide = GetFirstSlide();
            if (slide == null)
                return false;

            return slide.TryGetRideSpawnPose(heightOffset, bodyClearance, out position, out forward);
        }

        WaterSlideGenerator GetFirstSlide()
        {
            EnsureSlidesRegistered();
            return _slides.Count > 0 ? _slides[0] : null;
        }

        void EnsureSlidesRegistered()
        {
            if (_slides.Count > 0)
                return;

            if (!Application.isPlaying)
                return;

            if (TryAdoptSceneSlides())
                return;

            GenerateCourse();
        }

        public WaterSlideWater WaterPreset => GetWaterPreset();

        WaterSlideWater GetWaterPreset()
        {
            if (_waterPreset == null && !TryGetComponent(out _waterPreset))
                _waterPreset = gameObject.AddComponent<WaterSlideWater>();
            return _waterPreset;
        }

        void Awake()
        {
            if (Application.isPlaying && _slides.Count == 0)
                TryAdoptSceneSlides();
        }

        void OnEnable()
        {
            var preset = GetWaterPreset();
            preset.AutoAssignPrefabs();

            if (_slides.Count == 0)
            {
                // In Play mode keep slides already built in the scene — regenerating destroys Suimono
                // surfaces and they often fail to re‑register before the first rendered frame.
                if (Application.isPlaying && TryAdoptSceneSlides())
                {
                    BeginPlayModeWater();
                    return;
                }

                GenerateCourse();
                return;
            }

            if (Application.isPlaying)
                BeginPlayModeWater();
        }

        void BeginPlayModeWater()
        {
            WaterSlideWater.EnsureModuleInScene(transform, GetWaterPreset().SuimonoModulePrefab);
            StartCoroutine(RefreshSlideWaterWhenReady());
        }

        static IEnumerator RefreshSlideWaterWhenReady()
        {
            for (int i = 0; i < 120; i++)
            {
                WaterSlideWater.RefreshAllInScene();
                if (Camera.main != null)
                    yield break;

                yield return null;
            }

            WaterSlideWater.RefreshAllInScene();
        }

        bool TryAdoptSceneSlides()
        {
            var existing = GetComponentsInChildren<WaterSlideGenerator>(false);
            if (existing.Length == 0)
                return false;

            Array.Sort(existing, CompareSlides);
            _slides.AddRange(existing);
            WaterSlideWater.EnsureModuleInScene(transform, GetWaterPreset().SuimonoModulePrefab);
            return true;
        }

        static int CompareSlides(WaterSlideGenerator a, WaterSlideGenerator b)
        {
            int indexA = ParseSlideIndex(a != null ? a.name : null);
            int indexB = ParseSlideIndex(b != null ? b.name : null);
            int byIndex = indexA.CompareTo(indexB);
            return byIndex != 0 ? byIndex : string.Compare(a.name, b.name, StringComparison.Ordinal);
        }

        static int ParseSlideIndex(string slideName)
        {
            if (string.IsNullOrEmpty(slideName))
                return int.MaxValue;

            var match = Regex.Match(slideName, @"Slide_(\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, out int index) ? index : int.MaxValue;
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

            var waterPreset = GetWaterPreset();
            waterPreset.AutoAssignPrefabs();
            WaterSlideWater.EnsureModuleInScene(transform, waterPreset.SuimonoModulePrefab);

            int seed = useRandomSeed
                ? RandomSeed ^ System.Environment.TickCount
                : RandomSeed;

            var rng = new System.Random(seed);
            Vector3 cursorPos = transform.position + Vector3.up * 25f;
            Vector3 pendingEntryDir = Vector3.zero;
            float pendingCatchLength = 0f;

            for (int i = 0; i < SlideCount; i++)
            {
                var slideRng = new System.Random(rng.Next());
                var enclosure = PickEnclosure(slideRng, i);

                var loopKinds = PickLoops(slideRng, i);
                bool hasVerticalLoop = loopKinds != null && loopKinds.Contains(SlideLoopKind.Vertical);
                // A vertical loop goes upside-down, so the slide must be a closed tube.
                if (hasVerticalLoop)
                    enclosure = SlideEnclosure.Closed;

                var openShape = enclosure == SlideEnclosure.Open ? PickOpenShape(slideRng) : OpenFlumeShape.Square;

                Vector3 worldEntry = i == 0
                    ? ComputeFirstSlideEntryDirection(transform.forward, MaxStartTiltAngle, slideRng)
                    : pendingEntryDir;

                var slideGo = new GameObject(BuildSlideName(i, enclosure, openShape));
                slideGo.transform.SetParent(transform, false);
                // Identity transform: the path is built in world space, so the mesh must NOT be
                // transformed again by the GameObject (that would double-transform / scatter it).
                slideGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                float loopRadius = LoopRadius.Sample(slideRng);

                // The jump to the NEXT slide is decided BEFORE building this one: the
                // "match" exit style needs the launch angle, and the landing placement
                // below then reuses these exact values.
                //
                // The landing is placed ON the flight arc computed for AssumedExitSpeed
                // under the player's REAL gravity. The old geometric rule (drop = 60% of
                // the gap) needed a near-cap, no-braking exit for the longest jumps —
                // slower exits (normal after braking through a turn) fell short into the
                // void. Now a slow exit still reaches the entry, and a fast one simply
                // overflies it and lands a bit further along the slide.
                bool hasNextJump = i < SlideCount - 1;
                float jumpGap = 0f, jumpStep = 0f;
                Vector3 jumpOffsetXZ = Vector3.zero;

                // Exit style: up (ski jump), down along the launch (match), or straight.
                float kickAngle = 0f, kickLength = 0f;
                double styleRoll = slideRng.NextDouble();
                bool kickUp = styleRoll < ExitKickChance;
                bool kickMatch = !kickUp && hasNextJump && styleRoll < ExitKickChance + ExitMatchChance;
                if (kickUp)
                {
                    kickAngle = ExitKickAngle.Sample(slideRng);
                    kickLength = ExitKickLength.Sample(slideRng);
                }

                if (hasNextJump)
                {
                    jumpGap = Mathf.Max(JumpGap.Sample(slideRng), 1f);
                    jumpOffsetXZ = new Vector3(JumpOffsetX.Sample(slideRng), 0f, JumpOffsetZ.Sample(slideRng));

                    // The offset direction relative to the exit heading is unknown until
                    // the slide is built, so budget for the worst case (offset fully
                    // co-directional) — overestimating the distance only lowers the
                    // entry, which is the safe direction.
                    float offsetMag = new Vector2(jumpOffsetXZ.x, jumpOffsetXZ.z).magnitude;
                    float distance = jumpGap + offsetMag;

                    float gravity = PlayerGravity();
                    float speed = Mathf.Max(AssumedExitSpeed, 3f);

                    // Assumed launch angle for the reachability calc. An up-kick only adds
                    // range, so it safely counts as horizontal; straight ends usually
                    // leave slightly nose-down, hence the 8°. The safety margin covers
                    // steeper-than-assumed exits and the frame of the entry itself.
                    const float safety = 1.15f;
                    float launchDown = kickUp ? 0f : 8f;
                    if (kickMatch)
                    {
                        // The match style bends the lip DOWN along the launch; the bend
                        // steepens the fall, so its angle feeds back into the drop. One
                        // provisional pass fixes the angle (capped: aiming straight at a
                        // deep entry would turn the exit into a cliff dive).
                        float provisionalDrop = BallisticDropAt(distance, speed, 8f, gravity) * safety;
                        launchDown = Mathf.Min(Mathf.Atan2(provisionalDrop, distance) * Mathf.Rad2Deg, 25f);
                    }

                    jumpStep = BallisticDropAt(distance, speed, launchDown, gravity) * safety;
                    if (jumpStep > MaxJumpDrop)
                    {
                        // Too deep — keep the drop and shorten the jump instead, so the
                        // entry stays on the assumed-speed arc.
                        float maxDistance = BallisticDistanceAt(MaxJumpDrop / safety, speed, launchDown, gravity);
                        jumpGap = Mathf.Max(maxDistance - offsetMag, 1.5f);
                        jumpStep = MaxJumpDrop;
                    }
                    jumpStep = Mathf.Max(jumpStep, Mathf.Min(MinJumpDrop, MaxJumpDrop));

                    if (kickMatch)
                    {
                        kickAngle = -launchDown;
                        kickLength = ExitKickLength.Sample(slideRng);
                    }
                }

                var generatedPath = BuildSlidePathWorld(
                    slideRng, enclosure, cursorPos, worldEntry, i > 0, loopKinds, loopRadius, kickAngle, kickLength,
                    i > 0 ? pendingCatchLength : 0f);

                var slide = slideGo.AddComponent<WaterSlideGenerator>();
                ApplyRandomSlideSettings(slide, enclosure, slideRng);
                slide.Enclosure = enclosure;
                slide.OpenShape = openShape;
                slide.SlideColor = PickSlideColorStable(enclosure, openShape, slideRng);
                slide.GenerateCollider = GenerateColliders;
                // The upward lip must survive the mesh builder's descent clamps too.
                slide.PreserveVerticalMotion = hasVerticalLoop || kickAngle > 0.5f;
                slide.HasLoop = generatedPath.HasLoop;
                if (generatedPath.HasLoop)
                    slide.BankStrength = 0f;

                var water = slideGo.AddComponent<WaterSlideWater>();
                ApplyWaterSettings(water, i == SlideCount - 1);

                slide.SetControlPointsLocal(generatedPath.Points);

                _slides.Add(slide);

                if (i < SlideCount - 1)
                {
                    // Use the ACTUAL rendered end of the slide (the mesh can push it lower than the
                    // raw control point), so the next entrance really sits below the previous exit.
                    Vector3 worldEnd = slide.ProcessedWorldEnd;
                    var exitTangent = slide.ProcessedWorldEndTangent;

                    var heading = Vector3.ProjectOnPlane(exitTangent, Vector3.up);
                    if (heading.sqrMagnitude < 1e-4f)
                        heading = Vector3.forward;
                    heading.Normalize();

                    // Jump forward along the exit heading and down by the BALLISTIC drop
                    // computed above for AssumedExitSpeed — the entry sits on (or slightly
                    // below) the assumed flight arc. The values were pre-sampled before
                    // building this slide — the "match" exit style bent the lip to
                    // exactly this launch angle.
                    Vector3 landing = worldEnd + heading * jumpGap + jumpOffsetXZ + Vector3.down * jumpStep;

                    // Entry aims along the actual jump chord, so it stays codirectional with the flight.
                    pendingEntryDir = (landing - worldEnd).normalized;
                    if (pendingEntryDir.y > -0.05f)
                    {
                        var chordHoriz = new Vector3(landing.x - worldEnd.x, 0f, landing.z - worldEnd.z);
                        if (chordHoriz.sqrMagnitude < 1e-4f)
                            chordHoriz = heading;
                        pendingEntryDir = (chordHoriz.normalized + Vector3.down * 0.5f).normalized;
                    }

                    // CATCH RAMP: start the next slide UP the jump chord, not at the landing
                    // point itself. The landing sits on the arc for AssumedExitSpeed; a SLOWER
                    // real exit (braked through a corner) flies a steeper arc that passes BELOW
                    // that point — and the ride collider is the inner surface only (one-sided),
                    // so a body arriving from outside sailed straight through the slide
                    // ("пролетает сквозь горку"). A slow arc crosses the chord line upstream of
                    // the landing (at ~40-60% of the gap for exits down to ~8 m/s), so extending
                    // the entry flume up the chord catches those arcs from the INSIDE. Fast
                    // exits are unaffected — their flatter arcs still overfly the entry and land
                    // further along the slide, as before.
                    float catchLength = Mathf.Clamp(jumpGap * 0.6f, 3f, 12f);
                    pendingCatchLength = catchLength;
                    cursorPos = landing - pendingEntryDir * catchLength;
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

        void ApplyWaterSettings(WaterSlideWater water, bool isLastSlide)
        {
            water.CopyFrom(GetWaterPreset());
            if (!water.EnableWater || !WaterfallAtExits || (!WaterfallOnLastSlide && isLastSlide))
                water.EnableWaterfall = false;
        }

        [ContextMenu("Rebuild All Slide Meshes")]
        public void RebuildAllSlideMeshes()
        {
            foreach (var slide in GetComponentsInChildren<WaterSlideGenerator>())
                slide.Rebuild();
        }

        [ContextMenu("Apply Thickness To All Slides")]
        public void ApplyThicknessToAllSlides()
        {
            var slides = GetComponentsInChildren<WaterSlideGenerator>();
            for (int i = 0; i < slides.Length; i++)
            {
                var slide = slides[i];
                var rng = new System.Random(RandomSeed + i * 7919);
                if (slide.Enclosure == SlideEnclosure.Closed)
                    slide.WallThickness = ClosedWallThickness.Sample(rng);
                else
                    slide.FlumeWallThickness = OpenFlumeWallThickness.Sample(rng);
                slide.Rebuild();
            }
        }

        [ContextMenu("Apply Water Preset To All Slides")]
        public void ApplyWaterPresetToAllSlides()
        {
            var preset = GetWaterPreset();
            preset.AutoAssignPrefabs();

            var slides = GetComponentsInChildren<WaterSlideGenerator>();
            for (int i = 0; i < slides.Length; i++)
            {
                var water = slides[i].GetComponent<WaterSlideWater>();
                if (water == null)
                    continue;

                ApplyWaterSettings(water, i == slides.Length - 1);
                water.Rebuild();
            }
        }

        void ApplyRandomSlideSettings(WaterSlideGenerator slide, SlideEnclosure enclosure, System.Random rng)
        {
            float scale = Mathf.Max(ThicknessScale, 0.05f);

            slide.RingSegments = RingSegments.Sample(rng);
            slide.SamplesPerSegment = SamplesPerSegment.Sample(rng);
            slide.BankStrength = enclosure == SlideEnclosure.Open ? 0f : BankStrength.Sample(rng);

            if (enclosure == SlideEnclosure.Closed)
            {
                slide.WallThickness = ClosedWallThickness.Sample(rng);
                slide.TubeRadius = TubeRadius.Sample(rng) * scale;
            }
            else
            {
                slide.FlumeWallThickness = OpenFlumeWallThickness.Sample(rng);
                slide.FlumeWidth = FlumeWidth.Sample(rng) * scale;
                slide.FlumeDepth = FlumeDepth.Sample(rng) * scale;
                slide.FlumeLipHeight = FlumeLipHeight.Sample(rng) * scale;
                slide.FlumeCornerRadius = FlumeCornerRadius.Sample(rng) * scale;
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

        GeneratedSlidePath BuildSlidePathWorld(
            System.Random rng,
            SlideEnclosure enclosure,
            Vector3 worldStart,
            Vector3 worldEntry,
            bool isChainedSlide,
            List<SlideLoopKind> loopKinds,
            float loopRadius,
            float exitKickAngle,
            float exitKickLength,
            float entryCatchLength = 0f)
        {
            GetPlayerCornering(out float grip, out float brake, out float speedCap);

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
                // Chained: the short post-jump run PLUS the catch-ramp extension — the slide
                // was started up the jump chord by exactly that much (see Build), so the ramp
                // runs along the chord through the nominal landing point and beyond.
                InitialLaunchLength = isChainedSlide
                    ? Mathf.Clamp(StartLaunchLength.Sample(rng) * ChainedRampScale, 1.5f, 3.5f) + entryCatchLength
                    : StartLaunchLength.Sample(rng),
                AllowEntryRamp = true,
                AscendingStart = !isChainedSlide,
                Loops = loopKinds,
                LoopRadius = loopRadius,
                ExitKickAngle = exitKickAngle,
                ExitKickLength = exitKickLength,
                // Speed-aware corner fairness: real player physics plus the entry speed —
                // a chained slide is entered at roughly the jump's exit speed, the first
                // slide from a slow standing start.
                RideSpeedCap = speedCap,
                CorneringGrip = grip,
                BrakeDeceleration = brake,
                GravityAccel = PlayerGravity(),
                EntrySpeed = isChainedSlide
                    ? Mathf.Min(Mathf.Max(AssumedExitSpeed, 3f) * 1.05f, speedCap)
                    : 4f
            };

            return WaterSlidePathGenerator.GenerateInWorldSpace(rng, settings, worldStart, worldEntry);
        }

        // The list of loops for one slide, in ride order (null — no loops). Kinds may
        // repeat, so a slide can get several identical loops one after another.
        List<SlideLoopKind> PickLoops(System.Random rng, int index)
        {
            if (!EnableLoops || index == 0)
                return null;
            if (rng.NextDouble() >= LoopChancePerSlide)
                return null;

            // A vertical loop forces the slide to become a closed tube (it goes upside-down),
            // so it must not be picked when closed tubes are disabled.
            bool canVertical = AllowVerticalLoops && AllowClosedTubes;
            bool canHorizontal = AllowHorizontalLoops;
            if (!canVertical && !canHorizontal)
                return null;

            int count = LoopsPerSlide.Sample(rng);
            if (count <= 0)
                return null;

            var loops = new List<SlideLoopKind>(count);
            for (int k = 0; k < count; k++)
            {
                SlideLoopKind kind;
                if (canVertical && canHorizontal)
                    kind = rng.NextDouble() < 0.5 ? SlideLoopKind.Vertical : SlideLoopKind.Horizontal;
                else
                    kind = canVertical ? SlideLoopKind.Vertical : SlideLoopKind.Horizontal;
                loops.Add(kind);
            }

            return loops;
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

            if (!AllowClosedTubes)
                return SlideEnclosure.Open;

            return rng.NextDouble() < ClosedSlideChance
                ? SlideEnclosure.Closed
                : SlideEnclosure.Open;
        }
    }
}
