using UnityEngine;

[DefaultExecutionOrder(-50)]
public class PlayerCamera : MonoBehaviour
{
    [Tooltip("Цель слежения — трансформ игрока, за которым идёт камера. Если пусто, берётся MovementTransform из PlayerView (или родитель).")]
    [SerializeField] Transform _target;
    [Tooltip("Смещение камеры от игрока: назад по сглаженному повороту, вверх — по миру.")]
    [SerializeField] Vector3 _offset = new(0f, 0.6f, -5.5f);
    [Tooltip("Наклон камеры вниз, градусы. Меньше — камера ниже и смотрит на игрока площе (более тупой угол к вертикали), больше — выше и круче сверху. Смещение поворачивается вместе с наклоном, поэтому дистанция до игрока сохраняется.")]
    [SerializeField] float _pitch = 25f;
    [Tooltip("Сглаживание поворота камеры за игроком, сек (0 — без сглаживания).")]
    [SerializeField] float _yawSmoothTime = 0.12f;
    [Tooltip("Сглаживание позиции слежения на горке, сек — гасит физическую микротряску тела при контакте, не отставая от реального движения (0 — без сглаживания).")]
    [SerializeField] float _positionSmoothTime = 0.08f;
    [Tooltip("Сглаживание позиции слежения в ПОЛЁТЕ, сек. В воздухе тело чисто баллистическое (контактной микротряски нет), поэтому сильное сглаживание тут только копит отставание при разгоне падения и усиливает дрожание камеры за быстрой целью — держи малым. 0 — камера жёстко держит интерполированное тело.")]
    [SerializeField] float _flightPositionSmoothTime = 0.015f;

    [Header("Occlusion")]
    [Tooltip("Слои, коллайдеры которых заслоняют игрока: если между игроком и камерой есть такой коллайдер (обычно меш горки на спуске), камера подтягивается ближе к игроку. Триггеры и коллайдеры самого игрока игнорируются.")]
    [SerializeField] LayerMask _occlusionMask = ~0;
    [Tooltip("Радиус зазора при подтягивании, м — на каком удалении от преграды остановится камера. Держи заметно больше near clip камеры (0.12), чтобы у самой стены геометрия не прорезалась в кадр.")]
    [SerializeField] float _occlusionRadius = 0.3f;
    [Tooltip("Комфортный минимум дистанции подтягивания при заслонении, м. НЕ абсолютный: если преграда к игроку ближе этого значения (стена почти отвесного спуска прямо за спиной), жёсткая гарантия видимости всё равно поставит камеру ПЕРЕД преградой — вплотную к игроку, но не за мешем.")]
    [SerializeField] float _minOcclusionDistance = 1.2f;
    [Tooltip("Сколько секунд игрок должен быть НЕПРЕРЫВНО заслонён (по тонкому лучу к его центру), прежде чем камера начнёт подтягиваться. Скользящие касания кромок стенок жёлоба в поворотах длятся меньше и камеру не дёргают.")]
    [SerializeField] float _occlusionEngageDelay = 0.12f;
    [Tooltip("Сглаживание ПРИБЛИЖЕНИЯ камеры при подтверждённой преграде, сек.")]
    [SerializeField] float _occlusionZoomInTime = 0.12f;
    [Tooltip("Сглаживание ВОЗВРАТА камеры на полную дистанцию после исчезновения преграды, сек.")]
    [SerializeField] float _occlusionRecoverTime = 0.4f;
    [Tooltip("На сколько градусов максимум камера может ЗАДРАТЬ наклон сверх базового, когда игрок заслонён (крутой спуск): вместо ныряния за меш камера поднимается выше и смотрит на игрока под более прямым углом, не проходя сквозь горку. Суммарный наклон автоматически ограничен 89° (взгляд строго сверху), поэтому большое значение безопасно. 0 — отключить (останется только подтягивание по дистанции).")]
    [SerializeField] float _occlusionPitchMax = 65f;
    [Tooltip("Шаг перебора наклона при поиске незаслонённого направления, градусы. Меньше — точнее (камера задирается ровно насколько нужно), но больше лучей за кадр.")]
    [SerializeField] float _occlusionPitchStep = 5f;
    [Tooltip("Сглаживание ПОДЪЁМА наклона камеры при подтверждённой преграде, сек.")]
    [SerializeField] float _occlusionPitchRiseTime = 0.18f;
    [Tooltip("Сглаживание ВОЗВРАТА наклона к базовому после исчезновения преграды, сек.")]
    [SerializeField] float _occlusionPitchRecoverTime = 0.5f;
    [Tooltip("ПРОАКТИВНЫЙ подъём взгляда на спусках: камера задирает наклон так, чтобы смотреть на игрока сверху с этим запасом (в градусах) НАД крутизной спуска (крутизна берётся из вектора скорости игрока на горке, лучи заслонения не участвуют). Срабатывает только когда спуск круче, чем базовый наклон минус запас; суммарный наклон ограничен 89° и полем выше. Отрицательное значение — отключить.")]
    [SerializeField] float _slopePitchMargin = 10f;

    [Header("Speed Shake")]
    [Tooltip("Скорость игрока, с которой начинается дрожание камеры, м/с.")]
    [SerializeField] float _shakeSpeedThreshold = 9f;
    [Tooltip("Скорость, на которой дрожание достигает максимума, м/с.")]
    [SerializeField] float _shakeMaxSpeed = 15f;
    [Tooltip("Амплитуда дрожания на полной скорости, м. Дрожание идёт в плоскости экрана (перпендикулярно движению).")]
    [SerializeField] float _shakeAmplitude = 0.05f;
    [Tooltip("Частота дрожания, Гц.")]
    [SerializeField] float _shakeFrequency = 11f;

    Camera _camera;
    Rigidbody _targetBody;
    PlayerView _targetView;
    SpeedRushEffect _speedRush;
    float _shakeStrength;
    float _yaw;
    float _yawVelocity;
    bool _yawInitialized;
    Vector3 _followPoint;
    Vector3 _followVelocity;
    bool _followInitialized;
    float _activePositionSmoothTime;
    float _occlusionDistance;
    float _occlusionVelocity;
    bool _occlusionInitialized;
    float _occludedTime;
    float _extraPitch;
    float _extraPitchVelocity;
    float _blockedRun;
    float _hardBlockedRun;

    // Minimum CONSECUTIVE blocked time, s, before either occlusion response (the pitch
    // raise or the hard distance clamp) reacts. Touchdowns and mesh seams produce
    // single-frame ray blips; reacting to them instantly read as camera twitching on
    // every landing. ~3 frames of real blockage is still imperceptible as a delay.
    const float OcclusionConfirmTime = 0.05f;

    // The reverse (camera → player) ray stops this many metres SHORT of the player.
    // Surfaces this close to the player face him (channel interior), so the forward
    // ray covers the near zone by itself; the reverse ray only exists for far
    // back-facing crossings, and run to the very end it grazed the floor next to the
    // player on every flat stretch (constant false "blocked" dragging the camera in).
    const float ReverseRayNearExclusion = 1.5f;

    // All sight rays start from an anchor LIFTED off the slide surface, not from the
    // follow pivot itself. The pivot rides ON the surface, and rays started there
    // failed exactly on steep descents: with the slope steeper than the ray's
    // elevation the whole ray ran on the back side of the (one-sided) surface — the
    // entry crossing sat at distance ~0 (discarded) and no other front face was ever
    // crossed, so "camera behind the slide" produced zero hits. Lifting the start
    // along the slide normal gives every genuine blockage a real positive-distance
    // crossing. 0.9 м ≈ the body centre height above the pivot.
    const float SightAnchorLift = 0.9f;

    static readonly Collider[] OverlapBuffer = new Collider[8];

    // Height of the "under the slide roof" probe, m. In a corner the camera can hang in
    // FREE AIR just below the slide ribbon: the sight line to the player sneaks over the
    // flume wall lip (genuinely clear), the clearance sphere floats > _occlusionRadius
    // away from the mesh, and the slope-pitch term sees only a gentle descent — yet the
    // frame is full of back faces. The probe closes exactly this gap: any slide surface
    // within this height DIRECTLY ABOVE the camera means the camera is under the slide.
    // Kept short so riding UNDER a genuinely high crossing section does not trigger it;
    // a camera buried DEEPER than this under a mesh is caught by the thin sight rays
    // (the sight line crosses the surface) — the layers overlap.
    const float RoofProbeHeight = 2.5f;

    // True when slide geometry hangs over this position within RoofProbeHeight. Cast
    // TOP-DOWN: the one-sided colliders show their front face only to a descending ray
    // (ride surfaces face up); probing upward from below would see nothing. Inside a
    // closed tube both crossings of the shell are back faces for a downward ray, so the
    // probe stays silent there and the tube keeps its deliberate dive-inside behavior.
    bool UnderSlideRoof(Vector3 position, Transform playerRoot)
    {
        foreach (var hit in Physics.RaycastAll(position + Vector3.up * RoofProbeHeight,
                     Vector3.down, RoofProbeHeight, _occlusionMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance <= 0f)
                continue;
            if (playerRoot != null && hit.collider.transform.root == playerRoot)
                continue;
            return true;
        }
        return false;
    }

    // True when a clearance sphere at this position intersects world geometry (player's
    // own colliders excluded). A thin sight ray answers "is the player VISIBLE from
    // here", but not "is the camera body itself inside a wall" — riding along the flume
    // wall the sight line clears the lip while the camera hangs inside the outer wall.
    bool CameraSphereOverlaps(Vector3 position, Transform playerRoot)
    {
        int count = Physics.OverlapSphereNonAlloc(position, _occlusionRadius, OverlapBuffer,
            _occlusionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var collider = OverlapBuffer[i];
            if (playerRoot != null && collider.transform.root == playerRoot)
                continue;
            return true;
        }
        return false;
    }

    Vector3 GetSightAnchor()
    {
        // The slide normal is used WITHOUT an IsOnSlide gate: on a rough steep descent
        // the contact flickers (micro-hops), and falling back to world up in those
        // frames put the anchor 0.9·cos(slope) ≈ centimetres off a near-vertical
        // surface — the rays dove back under the slope and the whole occlusion
        // machinery blinked off. PlayerView keeps the last smoothed normal while
        // airborne, which is exactly right through micro-hops; during a long true
        // flight a stale lift direction is harmless (no surface hugs the player).
        Vector3 up = _targetView != null ? _targetView.SlideNormal : Vector3.up;
        if (up.sqrMagnitude < 0.25f)
            up = Vector3.up;
        return _followPoint + up * SightAnchorLift;
    }

    void Awake()
    {
        _camera = GetComponent<Camera>();
        if (_target == null)
        {
            var playerView = GetComponentInParent<PlayerView>();
            if (playerView != null)
                _target = playerView.MovementTransform;
            else if (transform.parent != null)
                _target = transform.parent;
        }

        ResolveTargetBody();

        // Effect components (SpeedRushEffect, FlightScreenDrops) live on the camera
        // object in the scene; nothing is added at runtime.
        if (TryGetComponent(out _speedRush))
            _speedRush.SetTargetBody(_targetBody);

        ClaimMainCamera();
    }

    void ResolveTargetBody()
    {
        _targetBody = _target != null ? _target.GetComponentInParent<Rigidbody>() : null;
        _targetView = _target != null ? _target.GetComponentInParent<PlayerView>() : null;
    }

    void Start()
    {
        BindSuimono();
    }

    void LateUpdate()
    {
        if (_target == null)
            return;

        // Always place the camera in WORLD space, even when it is a child of the player:
        // parenting with a local offset inherits the full body rotation, and once the body
        // lies along the slope the camera drops to spine level (behind, not above).

        // The body yaw carries every physics micro-correction; following it raw makes the
        // whole scene appear to twitch. The camera follows a smoothed yaw instead.
        //
        // Read the yaw from the HORIZONTAL projection of the body forward, not from
        // eulerAngles.y: with the body aligned to a near-vertical drop the forward
        // points almost straight down and the Euler yaw swings wildly (gimbal) — the
        // camera visibly wandered sideways exactly when the slide got steep. When the
        // projection is too short to trust, hold the current yaw instead of chasing it.
        Vector3 flatForward = Vector3.ProjectOnPlane(_target.forward, Vector3.up);
        float targetYaw = flatForward.sqrMagnitude > 0.0225f
            ? Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg
            : (_yawInitialized ? _yaw : _target.eulerAngles.y);
        if (!_yawInitialized || _yawSmoothTime <= 0f)
        {
            _yaw = targetYaw;
            _yawVelocity = 0f;
            _yawInitialized = true;
        }
        else
        {
            _yaw = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVelocity, _yawSmoothTime);
        }

        // Follow a SMOOTHED point, not the raw body position: every physics correction
        // (containment, depenetration, seam bumps) moves the body a few centimetres per
        // step, and a rigidly attached camera turns that into visible screen shake.
        //
        // But that smoothing is only needed WHILE RIDING. In free flight the body is
        // purely ballistic and moves perfectly smoothly (interpolated), so the same
        // 0.08 s only trails behind the accelerating fall and amplifies frame-time jitter
        // into visible judder. Ease the smooth-time down for flight and back up on
        // landing — easing the value itself (not switching it) lets SmoothDamp catch up
        // without a pop at take-off.
        float targetSmoothTime = (_targetView != null && !_targetView.IsOnSlide)
            ? _flightPositionSmoothTime
            : _positionSmoothTime;
        _activePositionSmoothTime = _followInitialized
            ? Mathf.MoveTowards(_activePositionSmoothTime, targetSmoothTime, 0.5f * Time.deltaTime)
            : targetSmoothTime;

        Vector3 targetPoint = _target.position;
        if (!_followInitialized || _activePositionSmoothTime <= 0f)
        {
            _followPoint = targetPoint;
            _followVelocity = Vector3.zero;
            _followInitialized = true;
        }
        else
        {
            _followPoint = Vector3.SmoothDamp(_followPoint, targetPoint, ref _followVelocity, _activePositionSmoothTime);
        }

        // On a steep descent the fixed-pitch offset lands on the far side of the slide
        // mesh. Before falling back to pulling the camera in, try STEEPENING the view:
        // a higher pitch lifts the camera above the slide and keeps the player in sight
        // at full distance (the offset rotates with the pitch, so distance is preserved).
        // Three sources drive the raise and the largest one wins: the PROACTIVE slope
        // term (steep descent → look down from above, no raycasts involved), the
        // PROACTIVE rim clamp (banked corners → stay above the tilted channel rim)
        // and the reactive occlusion search (whatever geometry actually hides the
        // player). The occlusion search owns the shared occlusion timer and must run
        // every frame regardless.
        float neededExtraPitch = Mathf.Max(ResolveSlopeExtraPitch(), ResolveOcclusionExtraPitch());
        neededExtraPitch = Mathf.Max(neededExtraPitch, ResolveRimExtraPitch());
        float pitchSmoothTime = neededExtraPitch > _extraPitch ? _occlusionPitchRiseTime : _occlusionPitchRecoverTime;
        _extraPitch = pitchSmoothTime <= 0f
            ? neededExtraPitch
            : Mathf.SmoothDamp(_extraPitch, neededExtraPitch, ref _extraPitchVelocity, pitchSmoothTime);

        var rotation = Quaternion.Euler(_pitch + _extraPitch, _yaw, 0f);
        Vector3 cameraOffset = rotation * _offset;
        float distance = ResolveOcclusionDistance(cameraOffset);
        float fullDistance = cameraOffset.magnitude;
        Vector3 position = fullDistance > 0.001f
            ? _followPoint + cameraOffset * (distance / fullDistance)
            : _followPoint;

        // HARD rim clamp on the POSITION, every frame. The pitch raise above is
        // SMOOTHED (rise ~0.18 s) while the channel frame in a corner banks faster —
        // in that window the camera transiently sinks below the rim plane; the
        // distance pull-in can also drag it down along the offset line after the
        // pitch check passed. Lift the final position along the rim normal instead:
        // the lift is a continuous function of the violation depth (starts at zero),
        // so there is no pop, and it fades out by itself as the smoothed pitch
        // catches up. Higher is always safer than behind the shell.
        if (_targetView != null && _targetView.TryGetFlumeRimPlane(out Vector3 rimPlanePoint, out Vector3 rimPlaneUp))
        {
            float rimHeight = Vector3.Dot(position - rimPlanePoint, rimPlaneUp);
            if (rimHeight < RimClearance)
                position += rimPlaneUp * (RimClearance - rimHeight);
        }

        transform.SetPositionAndRotation(position, rotation);
        ApplySpeedShake(rotation);
    }

    // On a downhill section the fixed behind-and-above offset can end up on the far
    // side of the slide mesh, leaving the player hidden behind it. Three stages with
    // different jobs:
    //  * a THIN ray decides IF the player's centre is hidden. The first version decided
    //    with the thick sphere, which brushes flume wall lips half a metre off the sight
    //    line on every curve — the camera lunged constantly ("летает непонятно как").
    //    A grazing touch that doesn't actually cover the player must not move the camera.
    //  * once the blockage has held for _occlusionEngageDelay, the pitch STEEPENS first
    //    (ResolveOcclusionExtraPitch) — above the slide the player is visible at full
    //    distance instead of the camera diving through/behind the mesh.
    //  * the SPHERE cast then picks the pulled-in distance along the actual offset for
    //    whatever blockage remains — its radius keeps the near plane off the wall.
    //
    // Thin-ray visibility test between the follow point and the camera end of
    // cameraOffset. Cast in BOTH directions: the slide colliders are ONE-SIDED (the
    // ride mesh is the inner surface only), so a surface crossed back-to-front is
    // invisible to the player→camera ray — the camera sat behind such a surface with
    // no hit at all. Every one-sided surface between the two points shows its front
    // face to exactly one of the two rays.
    bool SightBlocked(Vector3 cameraOffset)
    {
        Vector3 camEnd = _followPoint + cameraOffset;
        Vector3 anchor = GetSightAnchor();
        Vector3 sightVec = camEnd - anchor;
        float sightLen = sightVec.magnitude;
        if (sightLen <= 0.001f)
            return false;

        Vector3 sightDir = sightVec / sightLen;
        if (AnySightHit(anchor, sightDir, sightLen))
            return true;
        float reverseLength = sightLen - ReverseRayNearExclusion;
        return reverseLength > 0f
            && AnySightHit(camEnd, -sightDir, reverseLength);
    }

    // The player's own colliders (capsule, ragdoll bones) and the camera itself share
    // one hierarchy root — never treat them as walls; distance 0 means the collider
    // already overlaps the ray origin (slide floor under the riding player).
    bool AnySightHit(Vector3 origin, Vector3 direction, float maxDistance)
    {
        Transform playerRoot = _target != null ? _target.root : null;
        foreach (var hit in Physics.RaycastAll(origin, direction, maxDistance,
                     _occlusionMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance <= 0f)
                continue;
            if (playerRoot != null && hit.collider.transform.root == playerRoot)
                continue;
            return true;
        }
        return false;
    }

    // PROACTIVE pitch raise on descents — the primary mechanism for "look down at the
    // player on a steep slope". It works from the player's VELOCITY direction alone:
    // no raycasts, no one-sided-collider blind spots, no confirm windows, and — key —
    // NO contact gating: on a really steep drop the player spends half the time in
    // micro-hops next to the surface, and an IsOnSlide-gated version switched off
    // exactly where it was needed most. A steep ballistic fall gets the same top-down
    // view, which is what the descent visually is. _extraPitch smoothing (rise/recover
    // times) keeps the changes soft. The occlusion search below remains as a safety
    // net for geometry this term can't predict.
    float ResolveSlopeExtraPitch()
    {
        if (_slopePitchMargin < 0f || _targetBody == null)
            return 0f;

        Vector3 velocity = _targetBody.linearVelocity;
        if (velocity.magnitude < 3f)
            return 0f;

        float horizontal = new Vector2(velocity.x, velocity.z).magnitude;
        float slopeDown = Mathf.Atan2(-velocity.y, horizontal) * Mathf.Rad2Deg;
        if (slopeDown <= 0f)
            return 0f;

        float maxExtra = Mathf.Min(_occlusionPitchMax, 89f - _pitch);
        return Mathf.Clamp(slopeDown + _slopePitchMargin - _pitch, 0f, maxExtra);
    }

    // Minimum height of the camera above the flume rim plane, m. Below the rim the
    // frame fills with the back faces of the channel shell even when the thin sight
    // line to the player sneaks over the wall lip. 1.2, raised from 0.75: the plane
    // is sampled at the PLAYER's guide frame, and ~5.5 m behind him the actual rim of
    // the previous section can sit higher than the local plane (climbs, banked
    // transitions) — the extra margin covers that band too.
    const float RimClearance = 1.2f;

    // PROACTIVE rim clamp for corners. On a BANKED turn (bowl, helix) the channel
    // frame tilts while the camera offset stays world-oriented: swinging around the
    // outside of the corner the camera drops below the TILTED rim plane — under the
    // slide from the channel's point of view — with the sight line still clear over
    // the lip, the clearance sphere floating in free air and the slope term silent
    // (the descent itself is gentle). Pure geometry, no raycasts: find the smallest
    // extra pitch whose camera position stays RimClearance above the rim plane
    // published by PlayerView. On level ground the base position already satisfies
    // it and the term is 0.
    float ResolveRimExtraPitch()
    {
        if (_targetView == null || !_targetView.TryGetFlumeRimPlane(out Vector3 rimPoint, out Vector3 rimUp))
            return 0f;

        float maxExtra = Mathf.Min(_occlusionPitchMax, 89f - _pitch);
        if (maxExtra <= 0f)
            return 0f;

        for (float extra = 0f; extra < maxExtra; extra += 3f)
        {
            Vector3 pos = _followPoint + Quaternion.Euler(_pitch + extra, _yaw, 0f) * _offset;
            if (Vector3.Dot(pos - rimPoint, rimUp) >= RimClearance)
                return extra;
        }
        return maxExtra;
    }

    // When the base-pitch sight line has been blocked for _occlusionEngageDelay,
    // search for the smallest extra pitch that clears it. Returns 0 when the view is
    // clear, when steepening is disabled, or when NO pitch up to the max helps (a
    // closed tube blocks every direction — there the old distance pull-in must keep
    // diving inside instead of swinging the camera overhead). Also owns the occlusion
    // timer, so it must run before ResolveOcclusionDistance each frame.
    float ResolveOcclusionExtraPitch()
    {
        if (_occlusionMask.value == 0)
        {
            _occludedTime = 0f;
            return 0f;
        }

        // The timer DECAYS on clear frames instead of resetting: on a rough steep
        // descent the thin ray flickers clear for single frames, and a hard reset kept
        // the delay from ever elapsing — the steepening never engaged. A genuine graze
        // (one short touch, then long clear) still never accumulates the delay.
        // The timer is CAPPED so a long descent can't bank minutes of "engaged" time
        // that would keep the machinery armed long after the view cleared.
        // Like the engage timer, the confirm run DECAYS on clear frames instead of
        // resetting: rare single-frame clear blips on a rough descent must not keep
        // re-arming the confirm window (the pitch raise never got to run). An isolated
        // blip of BLOCKAGE still can't accumulate the confirm — it decays away faster
        // than it builds.
        bool blocked = SightBlocked(Quaternion.Euler(_pitch, _yaw, 0f) * _offset);
        _blockedRun = blocked
            ? Mathf.Min(_blockedRun + Time.deltaTime, OcclusionConfirmTime + 0.25f)
            : Mathf.Max(0f, _blockedRun - 4f * Time.deltaTime);
        _occludedTime = blocked
            ? Mathf.Min(_occludedTime + Time.deltaTime, _occlusionEngageDelay + 0.5f)
            : Mathf.Max(0f, _occludedTime - 2f * Time.deltaTime);

        // Cap the TOTAL pitch at 89°: looking straight down is the steepest useful view
        // (past 90° the offset tips over the top and the view flips). The consecutive-
        // blockage confirm keeps a stale engage timer (decaying after a descent) from
        // firing the search on a single-frame blip at touchdown.
        float maxExtra = Mathf.Min(_occlusionPitchMax, 89f - _pitch);
        if (!blocked || _occludedTime < _occlusionEngageDelay
            || _blockedRun < OcclusionConfirmTime || maxExtra <= 0f)
            return 0f;

        float step = Mathf.Max(1f, _occlusionPitchStep);
        for (float extra = step; extra <= maxExtra + 0.01f; extra += step)
        {
            if (!SightBlocked(Quaternion.Euler(_pitch + extra, _yaw, 0f) * _offset))
                return Mathf.Min(extra, maxExtra);
        }
        return 0f;
    }

    // Absolute floor for the hard visibility clamp, m. Deliberately well below
    // _minOcclusionDistance: when the wall is closer to the player than the comfortable
    // minimum (a near-vertical drop right behind their back), a very close camera is
    // still better than one parked behind the mesh.
    const float HardClampFloor = 0.35f;

    float ResolveOcclusionDistance(Vector3 cameraOffset)
    {
        float fullDistance = cameraOffset.magnitude;
        if (fullDistance <= 0.001f)
            return fullDistance;

        float allowed = fullDistance;
        float hardAllowed = fullDistance;
        if (_occlusionMask.value != 0)
        {
            // Every cast runs along the SIGHT segment (lifted anchor → camera end, see
            // GetSightAnchor), not along the raw offset from the surface-level pivot.
            // Hit distances live on that segment; the camera is clamped at the same
            // FRACTION of its own offset line — the two lines share the far endpoint,
            // so the mapping is exact where it matters.
            Vector3 camEnd = _followPoint + cameraOffset;
            Vector3 anchor = GetSightAnchor();
            Vector3 sightVec = camEnd - anchor;
            float sightLen = sightVec.magnitude;
            Transform playerRoot = _target != null ? _target.root : null;
            if (sightLen > 0.001f)
            {
                Vector3 sightDir = sightVec / sightLen;

                // HARD clamp, evaluated EVERY frame: the camera must never sit behind
                // the first surface that hides the player. The smoothed machinery below
                // is comfort on top of this guarantee.
                foreach (var hit in Physics.RaycastAll(anchor, sightDir, sightLen,
                             _occlusionMask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.distance <= 0f)
                        continue;
                    if (playerRoot != null && hit.collider.transform.root == playerRoot)
                        continue;
                    float atCamera = hit.distance / sightLen * fullDistance;
                    hardAllowed = Mathf.Min(hardAllowed, Mathf.Max(atCamera - _occlusionRadius, HardClampFloor));
                }

                // Reverse ray (camera → player): the one-sided slide colliders show no
                // front face to the forward ray when the sight line crosses a surface
                // back-to-front (camera behind another slide's shell) — this one sees
                // those. Stops ReverseRayNearExclusion short of the player: near
                // surfaces face the player and the forward ray covers them.
                float reverseLength = sightLen - ReverseRayNearExclusion;
                if (reverseLength > 0f)
                {
                    foreach (var hit in Physics.RaycastAll(camEnd, -sightDir, reverseLength,
                                 _occlusionMask, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.distance <= 0f)
                            continue;
                        if (playerRoot != null && hit.collider.transform.root == playerRoot)
                            continue;
                        float atCamera = (sightLen - hit.distance) / sightLen * fullDistance;
                        hardAllowed = Mathf.Min(hardAllowed, Mathf.Max(atCamera - _occlusionRadius, HardClampFloor));
                    }
                }

                // CAMERA VOLUME clamp, also every frame and independent of the sight
                // line: along a flume wall the thin ray to the player clears the wall
                // LIP while the camera itself hangs inside/behind the outer wall — the
                // ray sees nothing, but the frame is full of mesh interior. Walk the
                // camera in along its own line until the clearance sphere is free AND
                // no slide surface hangs just overhead (the corner case: camera in free
                // air slightly BELOW the slide ribbon — sphere clear, sight line clear
                // over the wall lip, frame full of back faces). Walking in along the
                // offset line brings the camera over the channel interior, which an
                // open flume leaves roof-free.
                {
                    Vector3 offsetDir = cameraOffset / fullDistance;
                    float overlapAllowed = Mathf.Min(hardAllowed, fullDistance);
                    for (int i = 0; i < 12 && overlapAllowed > HardClampFloor; i++)
                    {
                        Vector3 candidate = _followPoint + offsetDir * overlapAllowed;
                        if (!CameraSphereOverlaps(candidate, playerRoot)
                            && !UnderSlideRoof(candidate, playerRoot))
                            break;
                        overlapAllowed = Mathf.Max(overlapAllowed - 0.3f, HardClampFloor);
                    }
                    hardAllowed = Mathf.Min(hardAllowed, overlapAllowed);
                }

                // Comfortable distance choice (engaged only after the occlusion delay —
                // the timer lives in ResolveOcclusionExtraPitch, which runs earlier
                // this frame): the sphere cast keeps a clearance from walls near the
                // sight line. If the raised pitch already cleared the view it finds
                // nothing and the distance stays full.
                if (_occludedTime >= _occlusionEngageDelay)
                {
                    var hits = Physics.SphereCastAll(anchor, _occlusionRadius, sightDir, sightLen,
                        _occlusionMask, QueryTriggerInteraction.Ignore);
                    foreach (var hit in hits)
                    {
                        // distance 0 = the collider already overlaps the cast origin —
                        // not an obstruction the sphere can rank; the thin-ray hard
                        // clamp above covers it.
                        if (hit.distance <= 0f)
                            continue;
                        if (playerRoot != null && hit.collider.transform.root == playerRoot)
                            continue;
                        float atCamera = hit.distance / sightLen * fullDistance;
                        allowed = Mathf.Min(allowed, Mathf.Max(atCamera, _minOcclusionDistance));
                    }
                }
            }
        }

        // Two smoothing speeds: pulling IN is near-instant (a wall filling the frame is
        // worse than a small pop), easing back OUT is slow enough to read as intentional.
        if (!_occlusionInitialized)
        {
            _occlusionDistance = allowed;
            _occlusionVelocity = 0f;
            _occlusionInitialized = true;
        }
        else
        {
            float smoothTime = allowed < _occlusionDistance ? _occlusionZoomInTime : _occlusionRecoverTime;
            _occlusionDistance = smoothTime <= 0f
                ? allowed
                : Mathf.SmoothDamp(_occlusionDistance, allowed, ref _occlusionVelocity, smoothTime);
        }

        // The hard clamp is applied to the STATE, not just the returned value, so that
        // when the obstruction disappears the recovery eases out from the clamped
        // distance instead of snapping back to a stale smoothed one. It waits out the
        // short confirm window first — single-frame blips at touchdowns must not snap
        // the camera; a real wall stays blocked and clamps ~3 frames later.
        bool hardBlocked = hardAllowed < fullDistance - 0.01f;
        _hardBlockedRun = hardBlocked
            ? Mathf.Min(_hardBlockedRun + Time.deltaTime, OcclusionConfirmTime + 0.25f)
            : Mathf.Max(0f, _hardBlockedRun - 4f * Time.deltaTime);
        if (_hardBlockedRun >= OcclusionConfirmTime && _occlusionDistance > hardAllowed)
        {
            _occlusionDistance = hardAllowed;
            _occlusionVelocity = Mathf.Min(_occlusionVelocity, 0f);
        }
        return Mathf.Min(_occlusionDistance, fullDistance);
    }

    // Perlin-driven trembling in the CAMERA plane (perpendicular to the view/riding
    // direction), fading in with the player's speed. Perlin, not random: consecutive
    // samples are continuous, so the shake reads as vibration, not teleporting.
    void ApplySpeedShake(Quaternion rotation)
    {
        if (_targetBody == null || _shakeAmplitude <= 0f)
            return;

        float speed = _targetBody.linearVelocity.magnitude;
        float target = Mathf.Clamp01((speed - _shakeSpeedThreshold) / Mathf.Max(_shakeMaxSpeed - _shakeSpeedThreshold, 0.1f));

        // Riding effect only. A free fall quickly exceeds the speed threshold too, and
        // the full-strength tremble there read as the camera following jerkily.
        if (_targetView != null && !_targetView.IsOnSlide)
            target = 0f;
        _shakeStrength = Mathf.MoveTowards(_shakeStrength, target, 3f * Time.deltaTime);
        if (_shakeStrength <= 0.001f)
            return;

        float t = Time.time * _shakeFrequency;
        float x = Mathf.PerlinNoise(t, 0.37f) * 2f - 1f;
        float y = Mathf.PerlinNoise(0.71f, t) * 2f - 1f;
        transform.position += rotation * new Vector3(x, y, 0f) * (_shakeAmplitude * _shakeStrength);
    }

    public void SetTarget(Transform target)
    {
        _target = target;
        _yawInitialized = false;
        _followInitialized = false;
        _occlusionInitialized = false;
        _occludedTime = 0f;
        _blockedRun = 0f;
        _hardBlockedRun = 0f;
        _extraPitch = 0f;
        _extraPitchVelocity = 0f;
        ResolveTargetBody();
        if (_speedRush != null)
            _speedRush.SetTargetBody(_targetBody);
    }

    void ClaimMainCamera()
    {
        if (_camera == null)
            return;

        _camera.nearClipPlane = 0.12f;

        foreach (var other in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (other == _camera)
                continue;

            if (!other.CompareTag("MainCamera"))
                continue;

            other.tag = "Untagged";
            other.enabled = false;

            if (other.TryGetComponent<AudioListener>(out var listener))
                listener.enabled = false;
        }

        gameObject.tag = "MainCamera";
        _camera.enabled = true;

        if (TryGetComponent<AudioListener>(out var audioListener))
            audioListener.enabled = true;
    }

    void BindSuimono()
    {
        var module = FindFirstObjectByType<Suimono.Core.SuimonoModule>();
        if (module == null)
            return;

        module.setCamera = transform;
        module.setTrack = transform;
        module.mainCamera = transform;

        var course = FindFirstObjectByType<WaterSlides.WaterSlideCourse>();
        WaterSlides.WaterSlideWater.ConfigureModule(module, course != null ? course.WaterPreset : null);
        WaterSlides.WaterSlideWater.RefreshAllInScene();
    }

    // Place the camera in the Scene view the way you want it, then run this from the
    // component's context menu (⋮) — it converts the current placement into Offset/Pitch,
    // which are what LateUpdate actually drives the camera from.
    [ContextMenu("Capture Offset From Current Placement")]
    void CaptureOffsetFromCurrentPlacement()
    {
        if (_target == null)
        {
            Debug.LogWarning("PlayerCamera: assign Target before capturing the offset.");
            return;
        }

        float pitch = transform.eulerAngles.x;
        if (pitch > 180f)
            pitch -= 360f;

        var rotation = Quaternion.Euler(pitch, _target.eulerAngles.y, 0f);
        _offset = Quaternion.Inverse(rotation) * (transform.position - _target.position);
        _pitch = pitch;

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        Debug.Log($"PlayerCamera: captured Offset {_offset}, Pitch {_pitch:F1}.");
    }
}
