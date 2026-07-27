using System.Collections.Generic;
using UnityEngine;

// Visual ragdoll: the root capsule keeps doing all the real flying/riding under player
// control, while the humanoid model goes limp — the bones get physics and the HEAD stays
// a KINEMATIC anchor driven along the capsule's pose, so the doll dangles off its head
// and is dragged along, flailing from the neck down. The capsule is FULLY isolated from
// the doll BY CONSTRUCTION: no joint references the capsule and the bones ignore the
// capsule/slide colliders, so nothing the doll does can ever affect the ride or camera.
// _alwaysRagdoll (default ON): limp for the whole run, ride included — on the slide the
// limbs simply trail and clip through the surface (a cosmetic cost). Turn it OFF for the
// old flight-only behaviour, where the animator rides and the ragdoll engages only in a
// real flight between slides.
// The bone bodies/colliders are built procedurally from the humanoid rig — no Ragdoll
// Wizard setup needed. Lives on the Player object; disable the component to turn it off.
public class PlayerFlightRagdoll : MonoBehaviour
{
    [Tooltip("Рэгдол в ПОЛЁТЕ: в настоящем полёте между горками кукла обмякает и болтается от шеи вниз. Выкл — в полёте продолжает играть анимация (аниматор). Можно переключать прямо в Play Mode: при выключении в воздухе кукла сразу возвращается к анимации. На режим «рэгдол всегда» не влияет.")]
    [SerializeField] bool _flightRagdoll = true;
    [Tooltip("Задержка после отрыва от горки, сек — короткие подскоки на швах и гребнях не включают ragdoll.")]
    [SerializeField] float _activationDelay = 0.25f;
    [Tooltip("Время возврата из ragdoll в анимацию после приземления, сек.")]
    [SerializeField] float _blendDuration = 0.25f;
    [Tooltip("Свобода суставов конечностей, градусы.")]
    [SerializeField, Range(10f, 120f)] float _limbSwingLimit = 80f;
    [Tooltip("Жёсткость возврата суставов к исходной позе. 0 — полностью свободная тряпка. Исходная поза — кадр единственной анимации Flying (руки в стороны), поэтому большая жёсткость заставляет руки торчать; держи её малой, чтобы конечности свисали под гравитацией.")]
    [SerializeField] float _poseSpring = 1.5f;
    [Tooltip("Случайный начальный взмах конечностей при переходе в ragdoll, рад/с.")]
    [SerializeField] float _activationFlail = 2.5f;
    [Tooltip("Рэгдол ВСЕГДА: кукла становится тряпкой с первого касания горки и остаётся ею постоянно, включая езду (не только в полёте). Капсула полностью изолирована от куклы (connectedMassScale пина), поэтому на геймплей это не влияет — цена чисто косметическая: на горке кукла висит от головы, конечности проходят сквозь поверхность (кости игнорируют коллайдеры горок, иначе ломается езда — проверено). Выкл — рэгдол только в настоящем полёте, на горке едет аниматор.")]
    [SerializeField] bool _alwaysRagdoll = false;
    [Tooltip("Режим «рэгдол всегда»: задержка обмякания после касания горки, сек. Обмякание в сам момент приземления передаёт костям скорость ПАДЕНИЯ, и кукла ныряет сквозь пол (кости не сталкиваются с горками) — выглядит как «провалился сквозь горку в начале». За эту задержку капсула гасит вертикальную скорость о поверхность, и кукла обмякает уже со скоростью езды вдоль горки.")]
    [SerializeField] float _alwaysRagdollSettleDelay = 0.35f;
    [Tooltip("Виртуальный пол куклы: максимальная скорость поджатия костей к поверхности горки, м/с. Кости не сталкиваются с мешем горки (настоящие контакты ломают езду — проверено), поэтому утонувшую кость выталкивает эта «виртуальная» опора: мягко, только вдоль нормали, скольжение вдоль горки полностью свободно. 0 — выключить (кукла свисает сквозь пол, как раньше).")]
    [SerializeField] float _floorMaxLift = 1.5f;
    [Tooltip("Жёсткость виртуального пола, 1/с: скорость подъёма = глубина погружения × жёсткость (не выше Floor Max Lift). Держи умеренной: мгновенная коррекция (вся глубина за один шаг физики) подбрасывала кость, та падала обратно, кукла входила в резонанс и раскачка выбрасывала капсулу вверх.")]
    [SerializeField] float _floorStiffness = 6f;

    class Part
    {
        public Transform Bone;
        public Rigidbody Body;
        public Collider Collider;
        public float Radius;
    }

    PlayerView _view;
    Rigidbody _rootBody;
    Collider _rootCollider;
    Animator _animator;

    readonly List<Part> _parts = new();
    Part _hipsPart;
    Part _headPart;
    // The head's weld pose relative to the capsule, captured at activation: the head is
    // a KINEMATIC anchor driven to this pose every step (see DriveHeadAnchor).
    Vector3 _headLocalPosition;
    Quaternion _headLocalRotation = Quaternion.identity;

    bool _built;
    bool _active;
    float _airTime;
    float _settleTime;

    // Virtual-floor probe geometry (see ApplyVirtualFloor) and reusable buffers.
    const float FloorProbeUp = 2f;
    const float FloorProbeDown = 2.5f;
    readonly RaycastHit[] _floorHits = new RaycastHit[8];
    readonly HashSet<Collider> _slideColliders = new();

    // Pose snapshot for the ragdoll → animation blend after landing.
    Transform[] _blendBones;
    Quaternion[] _blendRotations;
    Vector3[] _blendPositions;
    float _blendTimeLeft;

    void Start()
    {
        _view = GetComponent<PlayerView>();
        _rootBody = GetComponent<Rigidbody>();
        _rootCollider = GetComponent<CapsuleCollider>();
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();

        if (_view == null || _rootBody == null || _animator == null || !_animator.isHuman)
        {
            Debug.LogWarning("PlayerFlightRagdoll: нужен humanoid Animator и PlayerView с Rigidbody на корне — ragdoll отключён.", this);
            enabled = false;
            return;
        }

        // The ragdoll is NOT built here: see LateUpdate — at Start the model still stands
        // in its import T-pose, and joints created against it drive the limbs back to a T.
    }

    void FixedUpdate()
    {
        if (!_built || _view == null)
            return;

        // Initial drop onto the first slide (and any respawn reset) is not a flight.
        if (!_view.HasSlideContactSinceSpawn)
        {
            _airTime = 0f;
            _settleTime = 0f;
            if (_active)
                Deactivate();
            return;
        }

        // Always-ragdoll: go limp shortly after the first slide touch and stay that way
        // for the whole run, ride included. Safe now that the capsule is fully isolated
        // from the doll (kinematic head anchor — no joint references the capsule) and the
        // bones ignore the slide colliders — the old failure modes (bones popping on mesh
        // facets, doll dynamics disturbing the ride) can't happen.
        // NOT at the exact touchdown step, though: there the capsule still carries the
        // fall velocity, the bones inherit it, and — colliding with nothing — the whole
        // doll dived through the slide floor ("провалился сквозь горку в начале"). The
        // settle delay lets the contact kill the vertical speed first, so the doll goes
        // limp already moving ALONG the slide; the animator keeps riding meanwhile, which
        // also gives the capsule its one proper fit before the fit is frozen on activation.
        if (_alwaysRagdoll)
        {
            if (!_active)
            {
                if (_view.IsOnSlide)
                {
                    _airTime = 0f;
                    // Accumulated, not reset on seam hops: a bumpy entry must not defer
                    // the activation forever.
                    _settleTime += Time.fixedDeltaTime;
                    if (_settleTime >= _alwaysRagdollSettleDelay)
                        Activate();
                }
                else
                {
                    // Thrown into a real flight before the settle completed — limp in the
                    // air exactly like the flight-only mode would.
                    _airTime += Time.fixedDeltaTime;
                    if (_airTime >= _activationDelay)
                        Activate();
                }
            }
        }
        // Flight ragdoll switched off in the inspector: the animator keeps playing in the
        // air too. Checked every step, so unticking mid-flight releases a live doll at once.
        else if (!_flightRagdoll)
        {
            _airTime = 0f;
            if (_active)
                Deactivate();
        }
        // Flight-only mode. On the slide the animator rides: a permanent doll there once
        // fought the physics from both sides — with collisions the bones popped on every
        // mesh facet (constant bouncing), without them the limbs sank through the floor
        // and the pin/tether pair jittered. The delay filters out seam hops.
        else if (!_view.IsOnSlide)
        {
            _airTime += Time.fixedDeltaTime;
            if (!_active && _airTime >= _activationDelay)
                Activate();
        }
        else
        {
            _airTime = 0f;
            if (_active)
                Deactivate();
        }

        if (_active)
        {
            DriveHeadAnchor();
            ApplyVirtualFloor();
        }
    }

    // Virtual floor for the riding doll. The bones must not collide with the slide mesh
    // (a limb jamming into the surface while the capsule slides past over-constrains the
    // joint chain and wrecks the ride — long-standing lesson), yet a limp doll hanging
    // off the head weld sags THROUGH the floor, worst at the slow flat start ("провалился
    // сквозь горку"). Middle path: clamp only each bone's velocity along the surface
    // normal — lift a sunken bone out at a capped rate, never touching its tangential
    // motion. No solver contacts (nothing can jam), the capsule is never touched at all.
    //
    // The floor is sampled PER BONE with a ray onto the slide colliders, not taken from
    // one plane under the capsule: the doll trails 1-1.5 m behind, and on a descent the
    // surface back there is HIGHER than under the capsule — with a single plane the
    // trailing body still sank into the slope mid-ride ("плыл-плыл и провалился").
    // Ray direction is -SlideNormal; mesh colliders ignore back faces, so even in a
    // closed tube the ray passes the ceiling from outside and lands on the floor.
    void ApplyVirtualFloor()
    {
        if (_floorMaxLift <= 0f || !_view.IsOnSlide || _slideColliders.Count == 0)
            return;

        Vector3 normal = _view.SlideNormal;
        for (int i = 0; i < _parts.Count; i++)
        {
            var part = _parts[i];
            var body = part.Body;
            if (body.isKinematic)
                continue;

            // Start the probe well above the bone so a sunken bone is still below the
            // ray origin; a bone over a gap between slides finds no floor and stays free.
            Vector3 origin = body.position + normal * FloorProbeUp;
            int count = Physics.RaycastNonAlloc(origin, -normal, _floorHits,
                FloorProbeUp + FloorProbeDown, ~0, QueryTriggerInteraction.Ignore);

            float bestDistance = float.MaxValue;
            int best = -1;
            for (int h = 0; h < count; h++)
            {
                if (_floorHits[h].distance < bestDistance && _slideColliders.Contains(_floorHits[h].collider))
                {
                    bestDistance = _floorHits[h].distance;
                    best = h;
                }
            }
            if (best < 0)
                continue;

            Vector3 floorNormal = _floorHits[best].normal;
            float height = Vector3.Dot(body.position - _floorHits[best].point, floorNormal) - part.Radius;
            if (height >= 0f)
                continue;

            // SOFT spring-style recovery: lift speed proportional to the depth, capped.
            // The first version corrected the whole depth in a single physics step
            // (depth/dt): even a 5 cm sink meant a 2.5 m/s kick, the bone flew up, fell
            // back under the 1.8× gravity, got kicked again — the doll built up a
            // resonant swing and the accumulated momentum threw the CAPSULE upward
            // through the head weld ("раскачивается и вылетает вверх"). Depth × gain
            // injects at most a gentle nudge near the surface, so the bone settles
            // instead of bouncing; raising vn to a non-negative lift also kills the
            // downward impact without any elastic rebound.
            float lift = Mathf.Min(-height * _floorStiffness, _floorMaxLift);
            float alongNormal = Vector3.Dot(body.linearVelocity, floorNormal);
            if (alongNormal < lift)
                body.linearVelocity += floorNormal * (lift - alongNormal);
        }
    }

    void LateUpdate()
    {
        // Build only after the animator has posed the body at least once (the first
        // LateUpdate runs after the first animator evaluation). Building at Start used
        // the import T-pose as the joints' rest pose — both the angular limits and the
        // pose spring then pulled the flailing limbs back into a visible T mid-flight.
        // Built this way the rest pose is the riding pose.
        if (!_built)
        {
            BuildRagdoll();
            return;
        }

        if (_blendTimeLeft <= 0f || _blendBones == null)
            return;

        _blendTimeLeft -= Time.deltaTime;

        // The animator has already posed the bones this frame; pull them back towards the
        // snapshot taken at the moment the ragdoll released, fading the snapshot out.
        float t = _blendDuration > 1e-4f
            ? Mathf.Clamp01(1f - _blendTimeLeft / _blendDuration)
            : 1f;

        for (int i = 0; i < _blendBones.Length; i++)
        {
            var bone = _blendBones[i];
            if (bone == null)
                continue;

            bone.localRotation = Quaternion.Slerp(_blendRotations[i], bone.localRotation, t);
            bone.localPosition = Vector3.Lerp(_blendPositions[i], bone.localPosition, t);
        }
    }

    void OnDisable()
    {
        if (_active)
        {
            Deactivate();
            // LateUpdate no longer runs — drop the blend so the animator just takes over.
            _blendTimeLeft = 0f;
        }
    }

    void Activate()
    {
        _active = true;
        _blendTimeLeft = 0f;

        if (_animator != null)
            _animator.enabled = false;

        // The capsule is normally fitted to the animated bones; fitting it to a flailing
        // ragdoll would inflate it mid-flight and change the collision behaviour.
        _view.CapsuleFitSuspended = true;

        Vector3 velocity = _rootBody != null ? _rootBody.linearVelocity : Vector3.zero;
        // The flail exists for MID-AIR activation: it breaks the frozen riding pose so
        // the doll reads as limp instead of a stiff T. Activating ON the slide (the
        // always-ragdoll settle) it did the opposite — spun the limbs into the surface
        // the bones cannot collide with.
        bool flail = !_view.IsOnSlide;
        for (int i = 0; i < _parts.Count; i++)
        {
            var body = _parts[i].Body;
            // The HEAD stays KINEMATIC: it is the doll's anchor, driven to the capsule's
            // pose every step (DriveHeadAnchor). The neck joint hangs the dynamic chest
            // off this kinematic body, so the chain solver sees an immovable anchor and
            // NO joint references the capsule at all — the doll's влияние on the capsule
            // is exactly zero by construction, not merely damped. (The previous scheme —
            // a dynamic head welded to the capsule by a ConfigurableJoint with a big
            // connectedMassScale — kept a real solver coupling: resonant doll motion,
            // e.g. the early too-stiff virtual floor, leaked through it and could throw
            // the capsule upward.)
            if (_parts[i] == _headPart)
            {
                body.interpolation = RigidbodyInterpolation.Interpolate;
                continue;
            }

            body.isKinematic = false;
            // Interpolate only while the doll is LIVE. The capsule root (and the camera
            // following it) renders interpolated; bones stepped raw at the fixed rate
            // stutter against it by a full physics step of travel — at fall speeds that
            // is half a metre and the whole model visibly judders. While the animator
            // drives the bones (kinematic, on the slide) interpolation must stay off,
            // or it fights the animator's transform writes.
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.linearVelocity = velocity;
            // A light random flail so the doll starts moving instead of holding the
            // riding pose it was frozen in (that read as a stiff T mid-air).
            body.angularVelocity = flail
                ? Random.insideUnitSphere * _activationFlail
                : Vector3.zero;
        }

        // Weld pose of the head on the capsule — captured from the animated pose at the
        // moment of activation, so the head keeps riding exactly where the animator had it.
        Quaternion inverseRoot = Quaternion.Inverse(_rootBody.rotation);
        _headLocalPosition = inverseRoot * (_headPart.Body.position - _rootBody.position);
        _headLocalRotation = inverseRoot * _headPart.Body.rotation;

        // No hips tether: it existed for the abandoned permanent-ragdoll-on-slide mode
        // (without slide collisions the body sank through the floor there). In flight
        // there is no floor to sink through, and the sprung tether visibly held the doll
        // by the WAIST — the body must hang and trail from the head anchor alone.
    }

    // The kinematic head follows the capsule rigidly. MovePosition/MoveRotation (not a
    // transform write): the physics engine then gives the kinematic body the proper
    // implicit velocity, which the neck joint and the interpolation both need.
    void DriveHeadAnchor()
    {
        var body = _headPart.Body;
        body.MovePosition(_rootBody.position + _rootBody.rotation * _headLocalPosition);
        body.MoveRotation(_rootBody.rotation * _headLocalRotation);
    }

    // Equal-acceleration forwarding for the air control (strafe, strafe damping, extra
    // gravity). A force applied to the capsule alone reaches the doll only through the
    // head weld: the body pendulum-swings behind the invisible capsule and catches up
    // ~0.3-0.5 s later, so steering read as "pressed, but it responds late". An equal
    // acceleration on every live part (gravity-style) induces NO relative swing — the
    // doll shifts as one piece, while flail from everything else is untouched.
    public void AddAcceleration(Vector3 acceleration)
    {
        if (!_active)
            return;

        for (int i = 0; i < _parts.Count; i++)
        {
            var body = _parts[i].Body;
            if (!body.isKinematic)
                body.AddForce(acceleration, ForceMode.Acceleration);
        }
    }

    void Deactivate()
    {
        _active = false;

        for (int i = 0; i < _parts.Count; i++)
        {
            var body = _parts[i].Body;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
        }

        // Snapshot the ragdoll pose BEFORE the animator writes over it, then fade it out.
        TakeBlendSnapshot();
        _blendTimeLeft = _blendDuration;

        if (_animator != null)
            _animator.enabled = true;

        _view.CapsuleFitSuspended = false;
    }

    void TakeBlendSnapshot()
    {
        if (_blendBones == null)
        {
            _blendBones = _hipsPart.Bone.GetComponentsInChildren<Transform>(true);
            _blendRotations = new Quaternion[_blendBones.Length];
            _blendPositions = new Vector3[_blendBones.Length];
        }

        for (int i = 0; i < _blendBones.Length; i++)
        {
            var bone = _blendBones[i];
            if (bone == null)
                continue;

            _blendRotations[i] = bone.localRotation;
            _blendPositions[i] = bone.localPosition;
        }
    }

    void BuildRagdoll()
    {
        var hips = Bone(HumanBodyBones.Hips);
        var chest = Bone(HumanBodyBones.Chest);
        if (chest == null)
            chest = Bone(HumanBodyBones.Spine);
        var head = Bone(HumanBodyBones.Head);

        if (hips == null || chest == null || head == null)
        {
            Debug.LogWarning("PlayerFlightRagdoll: в риге не хватает костей (Hips/Spine/Head) — ragdoll отключён.", this);
            enabled = false;
            return;
        }

        // All collider sizes scale with the character: 0.75 m hips-to-head is a standard
        // adult humanoid.
        float s = Mathf.Clamp(Vector3.Distance(hips.position, head.position) / 0.75f, 0.3f, 3f);

        _hipsPart = CreatePart(hips, 1.2f);
        AddCapsule(_hipsPart, chest.position, 0.13f * s);

        var chestPart = CreatePart(chest, 1.2f);
        AddCapsule(chestPart, head.position, 0.12f * s);
        Connect(chestPart, _hipsPart, 25f);

        _headPart = CreatePart(head, 0.5f);
        AddHeadSphere(_headPart, chest, 0.11f * s);
        Connect(_headPart, chestPart, 40f);

        CreateLimb(chestPart, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.055f * s, 0.045f * s, 0.35f, 0.25f);
        CreateLimb(chestPart, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.055f * s, 0.045f * s, 0.35f, 0.25f);
        CreateLimb(_hipsPart, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.08f * s, 0.06f * s, 0.5f, 0.35f);
        CreateLimb(_hipsPart, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.08f * s, 0.06f * s, 0.5f, 0.35f);

        // Bones DO collide with each other (only adjacent connected pairs are ignored, in
        // Connect — they overlap at the joint by construction). Non-adjacent limb contacts
        // are kept so the doll flails naturally. None of it can reach the capsule: the
        // doll hangs off the KINEMATIC head anchor (see Activate/DriveHeadAnchor) and no
        // joint references the capsule, so bone dynamics never nudge its speed or path.

        // The bone colliders live inside the player capsule; contacts against it would
        // wreck the slide physics. Kinematic-vs-static pairs generate no contacts, so
        // while inactive the enabled colliders cost nothing — and keeping them enabled
        // means the rigidbodies get proper inertia tensors from them.
        if (_rootCollider != null)
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i].Collider != null)
                    Physics.IgnoreCollision(_parts[i].Collider, _rootCollider);
            }
        }

        // The doll must NOT collide with the slides. Tried the opposite (limbs resting on
        // the surface) assuming the head pin's huge connectedMassScale would isolate the
        // capsule — but in practice a limb jamming into the slide while the capsule slides
        // past over-constrains the joint chain (the hard head weld plus the pinned limb),
        // the solver blows up and the ride gets thrashed and stalls ("колбасит, не
        // скатывается"). This is the long-standing lesson: a doll sharing the riding
        // capsule's space can't also collide with the ride mesh. Riding the slide is the
        // CAPSULE's job; the doll is held near it by the head weld and its limbs clip
        // through the surface — a cosmetic cost paid for a stable ride.
        foreach (var slide in FindObjectsByType<WaterSlides.WaterSlideGenerator>(FindObjectsSortMode.None))
        {
            foreach (var slideCollider in slide.GetComponentsInChildren<Collider>(true))
            {
                // Remembered for the virtual floor's per-bone probes (ApplyVirtualFloor).
                _slideColliders.Add(slideCollider);
                for (int i = 0; i < _parts.Count; i++)
                {
                    if (_parts[i].Collider != null)
                        Physics.IgnoreCollision(_parts[i].Collider, slideCollider);
                }
            }
        }

        _built = true;
    }

    void CreateLimb(
        Part parent,
        HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones end,
        float upperRadius, float lowerRadius,
        float upperMass, float lowerMass)
    {
        var upperT = Bone(upper);
        var lowerT = Bone(lower);
        if (upperT == null || lowerT == null)
            return;

        var upperPart = CreatePart(upperT, upperMass);
        AddCapsule(upperPart, lowerT.position, upperRadius);
        Connect(upperPart, parent, _limbSwingLimit);

        var endT = Bone(end);
        if (endT == null)
            return;

        var lowerPart = CreatePart(lowerT, lowerMass);
        AddCapsule(lowerPart, endT.position, lowerRadius);
        Connect(lowerPart, upperPart, _limbSwingLimit);
    }

    Part CreatePart(Transform bone, float mass)
    {
        var body = bone.gameObject.AddComponent<Rigidbody>();
        body.mass = mass;
        body.isKinematic = true;
        body.useGravity = true;
        body.linearDamping = 0f;
        // Low angular damping: the limbs keep swinging with the flight instead of
        // freezing in the pose they left the slide with.
        body.angularDamping = 0.35f;
        // Off while kinematic (animator drives the bones); switched to Interpolate for
        // the duration of the live ragdoll in Activate()/Deactivate() — see there.
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;

        // High solver iterations. The doll is a chain (capsule → welded head → chest →
        // hips → limbs, ~5 kg) hung under gravity ×1.8; at the default 6 iterations the
        // solver leaves visible residual stretch every step, and the head-weld projection
        // then snaps it back discretely 50 times/s — read as the WHOLE doll juddering
        // against the smoothly-following camera. More iterations resolve the chain tightly
        // so the joints barely stretch and projection stays inert. Cost is trivial (a
        // handful of bodies) and only while the ragdoll is live (kinematic bones skip solving).
        body.solverIterations = 24;
        body.solverVelocityIterations = 12;

        // The ragdoll goes live at the exact moment of the FIRST slide touch, with the
        // animated pose (arms spread) partially sunk into the surface. Uncapped
        // depenetration then fires the overlapping limbs out at solver speed and the
        // whole doll kicks off the slide — the visible "bounce" on landing. Capped, the
        // pop-out becomes a gentle push over several steps (same remedy as the root
        // capsule's 2.5).
        body.maxDepenetrationVelocity = 1.5f;

        var part = new Part { Bone = bone, Body = body };
        _parts.Add(part);
        return part;
    }

    // Capsule from the bone pivot towards the next joint. CapsuleCollider only supports
    // axis-aligned directions, so the dominant local axis is used — the standard Ragdoll
    // Wizard approximation.
    void AddCapsule(Part part, Vector3 endWorld, float radius)
    {
        Vector3 endLocal = part.Bone.InverseTransformPoint(endWorld);
        float length = endLocal.magnitude;
        part.Radius = radius;
        if (length < 0.01f)
        {
            var sphere = part.Bone.gameObject.AddComponent<SphereCollider>();
            sphere.radius = radius;
            part.Collider = sphere;
            return;
        }

        int axis = 0;
        float ax = Mathf.Abs(endLocal.x), ay = Mathf.Abs(endLocal.y), az = Mathf.Abs(endLocal.z);
        if (ay >= ax && ay >= az)
            axis = 1;
        else if (az >= ax && az >= ay)
            axis = 2;

        var capsule = part.Bone.gameObject.AddComponent<CapsuleCollider>();
        capsule.direction = axis;
        capsule.radius = radius;
        capsule.height = Mathf.Max(length + radius, radius * 2f);
        capsule.center = endLocal * 0.5f;
        part.Collider = capsule;
    }

    void AddHeadSphere(Part part, Transform chest, float radius)
    {
        var sphere = part.Bone.gameObject.AddComponent<SphereCollider>();
        sphere.radius = radius;
        part.Radius = radius;

        // Push the centre up away from the neck so the sphere sits on the skull.
        Vector3 away = part.Bone.position - chest.position;
        if (away.sqrMagnitude > 1e-6f)
            sphere.center = part.Bone.InverseTransformPoint(part.Bone.position + away.normalized * (radius * 0.6f));

        part.Collider = sphere;
    }

    void Connect(Part child, Part parent, float swingDegrees)
    {
        CreateJoint(child.Body, parent.Body, swingDegrees);

        // Connected bones overlap at the joint; contacts there just add jitter.
        if (child.Collider != null && parent.Collider != null)
            Physics.IgnoreCollision(child.Collider, parent.Collider);
    }

    // Rig-agnostic ragdoll joint: position locked, all angular axes limited symmetrically
    // (bone local axes are unknown, so no per-axis anatomy), plus a soft slerp drive back
    // to the build pose that keeps the flailing from turning into a tangle.
    ConfigurableJoint CreateJoint(Rigidbody body, Rigidbody connected, float swingDegrees)
    {
        var joint = body.gameObject.AddComponent<ConfigurableJoint>();
        joint.connectedBody = connected;
        joint.xMotion = ConfigurableJointMotion.Locked;
        joint.yMotion = ConfigurableJointMotion.Locked;
        joint.zMotion = ConfigurableJointMotion.Locked;
        joint.angularXMotion = ConfigurableJointMotion.Limited;
        joint.angularYMotion = ConfigurableJointMotion.Limited;
        joint.angularZMotion = ConfigurableJointMotion.Limited;

        var limit = new SoftJointLimit { limit = swingDegrees };
        joint.highAngularXLimit = limit;
        joint.angularYLimit = limit;
        joint.angularZLimit = limit;
        limit.limit = -swingDegrees;
        joint.lowAngularXLimit = limit;

        joint.rotationDriveMode = RotationDriveMode.Slerp;
        joint.slerpDrive = new JointDrive
        {
            positionSpring = _poseSpring,
            positionDamper = Mathf.Max(_poseSpring * 0.1f, 1f),
            maximumForce = float.MaxValue
        };
        joint.enablePreprocessing = false;
        return joint;
    }

    Transform Bone(HumanBodyBones bone) => _animator.GetBoneTransform(bone);
}
