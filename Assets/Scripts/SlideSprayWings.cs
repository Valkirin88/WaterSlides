using UnityEngine;
using WaterSlides;

// Крылья пены от тела на горке (референс — POV-рилсы с прозрачных горок: от
// бёдер в обе стороны по поверхности расходятся низкие белые полотна пены,
// едущие вместе с райдером). Частицами такое не собрать: нужна сплошная
// поверхность. Здесь это два полотна-меша, перестраиваемые каждый кадр от точки
// касания горки (PlayerView.SlideContactPoint) по скорости тела; пену рисует
// шейдер WaterSlides/SprayWing скользящей маской шума. Объект SprayWings —
// дочерний к игроку, создаётся пресетом Water Slides/Apply Splash Preset.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SlideSprayWings : MonoBehaviour
{
    [Header("Когда")]
    [Tooltip("Минимальная скорость скольжения, ниже которой пены нет, м/с.")]
    [SerializeField] float _minSpeed = 3f;
    [Tooltip("Скорость, на которой крылья выходят на полную длину и яркость, м/с.")]
    [SerializeField] float _fullSpeed = 14f;
    [Tooltip("Удержание после потери контакта с горкой, сек (контакт на швах пропадает на много шагов).")]
    [SerializeField] float _contactGrace = 0.35f;
    [Tooltip("Скорость плавного появления/исчезновения, 1/с.")]
    [SerializeField] float _fadeSpeed = 8f;

    [Header("Форма")]
    [Tooltip("Длина крыла на полной скорости, м.")]
    [SerializeField] float _length = 3.2f;
    [Tooltip("Ширина крыла у конца, м (у корня уже).")]
    [SerializeField] float _width = 1.5f;
    [Tooltip("Угол крыла: 0 — строго вбок от тела, 90 — строго назад, градусы.")]
    [SerializeField, Range(0f, 90f)] float _sideAngle = 75f;
    [Tooltip("Подъём середины крыла над поверхностью, м (полотно приподнимается и опадает к концу).")]
    [SerializeField] float _rise = 0.4f;
    [Tooltip("Подъём корня крыла над точкой касания по нормали, м — чуть выше поверхности воды.")]
    [SerializeField] float _lift = 0.2f;
    [Tooltip("Сдвиг корня вдоль движения от центра тела, м: плюс — к ногам (тело лежит ногами вперёд), минус — к голове. Пена начинается в районе ног.")]
    [SerializeField] float _alongOffset = 0.6f;
    [Tooltip("Разбиение полотна вдоль крыла.")]
    [SerializeField, Range(2, 32)] int _segments = 12;
    [Tooltip("Разбиение полотна поперёк крыла.")]
    [SerializeField, Range(1, 8)] int _acrossSegments = 4;

    [Header("Капли на концах")]
    [Tooltip("Системы частиц у кончиков крыльев (по одной на крыло, дочерние объекты WingTip0/1 — создаёт пресет Apply Splash Preset). Полотно к концу рассыпается на мелкие капли.")]
    [SerializeField] ParticleSystem[] _tipParticles;
    [Tooltip("Эмиссия капель с одного крыла на полной скорости, частиц/с. Капли рождаются прямо на полотне (между нижним и верхним слоями), плотность растёт к концу крыла — там же, где шейдер рвёт пену на клочья, так полотно переходит в брызги без шва.")]
    [SerializeField] float _tipRate = 1500f;
    [Tooltip("Где вдоль крыла начинается распад, доля длины (0..1): от этой точки пена в шейдере рвётся на островки и тает, а капли сыплются всё гуще к концу.")]
    [SerializeField, Range(0.3f, 1f)] float _tipStart = 0.45f;
    [Tooltip("Скорость капель наружу вдоль крыла, м/с.")]
    [SerializeField] float _tipOutwardSpeed = 3f;
    [Tooltip("Доля скорости тела у капель на самом КОНЦЕ крыла (0..1); у начала распада клочья получают почти всю скорость тела (0.95) и отстают от крыла постепенно. Полотно едет с телом; с малой долей капли мгновенно отстают — переход читается швом.")]
    [SerializeField, Range(0f, 1f)] float _tipInheritVelocity = 0.8f;

    PlayerView _view;
    Rigidbody _body;
    MeshFilter _filter;
    MeshRenderer _renderer;
    Mesh _mesh;
    Vector3[] _vertices;
    Vector2[] _uvs;
    Vector2[] _layerUvs;
    const int Layers = 2; // 0 — нижнее широкое полотно, 1 — верхняя арка над ним
    MaterialPropertyBlock _block;
    float _contactTimer;
    float _strength;
    readonly float[] _tipAccum = new float[2]; // дробный остаток эмиссии по крыльям
    // Сила каждого крыла (0..1): гаснет, когда эта сторона тела выходит из воды
    // (тело на стенке жёлоба) — пена бывает только от воды (просьба 27.10.2026).
    readonly float[] _wingWater = { 1f, 1f };
    static readonly int WingStrengthId = Shader.PropertyToID("_WingStrength");
    static readonly int StrengthId = Shader.PropertyToID("_Strength");
    static readonly int BreakStartId = Shader.PropertyToID("_BreakStart");

    void Awake()
    {
        _view = GetComponentInParent<PlayerView>();
        _body = GetComponentInParent<Rigidbody>();
        _filter = GetComponent<MeshFilter>();
        _renderer = GetComponent<MeshRenderer>();
        _block = new MaterialPropertyBlock();
        BuildTopology();
        _renderer.enabled = false;

        if (_view == null || _body == null)
            Debug.LogWarning("SlideSprayWings: PlayerView/Rigidbody не найдены среди родителей — крылья пены выключены.", this);
    }

    void BuildTopology()
    {
        int cols = _segments + 1, rows = _acrossSegments + 1;
        int perSheet = cols * rows;
        int sheets = 2 * Layers;
        _vertices = new Vector3[perSheet * sheets];
        _uvs = new Vector2[perSheet * sheets];
        _layerUvs = new Vector2[perSheet * sheets];
        var triangles = new int[_segments * _acrossSegments * 6 * sheets];
        int t = 0;
        for (int s = 0; s < sheets; s++)
        {
            int b = s * perSheet;
            int layer = s % Layers;
            for (int i = 0; i < cols; i++)
            for (int k = 0; k < rows; k++)
            {
                _uvs[b + i * rows + k] = new Vector2(i / (float)_segments, k / (float)_acrossSegments);
                _layerUvs[b + i * rows + k] = new Vector2(layer, s / Layers); // y — номер крыла (0 левое, 1 правое)
            }

            for (int i = 0; i < _segments; i++)
            for (int k = 0; k < _acrossSegments; k++)
            {
                int a = b + i * rows + k;
                triangles[t++] = a; triangles[t++] = a + rows; triangles[t++] = a + 1;
                triangles[t++] = a + 1; triangles[t++] = a + rows; triangles[t++] = a + rows + 1;
            }
        }

        _mesh = new Mesh { name = "SprayWings" };
        _mesh.MarkDynamic();
        _mesh.vertices = _vertices;
        _mesh.uv = _uvs;
        _mesh.uv2 = _layerUvs;
        _mesh.triangles = triangles;
        _mesh.RecalculateNormals();
        _filter.sharedMesh = _mesh;
    }

    // Подъём полотна над поверхностью в точке (t вдоль, q поперёк −0.5..0.5) для
    // слоя: 0 — нижнее широкое полотно, 1 — верхняя арка. К концу полотно НЕ
    // возвращается к поверхности (иначе на изгибе жёлоба конец уходил под воду/дно
    // и срезался по глубине — «отрезанные» крылья).
    static float SheetHeight(int layer, float t, float q, float rise)
    {
        float lift = rise * (0.35f * t + 0.65f * Mathf.Sin(Mathf.PI * Mathf.Min(t * 0.85f, 1f)));
        return layer == 0
            ? lift * (0.2f + 0.35f * (1f - 2f * Mathf.Abs(q)))
            : lift * (0.55f + 0.5f * Mathf.Cos(Mathf.PI * q));
    }

    // Распад полотна на капли (жалоба 27.10.2026: переход крыло → брызги был швом).
    // Капли рождаются вручную прямо на полотне между слоями: вдоль крыла от _tipStart
    // до конца с плотностью, растущей к концу (t = start + (1 − start)·√rnd), — ровно
    // там шейдер по _BreakStart рвёт пену на островки и гасит её. Скорость капли —
    // доля скорости тела (летит вместе с крылом и отстаёт плавно) + наружу + разброс.
    // Эмиттер системы частиц не используется (форма и эмиссия выключены пресетом).
    void EmitTipDrops(int wing, Vector3 origin, Vector3 outward, Vector3 spread, Vector3 up, float length, float rise, Vector3 bodyVelocity)
    {
        if (_tipParticles == null || wing >= _tipParticles.Length || _tipParticles[wing] == null)
            return;

        _tipAccum[wing] += _tipRate * _strength * _wingWater[wing] * Time.deltaTime;
        int count = Mathf.Min(Mathf.FloorToInt(_tipAccum[wing]), 200);
        if (count <= 0)
            return;
        _tipAccum[wing] -= count;

        var ps = _tipParticles[wing];
        var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        for (int i = 0; i < count; i++)
        {
            // Плотность ∝ f² (f — доля зоны распада): у начала зоны единичные
            // клочья, к концу — густо. Клочья у начала крупные, белые, почти со
            // скоростью тела (висят у полотна и отстают медленно); к концу —
            // мелкие голубоватые капли, отстающие и разлетающиеся сильнее.
            float f = Mathf.Pow(Random.value, 1f / 3f);
            float t = _tipStart + (1f - _tipStart) * f;
            float q = Random.Range(-0.5f, 0.5f);
            float width = _width * (0.2f + 0.8f * t);
            float h = Mathf.Lerp(SheetHeight(0, t, q, rise), SheetHeight(1, t, q, rise), Random.value);
            emit.position = origin + outward * (t * length) + spread * (q * width) + up * h;
            // Крупность — по РАССТОЯНИЮ от крыла, не по месту рождения: все капли
            // рождаются крупными клочьями на полотне и сжимаются за жизнь (кривая
            // размера в пресете 1 → 0.1), а расстояние от крыла растёт со временем —
            // у полотна крупные брызги, вдали мелкая взвесь (просьба 27.10.2026).
            float inherit = Mathf.Lerp(0.95f, _tipInheritVelocity, f);
            float scatter = 0.3f + 0.7f * f;
            emit.velocity = bodyVelocity * inherit + outward * (_tipOutwardSpeed * scatter) + up * (0.8f * f)
                + Random.insideUnitSphere * (Random.Range(0.3f, 2.5f) * scatter);
            // Мельче и прозрачнее: клочья 0.07–0.15 с альфой 0.6 читались снегом (27.10.2026).
            emit.startSize = Random.Range(0.03f, 0.08f) * (1f - 0.3f * f);
            emit.startColor = Color.Lerp(new Color(0.95f, 0.98f, 1f, 0.5f), new Color(0.85f, 0.94f, 1f, 0.35f), f);
            emit.startLifetime = Random.Range(0.3f, 0.55f);
            ps.Emit(emit, 1);
        }
    }

    void LateUpdate()
    {
        if (_view == null || _body == null)
            return;

        if (_view.IsOnSlide)
            _contactTimer = _contactGrace;
        else
            _contactTimer -= Time.deltaTime;

        Vector3 velocity = _body.linearVelocity;
        float speed = velocity.magnitude;
        float target = _contactTimer > 0f
            ? Mathf.Clamp01((speed - _minSpeed) / Mathf.Max(_fullSpeed - _minSpeed, 0.1f))
            : 0f;
        _strength = Mathf.MoveTowards(_strength, target, _fadeSpeed * Time.deltaTime);

        if (_strength < 0.02f || speed < 0.5f)
        {
            _renderer.enabled = false;
            return;
        }

        Vector3 up = _view.SlideNormal;
        if (up.sqrMagnitude < 0.25f)
            up = Vector3.up;
        up.Normalize();
        Vector3 forward = Vector3.ProjectOnPlane(velocity, up);
        if (forward.sqrMagnitude < 1e-4f)
        {
            _renderer.enabled = false;
            return;
        }
        forward.Normalize();
        Vector3 right = Vector3.Cross(up, forward).normalized;
        Vector3 back = -forward;
        Vector3 origin = _view.SlideContactPoint + forward * _alongOffset + up * _lift;

        float length = _length * Mathf.Lerp(0.45f, 1f, _strength);
        float rise = _rise * Mathf.Lerp(0.5f, 1f, _strength);

        // Сторона тела вне воды — без крыла: по поперечному положению тела в жёлобе
        // и полуширине ленты воды (на стенке верхняя сторона сухая). Плавно, 8/с.
        float waterHalf = -1f;
        var slide = _view.CurrentSlide;
        if (slide != null && slide.TryGetComponent<WaterSlideWater>(out var water) && water.WaterHalfWidth > 0.01f)
            waterHalf = water.WaterHalfWidth;
        float lateral = _view.IsOnSlide ? _view.RideLateral : 0f;
        float bodyHalf = _view.SpawnBodyClearance * 0.5f;
        for (int w = 0; w < 2; w++)
        {
            float side = w == 0 ? -1f : 1f;
            float wet = waterHalf > 0f ? Mathf.Clamp01((waterHalf - (lateral * side + bodyHalf)) / 0.25f) : 1f;
            _wingWater[w] = Mathf.MoveTowards(_wingWater[w], wet, 8f * Time.deltaTime);
        }
        float cosA = Mathf.Cos(_sideAngle * Mathf.Deg2Rad), sinA = Mathf.Sin(_sideAngle * Mathf.Deg2Rad);
        int cols = _segments + 1, rows = _acrossSegments + 1;

        // Меш строится в мировых осях и переводится в локальные: объект едет с
        // игроком, а полотно должно лежать на горке.
        // Объём: на крыло два слоя — нижнее широкое полотно у поверхности и верхняя
        // арка над ним (уже, выше, изогнута поперёк), с разной пеной в шейдере.
        for (int w = 0; w < 2; w++)
        {
            float side = w == 0 ? -1f : 1f;
            Vector3 outward = (right * (side * cosA) + back * sinA).normalized;
            Vector3 spread = Vector3.Cross(up, outward).normalized;
            EmitTipDrops(w, origin, outward, spread, up, length, rise, velocity);
            for (int layer = 0; layer < Layers; layer++)
            {
                int b = (w * Layers + layer) * cols * rows;
                for (int i = 0; i < cols; i++)
                {
                    float t = i / (float)_segments;
                    float along = t * length;
                    float width = _width * (0.2f + 0.8f * t) * (layer == 0 ? 1f : 0.7f);
                    for (int k = 0; k < rows; k++)
                    {
                        float q = k / (float)_acrossSegments - 0.5f;
                        Vector3 p = origin + outward * along + spread * (q * width) + up * SheetHeight(layer, t, q, rise);
                        _vertices[b + i * rows + k] = transform.InverseTransformPoint(p);
                    }
                }
            }
        }

        _mesh.vertices = _vertices;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        _renderer.GetPropertyBlock(_block);
        _block.SetFloat(StrengthId, _strength);
        _block.SetFloat(BreakStartId, _tipStart);
        _block.SetVector(WingStrengthId, new Vector4(_wingWater[0], _wingWater[1], 0f, 0f));
        _renderer.SetPropertyBlock(_block);
        _renderer.enabled = true;
    }
}
