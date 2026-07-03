using System.Collections.Generic;
using UnityEngine;
using WaterSlides;

[DefaultExecutionOrder(-100)]
public class PlayerView : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Rigidbody _rigidbody;
    [SerializeField] CapsuleCollider _capsule;
    [SerializeField] PlayerCollisionRelay _collisionRelay;
    [SerializeField] Transform _visualRoot;
    [SerializeField] Animator _animator;

    [Header("Physics")]
    [SerializeField] PhysicsMaterial _physicsMaterial;
    [Tooltip("Подгонять капсулу под анимированную позу (по костям humanoid-рига: голова и стопы). Размер и центр задаются на CapsuleCollider.")]
    [SerializeField] bool _fitCapsuleToBody = true;
    [Tooltip("Запас капсулы за макушкой и стопами, метры.")]
    [SerializeField] float _capsuleFitPadding = 0.12f;
    [Tooltip("Скорость сглаживания подгонки капсулы (меньше — плавнее).")]
    [SerializeField] float _capsuleFitSmoothing = 5f;
    [SerializeField] float _mass = 10f;
    [SerializeField] float _gravityMultiplier = 1f;
    [SerializeField] float _slideAlignSpeed = 10f;
    [SerializeField] Vector3 _visualLocalOffset = Vector3.zero;
    [SerializeField] Vector3 _visualLocalEuler = Vector3.zero;

    [Header("Slide Ride")]
    [Tooltip("Дополнительный разгон вдоль уклона, м/с².")]
    [SerializeField] float _slideAcceleration = 6f;
    [Tooltip("Максимальная скорость на горке, м/с.")]
    [SerializeField] float _maxSlideSpeed = 16f;
    [Tooltip("Прижим к поверхности, м/с² — не даёт подлетать на стыках и гребнях.")]
    [SerializeField] float _stickAcceleration = 30f;
    [Tooltip("Гашение боковых колебаний поперёк жёлоба.")]
    [SerializeField] float _lateralDamping = 0.5f;
    [Tooltip("Допустимая доля полуширины жёлоба до включения возврата к центру (от доступной полосы с учётом радиуса тела).")]
    [SerializeField, Range(0.2f, 1f)] float _boundsLimit = 0.92f;
    [Tooltip("Ускорение возврата к осевой линии у границы жёлоба, м/с².")]
    [SerializeField] float _boundsPushAccel = 35f;
    [Tooltip("Постоянное притягивание к центру жёлоба, м/с².")]
    [SerializeField] float _centeringAccel = 10f;
    [Tooltip("Скорость сглаживания нормали поверхности (больше — быстрее реагирует).")]
    [SerializeField] float _normalSmoothing = 14f;
    [Tooltip("Дистанция от осевой линии, в пределах которой игрок ведётся вдоль горки даже без контакта.")]
    [SerializeField] float _guideCaptureDistance = 2.5f;
    [Tooltip("Скорость доворота вектора скорости к направлению горки, рад/с.")]
    [SerializeField] float _velocitySteer = 7f;
    [Tooltip("Притягивание обратно к жёлобу при подлёте (без контакта), м/с².")]
    [SerializeField] float _airRecoverAccel = 18f;
    [Tooltip("Пауза ведения после намеренного прыжка, сек.")]
    [SerializeField] float _jumpGuideSuppression = 0.6f;
    [Tooltip("Сглаживание направления ведения по пути (больше — быстрее следует за поворотами).")]
    [SerializeField] float _guideTangentSmoothing = 18f;
    [Tooltip("Доля выхода за границу, убираемая за один физический шаг (мягкое удержание без телепортов).")]
    [SerializeField, Range(0.05f, 1f)] float _containmentStiffness = 0.35f;
    [Tooltip("Максимальная скорость возврата внутрь жёлоба, м/с.")]
    [SerializeField] float _maxContainmentSpeed = 8f;
    [Tooltip("Глубина выхода за границу, после которой позиция жёстко подрезается, м.")]
    [SerializeField] float _hardSnapDepth = 0.25f;
    [Tooltip("Зазор до верхней грани борта открытого жёлоба, м.")]
    [SerializeField] float _wallTopMargin = 0.05f;

    [Header("Movement")]
    [SerializeField] float _slideTurnSpeed = 140f;
    [SerializeField] float _slideSteerForce = 22f;
    [SerializeField] float _slideJumpForce = 9f;
    [SerializeField] float _airStrafeAcceleration = 28f;
    [SerializeField] float _airJumpForce = 7f;
    [SerializeField] float _maxAirStrafeSpeed = 9f;

    PlayerController _controller;
    bool _spawnPrepared;
    bool _hasSlideContactSinceSpawn;

    int _slideContactCount;
    Vector3 _slideNormal = Vector3.up;
    Vector3 _slideNormalTarget = Vector3.up;

    WaterSlideGenerator _currentSlide;
    WaterSlideCourse _course;
    int _lastGuideIndex = -1;
    Vector3 _guideTangent;
    Vector3 _smoothedGuideTangent;
    bool _hasSmoothedGuideTangent;
    bool _hasGuide;
    float _guideLateralHeadroom = 1f;
    float _guideSuppressTimer;

    Vector3 _fitCenter;
    float _fitHeight;
    bool _fitInitialized;

    public Rigidbody Rigidbody => _rigidbody;
    public Transform MovementTransform => transform;
    public bool IsOnSlide => _slideContactCount > 0;
    public bool IsGuided => _hasGuide;
    public float SlideSteerScale => Mathf.Clamp(_guideLateralHeadroom, 0.15f, 1f);
    public Vector3 SlideNormal => _slideNormal;
    public float SpawnBodyClearance => _capsule != null ? _capsule.radius : 0.35f;

    public PlayerMovementSettings MovementSettings => new()
    {
        SlideTurnSpeed = _slideTurnSpeed,
        SlideSteerForce = _slideSteerForce,
        SlideJumpForce = _slideJumpForce,
        AirStrafeAcceleration = _airStrafeAcceleration,
        AirJumpForce = _airJumpForce,
        MaxAirStrafeSpeed = _maxAirStrafeSpeed
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

        _rigidbody.isKinematic = true;
        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        _slideContactCount = 0;
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

        if (_fitCapsuleToBody)
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
    }

    void ApplySlidePhysics()
    {
        _hasGuide = false;

        if (_rigidbody == null)
            return;

        // Guidance is deliberately NOT gated on contact: at speed the capsule skips off
        // seams/crests for a few frames, and if containment dropped with the contact the
        // body sailed over the flume walls. It engages whenever the body is within the
        // capture distance of the slide centreline.
        if (_guideSuppressTimer > 0f)
            _guideSuppressTimer -= Time.fixedDeltaTime;
        else
        {
            var guideSlide = ResolveGuideSlide();
            if (guideSlide != null && guideSlide != _currentSlide)
                TrackCurrentSlide(guideSlide);

            if (guideSlide != null
                && TryGetGuideFrameOnSlide(guideSlide, out Vector3 center, out Vector3 tangent, out Vector3 guideUp, out float halfWidth))
                GuideAlongSlide(guideSlide, center, tangent, guideUp, halfWidth);
        }

        if (!IsOnSlide)
            return;

        _rigidbody.WakeUp();

        // Smooth the contact normal: raw per-triangle normals of the MeshCollider change
        // every step and are the main source of jitter.
        float normalLerp = 1f - Mathf.Exp(-_normalSmoothing * Time.fixedDeltaTime);
        _slideNormal = Vector3.Slerp(_slideNormal, _slideNormalTarget, normalLerp).normalized;

        Vector3 normal = _slideNormal;
        Vector3 vel = _rigidbody.linearVelocity;

        // Remove the velocity component pushing into the surface (no micro-bounces on seams).
        Vector3 intoSurface = Vector3.Project(vel, normal);
        if (Vector3.Dot(intoSurface, normal) < 0f)
        {
            vel -= intoSurface;
            _rigidbody.linearVelocity = vel;
        }

        // Press the body onto the surface so crests and triangle seams don't launch it.
        _rigidbody.AddForce(-normal * _stickAcceleration, ForceMode.Acceleration);

        // Downhill push with a hard speed cap — uncapped acceleration is what threw the
        // body over the flume walls in corners.
        float speed = vel.magnitude;
        if (speed < _maxSlideSpeed)
        {
            Vector3 downhill = Vector3.ProjectOnPlane(Physics.gravity, normal);
            if (downhill.sqrMagnitude > 1e-4f)
                _rigidbody.AddForce(downhill.normalized * _slideAcceleration, ForceMode.Acceleration);
        }
        else
        {
            _rigidbody.linearVelocity = vel * (_maxSlideSpeed / speed);
        }
    }

    void GuideAlongSlide(WaterSlideGenerator slide, Vector3 center, Vector3 tangent, Vector3 guideUp, float halfWidth)
    {
        Vector3 vel = _rigidbody.linearVelocity;

        // Tangent oriented along the current motion.
        Vector3 reference = vel.sqrMagnitude > 0.25f ? vel : transform.forward;
        if (Vector3.Dot(tangent, reference) < 0f)
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

        // Rotate the velocity towards the path direction (keeps the speed): in corners the
        // required centripetal acceleration exceeds what push forces can provide, so the
        // direction itself must follow the slide.
        float speed = vel.magnitude;
        if (speed > 1f)
        {
            float maxRadians = _velocitySteer * (IsOnSlide ? 1f : 0.6f) * Time.fixedDeltaTime;
            Vector3 dir = Vector3.RotateTowards(vel / speed, tangent, maxRadians, 0f);
            vel = dir * speed;
        }

        Vector3 right = Vector3.Cross(guideUp, tangent);
        if (right.sqrMagnitude < 1e-6f)
        {
            _rigidbody.linearVelocity = vel;
            return;
        }
        right.Normalize();

        Vector3 offset = _rigidbody.position - center;
        float lateral = Vector3.Dot(offset, right);
        float height = Vector3.Dot(offset, guideUp);
        float lateralVel = Vector3.Dot(vel, right);
        float bodyRadius = GetLateralBodyExtent();
        float usableHalf = Mathf.Max(halfWidth - bodyRadius, 0.04f);
        float limit = usableHalf * _boundsLimit;
        _guideLateralHeadroom = limit > 1e-4f ? Mathf.Clamp01(1f - Mathf.Abs(lateral) / limit) : 0f;

        if (slide.Enclosure == SlideEnclosure.Open)
        {
            float side = Mathf.Sign(lateral);
            float lateralExcess = Mathf.Abs(lateral) - limit;
            if (lateralExcess > 0f)
            {
                ContainAcrossBoundary(right * side, lateralExcess, ref vel);
                _rigidbody.AddForce(right * (-side * _boundsPushAccel), ForceMode.Acceleration);
            }
            else
            {
                // Inside the bounds just damp the side-to-side oscillation.
                _rigidbody.AddForce(right * (-lateralVel * _lateralDamping), ForceMode.Acceleration);
                if (_centeringAccel > 0f)
                    _rigidbody.AddForce(right * (-lateral * _centeringAccel), ForceMode.Acceleration);
            }

            // Open flume: never above the top edge of the side wall while guided. Frame
            // origin sits at the profile y=0, the wall top at +FlumeLipHeight, so the cap
            // is a plain height limit; the floor keeps it meaningful for shallow lips.
            float maxHeight = slide.FlumeLipHeight - _wallTopMargin - bodyRadius * 0.35f;
            maxHeight = Mathf.Max(maxHeight, -slide.FlumeDepth + bodyRadius + 0.05f);
            float heightExcess = height - maxHeight;
            if (heightExcess > 0f)
                ContainAcrossBoundary(guideUp, heightExcess, ref vel);
        }
        else
        {
            // Closed tube: containment is radial in the ring plane (walls and ceiling in
            // one), with a small margin so resting on the floor never triggers it — this
            // is a safety net against tunneling through the collider at speed.
            Vector2 q = new(lateral, height);
            float qLen = q.magnitude;
            float radialExcess = qLen - (usableHalf + 0.03f);
            if (radialExcess > 0f && qLen > 1e-4f)
            {
                Vector3 outward = right * (q.x / qLen) + guideUp * (q.y / qLen);
                ContainAcrossBoundary(outward, radialExcess, ref vel);
                _rigidbody.AddForce(-outward * _boundsPushAccel, ForceMode.Acceleration);
            }
            else
            {
                _rigidbody.AddForce(right * (-lateralVel * _lateralDamping), ForceMode.Acceleration);
                if (_centeringAccel > 0f)
                    _rigidbody.AddForce(right * (-lateral * _centeringAccel), ForceMode.Acceleration);
            }
        }

        _rigidbody.linearVelocity = vel;

        // Briefly airborne over the channel (bump, crest, seam): pull back down into it
        // instead of letting the body fly off on a ballistic arc.
        if (!IsOnSlide)
        {
            Vector3 toChannel = center - _rigidbody.position;
            toChannel -= tangent * Vector3.Dot(toChannel, tangent);
            _rigidbody.AddForce(toChannel * _airRecoverAccel, ForceMode.Acceleration);
        }
    }

    // Velocity-level containment: the pull-back happens through velocity so rigidbody
    // interpolation stays valid — the old per-step position snap reset it and read as
    // jitter whenever the body pressed against a boundary in a corner. A hard position
    // cut remains only for deep escapes (late capture, tunneling at speed).
    void ContainAcrossBoundary(Vector3 outward, float excess, ref Vector3 vel)
    {
        float outVel = Vector3.Dot(vel, outward);

        // Already returning inside fast (spawn drop, re-entry after a jump) — let it come
        // back on its own; forcing it in reads as a slam.
        if (outVel < -1.5f)
            return;

        if (outVel > 0f)
            vel -= outward * outVel;

        float corrective = Mathf.Min(excess * _containmentStiffness / Time.fixedDeltaTime, _maxContainmentSpeed);
        vel -= outward * corrective;

        if (excess > _hardSnapDepth)
            _rigidbody.position -= outward * (excess - _hardSnapDepth);
    }

    WaterSlideGenerator ResolveGuideSlide()
    {
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
        Vector3 pos = _rigidbody.position;

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

    bool TryGetGuideFrame(
        out Vector3 center,
        out Vector3 tangent,
        out Vector3 up,
        out float halfWidth) =>
        TryGetGuideFrameOnSlide(_currentSlide, out center, out tangent, out up, out halfWidth);

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

        Vector3 pos = _rigidbody.position;

        // Search around the last known index; the rider moves continuously along the path.
        int start = 0, end = frames.Count - 1;
        if (trackGuideIndex && slide == _currentSlide && _lastGuideIndex >= 0 && _lastGuideIndex < frames.Count)
        {
            start = Mathf.Max(0, _lastGuideIndex - 15);
            end = Mathf.Min(frames.Count - 1, _lastGuideIndex + 15);
        }

        int best = FindNearestFrame(frames, pos, start, end);
        if (best == start || best == end)
            best = FindNearestFrame(frames, pos, 0, frames.Count - 1);

        if (trackGuideIndex)
            _lastGuideIndex = best;

        var frame = frames[best];
        float bodyRadius = GetLateralBodyExtent();
        float maxGuideDistance = slide.RideHalfWidth + bodyRadius + Mathf.Max(_guideCaptureDistance, 0.5f);
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
        return true;
    }

    float GetLateralBodyExtent()
    {
        if (_capsule == null)
            return 0.35f;

        return _capsule.radius + 0.04f;
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
        if (_rigidbody == null)
            return;

        if (IsOnSlide)
        {
            Vector3 normal = _slideNormal;

            // Prefer the path tangent: it is smooth along the whole slide, while velocity
            // direction wobbles with every contact and makes the body twitch.
            Vector3 forward = _hasGuide
                ? Vector3.ProjectOnPlane(_guideTangent, normal)
                : Vector3.ProjectOnPlane(_rigidbody.linearVelocity, normal);
            if (forward.sqrMagnitude < 0.25f)
                forward = Vector3.ProjectOnPlane(transform.forward, normal);
            if (forward.sqrMagnitude < 1e-6f)
                return;

            forward.Normalize();
            var target = Quaternion.LookRotation(forward, normal);
            _rigidbody.MoveRotation(Quaternion.Slerp(_rigidbody.rotation, target, _slideAlignSpeed * Time.fixedDeltaTime));
            return;
        }

        // Initial drop onto the first slide: keep the spawn orientation (nose along the
        // slide) so the body lands flat instead of pitching into the near-vertical velocity.
        if (!_hasSlideContactSinceSpawn)
            return;

        Vector3 airVelocity = _rigidbody.linearVelocity;
        if (airVelocity.sqrMagnitude < 0.25f)
            return;

        // Minimal-arc rotation towards the velocity: LookRotation(velocity, up) degenerates
        // when falling straight down and flips the roll; FromToRotation keeps it stable.
        Vector3 airForward = airVelocity.normalized;
        var airTarget = Quaternion.FromToRotation(transform.forward, airForward) * _rigidbody.rotation;
        _rigidbody.MoveRotation(Quaternion.Slerp(_rigidbody.rotation, airTarget, _slideAlignSpeed * 0.5f * Time.fixedDeltaTime));
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

        _slideContactCount++;
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

        TrackCurrentSlide(slide);
        UpdateSlideNormal(collision);
    }

    public void NotifyCollisionExit(Collision collision)
    {
        if (GetSlideFrom(collision.collider) == null)
            return;

        _slideContactCount = Mathf.Max(0, _slideContactCount - 1);
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
        _slideContactCount = 0;
    }

    // Two ride feels, switchable from the component context menu (⋮). "Loose" lets the
    // body swing wall-to-wall like a real water slide; "Rail" keeps it glued to the path.
    [ContextMenu("Ride Preset — Loose (болтание)")]
    public void ApplyLooseRidePreset()
    {
        _velocitySteer = 4f;
        _boundsLimit = 0.88f;
        _lateralDamping = 0.5f;
        _boundsPushAccel = 20f;
        _centeringAccel = 4f;
        MarkDirtyInEditor();
    }

    [ContextMenu("Ride Preset — Rail (по рельсам)")]
    public void ApplyRailRidePreset()
    {
        _velocitySteer = 9f;
        _boundsLimit = 0.95f;
        _lateralDamping = 3f;
        _boundsPushAccel = 40f;
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
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }

        Physics.SyncTransforms();
        _slideContactCount = 0;
        _hasSlideContactSinceSpawn = false;
        _slideNormal = Vector3.up;
        _slideNormalTarget = Vector3.up;
        _lastGuideIndex = -1;
        _hasGuide = false;
        _hasSmoothedGuideTangent = false;
        _guideLateralHeadroom = 1f;
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
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        _rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
        _rigidbody.useGravity = true;

        if (_capsule == null)
            return;

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
    public float SlideJumpForce;
    public float AirStrafeAcceleration;
    public float AirJumpForce;
    public float MaxAirStrafeSpeed;
}
