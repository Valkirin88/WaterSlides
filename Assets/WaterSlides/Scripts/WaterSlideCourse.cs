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

        [SerializeField, HideInInspector] bool _preserveSceneLayout;
        internal bool PreservesSceneLayout => _preserveSceneLayout;

        [Header("Endless")]
        [Tooltip("Бесконечная трасса: в Play-режиме новые горки достраиваются впереди игрока, а далеко отставшие удаляются. Slide Count задаёт только стартовый набор в сцене.")]
        public bool Endless = true;

        [Tooltip("Сколько горок всегда держать ВПЕРЕДИ той, по которой едет игрок.")]
        [Min(1)] public int SlidesAhead = 3;

        [Tooltip("Сколько горок оставлять ПОЗАДИ игрока (остальные удаляются). Респавн после падения — на самую раннюю из оставшихся.")]
        [Min(1)] public int SlidesBehind = 1;

        [Header("Connection Between Slides")]
        [Tooltip("Желаемая дистанция прыжка вдоль направления вылета с конца предыдущей горки (метры). Может быть урезана, если баллистика при расчётной скорости вылета не дотягивает (см. AssumedExitSpeed и MaxJumpDrop).")]
        public FloatRange JumpGap = new() { Min = 4f, Max = 9f };

        [Tooltip("Расчётная скорость вылета с горки, м/с. Вход следующей горки ставится на баллистической дуге под ЭТУ скорость и фактическую гравитацию игрока. ВАЖНО: держи близко к реальной скорости вылета (обычно у потолка _maxSlideSpeed ≈ 16). Слишком МАЛО — дуга крутая, вход ставится близко и низко, а реально быстрый игрок летит полого и ПЕРЕЛЕТАЕТ его. Слишком МНОГО — заторможенный на поворотах вылет не дотянет и упадёт в провал. 14 — компромисс с лёгким запасом на торможение.")]
        [Min(3f)] public float AssumedExitSpeed = 14f;

        [Tooltip("Минимальная просадка входа следующей горки вниз относительно конца предыдущей, м.")]
        [Min(0.5f)] public float MinJumpDrop = 1.5f;

        [Tooltip("Максимальная просадка входа следующей горки вниз, м. Если баллистике при расчётной скорости нужно упасть глубже — дистанция прыжка урезается, чтобы вписаться в этот предел. Это ГЛАВНЫЙ ограничитель того, как далеко разлетаются горки: дальше по горизонтали = физически ниже (баллистика), поэтому маленький предел жмёт горки близко. Больше — горки дальше и с более глубокими провалами между ними.")]
        [Min(1f)] public float MaxJumpDrop = 16f;

        [Tooltip("Диапазон смещения начала следующей горки ВБОК от направления вылета (метры; минус — влево, плюс — вправо). Задаёт разброс трассы по ширине; вход при этом строится вдоль реальной хорды прыжка, поэтому угол входа остаётся согласован с полётом.")]
        public FloatRange JumpOffsetX = new() { Min = -3f, Max = 3f };

        [Tooltip("Диапазон добавки к дистанции прыжка ВДОЛЬ направления вылета (метры, отрицательные значения обрезаются в 0). Раньше оба смещения были в МИРОВЫХ осях: при развороте трассы «вдольная» добавка могла лечь против вылета, сжать горизонталь прыжка и построить вход крутым обрывом под другим углом, чем конец предыдущей горки.")]
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

        [Tooltip("Расширять повороты, которые на расчётной скорости подхода не удержать (вкл — крутых поворотов на быстрой трассе почти не остаётся). Выкл — радиус остаётся крутым, перед поворотом вставляется прямая, где игрок может затормозить сам; не затормозил — вылетел.")]
        public bool WidenUnfairTurns = true;

        [Tooltip("Доля дистанции прыжка, на которую вход следующей горки продлевается НАЗАД вверх по хорде (ловля медленных вылетов). 0.6 — ловит вылеты вплоть до ~8 м/с; 0.3 — заторможенный вылет не долетает, нужно разгоняться (W) перед концом горки.")]
        [Range(0f, 0.8f)] public float CatchRampFraction = 0.6f;

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

        [Header("Stripes")]
        [Tooltip("Красить открытые горки поперечными цветными полосами (как у больших парковых горок) вместо одного цвета из палитр ниже.")]
        public bool StripedSlides = true;

        [Tooltip("Палитра полос; каждая горка получает свою случайную последовательность.")]
        public Color[] StripeColors =
        {
            new(0.10f, 0.35f, 0.85f, 1f),
            new(0.40f, 0.72f, 0.95f, 1f),
            new(0.98f, 0.82f, 0.12f, 1f),
            new(0.96f, 0.96f, 0.94f, 1f),
            new(0.20f, 0.78f, 0.72f, 1f),
            new(0.06f, 0.14f, 0.48f, 1f),
            new(0.90f, 0.16f, 0.12f, 1f)
        };

        [Tooltip("Длина одной полосы вдоль горки, м.")]
        [Min(0.5f)] public float StripeLength = 5f;

        [Tooltip("Доля горок с ПРОДОЛЬНЫМИ полосами (цветные дорожки по ширине жёлоба). В референсах (Planet Coaster 2) полосы поперечные — 0.")]
        [Range(0f, 1f)] public float StripesAlongChance = 0f;

        [Tooltip("Доля горок со СМЕШАННОЙ палитрой (красный, жёлтый, синий, белый вперемешку, как на парковых горках); остальные — секции в оттенках одного базового цвета, свой у каждой горки.")]
        [Range(0f, 1f)] public float MixedPaletteChance = 0.5f;

        [Tooltip("Число продольных дорожек по ширине жёлоба.")]
        public IntRange StripeLanes = new() { Min = 4, Max = 6 };

        [Header("Boost Strips")]
        [Tooltip("Ускоряющих полос на горку (случайно из диапазона). Полоса разгоняет тело и поднимает потолок скорости (см. PlayerView: Boost Acceleration / Boost Speed Bonus) — замена газу W, которого на телефоне нет.")]
        public IntRange BoostStripsPerSlide = new() { Min = 1, Max = 2 };
        [Tooltip("Длина ускоряющей полосы, м.")]
        [Min(1f)] public float BoostStripLength = 6f;
        [Tooltip("Материал ускоряющих полос (создаёт меню Water Slides/Setup Controls). Пусто — полосы работают, но не рисуются.")]
        public Material BoostStripMaterial;

        [Header("Glass Tubes")]
        [Tooltip("Закрытые трубы — прозрачное стекло со светящимися кольцами подсветки (цвета из палитры полос).")]
        public bool GlassTubes = true;

        [Tooltip("Шаг колец подсветки вдоль трубы, м.")]
        public FloatRange TubeRingSpacing = new() { Min = 2.5f, Max = 4f };

        [Tooltip("Прозрачность стекла (альфа): 0 — невидимое, 1 — непрозрачное.")]
        [Range(0.05f, 0.8f)] public float GlassOpacity = 0.28f;

        [Tooltip("Доля ОТКРЫТЫХ жёлобов из прозрачного стекла с неоновыми кольцами (как прозрачные горки в рилсах): 0 — только полосатые, 1 — все стеклянные. Не требует закрытых труб.")]
        [Range(0f, 1f)] public float GlassOpenChance = 0f;

        [Tooltip("Доля стеклянных горок из БЕСЦВЕТНОГО стекла (как прозрачная горка над Дубаем в рилсе: стекло почти невидимо, светятся только кромки); остальные — голубое тонированное.")]
        [Range(0f, 1f)] public float GlassClearChance = 0.5f;

        [Tooltip("Доля стеклянных открытых горок с U-образным профилем (плоское дно, стенки, неон по двум уголкам) вместо скруглённого.")]
        [Range(0f, 1f)] public float GlassSquareChance = 0.5f;

        [Tooltip("Доля стеклянных горок с частой неоновой решёткой (сеткой). Остальные поровну: линии вдоль + кольца, только вдоль, только кольца.")]
        [Range(0f, 1f)] public float GlassGridChance = 0.25f;

        [Tooltip("Палитра неона стеклянных горок (кромки, линия по дну, кольца): цвет выбирается случайно на горку. Пусто — берётся из палитры полос.")]
        public Color[] GlassNeonColors =
        {
            new Color(1f, 0.2f, 0.75f, 1f),   // розовый (Майами)
            new Color(0.2f, 0.8f, 1f, 1f),    // голубой
            new Color(1f, 0.65f, 0.15f, 1f),  // золотой (Дубай)
            new Color(0.6f, 0.3f, 1f, 1f),    // фиолетовый
            new Color(0.3f, 1f, 0.6f, 1f)     // мятный
        };

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

        // Состояние «продолжения» трассы — всё, что нужно, чтобы достроить следующую
        // горку к уже построенным. Сериализуется вместе со сценой: горки, собранные в
        // редакторе, в Play-режиме продолжаются ровно оттуда, где кончились.
        [SerializeField, HideInInspector] int _baseSeed;
        [SerializeField, HideInInspector] int _nextSlideIndex;
        [SerializeField, HideInInspector] Vector3 _cursorPos;
        [SerializeField, HideInInspector] Vector3 _pendingEntryDir;
        [SerializeField, HideInInspector] float _pendingCatchLength;

        // Глобальный индекс _slides[0] (горки позади игрока удаляются).
        int _firstSlideIndex;
        PlayerView _player;
        float _nextStreamCheck;

        public IReadOnlyList<WaterSlideGenerator> Slides => _slides;

        // Растёт при каждом изменении набора горок (достройка/удаление) — потребители
        // (сетка высоты падения в EntryPoint) пересчитывают свои кэши.
        public int SlidesVersion { get; private set; }

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

        // ---- Бесконечная трасса: достройка впереди, удаление позади ----

        void Update()
        {
            if (!Application.isPlaying || !Endless || _slides.Count == 0)
                return;

            if (Time.time < _nextStreamCheck)
                return;
            _nextStreamCheck = Time.time + 0.2f;

            if (_player == null)
                _player = FindFirstObjectByType<PlayerView>();
            if (_player == null)
                return;

            int playerIndex = ResolvePlayerSlideIndex();
            if (playerIndex < 0)
                return;

            // По одной горке за проверку: сборка меша + коллайдера + воды — заметная
            // работа, размазываем её по кадрам.
            if (_slides.Count - 1 - playerIndex < SlidesAhead)
                AppendNextSlide();
            else if (playerIndex > SlidesBehind)
                RemoveOldestSlide();
        }

        // Локальный индекс горки игрока в _slides: та, к которой он пристёгнут (или от
        // которой оторвался); до первого касания — ближайшая по кадрам пути.
        int ResolvePlayerSlideIndex()
        {
            var current = _player.CurrentSlide;
            if (current != null)
            {
                int idx = _slides.IndexOf(current);
                if (idx >= 0)
                    return idx;
            }

            Vector3 pos = _player.MovementTransform.position;
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < _slides.Count; i++)
            {
                var frames = _slides[i] != null ? _slides[i].RideFrames : null;
                if (frames == null)
                    continue;
                for (int f = 0; f < frames.Count; f += 8)
                {
                    float d = (frames[f].Position - pos).sqrMagnitude;
                    if (d < bestSq)
                    {
                        bestSq = d;
                        best = i;
                    }
                }
            }

            return best;
        }

        void RemoveOldestSlide()
        {
            if (_slides.Count == 0)
                return;

            var slide = _slides[0];
            _slides.RemoveAt(0);
            _firstSlideIndex++;
            if (slide != null)
                Destroy(slide.gameObject);
            SlidesVersion++;
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
                if (!Application.isPlaying && _preserveSceneLayout && TryAdoptSceneSlides())
                    return;

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

            // Бесконечная трасса продолжается от сохранённого состояния; если горки в
            // сцене собраны старой версией кода (состояние не совпадает с набором),
            // продолжать некуда — честнее пересобрать курс заново.
            if (Endless && Application.isPlaying && _nextSlideIndex != existing.Length)
            {
                Debug.LogWarning($"WaterSlideCourse: горки сцены ({existing.Length}) не совпадают с сохранённым состоянием продолжения ({_nextSlideIndex}) — пересобираю трассу. Перегенерируй курс в редакторе, чтобы это не повторялось.");
                return false;
            }

            _slides.AddRange(existing);
            _firstSlideIndex = 0;
            SlidesVersion++;
            if (Application.isPlaying)
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

            _baseSeed = useRandomSeed
                ? RandomSeed ^ System.Environment.TickCount
                : RandomSeed;

            _nextSlideIndex = 0;
            _firstSlideIndex = 0;
            _cursorPos = transform.position + Vector3.up * 25f;
            _pendingEntryDir = Vector3.zero;
            _pendingCatchLength = 0f;

            for (int i = 0; i < SlideCount; i++)
                AppendNextSlide();
        }

        // Зерно горки по её номеру: не зависит от того, сколько горок уже построено или
        // удалено, поэтому трасса одна и та же и в редакторе, и при достройке на ходу.
        static int SlideSeed(int baseSeed, int index)
        {
            unchecked
            {
                int h = baseSeed * 1000003 + index * 7919 + 12345;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return h;
            }
        }

        // Строит ОДНУ следующую горку по состоянию продолжения и обновляет его.
        void AppendNextSlide()
        {
            int i = _nextSlideIndex++;
            var slideRng = new System.Random(SlideSeed(_baseSeed, i));
            Vector3 cursorPos = _cursorPos;
            Vector3 pendingEntryDir = _pendingEntryDir;
            float pendingCatchLength = _pendingCatchLength;

            {
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
                bool hasNextJump = Endless || i < SlideCount - 1;
                float jumpForward = 0f, jumpLateral = 0f, jumpStep = 0f;

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
                    // The offsets are relative to the EXIT HEADING (lateral / along), not
                    // world axes: a world-axis offset rotated with the course, and when it
                    // happened to point AGAINST the exit it shrank the jump's horizontal
                    // reach while the drop was budgeted for a longer one — the entry got
                    // built as a steep cliff at a very different angle than the previous
                    // exit. Heading-relative offsets make the horizontal chord length
                    // exact BEFORE the slide is built, so the drop matches the geometry.
                    jumpForward = Mathf.Max(JumpGap.Sample(slideRng), 1f)
                        + Mathf.Max(JumpOffsetZ.Sample(slideRng), 0f);
                    jumpLateral = JumpOffsetX.Sample(slideRng);
                    float distance = Mathf.Sqrt(jumpForward * jumpForward + jumpLateral * jumpLateral);

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
                        // entry stays on the assumed-speed arc. The chord shrinks
                        // PROPORTIONALLY (forward and lateral together), preserving its
                        // direction relative to the exit.
                        float maxDistance = BallisticDistanceAt(MaxJumpDrop / safety, speed, launchDown, gravity);
                        float scale = Mathf.Min(1f, maxDistance / Mathf.Max(distance, 0.01f));
                        jumpForward = Mathf.Max(jumpForward * scale, 1.5f);
                        jumpLateral *= scale;
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
                ApplyRandomSlideSettings(slide, enclosure, slideRng, i);
                slide.Enclosure = enclosure;
                slide.OpenShape = openShape;
                // Вариативность стекла (27.10.2026): часть открытых стеклянных —
                // U-образные с неоном по уголкам; рисунок неона — случайный стиль.
                if (slide.GlassTube && enclosure == SlideEnclosure.Open && slideRng.NextDouble() < GlassSquareChance)
                    slide.OpenShape = OpenFlumeShape.Square;
                if (slide.GlassTube)
                    slide.GlassLines = PickGlassLineStyle(slideRng);
                slide.SlideColor = PickSlideColorStable(enclosure, openShape, slideRng);
                slide.GenerateCollider = GenerateColliders;
                // The upward lip must survive the mesh builder's descent clamps too.
                slide.PreserveVerticalMotion = hasVerticalLoop || kickAngle > 0.5f;
                slide.HasLoop = generatedPath.HasLoop;
                if (generatedPath.HasLoop)
                    slide.BankStrength = 0f;

                var water = slideGo.AddComponent<WaterSlideWater>();
                ApplyWaterSettings(water, !hasNextJump);

                slide.SetControlPointsLocal(generatedPath.Points);

                _slides.Add(slide);
                SlidesVersion++;

                if (hasNextJump)
                {
                    // Use the ACTUAL rendered end of the slide (the mesh can push it lower than the
                    // raw control point), so the next entrance really sits below the previous exit.
                    Vector3 worldEnd = slide.ProcessedWorldEnd;
                    var exitTangent = slide.ProcessedWorldEndTangent;

                    var heading = Vector3.ProjectOnPlane(exitTangent, Vector3.up);
                    if (heading.sqrMagnitude < 1e-4f)
                        heading = Vector3.forward;
                    heading.Normalize();

                    // Jump forward along the exit heading (plus the heading-relative
                    // lateral offset) and down by the BALLISTIC drop computed above for
                    // AssumedExitSpeed — the entry sits on (or slightly below) the
                    // assumed flight arc. The values were pre-sampled before building
                    // this slide — the "match" exit style bent the lip to exactly this
                    // launch angle.
                    Vector3 exitRight = Vector3.Cross(Vector3.up, heading);
                    exitRight = exitRight.sqrMagnitude > 1e-4f ? exitRight.normalized : Vector3.right;
                    Vector3 landing = worldEnd + heading * jumpForward + exitRight * jumpLateral
                        + Vector3.down * jumpStep;

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
                    float catchLength = Mathf.Clamp(
                        Mathf.Sqrt(jumpForward * jumpForward + jumpLateral * jumpLateral) * CatchRampFraction, 2f, 12f);
                    pendingCatchLength = catchLength;
                    cursorPos = landing - pendingEntryDir * catchLength;
                }
            }

            _cursorPos = cursorPos;
            _pendingEntryDir = pendingEntryDir;
            _pendingCatchLength = pendingCatchLength;
        }

        // Пресет под референс (большие парковые горки Planet Coaster 2): широкий
        // скруглённый жёлоб с цветными полосами, пологие широкие повороты, длинные
        // горки, бесконечная трасса. Меняет только параметры — перегенерировать отдельно.
        [ContextMenu("Apply Reference Look (Wide Striped Flume)")]
        public void ApplyReferenceLook()
        {
            Endless = true;
            SlidesAhead = 3;
            SlidesBehind = 1;

            SlideLength = new FloatRange { Min = 90f, Max = 130f };
            SlideDrop = new FloatRange { Min = 25f, Max = 38f };
            FeaturesPerSlide = new IntRange { Min = 3, Max = 5 };
            // Радиус поворота должен быть заметно больше половины ширины жёлоба (1.6 м),
            // иначе внутренняя стенка складывается сама в себя (уточняется ниже).
            TurnRadius = new FloatRange { Min = 14f, Max = 30f };
            LoopRadius = new FloatRange { Min = 9f, Max = 14f };
            LoopChancePerSlide = 0.25f;

            // JumpOffsetX (боковое смещение входа) НЕ трогаем: по правилам игры прыжок
            // без доворота в воздухе должен иногда промахиваться — попасть на
            // следующую горку и есть задача игрока.

            // Расчётная скорость вылета — между минимумом езды и потолком игрока
            // (9 и 19 по пресету игрока): кто сбросил скорость в повороте перед концом
            // горки — не долетает без газа W; кто летит на потолке — перелетает вход и
            // садится дальше по горке. Короткая рампа ловли делает недолёт настоящим.
            AssumedExitSpeed = 15f;
            CatchRampFraction = 0.3f;

            // Крутые повороты остаются крутыми: на скорости выше √(сцепление·R)
            // инерция выносит за борт — рулить внутрь и тормозить. Перед таким
            // поворотом генератор оставляет прямую, где затормозить можно.
            WidenUnfairTurns = false;
            TurnRadius = new FloatRange { Min = 7f, Max = 20f };
            FeaturesPerSlide = new IntRange { Min = 4, Max = 6 };

            // Закрытые трубы выключены по просьбе пользователя (2026-10-26):
            // «прозрачная не должна быть закрытой» — прозрачными делаются ОТКРЫТЫЕ
            // жёлобы (GlassOpenChance), закрытые трубы для этого не нужны.
            AllowClosedTubes = false;
            // 0.5 давало 4 стеклянных горки из 5 подряд (27.10.2026) — снижено.
            GlassOpenChance = 0.3f;
            GlassSquareChance = 0.5f;
            GlassGridChance = 0.25f;
            ClosedSlideChance = 0.35f;
            GlassTubes = true;
            TubeRadius = new FloatRange { Min = 1.1f, Max = 1.4f };
            ClosedWallThickness = new FloatRange { Min = 0.05f, Max = 0.08f };
            OpenFlumeShapeMode = OpenShapeMode.Rounded;
            ThicknessScale = 1f;
            // Ширина 2.9–3.3 показалась широкой (26.10.2026) — сужено до 2.2–2.5.
            FlumeWidth = new FloatRange { Min = 2.2f, Max = 2.5f };
            FlumeDepth = new FloatRange { Min = 1.1f, Max = 1.3f };
            FlumeLipHeight = new FloatRange { Min = 0.25f, Max = 0.35f };
            FlumeCornerRadius = new FloatRange { Min = 0.3f, Max = 0.4f };
            OpenFlumeWallThickness = new FloatRange { Min = 0.1f, Max = 0.14f };
            RingSegments = new IntRange { Min = 28, Max = 34 };

            StripedSlides = true;
            StripeLength = 4f;
            StripesAlongChance = 0f;
            MixedPaletteChance = 0.5f;
            // Палитра референса: синий, голубой, жёлтый, белый, бирюзовый, тёмно-синий, красный.
            StripeColors = new[]
            {
                new Color(0.10f, 0.35f, 0.85f, 1f),
                new Color(0.40f, 0.72f, 0.95f, 1f),
                new Color(0.98f, 0.82f, 0.12f, 1f),
                new Color(0.82f, 0.85f, 0.88f, 1f),
                new Color(0.20f, 0.78f, 0.72f, 1f),
                new Color(0.06f, 0.14f, 0.48f, 1f),
                new Color(0.90f, 0.16f, 0.12f, 1f)
            };

            // На широком дне тонкий слой воды: при прежней доле глубина воды была бы
            // ~30 см и ноги тонули бы в ленте.
            var water = GetWaterPreset();
            // Явно: 26.10.2026 диагностический опыт выключил воду через пресет, строку
            // убрали, а сериализованное false осталось и жило в сцене до 27.10.
            water.EnableWater = true;
            water.WaterFill = 0.12f;
            // Лента воды на 90% ширины жёлоба: за пределами плоской хорды она
            // ложится тонкой плёнкой по эллиптическим стенкам (BuildWaterSurface).
            water.MinWaterWidth = 0.9f;
            water.EnableTransparency = true;
            water.WaterTransparency = 0.35f;
            water.SuimonoTransRenderDistance = 60f;

            // Вода как на референсе: быстрый тонкий поток ВДОЛЬ жёлоба, мелкие
            // вытянутые по течению струи, белая пена, светлая мелкая вода.
            water.FlowAlignment = WaterFlowAlignment.AlongSlide;
            water.FlowDirectionOffset = 0f;
            water.FlowSpeed = 1.6f;
            water.WaveHeight = 0.05f;
            water.WaveScale = 1.5f;
            water.WaveStretchAlong = 0.5f;
            water.WaveStretchAcross = 6f;
            water.OverrideLook = true;
            water.FoamAmount = 0.9f;
            water.FoamScale = 0.25f;
            water.ShallowColor = new Color(0.7f, 0.92f, 1f, 1f);
            water.DepthColor = new Color(0.15f, 0.55f, 0.9f, 1f);
        }

        // Своя палитра полос для каждой горки: один базовый цвет из общей палитры
        // (по кругу — соседние горки всегда разного цвета) плюс его светлый и тёмный
        // оттенки и белый. Горки читаются как «синяя», «жёлтая», «бирюзовая»…
        Color[] BuildSlideStripePalette(int slideIndex)
        {
            if (StripeColors == null || StripeColors.Length == 0)
                return null;

            Color baseColor = StripeColors[((slideIndex % StripeColors.Length) + StripeColors.Length) % StripeColors.Length];
            Color light = Color.Lerp(baseColor, Color.white, 0.35f);
            Color dark = Color.Lerp(baseColor, Color.black, 0.35f);
            // Не чисто белый: под солнцем силой 4.5 белая полоса выгорала в пятно.
            var white = new Color(0.82f, 0.85f, 0.88f, 1f);
            return new[] { baseColor, light, white, dark, baseColor };
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

        void ApplyRandomSlideSettings(WaterSlideGenerator slide, SlideEnclosure enclosure, System.Random rng, int slideIndex)
        {
            float scale = Mathf.Max(ThicknessScale, 0.05f);

            slide.RingSegments = RingSegments.Sample(rng);
            slide.SamplesPerSegment = SamplesPerSegment.Sample(rng);
            slide.BankStrength = enclosure == SlideEnclosure.Open ? 0f : BankStrength.Sample(rng);

            slide.Striped = StripedSlides && enclosure == SlideEnclosure.Open;
            slide.StripesAlong = rng.NextDouble() < StripesAlongChance;
            slide.StripeLanes = StripeLanes.Sample(rng);
            // Смешанная палитра (вся) либо оттенки одного базового цвета — чтобы
            // горки различались цветом; продольные дорожки всегда из всей палитры.
            bool mixed = slide.StripesAlong || rng.NextDouble() < MixedPaletteChance;
            slide.StripeColors = mixed && StripeColors != null
                ? (Color[])StripeColors.Clone()
                : BuildSlideStripePalette(slideIndex);
            slide.StripeLength = StripeLength;
            slide.StripeSeed = rng.Next();

            slide.BoostStripCount = BoostStripsPerSlide.Sample(rng);
            slide.BoostStripLength = BoostStripLength;
            slide.BoostSeed = rng.Next();
            slide.BoostStripMaterial = BoostStripMaterial;

            // Стекло: все закрытые трубы и часть открытых жёлобов (GlassOpenChance).
            slide.GlassTube = GlassTubes && (enclosure == SlideEnclosure.Closed || rng.NextDouble() < GlassOpenChance);
            slide.TubeRingSpacing = TubeRingSpacing.Sample(rng);
            bool clearGlass = rng.NextDouble() < GlassClearChance;
            slide.GlassTint = clearGlass
                ? new Color(0.92f, 0.96f, 1f, Mathf.Min(GlassOpacity, 0.1f))
                : new Color(0.75f, 0.9f, 1f, GlassOpacity);
            slide.GlassNeon = GlassNeonColors != null && GlassNeonColors.Length > 0
                ? GlassNeonColors[rng.Next(GlassNeonColors.Length)]
                : Color.clear;
            if (slide.GlassTube && (slide.StripeColors == null || slide.StripeColors.Length == 0))
                slide.StripeColors = StripeColors != null ? (Color[])StripeColors.Clone() : null;

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
            _firstSlideIndex = 0;
            SlidesVersion++;
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
                WidenUnfairTurns = WidenUnfairTurns,
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

        GlassLineStyle PickGlassLineStyle(System.Random rng)
        {
            if (rng.NextDouble() < GlassGridChance)
                return GlassLineStyle.Grid;
            return rng.Next(3) switch
            {
                0 => GlassLineStyle.EdgesOnly,
                1 => GlassLineStyle.RingsOnly,
                _ => GlassLineStyle.EdgesAndRings
            };
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
