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
    [Tooltip("Ближе этой дистанции камера к игроку не подтягивается даже при заслонении, м.")]
    [SerializeField] float _minOcclusionDistance = 1.2f;
    [Tooltip("Сколько секунд игрок должен быть НЕПРЕРЫВНО заслонён (по тонкому лучу к его центру), прежде чем камера начнёт подтягиваться. Скользящие касания кромок стенок жёлоба в поворотах длятся меньше и камеру не дёргают.")]
    [SerializeField] float _occlusionEngageDelay = 0.12f;
    [Tooltip("Сглаживание ПРИБЛИЖЕНИЯ камеры при подтверждённой преграде, сек.")]
    [SerializeField] float _occlusionZoomInTime = 0.12f;
    [Tooltip("Сглаживание ВОЗВРАТА камеры на полную дистанцию после исчезновения преграды, сек.")]
    [SerializeField] float _occlusionRecoverTime = 0.4f;

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
        float targetYaw = _target.eulerAngles.y;
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

        var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 cameraOffset = rotation * _offset;
        float distance = ResolveOcclusionDistance(cameraOffset);
        float fullDistance = cameraOffset.magnitude;
        Vector3 position = fullDistance > 0.001f
            ? _followPoint + cameraOffset * (distance / fullDistance)
            : _followPoint;
        transform.SetPositionAndRotation(position, rotation);
        ApplySpeedShake(rotation);
    }

    // On a downhill section the fixed behind-and-above offset can end up on the far
    // side of the slide mesh, leaving the player hidden behind it — pull the camera in
    // front of the obstruction. Two separate casts with different jobs:
    //  * a THIN ray decides IF the player's centre is hidden. The first version decided
    //    with the thick sphere, which brushes flume wall lips half a metre off the sight
    //    line on every curve — the camera lunged constantly ("летает непонятно как").
    //    A grazing touch that doesn't actually cover the player must not move the camera.
    //  * only once the blockage has held for _occlusionEngageDelay does the SPHERE cast
    //    choose the pulled-in distance — its radius keeps the near plane off the wall.
    float ResolveOcclusionDistance(Vector3 cameraOffset)
    {
        float fullDistance = cameraOffset.magnitude;
        if (fullDistance <= 0.001f)
            return fullDistance;

        float allowed = fullDistance;
        if (_occlusionMask.value != 0)
        {
            Vector3 direction = cameraOffset / fullDistance;
            Transform playerRoot = _target != null ? _target.root : null;

            bool blocked = false;
            foreach (var hit in Physics.RaycastAll(_followPoint, direction, fullDistance,
                         _occlusionMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance <= 0f)
                    continue;
                // The player's own colliders (capsule, ragdoll bones) and the camera
                // itself share one hierarchy root — never treat them as walls.
                if (playerRoot != null && hit.collider.transform.root == playerRoot)
                    continue;
                blocked = true;
                break;
            }
            _occludedTime = blocked ? _occludedTime + Time.deltaTime : 0f;

            if (blocked && _occludedTime >= _occlusionEngageDelay)
            {
                var hits = Physics.SphereCastAll(_followPoint, _occlusionRadius, direction, fullDistance,
                    _occlusionMask, QueryTriggerInteraction.Ignore);
                foreach (var hit in hits)
                {
                    // distance 0 = the collider already overlaps the cast origin (the
                    // slide floor right under the riding player) — not an obstruction.
                    if (hit.distance <= 0f)
                        continue;
                    if (playerRoot != null && hit.collider.transform.root == playerRoot)
                        continue;
                    allowed = Mathf.Min(allowed, Mathf.Max(hit.distance, _minOcclusionDistance));
                }
            }
        }
        else
        {
            _occludedTime = 0f;
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
