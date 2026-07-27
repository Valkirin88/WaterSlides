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

        float speed = _body.linearVelocity.magnitude;
        SetEmission(Mathf.Clamp01((speed - _minSpeed) / Mathf.Max(_fullRateSpeed - _minSpeed, 0.1f)));
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
