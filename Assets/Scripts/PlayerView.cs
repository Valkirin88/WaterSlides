using System.Collections.Generic;
using UnityEngine;
using WaterSlides;

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
    [Tooltip("Множитель гравитации игрока — «вес». Больше — сильнее прижимает к жёлобу и стенкам (труднее вылететь за борт), короче дуги полёта. Требуемая скорость прыжков автоматически пересчитывается с учётом множителя (SetCourse).")]
    [SerializeField] float _gravityMultiplier = 1.5f;
    [Tooltip("Скорость доворота тела к поверхности горки и к направлению полёта. Больше — быстрее и жёстче выравнивание ориентации.")]
    [SerializeField] float _slideAlignSpeed = 10f;
    [Tooltip("Максимальный наклон тела носом вниз/вверх в свободном полёте, градусы.")]
    [SerializeField, Range(0f, 90f)] float _maxAirPitch = 35f;
    [Tooltip("Локальное смещение визуальной модели относительно физического тела, метры. Применяется каждый кадр.")]
    [SerializeField] Vector3 _visualLocalOffset = Vector3.zero;
    [Tooltip("Локальный поворот визуальной модели относительно физического тела, градусы (углы Эйлера). Применяется каждый кадр.")]
    [SerializeField] Vector3 _visualLocalEuler = Vector3.zero;

    [Header("Slide Ride")]
    [Tooltip("Дополнительный разгон вдоль уклона, м/с².")]
    [SerializeField] float _slideAcceleration = 2.5f;
    [Tooltip("Максимальная скорость на горке, м/с.")]
    [SerializeField] float _maxSlideSpeed = 16f;
    [Tooltip("Прижим к поверхности при контакте, м/с² — гасит подлёты на швах и гребнях. Действует ТОЛЬКО пока есть касание: настоящий вылет с гребня теряет контакт на первом же шаге, и прижим исчезает.")]
    [SerializeField] float _stickAcceleration = 30f;
    [Tooltip("Гашение боковых колебаний поперёк жёлоба.")]
    [SerializeField] float _lateralDamping = 0.5f;
    [Tooltip("Допустимая доля полуширины жёлоба до отключения притягивания к центру (от доступной полосы с учётом радиуса тела).")]
    [SerializeField, Range(0.2f, 1f)] float _boundsLimit = 0.92f;
    [Tooltip("Постоянное притягивание к центру жёлоба, м/с². Действует только при контакте.")]
    [SerializeField] float _centeringAccel = 10f;
    [Tooltip("Скорость сглаживания нормали поверхности (больше — быстрее реагирует).")]
    [SerializeField] float _normalSmoothing = 14f;
    [Tooltip("Дистанция от осевой линии, в пределах которой ищется ближайшая горка для ведения.")]
    [SerializeField] float _guideCaptureDistance = 2.5f;
    [Tooltip("Дистанция, на которой уже активное ведение отпускает тело (гистерезис: захват ближе, отпускание дальше).")]
    [SerializeField] float _guideReleaseDistance = 7f;
    [Tooltip("Потолок скорости доворота вектора скорости к направлению горки, рад/с. Действует только при контакте. На открытом жёлобе фактический доворот дополнительно ограничен сцеплением (поле ниже) — на скорости он слабее этого потолка.")]
    [SerializeField] float _velocitySteer = 7f;
    [Tooltip("«Сцепление» с открытым жёлобом в поворотах, м/с² — потолок центростремительного ускорения, с которым ведение заворачивает скорость вдоль пути. Задаёт предельную безопасную скорость поворота: √(сцепление × радиус). При 20: радиус 5 м — до ~10 м/с, радиус 14 м — до ~16.7 м/с. Быстрее — тело сползает на внешний борт и инерция выносит за борт; тормози S перед крутыми поворотами. В закрытой трубе не действует (там держит сама труба).")]
    [SerializeField] float _corneringGrip = 20f;
    [Tooltip("Доля сцепления, остающаяся при зажатом A/D на открытом жёлобе (0–1). 0 — руль полностью отпускает доворот (вылет в повороте с рулём мгновенный и не зависит от скорости); 1 — руль не ослабляет сцепление. При 0.35 руление в повороте втрое режет предельную скорость — рискованно, но предсказуемо.")]
    [SerializeField, Range(0f, 1f)] float _steerGripFraction = 0.35f;
    [Tooltip("Доля сцепления, доступная автоведению БЕЗ нажатых A/D на открытом жёлобе (0–1). 1 — ведение держит любой честный поворот само (вылетов без руля не бывает); меньше — предельная скорость поворота без руля падает до √(доля × сцепление × радиус), выше неё инерция выносит за борт, и поворот нужно ДЕРЖАТЬ рулём в его сторону (сила руля прижимает к внутреннему борту) или сбрасывать скорость S. При 0.55 и сцеплении 20: поворот радиуса 15 м без руля держится до ~12.8 м/с, на потолке 16 м/с уже нужен руль.")]
    [SerializeField, Range(0f, 1f)] float _autoGripFraction = 0.55f;
    [Tooltip("Вынос за борт на пределе сцепления: сколько секунд ПОДРЯД требуемое поворотом центростремительное ускорение (скорость × кривизна пути) должно превышать доступное сцепление (доля сцепления с учётом руля; руль В сторону поворота добавляет свою силу, ОТ поворота — вычитает), чтобы жёлоб «отпустил» тело — его подбрасывает вдоль стенки и выносит за внешний борт. Нужно, потому что стенки узкого жёлоба геометрически держат тело как рельсы и честный занос перевалить через борт не может. 0 — механика выключена (вылетов в поворотах не будет).")]
    [SerializeField] float _wallLaunchDelay = 0.2f;
    [Tooltip("Пауза ведения после намеренного прыжка, сек.")]
    [SerializeField] float _jumpGuideSuppression = 0.6f;
    [Tooltip("Сглаживание направления ведения по пути (больше — быстрее следует за поворотами).")]
    [SerializeField] float _guideTangentSmoothing = 18f;
    [Tooltip("Время плавного включения ассиста после приземления, сек — чем больше, тем меньше «примагничивание» при попадании на следующую горку.")]
    [SerializeField] float _captureEaseTime = 0.8f;
    [Tooltip("Приземление на горку после полёта: в момент касания вектор скорости МГНОВЕННО задаётся вдоль горки (вниз по пути), величина скорости сохраняется полностью — весь запас хода переходит в езду. Выкл — скорость доворачивается постепенно (сцепление, рампа захвата и окно ловли поперечного приземления ниже).")]
    [SerializeField] bool _landingAlignVelocity = true;
    [Tooltip("Потолок скорости езды, которую задаёт снап приземления, м/с. При падении с большой высоты величина скорости огромна; без потолка она целиком переходила в ход вдоль горки — на первом же изгибе тело отрывалось от поверхности и вылетало (читалось как отскок). Излишек сверх потолка гасится ударом о воду. Если максимальная скорость на горке выше потолка — действует она. 0 — без потолка (прежнее поведение).")]
    [SerializeField] float _landingSnapSpeedCap = 18f;
    [Tooltip("Ловля ПОПЕРЕЧНОГО приземления: если после настоящего полёта скорость пришла к горке под углом к направлению пути больше этого порога (градусы, угол меряется в плоскости жёлоба), открывается окно быстрого доворота — ТЕЛО разворачивается вдоль горки на полной скорости выравнивания (а при выключенном мгновенном снапе выше — и скорость, со скоростью ниже). 0 — отключить.")]
    [SerializeField] float _crossLandingAngle = 40f;
    [Tooltip("Скорость доворота СКОРОСТИ вдоль пути в окне ловли, рад/с — используется, когда мгновенный снап скорости при приземлении выключен. Работает МИМО потолка сцепления и рампы захвата. Скорость при развороте не режется — только поворачивается.")]
    [SerializeField] float _crossLandingTurnRate = 5f;
    [Tooltip("Максимальная длительность окна быстрого доворота, сек — страховка: если довернуться вдоль пути не успели, окно закрывается само.")]
    [SerializeField] float _crossLandingMaxTime = 0.6f;
    [Tooltip("Антиотскок после приземления, сек: пока идёт окно (или окно удержания ниже), скорость при контакте удерживается в плоскости поверхности — «отдача» от гашения падения (импульс контакта, выталкивание из перекрытия) переводится в ход вдоль горки, а не в подскок. Величина скорости сохраняется. Вне окон при обычной езде НЕ действует (глушил бы честный подъём по стенке). Намеренный прыжок (пробел) окно не глушит. 0 — выключить.")]
    [SerializeField] float _landingNoBounceTime = 0.25f;
    [Tooltip("Удержание после приземления, сек: столько времени после касания горки тело НЕ может вылететь за борт — у стенки открытого жёлоба оно возвращается внутрь и наружная боковая скорость срезается, вынос инерцией за борт и приземления на кромку тоже ловятся. Намеренный прыжок пробелом работает как обычно. 0 — выключить.")]
    [SerializeField] float _landingContainTime = 1f;

    [Header("Movement")]
    [Tooltip("Ускорение по W вдоль движения, м/с². Работает и на горке, и в воздухе.")]
    [SerializeField] float _throttleAcceleration = 8f;
    [Tooltip("Замедление по S, м/с². Работает и на горке, и в воздухе.")]
    [SerializeField] float _brakeAcceleration = 12f;
    [Tooltip("Историческое поле скорости поворота на горке; в текущей логике движения не читается (сохранено ради сериализации), изменение значения ни на что не влияет.")]
    [SerializeField] float _slideTurnSpeed = 140f;
    [Tooltip("Сила руления A/D на горке, м/с². В открытом жёлобе прижимает к борту, в закрытой трубе раскачивает вокруг оси. Больше — резче уходит к краю.")]
    [SerializeField] float _slideSteerForce = 22f;
    [Tooltip("Потолок боковой скорости от руля A/D на открытом жёлобе, м/с. Держи НИЖЕ скорости выпрыгивания через стенку (~3.4 м/с при борте 0.6 м): сам руль лишь прижимает к борту, а выносит за борт только накопленная на скорости инерция — порог не срезает скорость, он только не даёт силе руля разгонять дальше.")]
    [SerializeField] float _maxSlideSteerSpeed = 2.5f;
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

    // Keyed by collider so an ignored (exterior) touch can never unbalance the count
    // the way a plain enter/exit counter could.
    readonly HashSet<Collider> _slideContacts = new();
    Vector3 _slideNormal = Vector3.up;
    Vector3 _slideNormalTarget = Vector3.up;

    WaterSlideGenerator _currentSlide;
    WaterSlideCourse _course;
    int _lastGuideIndex = -1;
    Vector3 _guideTangent;
    Vector3 _guideRight;
    bool _guideIsOpenFlume;
    Vector3 _smoothedGuideTangent;
    bool _hasSmoothedGuideTangent;
    bool _hasGuide;
    bool _guideWasActive;
    float _guideSuppressTimer;
    float _timeSinceSlideContact = 999f;
    float _assistRamp;

    // Cross-landing catch (see GuideAlongSlide): the pending flag is armed on a fresh
    // landing after a real flight; the first guided step then measures the arrival angle
    // and, if it is crosswise, opens the fast auto-turn window (the timer).
    bool _crossLandingCheckPending;
    float _crossLandingTimer;
    float _landingNoBounceTimer;
    // Post-landing containment window (see _landingContainTime): while open, the body
    // may not leave the channel over a wall — rescues run and the flyout is gated.
    float _landingContainTimer;

    Vector3 _fitCenter;
    float _fitHeight;
    bool _fitInitialized;
    float _slideSteerInput;
    // Over-grip launch state: the previous smoothed guide tangent (turn-rate probe),
    // when it was sampled, and how long the corner demand has exceeded the grip.
    Vector3 _prevGuideTangent;
    float _prevGuideTangentTime = float.NegativeInfinity;
    float _overGripTimer;
    // A failed launch (clipped the lip, fell back in) must not retry instantly —
    // the retry loop read as the body jerking up the wall and back every ~0.2 s.
    float _lastWallLaunchTime = float.NegativeInfinity;
    // Rim plane of the open flume currently guiding the body — published for the
    // camera so it can proactively stay above the channel rim on banked corners.
    Vector3 _rimPlanePoint;
    Vector3 _rimPlaneUp;
    bool _hasRimPlane;

    // Horizontal flight direction, frozen at the moment contact is lost: the whole
    // flight (strafe axis, body yaw, camera) is anchored to it so A/D reads as a
    // sideways SHIFT of the arc, never as a turn.
    Vector3 _flightHeading = Vector3.forward;

    public Rigidbody Rigidbody => _rigidbody;
    public Transform MovementTransform => transform;
    // The course generator reads this to place jump landings on the REAL flight arc.
    public float GravityMultiplier => Mathf.Max(_gravityMultiplier, 1f);
    // Read by WaterSlideCourse: the path generator keeps corners fair for THESE physics
    // values — corner-holding limit √(grip·R) and the braking distance before sharp turns.
    public float CorneringGrip => _corneringGrip;

    // Rim plane of the open flume currently guiding the body: a point at lip-top
    // level on the channel spine, plane normal = channel up. Kept briefly after
    // contact loss so camera logic survives the micro-hops of a rough corner; false
    // in closed tubes and in real flight.
    public bool TryGetFlumeRimPlane(out Vector3 point, out Vector3 up)
    {
        point = _rimPlanePoint;
        up = _rimPlaneUp;
        return _hasRimPlane && (IsOnSlide || _timeSinceSlideContact < 0.6f);
    }
    public float BrakeDeceleration => _brakeAcceleration;
    public float MaxRideSpeed => _maxSlideSpeed;
    public bool IsOnSlide => _slideContacts.Count > 0;
    public bool IsGuided => _hasGuide;

    // An airborne body still inside the current slide's capture volume (a seam hop, a
    // scramble onto the wall lip that is about to come back down) — the FLIGHT controls
    // must not engage there: the air strafe fired at full acceleration the instant the
    // contact flickered and shoved the body sideways off the lip, which read as the
    // slide itself kicking the player out. False once the body genuinely departs the
    // channel (past the end, beyond the walls, below the floor) — that is the real
    // flight to the next slide, where the controls belong. A deliberate jump and the
    // over-grip launch suppress guidance and get the flight controls immediately.
    public bool IsAirborneHopNearSlide =>
        _hasSlideContactSinceSpawn
        && _guideSuppressTimer <= 0f
        && _currentSlide != null
        && TryGetGuideFrameOnSlide(_currentSlide, out _, out _, out _, out _);
    // The initial drop after a spawn/teleport is not a flight between slides.
    public bool HasSlideContactSinceSpawn => _hasSlideContactSinceSpawn;
    // Set by PlayerFlightRagdoll: fitting the capsule to flailing ragdoll bones would
    // inflate it mid-flight, so the fit is frozen while the doll is live.
    public bool CapsuleFitSuspended { get; set; }
    public Vector3 SlideNormal => _slideNormal;

    // Strafe axis anchor for PlayerController.ApplyAirMovement (see _flightHeading).
    public Vector3 FlightHeading => _flightHeading;

    // Lateral steer axis for A/D in an OPEN flume: the guide frame right (oriented to the
    // right of the current motion). Unlike the surface-tangent right it never points up a
    // wall, so a force along it presses the body INTO the wall instead of over the lip.
    public bool TryGetOpenFlumeSteerAxis(out Vector3 right)
    {
        right = _guideRight;
        return _hasGuide && _guideIsOpenFlume && right.sqrMagnitude > 1e-6f;
    }
    public float SpawnBodyClearance => _capsule != null ? _capsule.radius : 0.35f;

    public PlayerMovementSettings MovementSettings => new()
    {
        SlideTurnSpeed = _slideTurnSpeed,
        SlideSteerForce = _slideSteerForce,
        MaxSlideSteerSpeed = _maxSlideSteerSpeed,
        SlideJumpForce = _slideJumpForce,
        AirStrafeAcceleration = _airStrafeAcceleration,
        AirStrafeDamping = _airStrafeDamping,
        AirJumpForce = _airJumpForce,
        MaxAirStrafeSpeed = _maxAirStrafeSpeed,
        ThrottleAcceleration = _throttleAcceleration,
        BrakeAcceleration = _brakeAcceleration,
        MaxForwardSpeed = _maxSlideSpeed
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

        // Zero the velocities BEFORE switching to kinematic: a kinematic body rejects
        // velocity writes with a console warning, and it has no velocity to clear anyway.
        if (!_rigidbody.isKinematic)
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }

        _rigidbody.isKinematic = true;
        _slideContacts.Clear();
    }

    public void BeginPlay()
    {
        if (_rigidbody == null)
            return;

        _rigidbody.isKinematic = false;
        _rigidbody.WakeUp();
        _spawnPrepared = false;
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
        ApplyExtraGravity();
        ApplySlidePhysics();
        AlignBodyToContacts();
        _controller?.FixedUpdate();
    }

    void LateUpdate()
    {
        ApplyVisualLocalPose();
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

    // The capsule ALWAYS lies along local Z (the body slides feet/head first along its
    // forward). Only the extent and the centre are fitted to the animated pose: picking
    // the axis from the head-to-feet line made the capsule flip sideways on arched poses,
    // and every flip in contact is a depenetration kick (visible as violent jitter).
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

        // Deadband: while sliding the capsule is effectively frozen (re-fitting it in
        // contact kicks the body off the surface); in the air a small threshold is enough.
        float deadband = IsOnSlide ? 0.06f : 0.02f;
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

        // The doll's bones fall under plain 1g; without the same extra pull the whole
        // doll "floats up" against the capsule for the entire flight and the head weld
        // keeps yanking it back down — visible as sluggish, rubbery flight response.
        if (_flightRagdoll != null)
            _flightRagdoll.AddAcceleration(Physics.gravity * (_gravityMultiplier - 1f));
    }

    // Air-control forces (strafe and its damping) must accelerate the capsule AND the
    // live flight doll identically: applied to the capsule alone they reach the visible
    // body only through the head weld, so it swings behind like a pendulum and catches
    // up late — steering read as delayed even though the capsule responds instantly.
    // While the doll is inactive (riding, or the component is absent) this is exactly
    // AddForce(Acceleration) on the capsule.
    public void AddAirControlAcceleration(Vector3 acceleration)
    {
        if (_rigidbody == null)
            return;

        _rigidbody.AddForce(acceleration, ForceMode.Acceleration);
        if (_flightRagdoll != null)
            _flightRagdoll.AddAcceleration(acceleration);
    }

    void ApplySlidePhysics()
    {
        _guideWasActive = _hasGuide;
        _hasGuide = false;

        if (_rigidbody == null)
            return;

        if (IsOnSlide)
        {
            // A landing after a real flight (not a seam hop) restarts the assist ramp:
            // the next slide must not yank the body onto its path the moment it touches.
            if (_timeSinceSlideContact > 0.3f)
            {
                _assistRamp = 0f;
                // Arm the cross-landing check: the first guided step measures the angle.
                _crossLandingCheckPending = true;
                // And the no-bounce window: the touchdown must convert the fall into
                // ride speed, never into a hop off the surface.
                _landingNoBounceTimer = _landingNoBounceTime;
                // And the containment window: right after a landing the body may not
                // leave the channel over a wall (user rule — a fresh landing gets a
                // beat of safety before flyouts become possible again).
                _landingContainTimer = _landingContainTime;
            }
            _assistRamp += Time.fixedDeltaTime;
            _timeSinceSlideContact = 0f;

            // While riding, the launch heading tracks the horizontal motion; once
            // contact is lost it simply stops updating — frozen for the whole flight.
            Vector3 horizontalVel = Vector3.ProjectOnPlane(_rigidbody.linearVelocity, Vector3.up);
            if (horizontalVel.sqrMagnitude > 0.25f)
                _flightHeading = horizontalVel.normalized;
        }
        else
            _timeSinceSlideContact += Time.fixedDeltaTime;

        if (_landingNoBounceTimer > 0f)
            _landingNoBounceTimer -= Time.fixedDeltaTime;
        if (_landingContainTimer > 0f)
            _landingContainTimer -= Time.fixedDeltaTime;

        // The path assist runs ONLY while the body actually touches the slide. Airborne
        // it is fully ballistic — nothing steers, contains or pulls it back — so enough
        // speed genuinely throws the player over a wall or off a crest.
        if (_guideSuppressTimer > 0f)
            _guideSuppressTimer -= Time.fixedDeltaTime;
        else if (IsOnSlide)
        {
            var guideSlide = ResolveGuideSlide();
            if (guideSlide != null && guideSlide != _currentSlide)
                TrackCurrentSlide(guideSlide);

            if (guideSlide != null
                && TryGetGuideFrameOnSlide(guideSlide, out Vector3 center, out Vector3 tangent, out Vector3 guideUp, out float halfWidth))
                GuideAlongSlide(guideSlide, center, tangent, guideUp, halfWidth);
        }

        // Smooth the surface normal: raw per-triangle contact normals change every step and
        // are the main source of jitter.
        if (IsOnSlide || _hasGuide)
        {
            float normalLerp = 1f - Mathf.Exp(-_normalSmoothing * Time.fixedDeltaTime);
            _slideNormal = Vector3.Slerp(_slideNormal, _slideNormalTarget, normalLerp).normalized;
        }

        if (!IsOnSlide)
            return;

        _rigidbody.WakeUp();

        Vector3 normal = _slideNormal;
        Vector3 vel = _rigidbody.linearVelocity;

        // Remove the velocity component pushing into the surface (no micro-bounces on seams).
        Vector3 intoSurface = Vector3.Project(vel, normal);
        if (Vector3.Dot(intoSurface, normal) < 0f)
        {
            vel -= intoSurface;
            _rigidbody.linearVelocity = vel;
        }
        // Anti-bounce inside the post-landing windows (the no-bounce beat AND the
        // containment second — leaving the channel is forbidden there anyway): the
        // contact impulse and the depenetration push both kick OUTWARD along the
        // normal, and with the touchdown snap feeding the full fall speed into the
        // ride the kick read as a visible hop off the next slide. The outward
        // velocity is ROTATED back into the surface plane (magnitude kept — the kick
        // becomes ride speed, not a hop). NOT active during plain riding: an honest
        // climb up the wall (banked corner, drifting to the lip with no input) reads
        // as "outward" against the lagging floor-and-wall smoothed normal, and a
        // full-time flatten kept hurling the body back down into the channel. The
        // ride-time wall-facet kicks are tamed at the source instead — see the
        // maxDepenetrationVelocity cap in EnsurePhysicsSetup. A deliberate jump
        // suppresses guidance and is never flattened.
        else if ((_landingNoBounceTimer > 0f || _landingContainTimer > 0f) && _guideSuppressTimer <= 0f)
        {
            // Checked against BOTH normals: the raw contact normal (the honest local
            // facet), and the smoothed one — a high-speed impact on a triangle edge
            // reports a junk contact normal, the solver kick along it reads as flat to
            // that junk plane and escaped the raw check, while the smoothed normal
            // barely follows one-frame junk and still sees the velocity as outward.
            bool changed = FlattenOutward(ref vel, _slideNormalTarget);
            changed |= FlattenOutward(ref vel, _slideNormal);
            if (changed)
                _rigidbody.linearVelocity = vel;
        }

        float speed = vel.magnitude;

        // Press the body onto the surface. Removed once as a "magnet" — and the ride
        // immediately started hopping off at every facet seam (read as constant bouncing
        // off the slide). Contact-only, so it does NOT prevent real launches: a crest
        // exit loses contact on the first airborne step and the force is gone with it.
        // Released while A/D is held: at the floor-wall junction the smoothed normal
        // averages BOTH faces, so the stick gains a tangential component down the wall
        // (up to ~sin(45°)·30 ≈ 21 м/с²) that overpowered the steer force — the edge
        // felt like an invisible wall where the keys stopped working.
        // Scaled by riding speed: the press exists to stop seam hops AT SPEED. For a
        // nearly stopped body (a crash into a tube corner) the smoothed normal averages
        // the floor and the facing wall, and the full press squeezed the body INTO the
        // wedge for seconds — penetration grew until depenetration found the shortest
        // exit on the OUTER side and popped the body through the shell.
        // Muted while guidance is suppressed (deliberate jump, over-grip launch): near
        // the floor-wall junction the smoothed normal averages both faces and the
        // press gains a tangential component DOWN the wall (up to ~21 м/с²) — enough
        // to stall a takeoff that climbs along the wall before it clears the lip.
        float stickScale = _guideSuppressTimer > 0f
            ? 0f
            : (1f - Mathf.Clamp01(Mathf.Abs(_slideSteerInput))) * Mathf.Clamp01(speed / 3f);
        if (stickScale > 0f)
            _rigidbody.AddForce(-normal * (_stickAcceleration * stickScale), ForceMode.Acceleration);

        // Downhill push with a hard speed cap — uncapped acceleration is what threw the
        // body over the flume walls in corners.
        if (speed < _maxSlideSpeed)
        {
            Vector3 downhill = Vector3.ProjectOnPlane(Physics.gravity, normal);
            if (downhill.sqrMagnitude > 1e-4f)
                _rigidbody.AddForce(downhill.normalized * _slideAcceleration, ForceMode.Acceleration);
        }
        else
        {
            // Over the cap right after landing: the excess bleeds off over the capture
            // ease instead of being cut the moment the body touches the next slide.
            _rigidbody.linearVelocity = vel * Mathf.Lerp(1f, _maxSlideSpeed / speed, AssistWeight);
        }
    }

    // Rotates the velocity component pointing OUT along the normal back into the
    // surface plane, keeping the magnitude — the kick becomes ride speed, not a hop.
    static bool FlattenOutward(ref Vector3 vel, Vector3 normal)
    {
        float outward = Vector3.Dot(vel, normal);
        if (outward <= 1e-3f)
            return false;

        Vector3 flat = vel - normal * outward;
        if (flat.sqrMagnitude <= 1e-4f)
            return false;

        vel = flat.normalized * vel.magnitude;
        return true;
    }

    // 0 → 1 over _captureEaseTime after a fresh landing: every path-assist force is
    // scaled by this, so a new slide takes hold gradually instead of magnetizing.
    float AssistWeight => _captureEaseTime > 1e-4f ? Mathf.Clamp01(_assistRamp / _captureEaseTime) : 1f;

    // Contact-only ride assist: rotates the velocity along the course and damps the
    // side-to-side wobble. It never PULLS the body onto the slide — containment is the
    // collider's job, and once contact is lost the flight is fully ballistic, so speed
    // can genuinely carry the player over a wall or off a crest.
    void GuideAlongSlide(WaterSlideGenerator slide, Vector3 center, Vector3 tangent, Vector3 guideUp, float halfWidth)
    {
        Vector3 vel = _rigidbody.linearVelocity;

        // Tangent oriented along the current motion. A cross-path landing (a hop that
        // came back down mid-curve) makes the velocity nearly PERPENDICULAR to the
        // path — the sign of its dot with the tangent is then pure noise, and a wrong
        // flip steered the velocity BACKWARD along the slide and spun the body ~180°
        // (read as "turned crosswise on landing"). When the along-path component is
        // too small to trust, the body forward picks the direction instead.
        Vector3 reference = vel.sqrMagnitude > 0.25f ? vel.normalized : transform.forward;
        float along = Vector3.Dot(tangent, reference);
        if (Mathf.Abs(along) < 0.35f)
            along = Vector3.Dot(tangent, transform.forward);
        // Fully crosswise landing: BOTH the velocity and the body forward (yaw is
        // anchored to the flight heading, i.e. also crosswise) are useless for the sign —
        // prefer the DOWNHILL end of the path, so the auto-turn never sends the body
        // riding backward up the slide.
        if (Mathf.Abs(along) < 0.35f)
            along = -tangent.y;
        if (along < 0f)
            tangent = -tangent;

        // Time-smooth the guide direction: per-segment tangents are piecewise, and every
        // joint would otherwise kick the velocity steering (visible as twitchy turns).
        if (!_hasSmoothedGuideTangent || Vector3.Dot(_smoothedGuideTangent, tangent) < 0f)
        {
            _smoothedGuideTangent = tangent;
            _hasSmoothedGuideTangent = true;
        }
        else
        {
            float tk = 1f - Mathf.Exp(-_guideTangentSmoothing * Time.fixedDeltaTime);
            _smoothedGuideTangent = Vector3.Slerp(_smoothedGuideTangent, tangent, tk).normalized;
        }

        tangent = _smoothedGuideTangent;
        _guideTangent = tangent;
        _hasGuide = true;

        // Everything below fades in with the capture ramp: a freshly landed body keeps
        // its arrival trajectory and the slide takes hold gradually.
        float assist = AssistWeight;

        // ...but the ramp must not zero the CATCH. A body coming back down onto the
        // slide mid-curve arrives with real cross-path velocity, and with the ramp at
        // zero nothing bent the motion along the channel — it crossed the flume,
        // climbed the far wall and left. The steering that catches (velocity steer,
        // cross-flow damping) keeps a floor; only the cosmetic pulls (centering, the
        // speed cut) still ramp from zero, so landings stay free of the magnet feel.
        float catchAssist = Mathf.Lerp(0.4f, 1f, assist);

        // While the player steers with A/D the assist FULLY releases the lateral axis:
        // the velocity steer and the cross-flow dampers otherwise eat the sideways speed
        // faster than the steer force can build it, and the input feels dead. Full
        // release also means corners stop auto-following at full steer — built-up
        // inertia can genuinely carry the body up and over the flume wall.
        float steerFree = 1f - Mathf.Clamp01(Mathf.Abs(_slideSteerInput));

        // Rotate the velocity towards the path direction (keeps the speed): in corners the
        // required centripetal acceleration exceeds what push forces can provide, so the
        // direction itself must follow the slide.
        //
        // GRIP MODEL (open flume): the turn rate is capped by lateral acceleration, not
        // by a fixed rad/s — a fixed rate held ANY corner at ANY speed (7 rad/s vs the
        // 3.2 needed for R=5 m at 16 м/с), so speed never mattered and flyouts felt
        // random (they came only from touching A/D or from the landing ramp). With the
        // cap each corner has a real max safe speed √(grip·R): above it the assist can't
        // bend the velocity fast enough, the body visibly drifts up the outer wall and
        // inertia carries it over — the player can SEE that braking is needed.
        // Steering input keeps a fraction of grip instead of zeroing it: a binary
        // release made any A/D touch in a corner an instant, speed-independent flyout.
        // WITHOUT input the assist also gets only a FRACTION of the grip
        // (_autoGripFraction): the course generator widens every corner to fit the FULL
        // grip at the projected speed, so at full auto-grip the ride self-steered every
        // corner and flyouts never happened hands-off. A fast corner must now be HELD —
        // steer into it (the steer force supplies the missing centripetal push) or brake.
        // A closed tube keeps the old fixed rate — the bore contains the body anyway,
        // and grip-starving the steer there only makes it bang around the walls.
        float speed = vel.magnitude;

        // CROSS-LANDING CATCH. A body that came back down ACROSS the channel (a jump
        // that landed mid-curve, a throw over a corner) arrives with the velocity nearly
        // perpendicular to the path. The grip-capped steer below turns ~1.2 rad/s at ride
        // speed — a 90° turn takes seconds, and the body crossed the flume and climbed
        // the far wall long before that. So on the FIRST guided step after a real flight
        // the arrival angle is measured IN THE CHANNEL PLANE (the fall component must not
        // count as "crosswise"); a crosswise arrival opens a short window in which the
        // velocity is rotated onto the path at its own fast rate — bypassing the grip cap,
        // the capture ramp and the steer input — while the magnitude is fully preserved:
        // the speed is turned, never cut.
        if (_crossLandingCheckPending)
        {
            _crossLandingCheckPending = false;
            Vector3 flatVel = Vector3.ProjectOnPlane(vel, guideUp);
            if (_crossLandingAngle > 0f && flatVel.sqrMagnitude > 1f
                && Vector3.Angle(flatVel, tangent) > _crossLandingAngle)
                _crossLandingTimer = _crossLandingMaxTime;

            // TOUCHDOWN SNAP: the velocity vector is set along the path THE MOMENT the
            // body lands — the full magnitude (fall speed included) becomes ride speed,
            // nothing is lost crossing the flume or slamming the far wall. The tangent
            // sign was already resolved above (downhill on a fully crosswise arrival).
            // The BODY still turns over the catch window below — a model can't snap
            // its orientation in one frame without a visible pop.
            // The ride direction is the path tangent LAID INTO the contact surface plane:
            // the raw tangent can point slightly off the local facet, and a snapped
            // velocity with an off-surface component reads as a bounce at touchdown.
            if (_landingAlignVelocity && speed > 1f)
            {
                // A big-height fall arrives far above the ride cap; transferring the
                // whole magnitude into ride speed threw the body off the first convex
                // bend (the surface drops away faster than gravity can bend a 30+ m/s
                // trajectory) — read as a bounce. The excess dies in the splash.
                if (_landingSnapSpeedCap > 0f)
                    speed = Mathf.Min(speed, Mathf.Max(_landingSnapSpeedCap, _maxSlideSpeed));
                Vector3 rideDir = Vector3.ProjectOnPlane(tangent, _slideNormalTarget);
                rideDir = rideDir.sqrMagnitude > 1e-4f ? rideDir.normalized : tangent;
                // A hard, fast impact often lands on a triangle EDGE and the reported
                // contact normal is junk (tilted far off the surface); projecting into
                // that plane aimed the snapped velocity ABOVE the slide — an instant
                // launch. A normal facet only bends the tangent a few degrees, so a
                // large bend means a junk normal: ride along the raw tangent instead.
                if (Vector3.Angle(rideDir, tangent) > 25f)
                    rideDir = tangent;
                vel = rideDir * speed;
            }
        }
        bool crossCatch = _crossLandingTimer > 0f;

        if (speed > 1f)
        {
            float maxRadians;
            if (crossCatch)
            {
                _crossLandingTimer -= Time.fixedDeltaTime;
                maxRadians = _crossLandingTurnRate * Time.fixedDeltaTime;
                // Both the velocity AND the body lie along the path — the job is done,
                // hand back to the normal ride assist early. (With the touchdown snap
                // the velocity is aligned from step one; the window then exists for the
                // body turn, so the body check keeps it open.)
                Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, guideUp);
                if (Vector3.Angle(vel, tangent) < 8f
                    && (flatForward.sqrMagnitude < 1e-4f || Vector3.Angle(flatForward, tangent) < 10f))
                    _crossLandingTimer = 0f;
            }
            else
            {
                bool openFlume = slide.Enclosure == SlideEnclosure.Open;
                float steerRate = openFlume ? Mathf.Min(_velocitySteer, _corneringGrip / speed) : _velocitySteer;
                float inputScale = openFlume
                    ? Mathf.Lerp(_autoGripFraction, _steerGripFraction, Mathf.Clamp01(Mathf.Abs(_slideSteerInput)))
                    : steerFree;
                maxRadians = steerRate * catchAssist * inputScale * Time.fixedDeltaTime;
            }
            vel = Vector3.RotateTowards(vel / speed, tangent, maxRadians, 0f) * speed;
        }
        else
            _crossLandingTimer = 0f;

        Vector3 right = Vector3.Cross(guideUp, tangent);
        if (right.sqrMagnitude < 1e-6f)
        {
            _guideRight = Vector3.zero;
            _rigidbody.linearVelocity = vel;
            return;
        }
        right.Normalize();

        // Published for the A/D slide steer: the frame right stays level with the channel
        // floor no matter where on the cross-section the body currently is.
        _guideRight = right;
        _guideIsOpenFlume = slide.Enclosure == SlideEnclosure.Open;

        // Measure from the CAPSULE centre, not the rigidbody pivot: the capsule is fitted
        // to the animated bones and sits away from the pivot.
        Vector3 bodyCenter = GetBodyCenterWorld();
        Vector3 offset = bodyCenter - center;
        float lateral = Vector3.Dot(offset, right);
        float bodyRadius = GetLateralBodyExtent();
        float usableHalf = Mathf.Max(halfWidth - bodyRadius, 0.04f);
        float limit = usableHalf * _boundsLimit;

        if (slide.Enclosure == SlideEnclosure.Open)
        {
            // Rim landing whose contact still classified as INNER (the lip normal can
            // tip either way): same rescue as the collision-side catch — while the
            // landing window is open, a body over the wall band is put back inside the
            // channel instead of toppling off the outer side. The containment window
            // extends this: for _landingContainTime after ANY fresh landing the body
            // may not leave over a wall at all.
            if ((_landingNoBounceTimer > 0f || _landingContainTimer > 0f)
                && Mathf.Abs(lateral) > usableHalf + bodyRadius * 0.5f)
            {
                float side = Mathf.Sign(lateral);
                _rigidbody.position += right * (side * usableHalf - lateral);
                lateral = side * usableHalf;
                float outwardVel = Vector3.Dot(vel, right) * side;
                if (outwardVel > 0f)
                    vel -= right * (side * outwardVel);
            }

            // OVER-GRIP LAUNCH — the flyout mechanic. The flume is barely wider than
            // the body and its walls are near-vertical: a body over the corner speed
            // limit does NOT climb out — the wall guides it around like a rail
            // (unlimited centripetal force from the rigid contact), so no amount of
            // grip starving can ever produce a flyout by honest drift. The release is
            // therefore explicit: while the IN-PLANE turn rate of the path demands
            // more centripetal acceleration than the grip provides (steer input
            // towards the corner adds the steer force, away from it subtracts), the
            // wall is doing the holding; after _wallLaunchDelay seconds of that the
            // channel lets go — the body is popped along the wall just hard enough to
            // clear the lip (computed from the effective gravity) and its own inertia
            // carries it over the OUTER side of the corner.
            if (_wallLaunchDelay > 0f && !crossCatch
                && _landingNoBounceTimer <= 0f && _landingContainTimer <= 0f
                && assist >= 0.9f && speed > 6f
                && Time.fixedTime - _prevGuideTangentTime < Time.fixedDeltaTime * 1.9f)
            {
                // Turn rate around the channel up only: loops and crests bend the
                // tangent in the VERTICAL plane and must not read as cornering.
                Vector3 prevFlat = Vector3.ProjectOnPlane(_prevGuideTangent, guideUp);
                Vector3 curFlat = Vector3.ProjectOnPlane(tangent, guideUp);
                if (prevFlat.sqrMagnitude > 0.1f && curFlat.sqrMagnitude > 0.1f)
                {
                    float omega = Vector3.Angle(prevFlat, curFlat) * Mathf.Deg2Rad / Time.fixedDeltaTime;
                    float needed = speed * omega;
                    float gripScale = Mathf.Lerp(_autoGripFraction, _steerGripFraction,
                        Mathf.Clamp01(Mathf.Abs(_slideSteerInput)));
                    float inwardSign = Mathf.Sign(Vector3.Dot(curFlat - prevFlat, right));
                    float available = _corneringGrip * gripScale
                        + _slideSteerInput * inwardSign * _slideSteerForce;
                    _overGripTimer = needed > available + 0.5f
                        ? _overGripTimer + Time.fixedDeltaTime
                        : Mathf.Max(0f, _overGripTimer - 4f * Time.fixedDeltaTime);
                    // FIRE GATES — the trigger (force balance) stays as is, but the pop
                    // itself may only fire when it can COMPLETE as a flyout; a pop that
                    // cannot depart just tosses the body up and drops it back into the
                    // channel — read exactly as "the side wall kicks me for nothing".
                    // (1) The body must actually BE on the outer wall: the "wall holds
                    // like a rail" premise is false mid-channel, where the body is still
                    // honestly drifting outward and needs no release.
                    // (2) The corner must KEEP turning ahead: the escape is ballistic
                    // (the path curves away under the airborne body), so a launch at the
                    // corner exit has nothing to carry it out. The timer keeps running
                    // while a gate blocks, so the pop fires the moment both hold.
                    bool onOuterWall = lateral * inwardSign < -usableHalf * 0.45f;
                    if (_overGripTimer >= _wallLaunchDelay
                        && Time.fixedTime - _lastWallLaunchTime > 1.2f
                        && onOuterWall
                        && CornerCarriesOutAhead(slide, guideUp, curFlat, inwardSign, speed))
                    {
                        _overGripTimer = 0f;
                        _lastWallLaunchTime = Time.fixedTime;
                        // PURE vertical pop, no outward push: the body is pressed
                        // against the OUTER wall, so an outward component points INTO
                        // the wall — the contact ate it instantly while keeping the
                        // contact (and the stick press) alive, and the launch stalled
                        // below the lip (visible as jerking up the wall and back).
                        // Vertically the escape is free, and the outward part comes by
                        // itself: the path keeps curving away while the airborne body
                        // flies straight, so it departs over the lip ballistically.
                        // CLEARANCE MARGIN — this is the "clean flyout or hold, never a
                        // stalled push" guarantee. The pop must lift the body clear ABOVE
                        // the lip and keep it there long enough for the curving path to
                        // carry it out; a pop that only just reaches the lip is pulled
                        // straight back by gravity before it departs — read exactly as
                        // "the edge shoves me up and back". The old +0.35 m / ×1.1 / 0.3 s
                        // margins were tuned at gravity ×1.8; at ×2 the ballistic return
                        // is faster, so the same numbers under-clear and the launch
                        // stalls. Overshoot the lip with room to spare (GravityMultiplier
                        // already scales the energy, so this stays correct at any weight).
                        float heightInFrame = Vector3.Dot(offset, guideUp);
                        float rise = Mathf.Max(slide.FlumeLipHeight + 0.6f - heightInFrame, 0.5f);
                        float upSpeed = Mathf.Sqrt(2f * Physics.gravity.magnitude * GravityMultiplier * rise) * 1.2f;
                        vel += guideUp * upSpeed;
                        // A launch is a real takeoff: suppress the guidance like a
                        // deliberate jump so the following steps don't steer and damp
                        // the body straight back into the channel (the suppression
                        // also mutes the surface stick press — see FixedUpdate).
                        // Held longer than the old 0.3 s: under gravity ×2 the body needs
                        // more airtime to clear the lip before the guide can recapture it
                        // and yank it back inside (the "and back" half of the stall).
                        _guideSuppressTimer = Mathf.Max(_guideSuppressTimer, 0.45f);
                    }
                }
            }

            // Soft feel-assists only: damp the side-to-side oscillation and drift back
            // towards the centreline while inside the channel. The walls hold the body
            // geometrically, and enough speed legitimately climbs and clears them.
            // Muted during the cross-landing catch: the damper would EAT the crosswise
            // speed (magnitude loss), while the catch must TURN it along the path intact.
            if (!crossCatch)
            {
                float lateralVel = Vector3.Dot(vel, right);
                _rigidbody.AddForce(right * (-lateralVel * _lateralDamping * catchAssist * steerFree), ForceMode.Acceleration);
                // The centreline pull must not act as EXTRA grip: in a corner the grip
                // model has already lost, the pull (up to _centeringAccel at the band
                // edge) quietly supplied the missing centripetal force and held corners
                // the reduced auto-grip was meant to lose. Detected via the steer lag:
                // while the steer keeps up, the velocity trails the path by ~1° per
                // step; a saturated steer lets the lag grow, so fade the pull out over
                // 4°→8° of lag and let the drift carry the body up the outer wall.
                float driftFree = 1f - Mathf.Clamp01((Vector3.Angle(vel, tangent) - 4f) / 4f);
                if (_centeringAccel > 0f && Mathf.Abs(lateral) < limit)
                    _rigidbody.AddForce(right * (-lateral * _centeringAccel * assist * steerFree * driftFree), ForceMode.Acceleration);
            }

            // Published every guided open-flume step: the turn-rate probe for the
            // over-grip launch above, and the rim plane for the camera (a point at
            // lip-top level, plane normal = channel up) so it can proactively stay
            // above the channel rim on banked corners.
            _prevGuideTangent = tangent;
            _prevGuideTangentTime = Time.fixedTime;
            _rimPlanePoint = center + guideUp * slide.FlumeLipHeight;
            _rimPlaneUp = guideUp;
            _hasRimPlane = true;
        }
        else
        {
            _hasRimPlane = false;

            // Closed tube: the mesh collider owns containment. Only damp the pendulum
            // swing around the ring so the body doesn't oscillate wall-to-wall forever;
            // the forward speed is untouched.
            float height = Vector3.Dot(offset, guideUp);
            Vector2 q = new(lateral, height);
            float qLen = q.magnitude;
            if (qLen > 1e-4f)
            {
                Vector3 radial = right * (q.x / qLen) + guideUp * (q.y / qLen);

                // ESCAPE NET, not a magnet: a CONTACTING body whose centre is deeper
                // into the tube wall than half a body radius is impossible in normal
                // riding (the solver keeps penetration to centimetres) — it is mid-
                // squeeze through the shell (a corner crash pressed it in until
                // depenetration was about to pop it out the FAR side). Put it back
                // against the inner surface and strip the outward speed. Runs only
                // under slide contact, so a flyer near the tube mouth (no contact,
                // no guide forces) can never be teleported in by this.
                float escapeRadius = halfWidth - bodyRadius * 0.5f;
                if (qLen > escapeRadius)
                {
                    Vector3 targetCenter = center + radial * Mathf.Max(halfWidth - bodyRadius, 0.05f);
                    _rigidbody.position += targetCenter - bodyCenter;
                    float outward = Vector3.Dot(vel, radial);
                    if (outward > 0f)
                        vel -= radial * outward;
                }

                // The pendulum damper is muted during the cross-landing catch for the
                // same reason as the open-flume dampers: turn the speed, don't eat it.
                Vector3 around = Vector3.Cross(tangent, radial);
                if (!crossCatch && around.sqrMagnitude > 1e-6f)
                {
                    around.Normalize();
                    _rigidbody.AddForce(around * (-Vector3.Dot(vel, around) * _lateralDamping * assist * steerFree), ForceMode.Acceleration);
                }
            }
        }

        // Speed cap while riding — uncapped speed is what loses corners (and makes the
        // CCD sweeps against the tube meshes expensive). Ramped like the rest so a fast
        // arrival is not clipped on first touch.
        float guidedSpeed = vel.magnitude;
        if (guidedSpeed > _maxSlideSpeed)
            vel *= Mathf.Lerp(1f, _maxSlideSpeed / guidedSpeed, assist);

        _rigidbody.linearVelocity = vel;
    }

    // Whether the corner KEEPS turning the same way far enough ahead for a wall
    // launch to depart. The pop is purely vertical: the outward escape only comes
    // from the path curving away under the airborne body, so a pop fired near the
    // corner EXIT (the path straightens right after) just hops up and drops back
    // into the channel — read as the side wall kicking the player for nothing.
    // Walk the ride frames over the distance the first half-second of flight covers
    // (that is what carries the body over the lip) and require the path to keep
    // turning the SAME way by enough to open a body-wide gap (~12°: at ride speed
    // the lateral gap s·θ/2 is then well past the capsule radius).
    bool CornerCarriesOutAhead(WaterSlideGenerator slide, Vector3 guideUp, Vector3 curFlat, float inwardSign, float speed)
    {
        var frames = slide.RideFrames;
        if (frames == null || frames.Count < 2)
            return false;

        int i = Mathf.Clamp(_lastGuideIndex, 0, frames.Count - 1);
        // Frames are ordered along the path; the tangent may be riding them in
        // reverse (the sign flip in GuideAlongSlide), so walk in the riding direction.
        float dirDot = Vector3.Dot(_smoothedGuideTangent, frames[i].Forward);
        int dir = dirDot >= 0f ? 1 : -1;

        float lookahead = Mathf.Max(speed * 0.45f, 4f);
        float travelled = 0f;
        int j = i;
        while (j + dir >= 0 && j + dir < frames.Count && travelled < lookahead)
        {
            travelled += Vector3.Distance(frames[j].Position, frames[j + dir].Position);
            j += dir;
        }

        Vector3 aheadFlat = Vector3.ProjectOnPlane(frames[j].Forward * dir, guideUp);
        if (aheadFlat.sqrMagnitude < 0.01f)
            return false;

        // Positive SignedAngle around guideUp bends the tangent toward the frame
        // right — the same convention inwardSign was measured with, so the product
        // is positive only when the path ahead keeps turning INTO the same corner.
        float turnAhead = Vector3.SignedAngle(curFlat, aheadFlat, guideUp);
        return turnAhead * inwardSign >= 12f;
    }

    WaterSlideGenerator ResolveGuideSlide()
    {
        // While the current slide still yields a valid guide frame, never re-pick another
        // one: switching mid-ride (e.g. another tube passing near a corner) moves the
        // containment to the OTHER tube, and escaping the current one becomes "legal".
        // Tracked search: the windowed lookup around the last index is ~20x cheaper than
        // the full-path scan, and this runs every physics step.
        if (_guideWasActive
            && _currentSlide != null
            && TryGetGuideFrameOnSlide(_currentSlide, out _, out _, out _, out _, out _))
            return _currentSlide;

        if (_course != null && _course.Slides.Count > 0)
        {
            if (TryPickGuideSlide(_course.Slides, out var best))
                return best;
            return null;
        }

        return _currentSlide;
    }

    bool TryPickGuideSlide(IReadOnlyList<WaterSlideGenerator> slides, out WaterSlideGenerator best)
    {
        best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < slides.Count; i++)
        {
            var slide = slides[i];
            if (slide == null)
                continue;

            if (!TryGetGuideFrameOnSlide(slide, out _, out _, out _, out float halfWidth, out float pathDist, trackGuideIndex: false))
                continue;

            float score = pathDist + halfWidth * 0.15f;

            // Sticky choice: switching the guide mid-ride resets the smoothed tangent and
            // shows up as a kick, so the current slide keeps a preference margin.
            if (slide == _currentSlide)
                score *= 0.7f;

            if (score >= bestDist)
                continue;

            bestDist = score;
            best = slide;
        }

        return best != null;
    }

    bool TryGetGuideFrameOnSlide(
        WaterSlideGenerator slide,
        out Vector3 center,
        out Vector3 tangent,
        out Vector3 up,
        out float halfWidth) =>
        TryGetGuideFrameOnSlide(slide, out center, out tangent, out up, out halfWidth, out _);

    bool TryGetGuideFrameOnSlide(
        WaterSlideGenerator slide,
        out Vector3 center,
        out Vector3 tangent,
        out Vector3 up,
        out float halfWidth,
        out float pathDistance,
        bool trackGuideIndex = true)
    {
        center = default;
        tangent = default;
        up = Vector3.up;
        halfWidth = 0f;
        pathDistance = float.MaxValue;

        if (slide == null)
            return false;

        var frames = slide.RideFrames;
        if (frames == null || frames.Count < 2)
            return false;

        // Capture/release distances are measured from the capsule centre so they agree
        // with the containment geometry in GuideAlongSlide.
        Vector3 pos = GetBodyCenterWorld();

        // Search around the last known index; the rider moves continuously along the path.
        int start = 0, end = frames.Count - 1;
        if (trackGuideIndex && slide == _currentSlide && _lastGuideIndex >= 0 && _lastGuideIndex < frames.Count)
        {
            // ±40 frames ≈ ±5 m at the current ring spacing — the same physical window
            // the old ±15 covered before the rings were densified.
            start = Mathf.Max(0, _lastGuideIndex - 40);
            end = Mathf.Min(frames.Count - 1, _lastGuideIndex + 40);
        }

        int best = FindNearestFrame(frames, pos, start, end);
        float bodyRadius = GetLateralBodyExtent();

        // Fall back to a full search only when the windowed result is genuinely lost (not
        // just at the window edge): on self-intersecting paths (loops) the global nearest
        // frame can belong to the OTHER branch, and jumping there flips the containment.
        if ((best == start || best == end)
            && Vector3.Distance(frames[best].Position, pos) > slide.RideHalfWidth + bodyRadius + 0.6f)
            best = FindNearestFrame(frames, pos, 0, frames.Count - 1);

        if (trackGuideIndex)
            _lastGuideIndex = best;

        var frame = frames[best];

        // Hysteresis: capture close to the centreline, but once guided release much
        // farther out — otherwise one hard bump past the capture radius turned the
        // containment off entirely and the body sailed away from the tube.
        float reach = Mathf.Max(_guideCaptureDistance, 0.5f);
        if (_guideWasActive && slide == _currentSlide)
            reach = Mathf.Max(reach, _guideReleaseDistance);

        float maxGuideDistance = slide.RideHalfWidth + bodyRadius + reach;
        pathDistance = Vector3.Distance(frame.Position, pos);
        if (pathDistance > maxGuideDistance)
            return false;

        // Past the end (or before the entrance) the rider is on a legitimate flight to the
        // next slide — guidance must release him, not drag him back.
        Vector3 endTangent = frame.Forward.sqrMagnitude > 1e-6f ? frame.Forward.normalized : transform.forward;
        float along = Vector3.Dot(pos - frame.Position, endTangent);
        if (best == frames.Count - 1 && along > 0.5f)
            return false;
        if (best == 0 && along < -0.5f)
            return false;

        // Project onto the neighbouring segment so the centre doesn't hop between samples.
        int next = Mathf.Min(best + 1, frames.Count - 1);
        int prev = Mathf.Max(best - 1, 0);
        Vector3 segStart = frames[prev].Position;
        Vector3 segEnd = frames[next].Position;
        Vector3 seg = segEnd - segStart;
        float t = seg.sqrMagnitude > 1e-6f
            ? Mathf.Clamp01(Vector3.Dot(pos - segStart, seg) / seg.sqrMagnitude)
            : 0f;

        center = segStart + seg * t;

        // Interpolate the frame across the segment as well: taking tangent/up from the
        // nearest sample makes them step every ~0.3 m, and each step kicks the steering.
        Vector3 tanBlend = Vector3.Slerp(frames[prev].Forward, frames[next].Forward, t);
        tangent = tanBlend.sqrMagnitude > 1e-6f
            ? tanBlend.normalized
            : (frame.Forward.sqrMagnitude > 1e-6f ? frame.Forward.normalized : transform.forward);
        Vector3 upBlend = Vector3.Slerp(frames[prev].Up, frames[next].Up, t);
        up = upBlend.sqrMagnitude > 1e-6f ? upBlend.normalized : frame.Up;
        halfWidth = slide.RideHalfWidth;
        pathDistance = Vector3.Distance(center, pos);

        // An open flume may not CAPTURE a body outside its channel. Under the floor the
        // air recovery pulled the player up against the slide's belly; sideways past the
        // walls (below the wall top) the containment pinned him to the OUTER wall — both
        // dragged him along the course. An ACTIVE guide keeps wider margins so escape
        // recovery near the boundary still works; the initial drop is exempt entirely.
        if (slide.Enclosure == SlideEnclosure.Open && _hasSlideContactSinceSpawn)
        {
            bool activeGuide = _guideWasActive && slide == _currentSlide;
            Vector3 offsetFromCenter = pos - center;

            float heightInFrame = Vector3.Dot(offsetFromCenter, up);
            if (heightInFrame < -(slide.FlumeDepth + (activeGuide ? 0.45f : 0.25f)))
                return false;

            // Above the wall top the body may legitimately be descending INTO the
            // channel — only reject lateral positions below the lip level.
            Vector3 rightAxis = Vector3.Cross(up, tangent);
            if (rightAxis.sqrMagnitude > 1e-6f && heightInFrame < slide.FlumeLipHeight + 0.1f)
            {
                float lateral = Mathf.Abs(Vector3.Dot(offsetFromCenter, rightAxis.normalized));
                if (lateral > halfWidth + GetLateralBodyExtent() + (activeGuide ? 0.75f : 0.3f))
                    return false;
            }
        }

        // A closed tube may only CAPTURE a body that is inside its bore. Flights between
        // slides pass within metres of other tubes (and of the just-exited one), and a
        // distance-only capture glued the body onto the tube EXTERIOR. An already active
        // guide is exempt (escape recovery must keep working), as is the initial drop.
        if (slide.Enclosure != SlideEnclosure.Open
            && _hasSlideContactSinceSpawn
            && !(_guideWasActive && slide == _currentSlide))
        {
            Vector3 radialOffset = pos - center;
            radialOffset -= tangent * Vector3.Dot(radialOffset, tangent);
            if (radialOffset.magnitude > halfWidth)
            {
                // Exception — the MOUTH of the tube: an arriving jump legitimately closes
                // in from outside the bore. Only within the first few rings, only close,
                // only moving along the tube, and only while still AT the entrance plane —
                // a body already alongside the first metres of the tube is flying past it,
                // and capturing it glued the player to the tube exterior.
                Vector3 vel = _rigidbody.linearVelocity;
                Vector3 entryForward = frames[0].Forward.sqrMagnitude > 1e-6f
                    ? frames[0].Forward.normalized
                    : tangent;
                float alongFromMouth = Vector3.Dot(pos - frames[0].Position, entryForward);
                bool arrivingAtMouth = best <= 30
                    && alongFromMouth < 1f
                    && radialOffset.magnitude <= halfWidth + 1f
                    && vel.sqrMagnitude > 1f
                    && Vector3.Dot(vel.normalized, tangent) > 0.35f;
                if (!arrivingAtMouth)
                    return false;
            }
        }

        return true;
    }

    float GetLateralBodyExtent()
    {
        if (_capsule == null)
            return 0.35f;

        return _capsule.radius + 0.04f;
    }

    // World position of the capsule centre — the point the containment must reason about.
    // The rigidbody pivot sits at the rig root (usually the feet), away from the collider.
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

    void AlignBodyToContacts()
    {
        // Kinematic during the spawn window — velocity writes would warn.
        if (_rigidbody == null || _rigidbody.isKinematic)
            return;

        // A grace after losing contact: a hop over a seam must not flip the body
        // into the air alignment — the orientation stays along the slide so the body
        // settles straight back. 0.6 s, not shorter: with A/D held the surface press
        // is muted and the wall-side hops regularly last 0.3–0.5 s; falling into the
        // air branch there yawed the body to the flight heading — the DIAGONAL strafe
        // velocity — so steering read as the character turning off the tube. A
        // deliberate jump suppresses guidance and skips the grace, so the flight
        // pose engages immediately; a real flight flips over once the grace runs out.
        const float RidingOrientationGrace = 0.6f;
        bool ridingGrace = _hasSlideContactSinceSpawn
            && _guideSuppressTimer <= 0f
            && _timeSinceSlideContact < RidingOrientationGrace;
        if (IsOnSlide || _hasGuide || ridingGrace)
        {
            Vector3 normal = _slideNormal;

            // Prefer the path tangent: it is smooth along the whole slide, while velocity
            // direction wobbles with every contact and makes the body twitch. The LAST
            // smoothed tangent also covers the steps where the guide is momentarily gone
            // (micro contact loss on seams — frequent with A/D held, because the input
            // mutes the surface press): falling back to the velocity there yawed the
            // body towards the DIAGONAL strafe motion on every flicker — read as "the
            // character turns across the slide whenever I steer". The body must always
            // lie ALONG the slide while riding; the steer forces are lateral-only and
            // remain untouched by this.
            bool freshTangent = _hasSmoothedGuideTangent && _timeSinceSlideContact < RidingOrientationGrace;
            Vector3 forward = _hasGuide || freshTangent
                ? Vector3.ProjectOnPlane(_smoothedGuideTangent, normal)
                : Vector3.ProjectOnPlane(_rigidbody.linearVelocity, normal);
            if (forward.sqrMagnitude < 0.25f)
                forward = Vector3.ProjectOnPlane(transform.forward, normal);
            if (forward.sqrMagnitude < 1e-6f)
            {
                _rigidbody.angularVelocity = Vector3.zero;
                return;
            }

            forward.Normalize();
            var target = Quaternion.LookRotation(forward, normal);

            // Ramped with the capture ease (floored so the body still settles): a full-
            // speed re-align on first touch reads as the slide snapping the body around.
            // During the cross-landing catch the ramp is bypassed — the body must swing
            // along the slide together with the fast velocity turn, not seconds later.
            float align = _slideAlignSpeed * (_crossLandingTimer > 0f
                ? 1f
                : Mathf.Lerp(0.35f, 1f, AssistWeight));
            SteerRotationTowards(Quaternion.Slerp(_rigidbody.rotation, target, align * Time.fixedDeltaTime));
            return;
        }

        // Initial drop onto the first slide: keep the spawn orientation (nose along the
        // slide) so the body lands flat instead of pitching into the near-vertical velocity.
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

        // The pitch is CLAMPED: following the full 3D velocity pointed the body nose-first
        // down the fall arc — any strafe or jump read as the player keeling over forward.
        Vector3 airForward = ClampPitch(airVelocity.normalized, Mathf.Min(_maxAirPitch, 80f));
        float pitch = -Mathf.Asin(Mathf.Clamp(airForward.y, -1f, 1f)) * Mathf.Rad2Deg;

        // The yaw is anchored to the FLIGHT HEADING frozen at launch, NEVER to the
        // velocity. Chasing the velocity (even only between strafes) meant every strafe
        // permanently turned the flight: the bent velocity became the new "forward",
        // the body and the camera swung after it, and A/D read as a turn instead of a
        // sideways shift. The heading is constant per flight, so the view stays put.
        // Roll is pinned to zero by construction.
        float yaw = _rigidbody.rotation.eulerAngles.y;
        if (_flightHeading.sqrMagnitude > 1e-4f)
        {
            float headingYaw = Mathf.Atan2(_flightHeading.x, _flightHeading.z) * Mathf.Rad2Deg;
            yaw = Mathf.MoveTowardsAngle(yaw, headingYaw, 90f * Time.fixedDeltaTime);
        }

        var airTarget = Quaternion.Euler(pitch, yaw, 0f);
        SteerRotationTowards(Quaternion.Slerp(_rigidbody.rotation, airTarget, _slideAlignSpeed * 0.5f * Time.fixedDeltaTime));
    }

    // The body rotation is driven by SETTING the angular velocity, never by
    // MoveRotation: on a dynamic interpolated rigidbody MoveRotation is a teleport —
    // the rotation stepped 50 times/s past the render interpolation, and the model
    // visibly twitched whenever the target orientation changed fast (riding a
    // changing slope, the pitch chasing the fall arc). Velocity-driven rotation is
    // integrated by the simulation, so the render interpolates it like any physical
    // motion. The value is overwritten every step, so control stays as absolute as
    // the teleport was, and spin the contacts add within a step is discarded.
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

    // Direction with its vertical angle limited to ±maxPitchDegrees; the horizontal
    // heading is preserved.
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
        if (_collisionRelay != null)
            return;

        NotifyCollisionExit(collision);
    }

    public void NotifyCollisionEnter(Collision collision)
    {
        var slide = GetSlideFrom(collision.collider);
        if (slide == null)
            return;

        if (!IsInnerSurfaceContact(slide, collision))
        {
            TryCatchRimLanding(slide);
            return;
        }

        _slideContacts.Add(collision.collider);
        _hasSlideContactSinceSpawn = true;
        TrackCurrentSlide(slide);
        UpdateSlideNormal(collision);
        _controller?.OnSlideContact();
    }

    public void NotifyCollisionStay(Collision collision)
    {
        var slide = GetSlideFrom(collision.collider);
        if (slide == null)
            return;

        // Re-evaluate the side every step: a touch can migrate between the inner and
        // the outer face of the wall (e.g. grazing the mouth rim).
        if (!IsInnerSurfaceContact(slide, collision))
        {
            _slideContacts.Remove(collision.collider);
            TryCatchRimLanding(slide);
            return;
        }

        if (_slideContacts.Add(collision.collider))
        {
            _hasSlideContactSinceSpawn = true;
            _controller?.OnSlideContact();
        }

        TrackCurrentSlide(slide);
        UpdateSlideNormal(collision);
    }

    public void NotifyCollisionExit(Collision collision)
    {
        if (GetSlideFrom(collision.collider) == null)
            return;

        _slideContacts.Remove(collision.collider);
    }

    // LANDING ON THE WALL LIP. A touchdown on the rim of an open flume reports a
    // contact whose normal points up along the lip, not towards the channel centre —
    // IsInnerSurfaceContact files it as exterior, so it never counts as a slide
    // contact: no guidance, no touchdown snap, and the body simply toppled off the
    // outer side and left the course. After a real flight such a touch IS a landing
    // the player expects to be caught: the body is moved inside the channel (against
    // the wall it hit) and the outward lateral speed is stripped; the floor contact a
    // moment later runs the normal touchdown snap that lays the velocity along the
    // slide. Deliberate jumps (guidance suppressed) and short seam hops are exempt,
    // so riding over the wall on built-up inertia still ejects as designed.
    void TryCatchRimLanding(WaterSlideGenerator slide)
    {
        // Normally only a touch after a REAL flight is a rim landing (seam hops must
        // not trigger the rescue) — but inside the post-landing containment window
        // every rim touch is caught: the body may not leave the channel at all there,
        // including via a short hop onto the lip right after touchdown.
        if (_rigidbody == null
            || slide.Enclosure != SlideEnclosure.Open
            || (_timeSinceSlideContact <= 0.3f && _landingContainTimer <= 0f)
            || _guideSuppressTimer > 0f)
            return;

        if (!TryGetGuideFrameOnSlide(slide, out Vector3 center, out Vector3 tangent, out Vector3 up, out float halfWidth))
            return;

        Vector3 right = Vector3.Cross(up, tangent);
        if (right.sqrMagnitude < 1e-6f)
            return;
        right.Normalize();

        Vector3 offset = GetBodyCenterWorld() - center;
        float lateral = Vector3.Dot(offset, right);
        float height = Vector3.Dot(offset, up);
        float bodyRadius = GetLateralBodyExtent();
        float usableHalf = Mathf.Max(halfWidth - bodyRadius, 0.04f);

        // Only a body over the wall band and around lip height is a rim landing:
        // inside the channel there is nothing to rescue, far past the wall or low on
        // the outer face the flight is genuinely outside the course.
        if (Mathf.Abs(lateral) <= usableHalf
            || Mathf.Abs(lateral) > halfWidth + bodyRadius * 2f
            || height < slide.FlumeLipHeight * 0.5f)
            return;

        float side = Mathf.Sign(lateral);
        _rigidbody.position += right * (side * usableHalf - lateral);

        Vector3 vel = _rigidbody.linearVelocity;
        float outward = Vector3.Dot(vel, right) * side;
        if (outward > 0f)
            _rigidbody.linearVelocity = vel - right * (side * outward);
    }

    // A body pressed against the OUTSIDE of a slide must not ride it: counting that
    // touch as a slide contact turned on the stick and downhill forces, which glued the
    // player to the exterior and carried him along the course. The collider is a SINGLE
    // inner-shell mesh, so contact POSITIONS are identical from both sides (a tube-
    // radius test passed exterior touches!) — only the contact normal tells the sides
    // apart: it points from the surface towards the body, and a valid inner contact
    // therefore points towards the slide interior (tube axis / flume channel centre).
    bool IsInnerSurfaceContact(WaterSlideGenerator slide, Collision collision)
    {
        if (collision.contactCount == 0)
            return true;

        var frames = slide.RideFrames;
        if (frames == null || frames.Count == 0)
            return true;

        var contact = collision.GetContact(0);
        var frame = frames[FindNearestFrame(frames, contact.point, 0, frames.Count - 1)];
        return Vector3.Dot(contact.normal, frame.Position - contact.point) > 0f;
    }

    void TrackCurrentSlide(WaterSlideGenerator slide)
    {
        if (_currentSlide == slide)
            return;

        _currentSlide = slide;
        _lastGuideIndex = -1;
        _hasSmoothedGuideTangent = false;
    }

    public void ResetSlideContacts()
    {
        _slideContacts.Clear();
    }

    // Two ride feels, switchable from the component context menu (⋮). "Loose" lets the
    // body swing wall-to-wall like a real water slide; "Rail" keeps it glued to the path.
    [ContextMenu("Ride Preset — Loose (болтание)")]
    public void ApplyLooseRidePreset()
    {
        _velocitySteer = 4f;
        _boundsLimit = 0.88f;
        _lateralDamping = 0.5f;
        _centeringAccel = 4f;
        MarkDirtyInEditor();
    }

    [ContextMenu("Ride Preset — Rail (по рельсам)")]
    public void ApplyRailRidePreset()
    {
        _velocitySteer = 9f;
        _boundsLimit = 0.95f;
        _lateralDamping = 3f;
        _centeringAccel = 14f;
        MarkDirtyInEditor();
    }

    void MarkDirtyInEditor()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    // A/D while riding: the guide assist releases the lateral axis for that time so the
    // steer force in PlayerController actually moves the body across the flow.
    public void SetSlideSteerInput(float axis)
    {
        _slideSteerInput = axis;
    }

    // W/S state, exposed for feedback effects (the speed-rush blur keys off it).
    public float ThrottleInput { get; private set; }

    public void SetThrottleInput(float axis)
    {
        ThrottleInput = axis;
    }

    // Intentional jumps must win against the rail assist for a moment.
    public void SuppressGuidance()
    {
        _guideSuppressTimer = _jumpGuideSuppression;
        _hasGuide = false;
        _hasSmoothedGuideTangent = false;
    }

    // Lets the spawner pre-assign the slide so guidance works during the initial drop,
    // before the first collision has been registered.
    public void SetCourse(WaterSlideCourse course)
    {
        _course = course;

        // The speed cap adapts to the course: a jump tuned longer in the Inspector
        // (JumpGap and friends) automatically raises the reachable top speed, so every
        // generated jump stays physically makeable. Never lowers the configured cap.
        if (course != null)
        {
            // No gravity scaling here: the course places jump landings ballistically for
            // AssumedExitSpeed under THIS player's actual gravity (multiplier included),
            // so RequiredJumpSpeed is already in real-flight terms.
            float required = course.RequiredJumpSpeed;
            if (required > _maxSlideSpeed)
            {
                Debug.Log($"PlayerView: max slide speed raised {_maxSlideSpeed:F1} → {required:F1} m/s to match the course jump settings.");
                _maxSlideSpeed = required;
            }
        }
    }

    public void SetGuideSlide(WaterSlideGenerator slide)
    {
        if (slide != null)
            TrackCurrentSlide(slide);
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

            // The spawn teleport happens while the body is still kinematic (BeginPlay
            // un-freezes it later) — kinematic bodies reject velocity writes with a warning.
            if (!_rigidbody.isKinematic)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
        }

        Physics.SyncTransforms();
        _slideContacts.Clear();
        _hasSlideContactSinceSpawn = false;
        _flightHeading = Vector3.ProjectOnPlane(forward, Vector3.up).sqrMagnitude > 1e-4f
            ? Vector3.ProjectOnPlane(forward, Vector3.up).normalized
            : Vector3.forward;
        _slideNormal = Vector3.up;
        _slideNormalTarget = Vector3.up;
        _lastGuideIndex = -1;
        _hasGuide = false;
        _guideWasActive = false;
        _hasSmoothedGuideTangent = false;
        _timeSinceSlideContact = 999f;
        _assistRamp = 0f;
        _crossLandingCheckPending = false;
        _crossLandingTimer = 0f;
        _landingNoBounceTimer = 0f;
        _landingContainTimer = 0f;
        _overGripTimer = 0f;
        _prevGuideTangentTime = float.NegativeInfinity;
        _lastWallLaunchTime = float.NegativeInfinity;
        _hasRimPlane = false;
        _fitInitialized = false;
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
        // Sweep CCD, NOT speculative: speculative contacts are created a full per-step
        // travel distance (~0.4 m at speed) ahead of the surface — the body rode an
        // invisible cushion above the slide and lost real contact entirely.
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        // Rotation is NOT frozen: the body alignment drives it by setting the angular
        // velocity every step (see SteerRotationTowards) — sim-driven rotation renders
        // interpolated, while the former FreezeRotation + per-step MoveRotation combo
        // teleported the rotation 50 times/s past the interpolation and the model
        // twitched whenever the target orientation changed fast. Control stays
        // absolute: the angular velocity is overwritten every step, so contact-induced
        // spin never survives longer than one step.
        _rigidbody.constraints = RigidbodyConstraints.None;
        // Default cap is 7 rad/s — the cross-landing body turn legitimately needs ~30.
        _rigidbody.maxAngularVelocity = 50f;
        _rigidbody.useGravity = true;

        // Soften ring-seam hits: the slide collider is a faceted polyline, and in turns
        // the capsule meets each next facet at a small head-on angle. A capped
        // depenetration velocity turns the pop-out kick into a gentle push. 1.0, down
        // from 2.5: riding pressed against a wall, every facet delivered its pop-out
        // as a sideways kick off the wall — at 2.5 м/с per step it read as endlessly
        // bouncing off the walls. Overlaps now resolve over a few frames instead of
        // one; the contact offset and CCD keep them small to begin with.
        _rigidbody.maxDepenetrationVelocity = 1f;

        if (_capsule == null)
            return;

        // A larger contact offset makes PhysX pick up the next facet EARLY and resolve
        // the transition over several solver steps instead of one impulse — the standard
        // remedy for capsules snagging on interior edges of a mesh collider.
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

    void UpdateSlideNormal(Collision collision)
    {
        if (collision.contactCount == 0)
            return;

        Vector3 avg = Vector3.zero;
        for (int i = 0; i < collision.contactCount; i++)
            avg += collision.GetContact(i).normal;

        if (avg.sqrMagnitude < 1e-6f)
            return;

        // Only the target here — the actual normal is slerped towards it in FixedUpdate,
        // otherwise per-triangle normal changes shake the body.
        _slideNormalTarget = avg.normalized;
    }
}

public struct PlayerMovementSettings
{
    public float SlideTurnSpeed;
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
