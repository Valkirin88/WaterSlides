using System.Collections.Generic;
using UnityEngine;
using WaterSlides;

// ДВИЖЕНИЕ ПО ГОРКЕ — ПУТЕВАЯ МОДЕЛЬ. Пока тело едет по горке, свободная физика
// выключена: состояние езды — три числа в координатах жёлоба (дистанция s вдоль
// пути, поперечное смещение l, скалярная скорость v вдоль пути), и каждый шаг
// позиция/ориентация тела ставятся из кадра пути (через velocity, чтобы рендер
// интерполировал). Правила игры выполняются ПО ПОСТРОЕНИЮ:
//  * A/D двигают только l, и ввод не может затолкать тело выше стенки — вывалиться
//    клавишами невозможно;
//  * тело всегда ориентировано вдоль касательной пути — развернуть его поперёк нельзя;
//  * W/S и проекция гравитации на касательную — одно скалярное уравнение на v:
//    вниз разгоняет, вверх тормозит, но не ниже минимума (назад не поехать);
//  * вылет за борт — честная инерция: центробежное ускорение v²·кривизна копит
//    поперечную скорость, тело взбирается по виртуальному скату стенки и, если
//    перевалило кромку, ОТСТЁГИВАЕТСЯ от пути в обычную баллистику — путь заворачивает,
//    а свободное тело летит прямо и уходит за борт. Никаких скриптовых подбросов.
// Воздух (прыжки, перелёты между горками, стрейф, рэгдол) остаётся на прежней
// свободной физике; приземление на горку пристёгивает тело обратно к пути.
[DefaultExecutionOrder(-100)]
public class PlayerView : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Rigidbody игрока — физическое тело, которым управляет вся симуляция. Если не задан, берётся с этого же объекта или создаётся автоматически.")]
    [SerializeField] Rigidbody _rigidbody;
    [Tooltip("CapsuleCollider тела: подгоняется под анимированную позу и служит контактом с горкой. Если не задан, берётся с этого же объекта или создаётся автоматически.")]
    [SerializeField] CapsuleCollider _capsule;
    [Tooltip("Ретранслятор столкновений с дочернего коллайдера. Если задан, собственные события OnCollision игнорируются и контакты приходят через него.")]
    [SerializeField] PlayerCollisionRelay _collisionRelay;
    [Tooltip("Корень визуальной модели: каждый кадр ему принудительно задаются локальные смещение и поворот (поля ниже).")]
    [SerializeField] Transform _visualRoot;
    [Tooltip("Animator humanoid-рига: по костям головы и стоп подгоняется капсула. Если не задан, ищется в дочерних объектах.")]
    [SerializeField] Animator _animator;

    [Header("Physics")]
    [Tooltip("Физматериал капсулы. Если не задан, создаётся скользкий материал без трения и отскока.")]
    [SerializeField] PhysicsMaterial _physicsMaterial;
    [Tooltip("Подгонять капсулу под анимированную позу (по костям humanoid-рига: голова и стопы). Размер и центр задаются на CapsuleCollider.")]
    [SerializeField] bool _fitCapsuleToBody = true;
    [Tooltip("Запас капсулы за макушкой и стопами, метры.")]
    [SerializeField] float _capsuleFitPadding = 0.12f;
    [Tooltip("Скорость сглаживания подгонки капсулы (меньше — плавнее).")]
    [SerializeField] float _capsuleFitSmoothing = 5f;
    [Tooltip("Масса тела, кг. Задаётся Rigidbody при старте; влияет на силу дополнительной гравитации.")]
    [SerializeField] float _mass = 10f;
    [Tooltip("Множитель гравитации игрока — «вес». На горке задаёт разгон на спусках/торможение на подъёмах и прижим в поворотах; в полёте укорачивает дуги. Требуемая скорость прыжков автоматически пересчитывается с учётом множителя (SetCourse).")]
    [SerializeField] float _gravityMultiplier = 1.5f;
    [Tooltip("Скорость доворота тела к поверхности горки и к направлению полёта. Больше — быстрее и жёстче выравнивание ориентации.")]
    [SerializeField] float _slideAlignSpeed = 10f;
    [Tooltip("Максимальный наклон тела ногами ВВЕРХ в свободном полёте (пока скорость направлена вверх), градусы. Малый — на вылете с трамплина тело не задирает ноги в небо.")]
    [SerializeField, Range(0f, 90f)] float _maxAirPitch = 35f;
    [Tooltip("Максимальный наклон тела ногами ВНИЗ при падении (скорость направлена вниз), градусы. Большой — в затяжном падении тело разворачивается ногами вперёд вдоль траектории, а не лежит плашмя.")]
    [SerializeField, Range(0f, 90f)] float _maxAirDivePitch = 75f;
    [Tooltip("Локальное смещение визуальной модели относительно физического тела, метры. Применяется каждый кадр.")]
    [SerializeField] Vector3 _visualLocalOffset = Vector3.zero;
    [Tooltip("Локальный поворот визуальной модели относительно физического тела, градусы (углы Эйлера). Применяется каждый кадр.")]
    [SerializeField] Vector3 _visualLocalEuler = Vector3.zero;
    [Tooltip("Поворот ПОЗЫ (кости таза и всего скелета) относительно физического тела, градусы. Модель — сам корень объекта (Animator на нём), отдельного дочернего объекта нет, поэтому поза разворачивается через таз после аниматора. Клип Flying лежит на животе головой по +Z; (180, 0, 0) кладёт на спину ногами вперёд. Пока работает рэгдол, не применяется (кости ведёт физика).")]
    [SerializeField] Vector3 _hipsPoseEuler = new(180f, 0f, 0f);
    [Tooltip("Прижать руки к туловищу: на сколько градусов плечевые кости доворачиваются к оси тела (от головы к тазу) ПОВЕРХ анимации, не дальше самой оси. 0 — как в клипе. Пока работает рэгдол, не применяется.")]
    [SerializeField, Range(0f, 90f)] float _armsTuckDegrees = 30f;
    [Tooltip("Свести ноги: на сколько градусов бедренные кости доворачиваются к средней линии между ногами ПОВЕРХ анимации, не дальше неё. 0 — как в клипе.")]
    [SerializeField, Range(0f, 45f)] float _legsTuckDegrees = 15f;

    [Header("Slide Ride")]
    [Tooltip("Максимальная скорость на горке, м/с. Может быть автоматически поднята под требуемую скорость прыжков трассы (SetCourse). 0 — БЕЗ потолка (с 28.10.2026, просьба пользователя): скорость ограничивают только геометрия трассы, автотормоз перед поворотами и трамплином и тормоз игрока; потолок посадки тогда тоже не действует, а множитель разгона на спуске работает всегда.")]
    [SerializeField] float _maxSlideSpeed = 0f;
    [Tooltip("Минимальная скорость езды вдоль горки, м/с — ниже неё гравитация замедлить не может (подъёмы: тело никогда не скатывается назад; недостающую скорость докручивает «поток» с разгоном 25 м/с²). Зажатый тормоз S — осознанный выбор и может замедлять ниже минимума до полной остановки, но задний ход не начинается никогда. 0 — выключить.")]
    [SerializeField] float _minRideSpeed = 14f;
    [Tooltip("«Сцепление» с жёлобом, м/с² — используется автотормозом перед поворотами и генератором трассы для расчёта честности поворотов: безопасная скорость поворота ≈ √(доля × сцепление × радиус).")]
    [SerializeField] float _corneringGrip = 20f;
    [Tooltip("Доля сцепления, доступная езде БЕЗ ввода (0–1): автотормоз перед поворотом сбрасывает скорость до √(доля × сцепление × радиус). Меньше — осторожнее автотормоз; вылеты в поворотах начинаются на меньшей скорости.")]
    [SerializeField, Range(0f, 1f)] float _autoGripFraction = 0.8f;
    [Tooltip("Сила автотормоза перед поворотами и трамплином без ввода игрока (0–1). 1 — тормозит сам до безопасной скорости; 0 — выключен: скорость держит только игрок (S), иначе инерция в крутом повороте выносит за борт.")]
    [SerializeField, Range(0f, 1f)] float _autoCornerBrake = 1f;
    [Tooltip("Крутизна виртуального ската наружной стенки (метров подъёма на метр поперечного хода). Определяет порог вылета в повороте: тело начинает взбираться по стенке, когда центробежное ускорение v²/R превышает примерно гравитацию × эту крутизну (при гравитации ×2 и 0.7 — около 14 м/с²: 16 м/с в повороте радиусом 18 м). Меньше — вылеты легче и чаще; больше — жёлоб держит сильнее. До 28.10.2026 было 1.2 — вместе с удержанием ползунка без руля вылететь было невозможно.")]
    [SerializeField] float _wallSlope = 0.7f;
    [Tooltip("Гашение поперечной скорости на полу жёлоба, 1/с — успокаивает болтание влево-вправо после руления и приземлений. 0 — не гасить.")]
    [SerializeField] float _lateralDamping = 1.5f;
    [Tooltip("Трение на скате стенки, 1/с — гасит поперечную скорость, пока тело выше полосы пола. Короткий всплеск инерции (пересёк жёлоб и ткнулся в стенку) глохнет и тело сползает назад; вылетает за борт только ПОСТОЯННЫЙ перегон скорости в повороте (центробежная сила кормит подъём быстрее, чем трение гасит). Меньше — вылеты легче и быстрее: скорость подъёма по стенке = (центробежное − порог стенки) / это число.")]
    [SerializeField] float _wallClimbDamping = 3f;
    [Tooltip("Центробежный снос цели руля-ползунка: метров поперёк на каждый м/с² центробежного ускорения v²/R (и боковой гравитации на кренах). В повороте цель уезжает НАРУЖУ, и чтобы остаться в жёлобе, игрок должен увести палец внутрь поворота; без руля тело прижимается к наружной стенке и на скорости вылетает. Палец у внутренней стенки компенсирует около (ширина пола ÷ это число) м/с² — выше спасает только тормоз. 0 — регулятор держит тело у пальца с полной силой, и вылететь без руля нельзя (так было до 28.10.2026).")]
    [SerializeField] float _cornerDriftPerAccel = 0.055f;
    [Tooltip("«Липкость» на перегибах профиля (открытый жёлоб). Тело отрывается от горки, когда на выпуклом изломе (резкий уход в спуск, гребень) центробежное v²·кривизна превышает прижим гравитации × это число. 1 — физически честно; больше — тело липнет к профилю сильнее (отрывы реже); меньше — отрывается охотнее.")]
    [SerializeField] float _crestStickiness = 1f;
    [Tooltip("Удержание после приземления, сек: столько времени после пристёгивания к горке тело не может вылететь за борт (взбирание по стенке отключено, поперечное смещение зажато в жёлобе). 0 — выключить.")]
    [SerializeField] float _landingContainTime = 1f;
    [Tooltip("Потолок скорости езды, которую задаёт пристёгивание при приземлении, м/с. При падении с большой высоты величина скорости огромна; излишек сверх потолка гасится ударом о воду. Если максимальная скорость на горке выше потолка — действует она; при выключенном потолке скорости (Max Slide Speed 0) не действует и этот. 0 — без потолка.")]
    [SerializeField] float _landingSnapSpeedCap = 18f;
    [Tooltip("Пауза повторного пристёгивания к горке после намеренного прыжка или вылета за борт, сек — чтобы жёлоб не поймал тело обратно в первый же миг.")]
    [SerializeField] float _jumpGuideSuppression = 0.6f;
    [Tooltip("Вылет за борт: минимальная скорость НАРУЖУ в момент перевала через кромку, м/с. Физически у кромки боковая скорость мала (3 м/с), тело за 0.6 с уходило на 2 м и падало обратно на стенку того же жёлоба — читалось как «край отталкивает обратно». С запасом наружу вылет становится настоящим.")]
    [SerializeField] float _flyoutMinOutwardSpeed = 4f;
    [Tooltip("Множитель боковой скорости наружу при вылете за борт (1 — физическая).")]
    [SerializeField] float _flyoutOutwardBoost = 1.5f;
    [Tooltip("После вылета за борт столько секунд тело не пристёгивается ни к какой горке — чтобы не ловиться за кромку той же горки в первые метры.")]
    [SerializeField] float _flyoutSuppression = 1.2f;
    [Tooltip("Зазор между поверхностью жёлоба и капсулой при езде, метры — тело ведётся чуть над мешем, чтобы коллайдер горки не пинал капсулу на швах.")]
    [SerializeField] float _surfaceClearance = 0.03f;
    [Tooltip("Скорость сглаживания нормали поверхности для ориентации тела и камеры (больше — быстрее реагирует).")]
    [SerializeField] float _normalSmoothing = 14f;
    [Tooltip("Разгон на спуске: множитель к составляющей гравитации вдоль пути, пока она РАЗГОНЯЕТ тело (на подъёмах торможение гравитацией без множителя). 1 — физически честно; больше — на спусках скорость набирается быстрее (потолок — максимальная скорость на горке). 1.8 → 1.3 06.11.2026: просьба «разгон помедленнее».")]
    [SerializeField] float _downhillAccelBoost = 1.3f;

    [Header("Приземление и падение")]
    [Tooltip("Ловля горки свёрткой: каждый шаг физики в полёте отрезок пути центра капсулы (прошлое положение → текущее, плюс радиус вперёд) проверяется лучом на пересечение с внутренней поверхностью горки. Пересёк — тело ставится на поверхность и пристёгивается, даже если PhysX не выдал контакт (пролёт сквозь меш на скорости падения). Выкл — только контакты PhysX, как раньше.")]
    [SerializeField] bool _sweepLanding = true;
    [Tooltip("Проверка «есть ли горка, на которую ещё можно упасть» (CanStillReachSlide): горизонт баллистического прогноза, сек.")]
    [SerializeField] float _reachHorizon = 8f;
    [Tooltip("Запас на воздушное управление в прогнозе: радиус сферы вокруг точки траектории растёт с этой скоростью, м/с (A/D в полёте уводят тело вбок до 14 м/с), плюс 3·t² м на газ/тормоз W/S вдоль полёта. Щедро: игра начинается заново только когда горки точно не достать.")]
    [SerializeField] float _reachSteerRadiusRate = 7f;
    [Tooltip("Потолок радиуса сферы прогноза, м.")]
    [SerializeField] float _reachMaxRadius = 25f;

    [Header("Movement")]
    [Tooltip("Ускорение по W / стрелке вверх вдоль движения, м/с². Работает и на горке, и в воздухе.")]
    [SerializeField] float _throttleAcceleration = 12f;
    [Tooltip("Газ W: на сколько м/с можно превысить потолок скорости горки, пока зажата кнопка (излишек потом стекает мягким потолком). На спуске тело и так на потолке, без надбавки газ ничего не давал (просьба 28.10.2026). Риск осознанный: выше ~21 м/с тело срывается с перегибов, под которые трасса не рассчитана.")]
    [SerializeField] float _throttleSpeedBonus = 3f;
    [Tooltip("Замедление по S, м/с². Работает и на горке, и в воздухе.")]
    [SerializeField] float _brakeAcceleration = 12f;
    [Tooltip("Боковое ускорение руля на горке (ползунок-палец и A/D зонда), м/с². Двигает тело поперёк жёлоба; выше стенки ввод затолкать не может. Это же — предел силы, с которой палец у внутренней стенки удерживает тело против центробежного сноса.")]
    [SerializeField] float _slideSteerForce = 40f;
    [Tooltip("Потолок поперечной скорости, которую может разогнать руль, м/с. Ограничивает только ввод: накопленную в повороте инерцию порог не срезает. 6 — пол жёлоба (±0.9 м) пересекается за ~0.3 с.")]
    [SerializeField] float _maxSlideSteerSpeed = 6f;
    [Tooltip("Руль-ползунок (палец): как быстро тело идёт к заданному поперечному положению, 1/с. Поперечная скорость к цели = расстояние × это число (с потолком Max Slide Steer Speed), ускорение ограничено Slide Steer Force. Демпфирование — см. Steer Damping Ratio. 10 — отклик ~0.1 с; 6 (до 28.10.2026) читалось как вялый руль.")]
    [SerializeField] float _lateralTargetGain = 10f;
    [Tooltip("Руль-ползунок: затухание регулятора. 1 — критическое: тело подходит к пальцу без перелёта и обратного качания; 0.7 (до 06.11.2026) давало небольшой перелёт, который читался как дёрганье.")]
    [SerializeField, Range(0.5f, 1.5f)] float _steerDampingRatio = 1f;
    [Tooltip("Ускоряющая полоса на горке: добавка к разгону, м/с², пока тело едет по ней.")]
    [SerializeField] float _boostAcceleration = 10f;
    [Tooltip("Ускоряющая полоса: на сколько м/с можно превысить потолок скорости горки, пока тело на полосе (и ещё секунду после — излишек потом стекает). 0 — полоса лишь быстрее добирает до потолка. ВАЖНО: с +4 тело на 21 м/с срывалось с перегибов, под которые трасса не рассчитана (проверено автозаездом 27.10.2026: респавн каждый круг).")]
    [SerializeField] float _boostSpeedBonus = 0f;
    [Tooltip("Трамплинный разгон: за сколько метров до конца горки тело незаметно подтягивается до расчётной скорости прыжка (WaterSlideCourse.RequiredJumpSpeed), если иначе вылетело бы медленнее. Вход следующей горки стоит на дуге полёта под эту скорость; горки, кончающиеся носом вниз, при медленном вылете (тормоз, автотормоз, медленный разгон) давали недолёт, который не спасал даже прыжок (06.11.2026). 0 — выключить.")]
    [SerializeField] float _exitBoostDistance = 15f;
    [Tooltip("Трамплинный разгон: предел добавочного ускорения, м/с².")]
    [SerializeField] float _exitBoostMaxAccel = 30f;
    [Tooltip("Помощь приземлению: после вылета с КОНЦА горки (или прыжка в последних Exit Boost Distance метрах) скорость вдоль направления на начало следующей горки плавно подгоняется так, чтобы дуга пришла на её вход — и при перелёте, и при недолёте, и после прыжка. Боковой промах (смещение входа) не исправляется — это задача игрока (стрейф). Предел ускорения, м/с²; 0 — выключить. Анализ 06.11.2026 (меню Water Slides/Analyze Jumps): горка с концом −25° ловила следующую только при вылете 12–20 м/с, с прыжком — 8–12.")]
    [SerializeField] float _landingAssistAccel = 25f;
    [Tooltip("Помощь приземлению: куда целиться — на столько метров от начала следующей горки (внутрь ловчей рампы).")]
    [SerializeField] float _landingAssistTargetS = 4f;
    [Tooltip("Без прыжка не долететь (просьба 10.11.2026): если на перелёте с конца горки не было прыжка (прыжок у конца горки или прыжок в воздухе), полёт вдоль направления на вход гасится так, чтобы дуга опустилась до уровня НАЧАЛА следующей горки (её ловчей рампы) на столько метров раньше него — и прошла под ней. Целиться в долю скорости нельзя: ловчая рампа тянется навстречу вдоль линии прыжка и ловила даже сильно замедленные дуги (замер 10.11.2026). После прыжка в воздухе в том же полёте включается обычная помощь. 0 — правило выключено.")]
    [SerializeField, Min(0f)] float _noJumpShortfall = 3f;
    [Tooltip("Скорость выпрыгивания с горки по нажатию прыжка, м/с (вдоль нормали поверхности с добавкой вверх). Больше — выше и дальше прыжок.")]
    [SerializeField] float _slideJumpForce = 9f;
    [Tooltip("Боковое ускорение A/D в воздухе, м/с² — в полёте манёвр должен быть заметно резче, чем на горке.")]
    [SerializeField] float _airStrafeAcceleration = 45f;
    [Tooltip("Скорость одиночного прыжка вверх в воздухе, м/с (один раз за полёт). Больше — выше подскок.")]
    [SerializeField] float _airJumpForce = 7f;
    [Tooltip("Потолок боковой скорости A/D в воздухе, м/с.")]
    [SerializeField] float _maxAirStrafeSpeed = 14f;
    [Tooltip("Гашение боковой скорости в воздухе после отпускания A/D, м/с² — стрейф останавливается, и полёт продолжается вдоль исходного направления схода с горки (движение вбок, а не поворот). 0 — накопленный боковой снос сохраняется до приземления.")]
    [SerializeField] float _airStrafeDamping = 30f;

    PlayerController _controller;
    PlayerFlightRagdoll _flightRagdoll;
    bool _spawnPrepared;
    bool _hasSlideContactSinceSpawn;

    // ---- Состояние путевой модели езды ----
    bool _riding;
    WaterSlideGenerator _currentSlide;
    WaterSlideCourse _course;
    float _ridePathS;        // дистанция вдоль пути, м
    // На сколько метров за край пола центробежный снос может увести цель ползунка:
    // ограничивает скорость, с которой регулятор подводит тело к стенке
    // (≤ DriftOverhang × гейн), дальше тело поднимает только физика ската.
    const float DriftOverhang = 0.35f;
    float _rideLateral;      // поперечное смещение от осевой, м (открытый жёлоб)
    float _rideLateralVel;   // поперечная скорость, м/с
    WaterSlideGenerator _landingAssistSlide; // следующая горка, на которую ведёт помощь приземлению
    bool _landingAssistJumped;               // был ли прыжок на этом перелёте (без него — недолёт)
    float _rideSpeed;        // скалярная скорость вдоль пути, м/с
    float _tubeAngle;        // закрытая труба: угол на сечении от нижней точки, рад
    float _tubeAngularVel;   // закрытая труба: угловая скорость, рад/с
    float _smoothedCurvature;
    float _cornerDanger;
    float _landingContainTimer;
    int _rideFrameIndex;
    // Подскок с перегиба (не прыжок и не вылет): пока таймер жив, воздушное
    // управление молчит — короткая баллистическая дуга над своей же горкой.
    float _crestHopTimer;
    // Сколько секунд тело пристёгнуто к текущей горке (0 в полёте).
    float _rideTime;

    // Кэш длин дуги кадров пути текущей горки (индекс i → дистанция от начала).
    WaterSlideGenerator _lengthCacheSlide;
    float[] _cumLengths;

    // ---- Общее состояние (езда + полёт) ----
    Vector3 _slideNormal = Vector3.up;
    Vector3 _slideNormalTarget = Vector3.up;
    Vector3 _guideTangent = Vector3.forward;
    float _guideSuppressTimer;
    float _timeSinceSlideContact = 999f;
    int _lastGuideIndex = -1;
    // Центр капсулы на прошлом шаге физики — начало отрезка свёртки (ловля горки).
    Vector3 _lastSweepCenter;
    bool _hasLastSweepCenter;
    readonly RaycastHit[] _sweepHits = new RaycastHit[16];
    readonly Collider[] _overlapBuffer = new Collider[16];

    // Плоскость кромки/осевая линия ведущего открытого жёлоба — для камеры.
    Vector3 _rimPlanePoint;
    Vector3 _rimPlaneUp;
    bool _hasRimPlane;
    Vector3 _flumeCenterPoint;

    Vector3 _fitCenter;
    float _fitHeight;
    bool _fitInitialized;
    float _slideSteerInput;
    float _slideLateralTarget;   // −1..1 (доля ширины пола), руль-ползунок
    bool _hasLateralTarget;
    float _boostTimer;           // >0 — тело на ускоряющей полосе или только что с неё

    // Горизонтальное направление полёта, замороженное в момент отрыва: весь полёт
    // (ось стрейфа, yaw тела, камера) привязан к нему, чтобы A/D читался как сдвиг
    // дуги вбок, а не как поворот.
    Vector3 _flightHeading = Vector3.forward;

    public Rigidbody Rigidbody => _rigidbody;
    public Transform MovementTransform => transform;
    // Генератор трассы читает это, чтобы ставить приземления прыжков на настоящую дугу.
    public float GravityMultiplier => Mathf.Max(_gravityMultiplier, 1f);
    // Читаются WaterSlideCourse: честность поворотов и тормозные прямые считаются
    // под эти значения игрока.
    public float CorneringGrip => _corneringGrip;
    public float BrakeDeceleration => _brakeAcceleration;
    // 0 в поле — потолка нет: наружу отдаём бесконечность, чтобы сравнения
    // «скорость < потолка» (разгон, газ W) и Mathf.Min в генераторе трассы
    // работали без особых случаев.
    public bool HasSpeedCap => _maxSlideSpeed > 0f;
    public float MaxRideSpeed => HasSpeedCap ? _maxSlideSpeed : float.PositiveInfinity;

    public bool IsOnSlide => _riding;
    // Горка, по которой тело едет (или ехало до отрыва); null до первого касания.
    public WaterSlideGenerator CurrentSlide => _currentSlide;
    public bool IsGuided => _riding;
    // Подскок с перегиба профиля: тело в воздухе, но это короткая дуга над своей же
    // горкой — воздушный стрейф там молчит (иначе зажатый A/D бил бы вбок с полной
    // силой на каждом отрыве). Настоящие полёты (прыжок, вылет, сход с конца)
    // таймер не взводят — там управление включается сразу.
    public bool IsAirborneHopNearSlide => !_riding && _crestHopTimer > 0f;
    public bool HasSlideContactSinceSpawn => _hasSlideContactSinceSpawn;
    // Сколько секунд тело в полёте (0 на горке; 999 после спавна до первого касания).
    public float TimeSinceSlideContact => _timeSinceSlideContact;
    // Ставится PlayerFlightRagdoll: подгонка капсулы под машущие кости куклы раздула бы её в полёте.
    public bool CapsuleFitSuspended { get; set; }
    public Vector3 SlideNormal => _slideNormal;
    // Центр капсулы в мире по ИНТЕРПОЛИРОВАННОМУ трансформу — точка слежения
    // камеры. Пивот объекта стоит у ног (~1 м от центра): при развороте тела на
    // перегибе пивот описывает дугу в полметра, и камера, идущая за пивотом,
    // дёргалась вместе с ним; центр при развороте почти неподвижен.
    public Vector3 BodyCenter => _capsule != null ? transform.TransformPoint(_capsule.center) : transform.position;
    // Мировая точка касания горки: низ капсулы по текущей нормали. Пивот Player
    // стоит у ног рига, поэтому эффекты «из-под тела» должны брать эту точку, а не
    // transform.position и не фиксированный локальный сдвиг.
    public Vector3 SlideContactPoint
    {
        get
        {
            Vector3 up = _slideNormal.sqrMagnitude > 1e-6f ? _slideNormal.normalized : Vector3.up;
            return GetBodyCenterWorld() - up * (_capsule != null ? _capsule.radius : 0.35f);
        }
    }
    // Ось стрейфа для PlayerController.ApplyAirMovement (см. _flightHeading).
    public Vector3 FlightHeading => _flightHeading;
    public float SpawnBodyClearance => _capsule != null ? _capsule.radius : 0.35f;

    // Телеметрия/отладка: текущее состояние путевой модели.
    public float RideSpeed => _rideSpeed;
    public float RideLateral => _rideLateral;
    public float RideLateralVelocity => _rideLateralVel;
    public float RidePathDistance => _ridePathS;
    // Тело на ускоряющей полосе (или сошло с неё меньше секунды назад) — для эффектов.
    public bool BoostActive => _boostTimer > 0f;
    public float AutoCornerBrake => _autoCornerBrake;
    // Опасность ближайшего поворота для HUD: отношение центробежного ускорения
    // v²·κ на самом крутом повороте в тормозном горизонте (включая текущий) к тому,
    // что удерживает палец, уведённый в поворот до упора. ≥ 1 — на этой скорости
    // поворот не удержать, спасает только тормоз; 0 — в воздухе, в трубе и на
    // скорости «рельсовой» стенки (не выше минимума езды + 1).
    public float CornerDanger => _riding ? _cornerDanger : 0f;
    // Следующая горка курса после текущей (по порядку в Slides); null, если нет.
    public WaterSlideGenerator NextSlide
    {
        get
        {
            if (_course == null || _currentSlide == null)
                return null;
            var slides = _course.Slides;
            for (int i = 0; i < slides.Count - 1; i++)
                if (slides[i] == _currentSlide)
                    return slides[i + 1];
            return null;
        }
    }
    // Вызывается при каждом (ре)старте игры — HUD сбрасывает счёт.
    public event System.Action PlayReset;

    // Плоскость кромки ведущего открытого жёлоба: точка на уровне верха борта на
    // осевой линии, нормаль = up жёлоба. Живёт только пока тело пристёгнуто к
    // открытому жёлобу — в полёте и трубах выключена.
    public bool TryGetFlumeRimPlane(out Vector3 point, out Vector3 up)
    {
        point = _rimPlanePoint;
        up = _rimPlaneUp;
        return _hasRimPlane && _riding;
    }

    // Осевая линия ведущего открытого жёлоба: точка на оси, касательная пути, up жёлоба.
    // Камера смещает точку подвеса к оси, когда тело прижато к стенке.
    public bool TryGetFlumeCenterline(out Vector3 point, out Vector3 tangent, out Vector3 up)
    {
        point = _flumeCenterPoint;
        tangent = _guideTangent;
        up = _rimPlaneUp;
        return _hasRimPlane && _riding;
    }

    // Касательная пути горки на `distanceAhead` метров ВПЕРЕДИ тела (для камеры:
    // смотреть вдоль жёлоба и заранее поворачивать в виражи). Только на езде.
    public bool TryGetRideAheadTangent(float distanceAhead, out Vector3 tangent)
    {
        tangent = Vector3.zero;
        // На подскоке с перегиба путь ещё «свой»: камера продолжает смотреть вдоль
        // жёлоба, а не дёргается на курс тела и обратно (s оценивается по скорости).
        bool hop = !_riding && _crestHopTimer > 0f;
        if ((!_riding && !hop) || _currentSlide == null || _cumLengths == null)
            return false;

        var frames = _currentSlide.RideFrames;
        if (frames == null || frames.Count < 2 || _cumLengths.Length != frames.Count)
            return false;

        float s = _ridePathS + (hop ? _rideSpeed * _timeSinceSlideContact : 0f);
        SampleFrame(frames, s + Mathf.Max(distanceAhead, 0f), out _, out tangent, out _, out _);
        return tangent.sqrMagnitude > 1e-6f;
    }

    public PlayerMovementSettings MovementSettings => new()
    {
        SlideSteerForce = _slideSteerForce,
        MaxSlideSteerSpeed = _maxSlideSteerSpeed,
        SlideJumpForce = _slideJumpForce,
        AirStrafeAcceleration = _airStrafeAcceleration,
        AirStrafeDamping = _airStrafeDamping,
        AirJumpForce = _airJumpForce,
        MaxAirStrafeSpeed = _maxAirStrafeSpeed,
        ThrottleAcceleration = _throttleAcceleration,
        BrakeAcceleration = _brakeAcceleration,
        MaxForwardSpeed = MaxRideSpeed
    };

    public void Bind(PlayerController controller)
    {
        _controller = controller;
    }

    public void PrepareForSpawn()
    {
        EnsurePhysicsSetup();
        _spawnPrepared = true;

        if (_rigidbody == null)
            return;

        // Обнулить скорости ДО перевода в кинематику: кинематическое тело отклоняет
        // запись скорости с предупреждением в консоли.
        if (!_rigidbody.isKinematic)
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }

        _rigidbody.isKinematic = true;
    }

    public void BeginPlay()
    {
        if (_rigidbody == null)
            return;

        _rigidbody.isKinematic = false;
        _rigidbody.WakeUp();
        _spawnPrepared = false;
        _boostTimer = 0f;
        PlayReset?.Invoke();
    }

    void Awake()
    {
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();

        if (_animator != null)
            _animator.applyRootMotion = false;

        _flightRagdoll = GetComponent<PlayerFlightRagdoll>();
        EnsurePhysicsSetup();
        _collisionRelay?.Bind(this);
    }

    void FixedUpdate()
    {
        if (_spawnPrepared || _rigidbody == null)
            return;

        UpdateColliderPose();

        if (_guideSuppressTimer > 0f)
            _guideSuppressTimer -= Time.fixedDeltaTime;
        TickIgnoredSlideCollision();

        if (_riding)
        {
            _timeSinceSlideContact = 0f;
            UpdateRide();
        }
        else
        {
            _timeSinceSlideContact += Time.fixedDeltaTime;
            if (_crestHopTimer > 0f)
                _crestHopTimer -= Time.fixedDeltaTime;
            if (!TrySweepLanding())
            {
                ApplyExtraGravity();
                ApplyLandingAssist();
            }
        }

        _lastSweepCenter = GetBodyCenterWorld();
        _hasLastSweepCenter = true;
        AlignBody();
        _controller?.FixedUpdate();
    }

    // Ловля горки свёрткой (жалоба 27.10.2026: «иногда пролетает сквозь горку при
    // падении»). Контакты PhysX на скорости падения 25–35 м/с иногда не возникают:
    // капсула за шаг проходит 0.5–0.7 м, а коллайдер горки — тонкая одиночная
    // оболочка; CCD не всегда успевает. Отрезок центра капсулы за прошедший шаг
    // (продлённый на радиус вперёд) проверяется лучом; пересечение внутренней
    // поверхности горки = приземление: тело ставится на поверхность и пристёгивается.
    // Возвращает true, если пристегнулось.
    bool TrySweepLanding()
    {
        if (!_sweepLanding || !_hasLastSweepCenter || _rigidbody == null || _rigidbody.isKinematic)
            return false;

        Vector3 center = GetBodyCenterWorld();
        Vector3 motion = center - _lastSweepCenter;
        float travelled = motion.magnitude;
        if (travelled < 1e-4f)
            return false;

        float radius = _capsule != null ? _capsule.radius : 0.35f;
        Vector3 dir = motion / travelled;
        float length = travelled + radius;
        int count = Physics.RaycastNonAlloc(_lastSweepCenter, dir, _sweepHits, length, ~0, QueryTriggerInteraction.Ignore);
        if (count == 0)
            return false;

        WaterSlideGenerator best = null;
        RaycastHit bestHit = default;
        for (int i = 0; i < count; i++)
        {
            var hit = _sweepHits[i];
            if (best != null && hit.distance >= bestHit.distance)
                continue;
            if (hit.collider == null || hit.collider == _ignoredSlideCollider)
                continue;
            var slide = GetSlideFrom(hit.collider);
            if (slide == null || !IsInnerSurfacePoint(slide, hit.point, hit.normal))
                continue;
            // Подавление после прыжка/вылета — только для СВОЕЙ горки: другая горка
            // на пути — настоящее приземление.
            if (_guideSuppressTimer > 0f && slide == _currentSlide)
                continue;
            best = slide;
            bestHit = hit;
        }

        if (best == null)
            return false;

        // Тело — на поверхность в точке пересечения, затем обычное пристёгивание
        // (оно само считает s, поперечное положение и скорость по положению тела).
        Vector3 targetCenter = bestHit.point + bestHit.normal * (radius + _surfaceClearance);
        _rigidbody.position += targetCenter - center;
        AttachToSlide(best);
        return _riding;
    }

    // Может ли тело из текущего положения и скорости ещё попасть на какую-нибудь
    // горку: баллистический прогноз (гравитация игрока, без сопротивления) на
    // _reachHorizon секунд шагами 0.1 с; каждый отрезок проверяется сферой, радиус
    // которой растёт со временем — запас на воздушное управление A/D. Считается
    // только ВНУТРЕННЯЯ поверхность горки (на дно снизу не приземлиться). Читает
    // EntryPoint: игра начинается заново, только когда это false.
    public bool CanStillReachSlide()
    {
        if (_rigidbody == null)
            return true;

        Vector3 pos = GetBodyCenterWorld();
        Vector3 vel = _rigidbody.linearVelocity;
        Vector3 g = Physics.gravity * GravityMultiplier;
        const float step = 0.1f;
        float t = 0f;
        while (t < _reachHorizon)
        {
            Vector3 next = pos + vel * step + g * (0.5f * step * step);
            Vector3 seg = next - pos;
            float len = seg.magnitude;
            if (len > 1e-4f)
            {
                // Боковой стрейф (линейно по t) + газ/тормоз вдоль полёта (±6 м/с² → 3·t²).
                float radius = Mathf.Min(0.8f + _reachSteerRadiusRate * t + 3f * t * t, _reachMaxRadius);
                int count = Physics.SphereCastNonAlloc(pos, radius, seg / len, _sweepHits, len, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var hit = _sweepHits[i];
                    if (hit.collider == null)
                        continue;
                    var slide = GetSlideFrom(hit.collider);
                    if (slide != null && IsInnerSurfacePoint(slide, hit.point, hit.normal))
                        return true;
                }

                // SphereCast НЕ видит коллайдер, который сфера задевает уже в начале
                // отрезка, — а с радиусом в метры это ровно горка рядом с телом.
                // Перекрытие: горка, чей ближайший кадр пути не выше тела и в пределах
                // радиуса (+ полуширина жёлоба), считается достижимой.
                int overlaps = Physics.OverlapSphereNonAlloc(pos, radius, _overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < overlaps; i++)
                {
                    var slide = _overlapBuffer[i] != null ? GetSlideFrom(_overlapBuffer[i]) : null;
                    var frames = slide != null ? slide.RideFrames : null;
                    if (frames == null || frames.Count == 0)
                        continue;
                    Vector3 fp = frames[FindNearestFrame(frames, pos, 0, frames.Count - 1)].Position;
                    if (fp.y <= pos.y + 1f && (fp - pos).magnitude <= radius + slide.RideHalfWidth)
                        return true;
                }
            }
            pos = next;
            vel += g * step;
            t += step;
        }
        return false;
    }

    void LateUpdate()
    {
        ApplyVisualLocalPose();
        ApplyHipsPose();
        ApplyLimbTuck();
    }

    // Руки к туловищу, ноги вместе — поверх клипа, в МИРОВЫХ осях (локальные оси
    // костей ригов разные, а поворот «направление кости → цель» от них не зависит).
    // Поворачивается только плечевая/бедренная кость, предплечье и голень едут с ней.
    void ApplyLimbTuck()
    {
        if (_animator == null || !_animator.isHuman)
            return;
        if (_flightRagdoll != null && _flightRagdoll.IsActive)
            return;

        var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
        var head = _animator.GetBoneTransform(HumanBodyBones.Head);
        if (hips == null || head == null)
            return;

        if (_armsTuckDegrees > 0f)
        {
            Vector3 torsoDown = (hips.position - head.position).normalized;
            TuckLimb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, torsoDown, _armsTuckDegrees);
            TuckLimb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, torsoDown, _armsTuckDegrees);
        }

        if (_legsTuckDegrees > 0f)
        {
            var lUp = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var lLow = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var rUp = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var rLow = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            if (lUp != null && lLow != null && rUp != null && rLow != null)
            {
                Vector3 mid = ((lLow.position - lUp.position).normalized + (rLow.position - rUp.position).normalized).normalized;
                if (mid.sqrMagnitude > 0.5f)
                {
                    TuckLimb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, mid, _legsTuckDegrees);
                    TuckLimb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, mid, _legsTuckDegrees);
                }
            }
        }
    }

    void TuckLimb(HumanBodyBones upperBone, HumanBodyBones lowerBone, Vector3 target, float degrees)
    {
        var upper = _animator.GetBoneTransform(upperBone);
        var lower = _animator.GetBoneTransform(lowerBone);
        if (upper == null || lower == null)
            return;

        Vector3 dir = lower.position - upper.position;
        if (dir.sqrMagnitude < 1e-6f)
            return;
        dir.Normalize();
        Vector3 turned = Vector3.RotateTowards(dir, target, degrees * Mathf.Deg2Rad, 0f);
        upper.rotation = Quaternion.FromToRotation(dir, turned) * upper.rotation;
    }

    // Разворот позы после аниматора: таз — прямой ребёнок корня, его локальное
    // пространство = пространство тела, поэтому поворот и позиция просто домножаются.
    // Рэгдол (DefaultExecutionOrder 10) бежит ПОСЛЕ этого LateUpdate: строит куклу и
    // смешивает снимок позы уже с развёрнутым скелетом; пока кукла жива — кости у физики.
    void ApplyHipsPose()
    {
        if (_animator == null || !_animator.isHuman || _animator.transform != transform)
            return;
        if (_flightRagdoll != null && _flightRagdoll.IsActive)
            return;
        if (_hipsPoseEuler.sqrMagnitude < 1e-6f)
            return;

        var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null || hips.parent != transform)
            return;

        var flip = Quaternion.Euler(_hipsPoseEuler);
        hips.localRotation = flip * hips.localRotation;
        hips.localPosition = flip * hips.localPosition;
    }

    void ApplyVisualLocalPose()
    {
        if (_visualRoot == null)
            return;

        _visualRoot.localPosition = _visualLocalOffset;
        _visualRoot.localRotation = Quaternion.Euler(_visualLocalEuler);
    }

    void UpdateColliderPose()
    {
        if (_capsule == null)
            return;

        if (_fitCapsuleToBody && !CapsuleFitSuspended)
            TryFitCapsuleToAnimatedBody();

        SyncCenterOfMass();
    }

    void SyncCenterOfMass()
    {
        if (_rigidbody != null && (_rigidbody.centerOfMass - _capsule.center).sqrMagnitude > 1e-4f)
            _rigidbody.centerOfMass = _capsule.center;
    }

    // Капсула ВСЕГДА лежит вдоль локального Z (тело скользит головой/ногами вперёд).
    // Подгоняются только длина и центр: выбор оси по линии голова-ноги переворачивал
    // капсулу вбок на выгнутых позах, а каждый переворот в контакте — пинок депенетрации.
    bool TryFitCapsuleToAnimatedBody()
    {
        if (_animator == null || !_animator.isHuman)
            return false;

        var head = _animator.GetBoneTransform(HumanBodyBones.Head);
        var leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (head == null || leftFoot == null || rightFoot == null)
            return false;

        Vector3 headLocal = transform.InverseTransformPoint(head.position);
        Vector3 feetLocal = transform.InverseTransformPoint((leftFoot.position + rightFoot.position) * 0.5f);

        if ((headLocal - feetLocal).magnitude < 0.2f)
            return false;

        float pad = _capsuleFitPadding + _capsule.radius * 0.25f;
        float zMin = Mathf.Min(headLocal.z, feetLocal.z) - pad;
        float zMax = Mathf.Max(headLocal.z, feetLocal.z) + pad;

        float height = Mathf.Max(zMax - zMin, _capsule.radius * 2f);
        Vector3 center = new(
            (headLocal.x + feetLocal.x) * 0.5f,
            (headLocal.y + feetLocal.y) * 0.5f,
            (zMin + zMax) * 0.5f);

        if (!_fitInitialized)
        {
            _fitCenter = center;
            _fitHeight = height;
            _fitInitialized = true;
        }
        else
        {
            float k = 1f - Mathf.Exp(-_capsuleFitSmoothing * Time.fixedDeltaTime);
            _fitCenter = Vector3.Lerp(_fitCenter, center, k);
            _fitHeight = Mathf.Lerp(_fitHeight, height, k);
        }

        // Мёртвая зона: при езде капсула фактически заморожена (переподгонка в контакте
        // пинает тело от поверхности); в воздухе достаточно маленького порога.
        float deadband = _riding ? 0.06f : 0.02f;
        bool changed = _capsule.direction != 2
            || (_capsule.center - _fitCenter).sqrMagnitude > deadband * deadband
            || Mathf.Abs(_capsule.height - _fitHeight) > deadband;
        if (!changed)
            return true;

        _capsule.direction = 2;
        _capsule.center = _fitCenter;
        _capsule.height = _fitHeight;
        return true;
    }

    void ApplyExtraGravity()
    {
        if (_rigidbody == null || _gravityMultiplier <= 1f)
            return;

        _rigidbody.AddForce(Physics.gravity * (_gravityMultiplier - 1f) * _rigidbody.mass, ForceMode.Force);

        // Кости куклы падают под обычным 1g; без той же добавки кукла «всплывает»
        // относительно капсулы весь полёт.
        if (_flightRagdoll != null)
            _flightRagdoll.AddAcceleration(Physics.gravity * (_gravityMultiplier - 1f));
    }

    // Силы воздушного управления должны разгонять капсулу И живую куклу одинаково,
    // иначе видимое тело маятником отстаёт от капсулы.
    public void AddAirControlAcceleration(Vector3 acceleration)
    {
        if (_rigidbody == null)
            return;

        _rigidbody.AddForce(acceleration, ForceMode.Acceleration);
        if (_flightRagdoll != null)
            _flightRagdoll.AddAcceleration(acceleration);
    }

    // ================= ЕЗДА ПО ПУТИ =================

    void UpdateRide()
    {
        var frames = _currentSlide != null ? _currentSlide.RideFrames : null;
        if (frames == null || frames.Count < 2)
        {
            DetachFromSlide(_rigidbody.linearVelocity, 0.2f);
            return;
        }

        // Перегенерация горки в Play-режиме пересобирает кадры — кэш длин обязан
        // совпадать с ними по числу элементов.
        if (_cumLengths == null || _cumLengths.Length != frames.Count)
            BuildLengthCache(_currentSlide);

        float dt = Time.fixedDeltaTime;
        _rideTime += dt;
        if (_landingContainTimer > 0f)
            _landingContainTimer -= dt;

        SampleFrame(frames, _ridePathS, out Vector3 center, out Vector3 tangent, out Vector3 up, out _rideFrameIndex);
        _lastGuideIndex = _rideFrameIndex;
        Vector3 right = Vector3.Cross(up, tangent);
        if (right.sqrMagnitude < 1e-6f)
        {
            DetachFromSlide(tangent * _rideSpeed, 0.2f);
            return;
        }
        right.Normalize();

        Vector3 gravity = Physics.gravity * GravityMultiplier;
        bool openFlume = _currentSlide.Enclosure == SlideEnclosure.Open;

        // ---- Скорость вдоль пути: гравитация по касательной + W/S + автотормоз ----
        float gAlong = Vector3.Dot(gravity, tangent);
        // На спуске гравитация разгоняет с множителем (просьба 27.10.2026: скорость
        // вниз по горке должна набираться быстрее); на подъёме тормозит честно.
        // Множитель только ниже потолка: иначе на крутом спуске он перегонял мягкий
        // потолок (стекает 10 м/с²), а подскок с перегиба потом срезал 6 м/с разом.
        float dv = gAlong > 0f && _rideSpeed < MaxRideSpeed ? gAlong * Mathf.Max(_downhillAccelBoost, 1f) : gAlong;

        // Ускоряющая полоса: разгон и временно поднятый потолок. Замена газу W —
        // на телефоне кнопки газа нет, разгон лежит на горке и его надо «собрать».
        bool onBoost = _currentSlide.IsInBoostStrip(_ridePathS);
        if (onBoost)
            _boostTimer = 1f;
        else if (_boostTimer > 0f)
            _boostTimer -= dt;
        float throttle = ThrottleInput;
        // Без потолка (Max Slide Speed 0) ceiling бесконечен: полоса и газ разгоняют
        // всегда, мягкий потолок ниже не срабатывает.
        float ceiling = MaxRideSpeed
            + (_boostTimer > 0f ? _boostSpeedBonus : 0f)
            + (throttle > 0.01f ? _throttleSpeedBonus : 0f);
        if (onBoost && _rideSpeed < ceiling)
            dv += _boostAcceleration;

        if (throttle > 0.01f && _rideSpeed < ceiling)
            dv += _throttleAcceleration * throttle;
        else if (throttle < -0.01f)
            dv += _brakeAcceleration * throttle; // throttle отрицательный — торможение

        // Автотормоз перед поворотами и трамплином — честный ассист: работает с
        // силой _autoCornerBrake ВСЕГДА, ввод игрока его не выключает (раньше любой
        // руль или газ отключали его молча, и игрок не понимал, почему «само ехало
        // хорошо, а с рулём выносит»). 1 — сам тормозит до безопасной скорости,
        // меньше — лишь часть, остальное за игроком (тормоз пальцем).
        if (_autoCornerBrake > 0.001f && _rideSpeed > 6f)
        {
            float governor = ResolveCornerBrake(frames, _rideFrameIndex, _rideSpeed) * _autoCornerBrake;
            // Кредитуем торможение, которое подъём уже даёт сам (gAlong < 0).
            governor += Mathf.Min(gAlong, 0f);
            if (governor > 0f)
                dv -= governor;
        }

        _rideSpeed += dv * dt;
        ApplyExitBoost(frames, gravity, dt);

        // Пол скорости: гравитация не замедляет ниже минимума (подъёмы), «поток»
        // докручивает обратно с ограниченным разгоном. Зажатый S может тормозить до
        // нуля, но задний ход не начинается никогда.
        float floor = throttle < -0.01f ? 0f : _minRideSpeed;
        if (_rideSpeed < floor)
            _rideSpeed = Mathf.MoveTowards(_rideSpeed, floor, 25f * dt);
        _rideSpeed = Mathf.Max(_rideSpeed, 0f);

        // Мягкий потолок: излишек сверх максимума стекает, а не срезается за кадр.
        if (_rideSpeed > ceiling)
            _rideSpeed = Mathf.MoveTowards(_rideSpeed, ceiling, 10f * dt);

        // ---- Отрыв на перегибах (открытый жёлоб) ----
        // Тело НЕ примагничено к профилю: на выпуклом изломе (резкий уход в спуск,
        // гребень) поверхность уходит из-под тела быстрее, чем гравитация прижимает —
        // v²·κ_выпуклая > прижим → отстёгивание в баллистику до следующего касания.
        if (openFlume)
        {
            float convex = MeasureConvexCurvature(frames, _rideFrameIndex, right);
            if (convex > 1e-3f && _rideSpeed > 1f)
            {
                float needed = _rideSpeed * _rideSpeed * convex;
                float press = Mathf.Max(-Vector3.Dot(gravity, up), 0.1f) * Mathf.Max(_crestStickiness, 0.05f);
                if (needed > press)
                {
                    _guideTangent = tangent;
                    _crestHopTimer = 0.7f;
                    // Тело, только что севшее с подскока и тут же отпущенное перегибом,
                    // ещё не ехало: оставляем ему его же скорость. Снап на касательную
                    // кадра (круче полёта на 10–15°) давал рывок 4 м/с за шаг.
                    DetachFromSlide(_rideTime < 0.05f ? _rigidbody.linearVelocity : ComposeRideVelocity(), 0.1f);
                    return;
                }
            }
        }

        // ---- Поперечная динамика ----
        float halfWidth = _currentSlide.RideHalfWidth;
        float bodyRadius = GetLateralBodyExtent();
        float lift = (_capsule != null ? _capsule.radius : 0.35f) + _surfaceClearance;

        // Кривизна пути в плоскости кадра (вертикальные изгибы — гребни, петли — не
        // считаются поворотом). Знак: положительная — путь заворачивает вправо.
        float curvature = MeasureFlatCurvature(frames, _rideFrameIndex, up);
        float ck = 1f - Mathf.Exp(-10f * dt);
        _smoothedCurvature = Mathf.Lerp(_smoothedCurvature, curvature, ck);

        // Центробежное ускорение: поворот вправо выталкивает тело влево.
        float centrifugal = -_rideSpeed * _rideSpeed * _smoothedCurvature;
        float gRight = Vector3.Dot(gravity, right);
        float gPress = Mathf.Max(-Vector3.Dot(gravity, up), 1f); // прижим к полу в раме жёлоба

        bool containment = _landingContainTimer > 0f;
        Vector3 detachVelocity = Vector3.zero;
        bool flyOut = false;

        if (openFlume)
        {
            float flatHalf = Mathf.Max(halfWidth - bodyRadius, 0.05f);
            var profile = GetFlumeProfile();

            // Опасность поворота для HUD (см. CornerDanger). Удержание: палец до
            // упора внутрь компенсирует снос цели на 2·flatHalf/_cornerDriftPerAccel
            // (≈32 м/с² при ширине пола 1.76 м), а сам скат стенки держит до
            // gPress·_wallSlope (≈12 м/с²) — берётся большее. Спрос — на текущей
            // скорости по самому крутому участку в тормозном горизонте.
            if (_rideSpeed <= _minRideSpeed + 1f)
                _cornerDanger = 0f;
            else
            {
                float hold = gPress * Mathf.Max(_wallSlope, 0f);
                if (_cornerDriftPerAccel > 1e-4f)
                    hold = Mathf.Max(hold, 2f * flatHalf / _cornerDriftPerAccel);
                else
                    hold = float.PositiveInfinity; // регулятор держит у пальца с полной силой
                float demand = MeasureCornerDemandAhead(frames, _rideFrameIndex, _rideSpeed);
                demand = Mathf.Max(demand, _rideSpeed * _rideSpeed * Mathf.Abs(_smoothedCurvature));
                _cornerDanger = float.IsInfinity(hold) ? 0f : demand / Mathf.Max(hold, 1f);
            }

            // Ввод A/D: двигает поперёк, но не разгоняет выше своего потолка и не
            // может затолкать тело выше стенки (наружный ввод на стенке игнорируется).
            float steerAccel = 0f;
            if (_hasLateralTarget)
            {
                // Руль-ползунок: цель = доля ширины пола + ЦЕНТРОБЕЖНЫЙ СНОС. В
                // повороте цель уезжает наружу на (v²/R + боковая гравитация) ×
                // _cornerDriftPerAccel, и чтобы остаться в жёлобе, игрок должен «лечь»
                // пальцем внутрь поворота. Без сноса регулятор держал тело у пальца
                // силой до Slide Steer Force — больше любой центробежной силы на
                // трассе, и без руля вылететь было невозможно (28.10.2026).
                // Снесённая цель заходит за край пола не дальше DriftOverhang: к
                // стенке тело подходит с ограниченной скоростью, а дальше решает
                // физика ската (центробежная сила против крутизны стенки и трения).
                // Палец сам (без сноса) за край пола цель не выводит.
                float targetRaw = _slideLateralTarget * flatHalf + (centrifugal + gRight) * _cornerDriftPerAccel;
                float reach = flatHalf + DriftOverhang;
                float targetL = Mathf.Clamp(targetRaw, -reach, reach);
                float desiredVel = Mathf.Clamp((targetL - _rideLateral) * _lateralTargetGain, -_maxSlideSteerSpeed, _maxSlideSteerSpeed);
                steerAccel = ClampSteerAccel((desiredVel - _rideLateralVel) * SteerVelocityGain);
                bool onWall = Mathf.Abs(_rideLateral) >= flatHalf - 1e-3f;
                bool aboveFloor = Mathf.Abs(_rideLateral) > flatHalf;
                // Снесённая цель ушла за край пола в ту же сторону, где тело на
                // стенке, — регулятор ОТПУСКАЕТ тело целиком (ни наружу, ни назад):
                // взберётся оно до кромки или сползёт — решает физика ската. Иначе
                // регулятор тянул бы тело назад к ограниченной цели силой до Slide
                // Steer Force, и до кромки (≈1.5 м выше края пола) не добраться никогда.
                bool driftedOut = Mathf.Abs(targetRaw) > flatHalf && Mathf.Sign(targetRaw) == Mathf.Sign(_rideLateral);
                if (aboveFloor && driftedOut)
                    steerAccel = 0f;
                // На стенке руль наружу не помогает инерции (как и раньше).
                else if (onWall && Mathf.Sign(steerAccel) == Mathf.Sign(_rideLateral))
                    steerAccel = 0f;
            }
            else if (Mathf.Abs(_slideSteerInput) > 0.01f)
            {
                bool onWall = Mathf.Abs(_rideLateral) >= flatHalf - 1e-3f;
                bool pushesOutward = Mathf.Sign(_slideSteerInput) == Mathf.Sign(_rideLateral);
                if (!(onWall && pushesOutward) && _slideSteerInput * _rideLateralVel < _maxSlideSteerSpeed)
                    steerAccel = _slideSteerInput * _slideSteerForce;
            }

            // Частица на кривой сечения y(l): восстановление к ложбине/сопротивление
            // ската стенки — через наклон dy/dl. На скате стенки трение сильнее
            // (см. _wallClimbDamping): без него любой всплеск боковой инерции
            // энергетически перелетал кромку — хватало ~3 м/с поперёк.
            float slope = SurfaceSlope(_rideLateral, flatHalf, profile);
            bool onWallBand = Mathf.Abs(_rideLateral) > flatHalf;
            float dampRate = onWallBand ? _wallClimbDamping : _lateralDamping;
            float damping = -_rideLateralVel * dampRate;
            float aExt = centrifugal + gRight + steerAccel + damping;
            float aLat = (aExt - gPress * slope) / (1f + slope * slope);

            _rideLateralVel += aLat * dt;
            _rideLateral += _rideLateralVel * dt;

            // Стенка становится «жёсткой» (вылет невозможен), пока скорость не выше
            // минимума езды: на минимуме держится любой поворот — сбавил скорость,
            // значит удержался (правило игры). Вылеты начинаются только на скорости,
            // которую игрок набрал сам (зажатый W, разгон со спуска).
            bool wallIsRail = containment || _wallSlope <= 0f
                || _rideSpeed <= _minRideSpeed + 1f;
            if (wallIsRail)
            {
                // Окно удержания после приземления: за борт не выйти, стенка жёсткая.
                if (Mathf.Abs(_rideLateral) > flatHalf)
                {
                    _rideLateral = Mathf.Sign(_rideLateral) * flatHalf;
                    if (Mathf.Sign(_rideLateralVel) == Mathf.Sign(_rideLateral))
                        _rideLateralVel = 0f;
                }
            }
            else
            {
                // Вылет: тело взобралось по скату стенки выше кромки — жёлоб отпускает.
                float surfaceY = SurfaceHeight(_rideLateral, flatHalf, profile);
                if (surfaceY >= _currentSlide.FlumeLipHeight)
                {
                    float sideSlope = Mathf.Abs(SurfaceSlope(_rideLateral, flatHalf, profile));
                    float outwardSign = Mathf.Sign(_rideLateral);
                    float outward = Mathf.Max(Mathf.Abs(_rideLateralVel), _flyoutMinOutwardSpeed) * Mathf.Max(_flyoutOutwardBoost, 0.1f);
                    detachVelocity = tangent * _rideSpeed
                        + right * (outwardSign * outward)
                        + up * (Mathf.Abs(_rideLateralVel) * sideSlope);
                    flyOut = true;
                }
            }

            if (flyOut)
            {
                // Кромка не должна ловить вылетающее тело: капсула на высоте кромки
                // цепляла бортик коллайдера изнутри, скользила по нему, теряла
                // скорость и падала обратно в жёлоб («край отталкивает»). На время
                // полёта коллайдер этой горки для капсулы отключается, а тело
                // приподнимается над бортиком.
                IgnoreSlideCollision(_currentSlide, Mathf.Max(_flyoutSuppression, 0.3f));
                _rigidbody.position += up * (_capsule != null ? _capsule.radius : 0.35f) * 0.8f
                    + right * (Mathf.Sign(_rideLateral) * 0.15f);
                DetachFromSlide(detachVelocity, Mathf.Max(_flyoutSuppression, _jumpGuideSuppression));
                return;
            }

            // ---- Целевая поза (жёлоб) ----
            // Видимая поза — на РЕАЛЬНОЙ поверхности сечения (дно/эллипс стенки), со
            // смещением по её нормали, а не по виртуальному скату физики: пока тело
            // лезет по стенке, оно прижато к ней и наклонено по стенке, доходит до
            // верха кромки ровно в момент вылета. Так игрок видит, насколько близок
            // вылет, а тело не висит в воздухе у кромки.
            ComputeVisualPose(_rideLateral, flatHalf, profile, _currentSlide.FlumeLipHeight,
                out float visualLateral, out float visualY, out Vector2 n2);
            Vector3 surfaceNormal = (right * n2.x + up * n2.y).normalized;
            float poseLateral = visualLateral + n2.x * lift;
            float poseHeight = visualY + n2.y * lift;

            if (!AdvanceAndDrive(frames, dt, poseLateral, poseHeight, surfaceNormal))
                return;

            // Публикация для камеры.
            _rimPlanePoint = center + up * _currentSlide.FlumeLipHeight;
            _rimPlaneUp = up;
            _hasRimPlane = true;
            _flumeCenterPoint = center;
        }
        else
        {
            // Закрытая труба: маятник на окружности сечения. Вылет невозможен —
            // держит сама труба.
            _cornerDanger = 0f;
            float rEff = Mathf.Max(halfWidth - lift, 0.1f);

            float steerAccel = 0f;
            if (_hasLateralTarget)
            {
                // Ползунок в трубе: цель — угол на сечении до ±60° от дна, плюс тот же
                // центробежный снос наружу (в дуге сечения), что и в открытом жёлобе.
                float driftAngle = centrifugal * _cornerDriftPerAccel / rEff;
                float targetAngle = Mathf.Clamp(_slideLateralTarget * (Mathf.PI / 3f) + driftAngle, -Mathf.PI * 0.5f, Mathf.PI * 0.5f);
                float desiredVel = Mathf.Clamp((targetAngle - _tubeAngle) * rEff * _lateralTargetGain, -_maxSlideSteerSpeed, _maxSlideSteerSpeed);
                steerAccel = ClampSteerAccel((desiredVel - _tubeAngularVel * rEff) * SteerVelocityGain);
            }
            else if (Mathf.Abs(_slideSteerInput) > 0.01f
                && _slideSteerInput * (_tubeAngularVel * rEff) < _maxSlideSteerSpeed)
                steerAccel = _slideSteerInput * _slideSteerForce;

            // Направление вдоль окружности в текущей точке сечения.
            Vector3 tdir = right * Mathf.Cos(_tubeAngle) + up * Mathf.Sin(_tubeAngle);
            Vector3 external = right * (centrifugal + steerAccel) + gravity;
            float aTangential = Vector3.Dot(external, tdir) - _tubeAngularVel * rEff * _lateralDamping;

            _tubeAngularVel += aTangential / rEff * dt;
            _tubeAngle += _tubeAngularVel * dt;

            Vector3 radial = right * Mathf.Sin(_tubeAngle) - up * Mathf.Cos(_tubeAngle);
            Vector3 surfaceNormal = -radial;

            _hasRimPlane = false;
            AdvanceAndDriveTube(frames, dt, rEff, surfaceNormal);
        }
    }

    // Продвигает s, семплирует новый кадр и ведёт тело к целевой позе через velocity
    // (жёлоб). Сход с конца пути — честный отрыв в баллистику. Возвращает false,
    // если тело отстегнулось.
    bool AdvanceAndDrive(IReadOnlyList<WaterSlideMeshBuilder.RideFrame> frames, float dt,
        float lateral, float heightAboveFrame, Vector3 surfaceNormal)
    {
        _ridePathS += _rideSpeed * dt;
        if (_ridePathS >= _cumLengths[^1])
        {
            _landingAssistSlide = NextSlide;
            _landingAssistJumped = false;
            DetachFromSlide(ComposeRideVelocity(), 0.25f);
            return false;
        }

        SampleFrame(frames, _ridePathS, out Vector3 center, out Vector3 tangent, out Vector3 up, out _rideFrameIndex);
        Vector3 right = Vector3.Cross(up, tangent).normalized;

        Vector3 targetCenter = center + right * lateral + up * heightAboveFrame;
        DriveBodyTo(targetCenter, tangent, surfaceNormal);
        return true;
    }

    void AdvanceAndDriveTube(IReadOnlyList<WaterSlideMeshBuilder.RideFrame> frames, float dt,
        float rEff, Vector3 surfaceNormal)
    {
        _ridePathS += _rideSpeed * dt;
        if (_ridePathS >= _cumLengths[^1])
        {
            _landingAssistSlide = NextSlide;
            _landingAssistJumped = false;
            DetachFromSlide(ComposeRideVelocity(), 0.25f);
            return;
        }

        SampleFrame(frames, _ridePathS, out Vector3 center, out Vector3 tangent, out Vector3 up, out _rideFrameIndex);
        Vector3 right = Vector3.Cross(up, tangent).normalized;
        Vector3 radial = right * Mathf.Sin(_tubeAngle) - up * Mathf.Cos(_tubeAngle);

        Vector3 targetCenter = center + radial * rEff;
        DriveBodyTo(targetCenter, tangent, surfaceNormal);
    }

    // Скорость тела, соответствующая текущему состоянию путевой модели (для отрыва).
    Vector3 ComposeRideVelocity()
    {
        Vector3 tangent = _guideTangent.sqrMagnitude > 1e-6f ? _guideTangent : transform.forward;
        Vector3 vel = tangent * _rideSpeed;
        if (_currentSlide != null && _currentSlide.Enclosure == SlideEnclosure.Open)
        {
            Vector3 up = _slideNormal;
            Vector3 right = Vector3.Cross(up, tangent);
            if (right.sqrMagnitude > 1e-6f)
                vel += right.normalized * _rideLateralVel;
        }
        return vel;
    }

    // Ведение тела к целевой позе ЧЕРЕЗ скорость: рендер интерполирует движение как
    // физическое, а ошибки позиции самокорректируются каждый шаг.
    void DriveBodyTo(Vector3 targetBodyCenter, Vector3 tangent, Vector3 surfaceNormal)
    {
        _guideTangent = tangent;
        _slideNormalTarget = surfaceNormal;

        float k = 1f - Mathf.Exp(-_normalSmoothing * Time.fixedDeltaTime);
        _slideNormal = Vector3.Slerp(_slideNormal, _slideNormalTarget, k).normalized;

        var targetRot = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(tangent, _slideNormal).sqrMagnitude > 1e-6f
                ? Vector3.ProjectOnPlane(tangent, _slideNormal).normalized
                : tangent,
            _slideNormal);

        // Смещение центра капсулы — по ТЕКУЩЕМУ повороту тела, не по целевому:
        // linearVelocity двигает центр масс (= центр капсулы), а пивот ходит за
        // поворотом сам. С целевым поворотом при смене наклона на перегибе (30–40°)
        // пивот «промахивался» на 0.7 м, коррекция выдавала +6 м/с, а центр тела
        // съезжал с пути, пока тело доворачивалось, — рывок камеры (27.10.2026).
        Vector3 capsuleOffset = _capsule != null ? _rigidbody.rotation * _capsule.center : Vector3.zero;
        Vector3 targetRbPos = targetBodyCenter - capsuleOffset;

        // Скорость = движение вдоль пути + ОГРАНИЧЕННАЯ коррекция промаха позиции.
        // Без капа промах пристёгивания (~0.3–0.5 м в момент приземления) выдавал
        // одношаговый рывок скорости в десятки м/с — видимый дёрг. С капом остаток
        // рассасывается за несколько шагов плавно.
        Vector3 delta = targetRbPos - _rigidbody.position;
        Vector3 pathStep = ComposeRideVelocity() * Time.fixedDeltaTime;
        Vector3 correction = delta - pathStep;
        // Мелкий промах слежения (сантиметры у швов) гасится за шаг — иначе тело
        // ложится на коллайдер и его пинает выталкивание. Крупный (после подскока
        // или посадки, 0.1–0.5 м) — пропорционально промаху, не быстрее 2.5 м/с на
        // малом и ~15·промах на большом: прежний кап 8 м/с гасил 16 см за ОДИН шаг —
        // рывок 7 м/с на каждом перегибе (27.10.2026).
        float dt = Time.fixedDeltaTime;
        float miss = correction.magnitude;
        // Крупный промах при пристёгивании на перегибе — в основном от разворота
        // тела (пивот у ног, центр капсулы ~1 м от него: поворот на 30° = 0.5 м);
        // он рассасывается, пока тело доворачивается, гнать его быстро незачем.
        float corrSpeed = Mathf.Min(miss / dt, Mathf.Clamp(miss * 8f, 2.5f, 6f));
        if (miss > 1e-5f)
            correction = correction / miss * (corrSpeed * dt);

        _rigidbody.WakeUp();
        _rigidbody.linearVelocity = (pathStep + correction) / dt;

        // Направление полёта на случай отрыва — горизонтальная проекция касательной.
        Vector3 flat = Vector3.ProjectOnPlane(tangent, Vector3.up);
        if (flat.sqrMagnitude > 1e-4f)
            _flightHeading = flat.normalized;
    }

    // ---- Профиль сечения открытого жёлоба ----

    struct FlumeProfile
    {
        public float Depth;
        public float HalfWidth;
        public bool Rounded;
    }

    FlumeProfile GetFlumeProfile() => new()
    {
        Depth = _currentSlide.FlumeDepth,
        HalfWidth = Mathf.Max(_currentSlide.RideHalfWidth, 0.1f),
        Rounded = _currentSlide.OpenShape == OpenFlumeShape.Rounded
    };

    // Высота поверхности сечения (в раме кадра: 0 — основание борта, пол на -Depth)
    // в точке поперечного смещения l. За пределами полосы пола — виртуальный скат
    // стенки крутизной _wallSlope, по которому инерция может вынести тело за кромку.
    float SurfaceHeight(float l, float flatHalf, in FlumeProfile p)
    {
        float a = Mathf.Abs(l);
        float baseY;
        if (p.Rounded)
        {
            float x = Mathf.Clamp01(Mathf.Min(a, flatHalf) / p.HalfWidth);
            baseY = -p.Depth * Mathf.Sqrt(Mathf.Max(1f - x * x, 0f));
        }
        else
            baseY = -p.Depth;

        if (a <= flatHalf)
            return baseY;

        return baseY + (a - flatHalf) * Mathf.Max(_wallSlope, 0.1f);
    }

    // Высота РЕАЛЬНОЙ поверхности сечения (дно или эллипс стенки) на |l| = a.
    static float RealSurfaceHeight(float a, in FlumeProfile p)
    {
        if (!p.Rounded)
            return -p.Depth;
        float x = Mathf.Clamp(a / p.HalfWidth, 0f, 0.995f);
        return -p.Depth * Mathf.Sqrt(Mathf.Max(1f - x * x, 0f));
    }

    // Внутренняя нормаль реальной поверхности в точке |l| = a (x — наружу, y — вверх).
    static Vector2 RealSurfaceNormal(float a, in FlumeProfile p)
    {
        if (!p.Rounded)
            return Vector2.up;
        float y = RealSurfaceHeight(a, p);
        float x = Mathf.Min(a, p.HalfWidth * 0.995f);
        // Градиент эллипса (x/a², y/b²) смотрит наружу; внутренняя нормаль — минус он.
        var n = new Vector2(-x / (p.HalfWidth * p.HalfWidth), -y / (p.Depth * p.Depth));
        return n.sqrMagnitude > 1e-8f ? n.normalized : Vector2.up;
    }

    // Видимая поза тела по физическому поперечному смещению l: на полосе пола —
    // точка реальной поверхности; на «скате» (|l| > flatHalf) — прямая от края пола
    // к верху кромки, пройденная на ту же долю, что и физический подъём до порога
    // вылета (y_phys от края пола до LipHeight). Нормаль — реальной стенки.
    void ComputeVisualPose(float l, float flatHalf, in FlumeProfile p, float lipHeight,
        out float x, out float y, out Vector2 normal)
    {
        float a = Mathf.Abs(l);
        float sign = l >= 0f ? 1f : -1f;

        if (a <= flatHalf)
        {
            x = a;
            y = RealSurfaceHeight(a, p);
            normal = RealSurfaceNormal(a, p);
        }
        else
        {
            float yEdge = SurfaceHeight(flatHalf, flatHalf, p);
            float yPhys = SurfaceHeight(a, flatHalf, p);
            float t = Mathf.Clamp01((yPhys - yEdge) / Mathf.Max(lipHeight - yEdge, 0.05f));
            x = Mathf.Lerp(flatHalf, p.HalfWidth, t);
            y = Mathf.Lerp(RealSurfaceHeight(flatHalf, p), lipHeight, t);
            // К кромке тело поднимается над бортиком (центр капсулы выше его верха),
            // наклон к стенке умеренный: полностью горизонтальная нормаль клала тело
            // на бок и читалась как «разворачивает».
            y += t * t * 0.35f;
            normal = Vector2.Lerp(RealSurfaceNormal(x, p), new Vector2(-0.55f, 0.83f).normalized, t * t).normalized;
        }

        x *= sign;
        normal.x *= sign;
    }

    // Наклон dy/dl той же кривой (со знаком поперечного направления).
    float SurfaceSlope(float l, float flatHalf, in FlumeProfile p)
    {
        float a = Mathf.Abs(l);
        float sign = l >= 0f ? 1f : -1f;

        if (a > flatHalf)
            return sign * Mathf.Max(_wallSlope, 0.1f);

        if (!p.Rounded)
            return 0f;

        float x = Mathf.Clamp(a / p.HalfWidth, 0f, 0.99f);
        // y = -D·√(1-x²) → dy/da = D·x / (HW·√(1-x²))
        float slope = p.Depth * x / (p.HalfWidth * Mathf.Sqrt(1f - x * x));
        return sign * slope;
    }

    // Выпуклая кривизна профиля вдоль пути около индекса i, рад/м; >0 — касательная
    // заворачивает вниз (поверхность уходит из-под тела). Меряется вокруг оси right,
    // боковые повороты (yaw) на неё не влияют.
    static float MeasureConvexCurvature(IReadOnlyList<WaterSlideMeshBuilder.RideFrame> frames, int i, Vector3 right)
    {
        const int Span = 4;
        int a = Mathf.Max(i - Span, 0);
        int b = Mathf.Min(i + Span, frames.Count - 1);
        if (b - a < 2)
            return 0f;

        Vector3 fa = Vector3.ProjectOnPlane(frames[a].Forward, right);
        Vector3 fb = Vector3.ProjectOnPlane(frames[b].Forward, right);
        if (fa.sqrMagnitude < 0.01f || fb.sqrMagnitude < 0.01f)
            return 0f;

        float dist = Vector3.Distance(frames[a].Position, frames[b].Position);
        if (dist < 0.05f)
            return 0f;

        return Vector3.SignedAngle(fa, fb, right) * Mathf.Deg2Rad / dist;
    }

    // Кривизна пути в плоскости кадра около индекса i, рад/м; >0 — поворот вправо.
    static float MeasureFlatCurvature(IReadOnlyList<WaterSlideMeshBuilder.RideFrame> frames, int i, Vector3 up)
    {
        // Окно ~±0.6 м при шаге колец 0.12–0.22 м.
        const int Span = 4;
        int a = Mathf.Max(i - Span, 0);
        int b = Mathf.Min(i + Span, frames.Count - 1);
        if (b - a < 2)
            return 0f;

        Vector3 fa = Vector3.ProjectOnPlane(frames[a].Forward, up);
        Vector3 fb = Vector3.ProjectOnPlane(frames[b].Forward, up);
        if (fa.sqrMagnitude < 0.01f || fb.sqrMagnitude < 0.01f)
            return 0f;

        float dist = Vector3.Distance(frames[a].Position, frames[b].Position);
        if (dist < 0.05f)
            return 0f;

        return Vector3.SignedAngle(fa, fb, up) * Mathf.Deg2Rad / dist;
    }

    // Наибольшее центробежное ускорение v²·κ (м/с²), которое ждёт тело в поворотах
    // впереди в горизонте «тормозной путь + 6 м, не меньше 2 с хода» (тот же обход
    // чанками ~1.2 м в плоскости кадра, что и у автотормоза). Скорость в точке
    // поворота ПРОГНОЗИРУЕТСЯ по энергии спуска до неё (v² + 2·g·перепад; подъём
    // гасит не ниже минимума езды): на спуске тело набирает с 16 до 23 м/с за
    // секунду, и по текущей скорости красный загорался лишь за секунду до вылета
    // (замер 29.10.2026). Автотормоз 0.6 и трение в прогнозе не учтены (журнал:
    // на спусках они скорость почти не сбивают). Для подкраски скорости в HUD.
    float MeasureCornerDemandAhead(IReadOnlyList<WaterSlideMeshBuilder.RideFrame> frames, int startIndex, float speed)
    {
        float lookahead = Mathf.Max(speed * speed / (2f * Mathf.Max(_brakeAcceleration, 1f)) + 6f, speed * 2f);
        lookahead = Mathf.Min(lookahead, 60f);
        float gEff = Physics.gravity.magnitude * GravityMultiplier;
        float minSq = _minRideSpeed * _minRideSpeed;
        float demand = 0f;
        float travelled = 0f;
        float chunkDist = 0f;
        int i = Mathf.Clamp(startIndex, 0, frames.Count - 1);
        float y0 = frames[i].Position.y;
        Vector3 chunkStartFwd = frames[i].Forward;
        int j = i;
        while (j + 1 < frames.Count && travelled < lookahead)
        {
            float step = Vector3.Distance(frames[j].Position, frames[j + 1].Position);
            j++;
            travelled += step;
            chunkDist += step;
            if (chunkDist < 1.2f)
                continue;

            Vector3 up = frames[j].Up;
            Vector3 a = Vector3.ProjectOnPlane(chunkStartFwd, up);
            Vector3 b = Vector3.ProjectOnPlane(frames[j].Forward, up);
            if (a.sqrMagnitude > 0.04f && b.sqrMagnitude > 0.04f)
            {
                float kappa = Vector3.Angle(a, b) * Mathf.Deg2Rad / chunkDist;
                float speedSqThere = Mathf.Max(speed * speed + 2f * gEff * (y0 - frames[j].Position.y), minSq);
                float need = speedSqThere * kappa;
                if (need > demand)
                    demand = need;
            }

            chunkStartFwd = frames[j].Forward;
            chunkDist = 0f;
        }
        return demand;
    }

    // Замедление (м/с²), нужное прямо сейчас, чтобы подойти к каждому повороту в
    // тормозном горизонте на скорости, которую держит безрулевое сцепление, а к
    // трамплину (конец пути) — не быстрее расчётной скорости прыжка. Ограничено
    // силой тормоза S игрока.
    float ResolveCornerBrake(IReadOnlyList<WaterSlideMeshBuilder.RideFrame> frames, int startIndex, float speed)
    {
        float minSq = _minRideSpeed * _minRideSpeed;
        float availableAuto = Mathf.Max(_corneringGrip * _autoGripFraction, 1f);
        float lookahead = Mathf.Min(speed * speed / (2f * Mathf.Max(_brakeAcceleration, 1f)) + 6f, 60f);

        float travelled = 0f;
        float chunkDist = 0f;
        float bestDecel = 0f;
        int i = Mathf.Clamp(startIndex, 0, frames.Count - 1);
        Vector3 chunkStartFwd = frames[i].Forward;
        int j = i;
        while (j + 1 < frames.Count && travelled < lookahead)
        {
            float step = Vector3.Distance(frames[j].Position, frames[j + 1].Position);
            j++;
            travelled += step;
            chunkDist += step;
            if (chunkDist < 1.2f)
                continue;

            Vector3 up = frames[j].Up;
            Vector3 a = Vector3.ProjectOnPlane(chunkStartFwd, up);
            Vector3 b = Vector3.ProjectOnPlane(frames[j].Forward, up);
            if (a.sqrMagnitude > 0.04f && b.sqrMagnitude > 0.04f)
            {
                float kappa = Vector3.Angle(a, b) * Mathf.Deg2Rad / chunkDist;
                if (kappa > 1e-3f)
                {
                    // 0.85 — запас, чтобы подход был заметно ПОД пределом сцепления.
                    float safeSpeedSq = availableAuto / kappa * 0.85f;
                    // Ниже минимума автотормоз не гасит: поворот, требующий меньше,
                    // жёлоб осознанно отпускает — вылеты по инерции остаются в игре.
                    if (safeSpeedSq < minSq)
                        safeSpeedSq = minSq;
                    float distance = Mathf.Max(travelled - chunkDist * 0.5f, 1f);
                    float decel = (speed * speed - safeSpeedSq) / (2f * distance);
                    if (decel > bestDecel)
                        bestDecel = decel;
                }
            }

            chunkStartFwd = frames[j].Forward;
            chunkDist = 0f;
        }

        // Конец пути в тормозном горизонте — трамплин. Кап скорости выхода считается
        // ПО ЭНЕРГИИ в точке вылета (подъём/спуск до неё учтён), иначе кап, снятый
        // здесь, съедался подъёмом и дуга падала с недолётом — фатальнее перелёта.
        if (_course != null && j + 1 >= frames.Count)
        {
            float exitCap = _course.RequiredJumpSpeed * 1.25f;
            float gEff = Physics.gravity.magnitude * GravityMultiplier;
            float dropToEnd = transform.position.y - frames[j].Position.y;
            float allowedSq = exitCap * exitCap - 2f * gEff * dropToEnd;
            if (allowedSq < Mathf.Max(minSq, 9f))
                allowedSq = Mathf.Max(minSq, 9f);
            if (speed * speed > allowedSq)
            {
                float decel = (speed * speed - allowedSq) / (2f * Mathf.Max(travelled, 1f));
                if (decel > bestDecel)
                    bestDecel = decel;
            }
        }

        return Mathf.Min(bestDecel, _brakeAcceleration);
    }

    // ---- Кадры пути: длины дуги и интерполяция ----

    void BuildLengthCache(WaterSlideGenerator slide)
    {
        var frames = slide.RideFrames;
        _cumLengths = new float[frames.Count];
        float total = 0f;
        _cumLengths[0] = 0f;
        for (int i = 1; i < frames.Count; i++)
        {
            total += Vector3.Distance(frames[i - 1].Position, frames[i].Position);
            _cumLengths[i] = total;
        }
        _lengthCacheSlide = slide;
    }

    void SampleFrame(IReadOnlyList<WaterSlideMeshBuilder.RideFrame> frames, float s,
        out Vector3 center, out Vector3 tangent, out Vector3 up, out int index)
    {
        s = Mathf.Clamp(s, 0f, _cumLengths[^1]);

        // Бинарный поиск сегмента по длине дуги.
        int lo = 0, hi = _cumLengths.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (_cumLengths[mid] <= s)
                lo = mid;
            else
                hi = mid;
        }

        float segLen = Mathf.Max(_cumLengths[hi] - _cumLengths[lo], 1e-4f);
        float t = (s - _cumLengths[lo]) / segLen;

        center = Vector3.LerpUnclamped(frames[lo].Position, frames[hi].Position, t);
        Vector3 tanBlend = Vector3.Slerp(frames[lo].Forward, frames[hi].Forward, t);
        tangent = tanBlend.sqrMagnitude > 1e-6f ? tanBlend.normalized : frames[lo].Forward.normalized;
        Vector3 upBlend = Vector3.Slerp(frames[lo].Up, frames[hi].Up, t);
        up = upBlend.sqrMagnitude > 1e-6f ? upBlend.normalized : Vector3.up;
        index = t < 0.5f ? lo : hi;
    }

    // ================= ПРИСТЁГИВАНИЕ / ОТРЫВ =================

    // Пристегнуть тело к пути горки. Вызывается при первом внутреннем контакте
    // капсулы с жёлобом после полёта (или спавн-падения).
    void AttachToSlide(WaterSlideGenerator slide)
    {
        var frames = slide.RideFrames;
        if (frames == null || frames.Count < 2)
            return;

        _currentSlide = slide;
        if (_lengthCacheSlide != slide)
            BuildLengthCache(slide);

        Vector3 pos = GetBodyCenterWorld();
        int nearest = FindNearestFrame(frames, pos, 0, frames.Count - 1);

        // Дистанция s: длина до кадра + проекция на сегмент.
        int segA = Mathf.Clamp(nearest, 0, frames.Count - 2);
        Vector3 seg = frames[segA + 1].Position - frames[segA].Position;
        float t = seg.sqrMagnitude > 1e-6f
            ? Mathf.Clamp01(Vector3.Dot(pos - frames[segA].Position, seg) / seg.sqrMagnitude)
            : 0f;
        _ridePathS = _cumLengths[segA] + Vector3.Distance(frames[segA].Position, frames[segA].Position + seg * t);
        _rideFrameIndex = nearest;
        _lastGuideIndex = nearest;

        SampleFrame(frames, _ridePathS, out Vector3 center, out Vector3 tangent, out Vector3 up, out _);
        Vector3 right = Vector3.Cross(up, tangent);
        right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;

        Vector3 vel = _rigidbody.linearVelocity;
        Vector3 offset = pos - center;

        // Короткий подскок с перегиба (профиль ушёл из-под тела и тут же вернулся) —
        // не приземление: скорость и нормаль переходят без скачков. Потолок посадки
        // и секунда защиты от вылета — только после настоящего полёта.
        bool shortHop = _timeSinceSlideContact <= 0.5f;

        // Снап скорости: весь запас хода переходит в езду вдоль пути (потолок —
        // против переносов огромной скорости падения в езду). На подскоке потолок
        // не применяется: он резал 25 → 19 м/с одним шагом — камера дёргалась на
        // перегибах «поворот + спуск» (27.10.2026).
        float speed = vel.magnitude;
        if (_landingSnapSpeedCap > 0f && !shortHop)
            speed = Mathf.Min(speed, Mathf.Max(_landingSnapSpeedCap, MaxRideSpeed));
        _rideSpeed = speed;
        _smoothedCurvature = 0f;
        _cornerDanger = 0f;

        float bodyRadius = GetLateralBodyExtent();
        float halfWidth = slide.RideHalfWidth;
        float flatHalf = Mathf.Max(halfWidth - bodyRadius, 0.05f);

        if (slide.Enclosure == SlideEnclosure.Open)
        {
            _rideLateral = Mathf.Clamp(Vector3.Dot(offset, right), -flatHalf, flatHalf);
            _rideLateralVel = Mathf.Clamp(Vector3.Dot(vel, right), -4f, 4f);
        }
        else
        {
            float lat = Vector3.Dot(offset, right);
            float h = Vector3.Dot(offset, up);
            _tubeAngle = Mathf.Atan2(lat, -h);
            Vector3 tdir = right * Mathf.Cos(_tubeAngle) + up * Mathf.Sin(_tubeAngle);
            float lift = (_capsule != null ? _capsule.radius : 0.35f) + _surfaceClearance;
            float rEff = Mathf.Max(halfWidth - lift, 0.1f);
            _tubeAngularVel = Mathf.Clamp(Vector3.Dot(vel, tdir) / rEff, -6f, 6f);
        }

        _riding = true;
        _landingAssistSlide = null;
        _rideTime = 0f;
        // Секунда защиты от вылета — только после НАСТОЯЩЕГО полёта. Короткий
        // подскок с перегиба (профиль ушёл из-под тела и тут же вернулся) защиту
        // не взводит, иначе трасса со ступенчатым профилем стала бы сплошной рельсой.
        _landingContainTimer = !shortHop ? _landingContainTime : 0f;
        _crestHopTimer = 0f;
        _timeSinceSlideContact = 0f;
        _guideTangent = tangent;
        // Нормаль на подскоке не прыгает: её доводит сглаживание в DriveBodyTo
        // (камера и наклон тела читают _slideNormal).
        if (!shortHop)
            _slideNormal = up;
        _slideNormalTarget = up;
        _rigidbody.useGravity = false;
        _hasSlideContactSinceSpawn = true;
        _controller?.OnSlideContact();
    }

    // Отрыв от пути: тело получает честную скорость состояния езды и уходит в
    // свободную баллистику; повторное пристёгивание подавлено на suppress секунд.
    void DetachFromSlide(Vector3 velocity, float suppress)
    {
        _riding = false;
        _hasRimPlane = false;
        _guideSuppressTimer = Mathf.Max(_guideSuppressTimer, suppress);
        _rigidbody.useGravity = true;
        _rigidbody.linearVelocity = velocity;

        Vector3 flat = Vector3.ProjectOnPlane(velocity, Vector3.up);
        if (flat.sqrMagnitude > 1e-4f)
            _flightHeading = flat.normalized;
    }

    // Намеренный прыжок с горки (пробел): отрыв со скоростью езды + импульс прыжка.
    public bool TryJumpFromSlide()
    {
        if (!_riding)
            return false;

        Vector3 normal = _slideNormal;
        Vector3 jumpDir = (normal + Vector3.up * 0.65f).normalized;
        Vector3 velocity = ComposeRideVelocity() + jumpDir * _slideJumpForce;
        // Прыжок у самого конца — это прыжок на следующую горку: помощь
        // приземлению ведёт и его (иначе прыжок давал перелёт).
        bool nearEnd = _cumLengths != null && _cumLengths.Length > 1
            && _cumLengths[^1] - _ridePathS <= Mathf.Max(_exitBoostDistance, 0f);
        _landingAssistSlide = nearEnd ? NextSlide : null;
        _landingAssistJumped = nearEnd;
        DetachFromSlide(velocity, _jumpGuideSuppression);
        return true;
    }

    // ================= ОРИЕНТАЦИЯ ТЕЛА =================

    void AlignBody()
    {
        if (_rigidbody == null || _rigidbody.isKinematic)
            return;

        // На подскоке с перегиба тело держит позу езды: иначе на банкированном
        // вираже крен и наклон за 0.1 с уходили к «воздушным» и возвращались рывком.
        if (_riding || _crestHopTimer > 0f)
        {
            // Тело ВСЕГДА вдоль пути: касательная кадра + сглаженная нормаль
            // поверхности. Никакая скорость/руль повернуть его поперёк не могут.
            Vector3 normal = _slideNormal;
            Vector3 forward = Vector3.ProjectOnPlane(_guideTangent, normal);
            if (forward.sqrMagnitude < 1e-6f)
            {
                _rigidbody.angularVelocity = Vector3.zero;
                return;
            }

            forward.Normalize();
            var target = Quaternion.LookRotation(forward, normal);
            SteerRotationTowards(Quaternion.Slerp(_rigidbody.rotation, target, _slideAlignSpeed * Time.fixedDeltaTime));
            return;
        }

        // Стартовое падение после спавна: сохранить ориентацию спавна (нос вдоль
        // горки), чтобы тело легло плашмя, а не клюнуло в почти вертикальную скорость.
        if (!_hasSlideContactSinceSpawn)
        {
            _rigidbody.angularVelocity = Vector3.zero;
            return;
        }

        Vector3 airVelocity = _rigidbody.linearVelocity;
        if (airVelocity.sqrMagnitude < 0.25f)
        {
            _rigidbody.angularVelocity = Vector3.zero;
            return;
        }

        // Наклон ограничен асимметрично: вверх (вылет с гребня) — мягко, чтобы не
        // задирать ноги в небо; вниз (падение) — круто, чтобы тело шло ногами
        // вперёд вдоль траектории. Полный разворот по скорости (90°) не даём:
        // у вертикального «вперёд» теряется курс (yaw).
        float maxPitch = airVelocity.y < 0f ? _maxAirDivePitch : _maxAirPitch;
        Vector3 airForward = ClampPitch(airVelocity.normalized, Mathf.Min(maxPitch, 85f));
        float pitch = -Mathf.Asin(Mathf.Clamp(airForward.y, -1f, 1f)) * Mathf.Rad2Deg;

        // Yaw привязан к направлению полёта, замороженному при отрыве, — стрейф
        // читается как сдвиг вбок, а не поворот. Крен всегда ноль.
        float yaw = _rigidbody.rotation.eulerAngles.y;
        if (_flightHeading.sqrMagnitude > 1e-4f)
        {
            float headingYaw = Mathf.Atan2(_flightHeading.x, _flightHeading.z) * Mathf.Rad2Deg;
            yaw = Mathf.MoveTowardsAngle(yaw, headingYaw, 90f * Time.fixedDeltaTime);
        }

        var airTarget = Quaternion.Euler(pitch, yaw, 0f);
        SteerRotationTowards(Quaternion.Slerp(_rigidbody.rotation, airTarget, _slideAlignSpeed * 0.5f * Time.fixedDeltaTime));
    }

    // Поворот тела ведётся ЗАДАНИЕМ угловой скорости, а не MoveRotation: для
    // динамического интерполируемого Rigidbody MoveRotation — телепорт мимо
    // интерполяции рендера (модель тикала при быстрой смене цели). Значение
    // перезаписывается каждый шаг — закрутка от контактов не живёт дольше шага.
    void SteerRotationTowards(Quaternion desired)
    {
        (desired * Quaternion.Inverse(_rigidbody.rotation)).ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f)
            angle -= 360f;
        if (Mathf.Abs(angle) < 1e-3f || float.IsInfinity(axis.x) || axis.sqrMagnitude < 1e-8f)
        {
            _rigidbody.angularVelocity = Vector3.zero;
            return;
        }

        _rigidbody.angularVelocity = axis.normalized * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime);
    }

    // Направление с вертикальным углом не больше maxPitchDegrees; горизонтальный
    // курс сохраняется.
    Vector3 ClampPitch(Vector3 direction, float maxPitchDegrees)
    {
        float sinMax = Mathf.Sin(maxPitchDegrees * Mathf.Deg2Rad);
        if (Mathf.Abs(direction.y) <= sinMax)
            return direction;

        Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (horizontal.sqrMagnitude < 1e-6f)
            horizontal = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (horizontal.sqrMagnitude < 1e-6f)
            return direction;

        float cosMax = Mathf.Cos(maxPitchDegrees * Mathf.Deg2Rad);
        return (horizontal.normalized * cosMax + Vector3.up * (Mathf.Sign(direction.y) * sinMax)).normalized;
    }

    // ================= КОНТАКТЫ (только детекция приземления) =================

    void OnCollisionEnter(Collision collision)
    {
        if (_collisionRelay != null)
            return;

        NotifyCollisionEnter(collision);
    }

    void OnCollisionStay(Collision collision)
    {
        if (_collisionRelay != null)
            return;

        NotifyCollisionStay(collision);
    }

    void OnCollisionExit(Collision collision)
    {
        // Контакты в путевой модели используются только как триггер пристёгивания.
    }

    public void NotifyCollisionEnter(Collision collision) => HandleSlideTouch(collision);

    public void NotifyCollisionStay(Collision collision) => HandleSlideTouch(collision);

    public void NotifyCollisionExit(Collision collision)
    {
    }

    void HandleSlideTouch(Collision collision)
    {
        // Пока тело пристёгнуто, контакты не нужны (позу ведёт путь). Полёт с
        // подавлением (прыжок/вылет) касания игнорирует — вылет должен состояться.
        if (_riding || _guideSuppressTimer > 0f || _rigidbody == null)
            return;

        var slide = GetSlideFrom(collision.collider);
        if (slide == null)
            return;

        // Касание внешней стороны оболочки — не приземление (пролетаем мимо).
        if (!IsInnerSurfaceContact(slide, collision))
            return;

        AttachToSlide(slide);
    }

    // Тело, прижатое к НАРУЖНОЙ стороне горки, пристёгивать нельзя. Коллайдер —
    // одиночная внутренняя оболочка, позиции контакта совпадают с обеих сторон —
    // стороны различает только нормаль контакта: валидная внутренняя смотрит к
    // центру жёлоба/оси трубы.
    bool IsInnerSurfaceContact(WaterSlideGenerator slide, Collision collision)
    {
        if (collision.contactCount == 0)
            return true;

        var frames = slide.RideFrames;
        if (frames == null || frames.Count == 0)
            return true;

        var contact = collision.GetContact(0);
        return IsInnerSurfacePoint(slide, contact.point, contact.normal);
    }

    // Точка поверхности горки с нормалью смотрит к оси жёлоба/трубы — внутренняя
    // (посадочная) сторона оболочки.
    static bool IsInnerSurfacePoint(WaterSlideGenerator slide, Vector3 point, Vector3 normal)
    {
        var frames = slide.RideFrames;
        if (frames == null || frames.Count == 0)
            return true;
        var frame = frames[FindNearestFrame(frames, point, 0, frames.Count - 1)];
        return Vector3.Dot(normal, frame.Position - point) > 0f;
    }

    float GetLateralBodyExtent()
    {
        if (_capsule == null)
            return 0.35f;

        return _capsule.radius + 0.04f;
    }

    // Мировая позиция центра капсулы — точка, о которой рассуждает вся геометрия
    // ведения. Пивот rigidbody стоит в корне рига (обычно у ног), в стороне от
    // коллайдера.
    Vector3 GetBodyCenterWorld()
    {
        if (_rigidbody == null)
            return transform.position;

        return _capsule != null
            ? _rigidbody.position + _rigidbody.rotation * _capsule.center
            : _rigidbody.position;
    }

    static int FindNearestFrame(IReadOnlyList<WaterSlideMeshBuilder.RideFrame> frames, Vector3 pos, int start, int end)
    {
        int best = start;
        float bestDist = float.MaxValue;
        for (int i = start; i <= end; i++)
        {
            float d = (frames[i].Position - pos).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        return best;
    }

    // ================= ПУБЛИЧНОЕ УПРАВЛЕНИЕ =================

    // A/D на горке: поперечное перемещение в путевой модели.
    public void SetSlideSteerInput(float axis)
    {
        _slideSteerInput = axis;
    }

    // Руль-ползунок: целевое поперечное положение, −1..1 (доля ширины пола;
    // в трубе — доля угла ±60°). active=false — цели нет, рулит SetSlideSteerInput.
    // Гейн скоростного контура руля: ускорение = (желаемая − текущая поперечная
    // скорость) × это. Для контура «скорость = гейн × ошибка» затухание
    // ζ = √(c/k)/2, отсюда c = 4ζ²k (ζ = 1 → 4k, критическое).
    float SteerVelocityGain => 4f * _steerDampingRatio * _steerDampingRatio * _lateralTargetGain;

    // Ускорение руля с пределом силы. Ограничивать ещё и скорость его изменения
    // (рывок) НЕЛЬЗЯ: при пределе 300 м/с³ запаздывание раскачивало регулятор —
    // поперечная скорость меняла знак ~2 раза в секунду (замер 06.11.2026).
    // Плавность даёт сглаживание цели пальца в InputController.
    float ClampSteerAccel(float raw)
    {
        return Mathf.Clamp(raw, -_slideSteerForce, _slideSteerForce);
    }

    public void SetSlideLateralTarget(float normalized, bool active)
    {
        _slideLateralTarget = Mathf.Clamp(normalized, -1f, 1f);
        _hasLateralTarget = active;
    }

    // Прогноз точки приземления в полёте: баллистика от центра тела с текущей
    // скоростью, шагами 0.05 с до horizon секунд; первая ВНУТРЕННЯЯ поверхность
    // горки на пути — точка. Для прицела приземления (LandingReticle).
    public bool TryPredictLanding(float horizon, out Vector3 point, out Vector3 normal, out WaterSlideGenerator slide)
    {
        point = default;
        normal = Vector3.up;
        slide = null;
        if (_rigidbody == null)
            return false;

        Vector3 pos = GetBodyCenterWorld();
        Vector3 vel = _rigidbody.linearVelocity;
        Vector3 g = Physics.gravity * GravityMultiplier;
        const float step = 0.05f;
        float radius = _capsule != null ? _capsule.radius : 0.35f;
        for (float t = 0f; t < horizon; t += step)
        {
            Vector3 next = pos + vel * step + g * (0.5f * step * step);
            Vector3 seg = next - pos;
            float len = seg.magnitude;
            if (len > 1e-4f)
            {
                int count = Physics.RaycastNonAlloc(pos, seg / len, _sweepHits, len + radius, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    var hit = _sweepHits[i];
                    if (hit.collider == null || hit.distance >= best)
                        continue;
                    var s = GetSlideFrom(hit.collider);
                    if (s == null || !IsInnerSurfacePoint(s, hit.point, hit.normal))
                        continue;
                    best = hit.distance;
                    point = hit.point;
                    normal = hit.normal;
                    slide = s;
                }
                if (slide != null)
                    return true;
            }
            pos = next;
            vel += g * step;
        }
        return false;
    }

    // W/S — читается эффектами (блюр разгона) и путевой моделью.
    public float ThrottleInput { get; private set; }

    public void SetThrottleInput(float axis)
    {
        ThrottleInput = axis;
    }

    // Пауза пристёгивания — используется воздушным прыжком.
    // Трамплинный разгон (см. _exitBoostDistance): по энергии оценивается скорость
    // в точке вылета (спуск/подъём до конца учтён), и если она ниже расчётной
    // скорости прыжка, добирается ровно столько ускорения, чтобы к концу горки её
    // достичь. Тормоз пальцем в этой зоне перекрывается — вылет всегда достаёт до
    // входа следующей горки (боковой промах по-прежнему исправляет стрейф).
    void ApplyExitBoost(IReadOnlyList<WaterSlideMeshBuilder.RideFrame> frames, Vector3 gravity, float dt)
    {
        if (_course == null || _exitBoostDistance <= 0f || _cumLengths == null || _cumLengths.Length < 2)
            return;
        var slides = _course.Slides;
        if (!_course.Endless && slides.Count > 0 && slides[slides.Count - 1] == _currentSlide)
            return;
        float remaining = _cumLengths[^1] - _ridePathS;
        if (remaining <= 0.05f || remaining > _exitBoostDistance)
            return;
        float target = _course.RequiredJumpSpeed;
        int here = Mathf.Clamp(_rideFrameIndex, 0, frames.Count - 1);
        float dropToEnd = frames[here].Position.y - frames[frames.Count - 1].Position.y;
        float endSq = _rideSpeed * _rideSpeed + 2f * gravity.magnitude * dropToEnd;
        if (endSq >= target * target)
            return;
        float need = (target * target - Mathf.Max(endSq, 0f)) / (2f * Mathf.Max(remaining, 0.5f));
        _rideSpeed += Mathf.Min(need, _exitBoostMaxAccel) * dt;
    }

    // Прыжок в воздухе (PlayerController): на перелёте с конца горки он и есть
    // «прыжок на следующую горку» — включает полную помощь приземлению.
    public void NotifyAirJump()
    {
        if (_landingAssistSlide != null)
            _landingAssistJumped = true;
    }

    // Был ли уже прыжок на текущем перелёте к следующей горке (для подсказок).
    public bool NeedsJumpToReachNext => !_riding && _landingAssistSlide != null && !_landingAssistJumped;

    // Помощь приземлению (см. _landingAssistAccel). Цель — точка на осевой
    // следующей горки в _landingAssistTargetS метрах от её начала, поднятая на
    // радиус тела. По текущей вертикальной скорости и высоте над целью — время
    // падения до её уровня; нужная горизонтальная скорость = расстояние / время.
    // Подгоняется только составляющая вдоль направления на цель, с пределом
    // ускорения и за половину оставшегося времени (без рывка). Перпендикулярная
    // составляющая — за игроком.
    void ApplyLandingAssist()
    {
        var target = _landingAssistSlide;
        if (target == null || _landingAssistAccel <= 0f || _rigidbody == null || _rigidbody.isKinematic)
            return;
        var frames = target.RideFrames;
        if (frames == null || frames.Count < 2)
            return;

        float s = 0f;
        int k = 0;
        while (k < frames.Count - 1 && s < _landingAssistTargetS)
        {
            s += Vector3.Distance(frames[k].Position, frames[k + 1].Position);
            k++;
        }
        if (!_landingAssistJumped)
        {
            if (_noJumpShortfall <= 0f)
                return;
            k = 0; // без прыжка — относительно самого начала горки (рампы)
        }
        float lift = _capsule != null ? _capsule.radius : 0.35f;
        Vector3 aim = frames[k].Position + frames[k].Up * lift;
        Vector3 pos = GetBodyCenterWorld();
        Vector3 vel = _rigidbody.linearVelocity;
        float g = Physics.gravity.magnitude * GravityMultiplier;
        float h = pos.y - aim.y;
        float disc = vel.y * vel.y + 2f * g * h;
        if (disc <= 0f)
        {
            // Цель выше вершины дуги — достать нельзя; не дёргаем, отключаемся.
            _landingAssistSlide = null;
            return;
        }
        float tFall = (vel.y + Mathf.Sqrt(disc)) / g;
        if (tFall < 0.12f)
            return;
        Vector3 toAim = Vector3.ProjectOnPlane(aim - pos, Vector3.up);
        float dist = toAim.magnitude;
        if (dist < 0.05f)
            return;
        Vector3 dir = toAim / dist;
        float along = Vector3.Dot(Vector3.ProjectOnPlane(vel, Vector3.up), dir);
        float desired = dist / tFall;
        // Без прыжка — недолёт: цель ближе нужного, и только торможение (если дуга
        // и так короче — не трогаем).
        if (!_landingAssistJumped)
            desired = Mathf.Max(dist - _noJumpShortfall, 0f) / tFall;
        // Без прыжка тормозим втрое сильнее и быстрее: на вылете 25–30 м/с предела
        // помощи (25 м/с²) не хватало, чтобы уложить дугу перед рампой (замер 10.11.2026).
        float limit = _landingAssistJumped ? _landingAssistAccel : _landingAssistAccel * 3f;
        float settle = _landingAssistJumped ? Mathf.Max(tFall * 0.5f, 0.2f) : Mathf.Max(tFall * 0.25f, 0.1f);
        float acc = Mathf.Clamp((desired - along) / settle, -limit, limit);
        if (!_landingAssistJumped && acc > 0f)
            return;
        AddAirControlAcceleration(dir * acc);
    }

    public void SuppressGuidance()
    {
        _guideSuppressTimer = Mathf.Max(_guideSuppressTimer, _jumpGuideSuppression);
    }

    // Спавнер задаёт трассу заранее, чтобы ведение знало курс до первого касания.
    public void SetCourse(WaterSlideCourse course)
    {
        _course = course;

        // Потолок скорости подстраивается под трассу: прыжок, настроенный длиннее,
        // автоматически поднимает достижимую скорость. Никогда не опускает.
        if (course != null)
        {
            float required = course.RequiredJumpSpeed;
            if (HasSpeedCap && required > _maxSlideSpeed)
            {
                Debug.Log($"PlayerView: max slide speed raised {_maxSlideSpeed:F1} → {required:F1} m/s to match the course jump settings.");
                _maxSlideSpeed = required;
            }
        }
    }

    // Временное игнорирование коллайдера горки капсулой (вылет за борт).
    Collider _ignoredSlideCollider;
    float _ignoreSlideTimer;

    void IgnoreSlideCollision(WaterSlideGenerator slide, float seconds)
    {
        if (slide == null || _capsule == null)
            return;
        var col = slide.GetComponent<MeshCollider>();
        if (col == null)
            return;

        RestoreIgnoredSlideCollision();
        Physics.IgnoreCollision(_capsule, col, true);
        _ignoredSlideCollider = col;
        _ignoreSlideTimer = seconds;
    }

    void RestoreIgnoredSlideCollision()
    {
        if (_ignoredSlideCollider != null && _capsule != null)
            Physics.IgnoreCollision(_capsule, _ignoredSlideCollider, false);
        _ignoredSlideCollider = null;
        _ignoreSlideTimer = 0f;
    }

    void TickIgnoredSlideCollision()
    {
        if (_ignoredSlideCollider == null)
            return;
        _ignoreSlideTimer -= Time.fixedDeltaTime;
        if (_ignoreSlideTimer <= 0f)
            RestoreIgnoredSlideCollision();
    }

    public void SetGuideSlide(WaterSlideGenerator slide)
    {
        if (slide != null)
            _currentSlide = slide;
    }

    public void Teleport(Vector3 worldPosition, Vector3 worldForward)
    {
        Vector3 forward = worldForward.sqrMagnitude > 1e-6f ? worldForward.normalized : Vector3.forward;
        var rotation = Quaternion.LookRotation(forward, Vector3.up);

        transform.SetPositionAndRotation(worldPosition, rotation);

        if (_rigidbody != null)
        {
            _rigidbody.position = worldPosition;
            _rigidbody.rotation = rotation;
            _rigidbody.useGravity = true;

            // Спавн-телепорт происходит, пока тело кинематическое (BeginPlay
            // разморозит позже) — кинематика отклоняет запись скорости.
            if (!_rigidbody.isKinematic)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
        }

        Physics.SyncTransforms();
        _riding = false;
        _hasSlideContactSinceSpawn = false;
        _flightHeading = Vector3.ProjectOnPlane(forward, Vector3.up).sqrMagnitude > 1e-4f
            ? Vector3.ProjectOnPlane(forward, Vector3.up).normalized
            : Vector3.forward;
        _slideNormal = Vector3.up;
        _slideNormalTarget = Vector3.up;
        _guideTangent = forward;
        _lastGuideIndex = -1;
        _rideFrameIndex = 0;
        _ridePathS = 0f;
        _rideLateral = 0f;
        _rideLateralVel = 0f;
        _landingAssistSlide = null;
        _rideSpeed = 0f;
        _tubeAngle = 0f;
        _tubeAngularVel = 0f;
        _smoothedCurvature = 0f;
        _cornerDanger = 0f;
        _guideSuppressTimer = 0f;
        _timeSinceSlideContact = 999f;
        _landingContainTimer = 0f;
        _crestHopTimer = 0f;
        _hasRimPlane = false;
        _fitInitialized = false;
        _hasLastSweepCenter = false;
        UpdateColliderPose();
    }

    void EnsurePhysicsSetup()
    {
        EnsurePhysicsOnRoot();
        if (_rigidbody == null)
            return;

        _rigidbody.mass = _mass;
        _rigidbody.linearDamping = 0f;
        _rigidbody.angularDamping = 0.05f;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        // Sweep CCD, НЕ спекулятивный: спекулятивные контакты создаются на дистанции
        // шага перед поверхностью — тело ехало на невидимой подушке.
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        // Вращение не заморожено: ориентацию ведёт задание угловой скорости
        // (SteerRotationTowards) — рендер интерполирует её как физическое движение.
        _rigidbody.constraints = RigidbodyConstraints.None;
        _rigidbody.maxAngularVelocity = 50f;
        _rigidbody.useGravity = true;

        // Смягчение ударов о швы колец: пинок выталкивания из фасетки не больше
        // 1 м/с за шаг (при езде это отклонение ~5°, незаметно).
        _rigidbody.maxDepenetrationVelocity = 1f;

        if (_capsule == null)
            return;

        // Больший contact offset заставляет PhysX подхватывать следующую фасетку
        // заранее и решать переход за несколько шагов, а не одним импульсом.
        _capsule.contactOffset = 0.06f;

        if (_physicsMaterial == null)
        {
            _physicsMaterial = new PhysicsMaterial("PlayerSlide")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }

        _capsule.material = _physicsMaterial;
        SyncCenterOfMass();
    }

    void EnsurePhysicsOnRoot()
    {
        if (PhysicsNeedsMigration())
            MigratePhysicsToRoot();

        if (_rigidbody == null)
            _rigidbody = GetComponent<Rigidbody>();

        if (_capsule == null)
            _capsule = GetComponent<CapsuleCollider>();

        if (_rigidbody == null)
            _rigidbody = gameObject.AddComponent<Rigidbody>();

        if (_capsule == null)
            _capsule = gameObject.AddComponent<CapsuleCollider>();

        if (_collisionRelay != null && _collisionRelay.transform != transform)
            _collisionRelay = null;
    }

    bool PhysicsNeedsMigration()
    {
        if (_rigidbody != null && _rigidbody.transform != transform)
            return true;

        if (_capsule != null && _capsule.transform != transform)
            return true;

        return GetComponent<Rigidbody>() == null && GetComponentsInChildren<Rigidbody>(true).Length > 0;
    }

    void MigratePhysicsToRoot()
    {
        if (_rigidbody != null && _rigidbody.transform != transform)
        {
            if (Application.isPlaying)
                Destroy(_rigidbody);
            else
                DestroyImmediate(_rigidbody);
            _rigidbody = null;
        }

        if (_capsule != null && _capsule.transform != transform)
        {
            if (Application.isPlaying)
                Destroy(_capsule);
            else
                DestroyImmediate(_capsule);
            _capsule = null;
        }

        foreach (var childBody in GetComponentsInChildren<Rigidbody>(true))
        {
            if (childBody.transform == transform)
                continue;

            if (Application.isPlaying)
                Destroy(childBody);
            else
                DestroyImmediate(childBody);
        }

        foreach (var childCollider in GetComponentsInChildren<CapsuleCollider>(true))
        {
            if (childCollider.transform == transform)
                continue;

            if (Application.isPlaying)
                Destroy(childCollider);
            else
                DestroyImmediate(childCollider);
        }
    }

    static WaterSlideGenerator GetSlideFrom(Collider other) =>
        other.GetComponentInParent<WaterSlideGenerator>();
}

public struct PlayerMovementSettings
{
    public float SlideSteerForce;
    public float MaxSlideSteerSpeed;
    public float SlideJumpForce;
    public float AirStrafeAcceleration;
    public float AirStrafeDamping;
    public float AirJumpForce;
    public float MaxAirStrafeSpeed;
    public float ThrottleAcceleration;
    public float BrakeAcceleration;
    public float MaxForwardSpeed;
}
