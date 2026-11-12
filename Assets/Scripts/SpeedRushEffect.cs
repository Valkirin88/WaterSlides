using UnityEngine;

// Nitro-style boost effect: while W is held the view "stretches" (FOV kick plus a
// slight centre zoom) and the screen edges smear with a radial blur, scaled by the
// current speed (W/вверх — газ, полосы разгона). Built-in RP image effect; added to the player camera automatically by
// PlayerCamera — disable the component to turn the effect off.
[RequireComponent(typeof(Camera))]
public class SpeedRushEffect : MonoBehaviour
{
    [Tooltip("Скорость, с которой эффект начинает появляться (при газе или на полосе разгона), м/с. Ниже — эффекта нет. Сила растёт линейно от этой скорости до Full Effect Speed (просьба 28.10.2026: размытие только на высоких скоростях; раньше порог 4 и полная сила с 12 — эффект бил сразу).")]
    [SerializeField] float _minSpeed = 14f;
    [Tooltip("Скорость, на которой эффект выходит на полную силу, м/с.")]
    [SerializeField] float _fullEffectSpeed = 24f;
    [Tooltip("Прибавка поля зрения на полной скорости, градусы — даёт ощущение растяжения.")]
    [SerializeField] float _fovKick = 9f;
    [Tooltip("Длина радиального размытия по краям экрана на полной скорости.")]
    [SerializeField, Range(0f, 0.3f)] float _blurLength = 0.12f;
    [Tooltip("Растяжение картинки от центра на полной скорости.")]
    [SerializeField, Range(0f, 0.15f)] float _stretch = 0.05f;
    [Tooltip("Сглаживание СПАДА эффекта, сек (отпустил газ / сошёл с полосы).")]
    [SerializeField] float _smoothTime = 0.25f;
    [Tooltip("Сглаживание НАРАСТАНИЯ эффекта, сек — размытие появляется плавно, а не ударом (просьба 28.10.2026: «чуть медленнее появляется»).")]
    [SerializeField] float _riseTime = 0.6f;

    [Tooltip("Шейдер Hidden/WaterSlides/SpeedRush. Сериализованная ссылка гарантирует попадание шейдера в билд; в Editor подставляется автоматически.")]
    [SerializeField] Shader _shader;

    Camera _camera;
    Rigidbody _targetBody;
    PlayerView _view;
    Material _material;
    float _strength;
    float _strengthVelocity;
    float _baseFov;
    bool _baseFovCaptured;

    public void SetTargetBody(Rigidbody body) => _targetBody = body;

    void Awake()
    {
        _camera = GetComponent<Camera>();

        if (_shader == null)
            _shader = Shader.Find("Hidden/WaterSlides/SpeedRush");
        if (_shader != null)
            _material = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave };
        else
            Debug.LogWarning("SpeedRushEffect: shader Hidden/WaterSlides/SpeedRush not found — blur disabled.");
    }

#if UNITY_EDITOR
    void Reset()
    {
        AutoAssignShader();
    }

    void OnValidate()
    {
        AutoAssignShader();
    }

    void AutoAssignShader()
    {
        if (_shader == null)
            _shader = Shader.Find("Hidden/WaterSlides/SpeedRush");
    }
#endif

    void LateUpdate()
    {
        if (_view == null)
            _view = FindFirstObjectByType<PlayerView>();
        if (_targetBody == null && _view != null)
            _targetBody = _view.Rigidbody;

        // The boost look belongs to the BOOST: it turns on with W and fades out with it,
        // scaled by how fast the player actually goes.
        float speed = _targetBody != null ? _targetBody.linearVelocity.magnitude : 0f;
        // Разгон теперь с ускоряющих полос (W остался только для отладки в редакторе).
        bool boosting = _view != null && (_view.BoostActive || _view.ThrottleInput > 0.01f);
        // Сила — линейно от порога до полной скорости (не от нуля: иначе на пороге
        // эффект уже стоял на половине силы и бил сразу).
        float target = boosting && speed > _minSpeed
            ? Mathf.Clamp01((speed - _minSpeed) / Mathf.Max(_fullEffectSpeed - _minSpeed, 0.1f))
            : 0f;
        float smooth = target > _strength ? _riseTime : _smoothTime;
        _strength = Mathf.SmoothDamp(_strength, target, ref _strengthVelocity, smooth);

        if (_camera == null)
            return;

        // The base FOV is captured once so the kick never accumulates on itself.
        if (!_baseFovCaptured)
        {
            _baseFov = _camera.fieldOfView;
            _baseFovCaptured = true;
        }

        _camera.fieldOfView = _baseFov + _fovKick * _strength;
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (_material == null || _strength < 0.01f)
        {
            Graphics.Blit(source, destination);
            return;
        }

        _material.SetFloat("_Strength", _strength);
        _material.SetFloat("_Stretch", _stretch);
        _material.SetFloat("_BlurLength", _blurLength);
        Graphics.Blit(source, destination, _material);
    }

    void OnDestroy()
    {
        if (_material != null)
            Destroy(_material);
    }
}
