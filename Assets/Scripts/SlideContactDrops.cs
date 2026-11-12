using UnityEngine;

// Капли из-под игрока (дочерний объект Drops на игроке). Скрипт только управляет
// эмиссией: капли летят, лишь пока игрок РЕАЛЬНО касается горки, и их количество
// растёт со скоростью. Весь визуал (размер, цвет, конус, материал) настраивается
// на самих ParticleSystem в инспекторе — заданный там Rate over Time принимается
// за 100% и масштабируется отсюда.
public class SlideContactDrops : MonoBehaviour
{
    [Tooltip("Минимальная скорость скольжения, ниже которой капель нет, м/с.")]
    [SerializeField] float _minSpeed = 2f;
    [Tooltip("Скорость, на которой эмиссия выходит на заданную в инспекторе, м/с.")]
    [SerializeField] float _fullRateSpeed = 12f;
    [Tooltip("Удержание эмиссии после потери контакта, сек. На швах и гребнях контакт пропадает на много физических шагов подряд (см. PlayerView), поэтому меньше ~0.3 — и капли гаснут прямо во время езды; в полёте они гаснут за это же время.")]
    [SerializeField] float _contactGrace = 0.35f;
    [Tooltip("Скорость разлёта брызг НАЗАД от направления движения тела, м/с (мировые оси, задаётся модулем Velocity over Lifetime).")]
    [SerializeField] float _backSpeed = 4f;
    [Tooltip("Скорость разлёта брызг ВВЕРХ по нормали горки, м/с.")]
    [SerializeField] float _upSpeed = 4f;
    [Tooltip("Сдвиг источника брызг от точки касания горки ВДОЛЬ движения, м: 0 — ровно под центром тела, минус — ближе к хвосту (голове), плюс — к ногам.")]
    [SerializeField] float _sourceAlongOffset = -0.2f;
    [Tooltip("Подъём источника над точкой касания по нормали горки, м: чуть выше поверхности, чтобы капли не рождались под водой горки.")]
    [SerializeField] float _sourceLift = 0.05f;

    PlayerView _view;
    Rigidbody _body;
    ParticleSystem[] _systems;
    float[] _baseRates;
    float _contactTimer;

    void Awake()
    {
        _view = GetComponentInParent<PlayerView>();
        _body = GetComponentInParent<Rigidbody>();
        _systems = GetComponentsInChildren<ParticleSystem>(true);

        _baseRates = new float[_systems.Length];
        for (int i = 0; i < _systems.Length; i++)
            _baseRates[i] = _systems[i].emission.rateOverTimeMultiplier;

        // До первого контакта (спавн-падение) капель быть не должно. Но если связка
        // сломана (не нашли PlayerView/Rigidbody) — оставляем эмиссию как в инспекторе:
        // постоянные капли заметить легко, а «навсегда выключено» выглядит как поломка.
        if (_view != null && _body != null)
            SetEmission(0f);
        else
            Debug.LogWarning("SlideContactDrops: PlayerView/Rigidbody не найдены среди родителей — эмиссия не управляется.", this);
    }

    void FixedUpdate()
    {
        if (_view == null || _body == null)
            return;

        // Именно контакт (IsOnSlide), а не ведение по пути: в полёте между горками
        // ведение ещё активно, но капель из-под игрока там быть не должно.
        if (_view.IsOnSlide)
            _contactTimer = _contactGrace;
        else
            _contactTimer -= Time.fixedDeltaTime;

        if (_contactTimer <= 0f)
        {
            SetEmission(0f);
            return;
        }

        Vector3 velocity = _body.linearVelocity;
        float speed = velocity.magnitude;
        SetEmission(Mathf.Clamp01((speed - _minSpeed) / Mathf.Max(_fullRateSpeed - _minSpeed, 0.1f)));

        // Брызги летят НАЗАД от реального движения тела (ось +Z систем частиц смотрит
        // против скорости, «вверх» — по нормали горки); конус в инспекторе лишь
        // приподнимает веер. Ориентация по трансформу персонажа была ненадёжна —
        // брызги вылетали перед телом.
        if (speed > 1f)
        {
            Vector3 up = _view.SlideNormal;
            if (up.sqrMagnitude < 0.25f || Mathf.Abs(Vector3.Dot(up.normalized, velocity / speed)) > 0.95f)
                up = Vector3.up;
            up.Normalize();
            Vector3 back = -velocity / speed;
            // Скорость частиц задаётся явно в МИРОВЫХ осях (назад и вверх от движения):
            // не зависит ни от ориентации трансформа, ни от конуса.
            Vector3 drift = back * _backSpeed + up * _upSpeed;
            var rotation = Quaternion.LookRotation(back, up);
            // Источник — точка касания горки под телом (низ капсулы), а не локальный
            // сдвиг объекта Drops в иерархии: пивот Player стоит у ног, и любой
            // фиксированный сдвиг давал брызги впереди/позади тела в зависимости от позы.
            Vector3 along = Vector3.ProjectOnPlane(velocity / speed, up).normalized;
            Vector3 source = _view.SlideContactPoint + along * _sourceAlongOffset + up * _sourceLift;
            for (int i = 0; i < _systems.Length; i++)
            {
                _systems[i].transform.SetPositionAndRotation(source, rotation);
                var vel = _systems[i].velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = drift.x;
                vel.y = drift.y;
                vel.z = drift.z;
            }
        }
    }

    void SetEmission(float scale)
    {
        for (int i = 0; i < _systems.Length; i++)
        {
            var emission = _systems[i].emission;
            emission.rateOverTimeMultiplier = _baseRates[i] * scale;
        }
    }
}
