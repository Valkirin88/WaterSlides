using UnityEngine;

// Water drops on the screen when the player launches off a slide into a flight between
// tubes. Renders through its own image-effect shader (Hidden/WaterSlides/ScreenDrops)
// with controllable drop size; the drops mask texture is borrowed from Suimono's module
// library. Added to the player camera automatically by PlayerCamera; disable to turn off.
[RequireComponent(typeof(Camera))]
public class FlightScreenDrops : MonoBehaviour
{
    [Tooltip("Сколько нужно пробыть в воздухе после отрыва, чтобы появились капли — отсекает мелкие подскоки на стыках, сек.")]
    [SerializeField] float _airborneDelay = 0.18f;
    [Tooltip("Минимальный интервал между срабатываниями, сек.")]
    [SerializeField] float _cooldown = 2f;
    [Tooltip("Время высыхания капель, сек.")]
    [SerializeField] float _dryTime = 2.2f;
    [Tooltip("Максимальная заметность капель.")]
    [SerializeField, Range(0f, 1f)] float _maxStrength = 0.65f;
    [Tooltip("Плотность капель: больше — мельче капли, меньше — крупнее (1.6 ≈ крупные капли).")]
    [SerializeField] float _tiles = 1.6f;
    [Tooltip("Сила линзирования под каплей. При укрупнении капель (меньше Tiles) поднимай вместе: растянутая маска даёт более пологие градиенты, и без компенсации крупные капли выглядят плоско.")]
    [SerializeField, Range(0f, 0.12f)] float _refraction = 0.04f;
    [Tooltip("Вес второго, мелкого слоя капель (0–1). Шейдер кладёт два слоя маски: крупный и в 1.63 раза мельче; чем меньше вес, тем меньше мелких капель на экране и тем реже они в целом. 0 — только крупные.")]
    [SerializeField, Range(0f, 1f)] float _fineLayer = 0.85f;
    [Tooltip("Скорость сползания капель вниз по экрану.")]
    [SerializeField] float _slideSpeed = 0.05f;

    [Tooltip("Шейдер Hidden/WaterSlides/ScreenDrops. В Editor подставляется автоматически.")]
    [SerializeField] Shader _shader;
    [Tooltip("Тайлящаяся маска капель. Если пусто — берётся texDrops из библиотеки Suimono-модуля в сцене.")]
    [SerializeField] Texture _dropsTexture;

    PlayerView _view;
    Material _material;
    float _phase;      // 1 сразу после вылета → 0 к высыханию
    float _slide;
    Vector2 _offset;
    float _airTimer;
    float _cooldownTimer;
    bool _wasRiding;

    void Awake()
    {
        if (_shader == null)
            _shader = Shader.Find("Hidden/WaterSlides/ScreenDrops");
        if (_shader != null)
            _material = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave };

        if (_dropsTexture == null)
        {
            var library = FindFirstObjectByType<Suimono.Core.SuimonoModuleLib>();
            if (library != null)
                _dropsTexture = library.texDrops;
        }
    }

    void Update()
    {
        if (_view == null)
        {
            _view = FindFirstObjectByType<PlayerView>();
            if (_view == null)
                return;
        }

        _cooldownTimer -= Time.deltaTime;

        if (_phase > 0f)
        {
            _phase = Mathf.MoveTowards(_phase, 0f, Time.deltaTime / Mathf.Max(_dryTime, 0.1f));
            // Drops crawl down fast at first, then settle as they dry.
            _slide += Time.deltaTime * _slideSpeed * (0.35f + 0.65f * _phase);
        }

        // "Riding" includes guided air over a slide: hops across seams and crests must
        // not count as launching into a flight.
        bool riding = _view.IsOnSlide || _view.IsGuided;
        if (riding)
        {
            _wasRiding = true;
            _airTimer = 0f;
            return;
        }

        if (!_wasRiding)
            return;

        _airTimer += Time.deltaTime;
        if (_airTimer < _airborneDelay || _cooldownTimer > 0f)
            return;

        _phase = 1f;
        _offset = new Vector2(Random.value, Random.value);
        _cooldownTimer = _cooldown;
        _wasRiding = false; // once per flight
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        float strength = _maxStrength * _phase;
        if (_material == null || _dropsTexture == null || strength < 0.01f)
        {
            Graphics.Blit(source, destination);
            return;
        }

        _material.SetTexture("_DropsTex", _dropsTexture);
        _material.SetFloat("_Strength", strength);
        _material.SetFloat("_Tiles", _tiles);
        _material.SetFloat("_Refract", _refraction);
        _material.SetFloat("_Slide", _slide);
        _material.SetVector("_Offset", _offset);
        _material.SetFloat("_FineLayer", _fineLayer);
        Graphics.Blit(source, destination, _material);
    }

    void OnDestroy()
    {
        if (_material != null)
            Destroy(_material);
    }

#if UNITY_EDITOR
    void Reset() => AutoAssignShader();
    void OnValidate() => AutoAssignShader();

    void AutoAssignShader()
    {
        if (_shader == null)
            _shader = Shader.Find("Hidden/WaterSlides/ScreenDrops");
    }
#endif
}
