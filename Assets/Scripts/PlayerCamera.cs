using UnityEngine;

[DefaultExecutionOrder(-50)]
public class PlayerCamera : MonoBehaviour
{
    [SerializeField] Transform _target;
    [SerializeField] Vector3 _offset = new(0f, 1.6f, -3f);
    [SerializeField] float _pitch = 14f;
    [Tooltip("Сглаживание поворота камеры за игроком, сек (0 — без сглаживания).")]
    [SerializeField] float _yawSmoothTime = 0.12f;

    Camera _camera;
    float _yaw;
    float _yawVelocity;
    bool _yawInitialized;

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

        ClaimMainCamera();
    }

    void Start()
    {
        BindSuimono();
    }

    void LateUpdate()
    {
        if (_target == null)
            return;

        if (transform.parent == _target)
        {
            transform.localPosition = _offset;
            transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            return;
        }

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

        var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        transform.SetPositionAndRotation(_target.position + rotation * _offset, rotation);
    }

    public void SetTarget(Transform target)
    {
        _target = target;
        _yawInitialized = false;
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

        if (!TryGetComponent<AudioListener>(out var audioListener))
            audioListener = gameObject.AddComponent<AudioListener>();
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
