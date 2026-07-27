using UnityEngine;

// Water spray at the player-slide contact, driven by hand-authored particle prefabs:
// a continuous spray while riding (emission scaled with the ride speed) plus a one-shot
// splash on hard landings. The prefabs own ALL the visual settings — this component only
// spawns them, aims them and drives the emission. Lives on the Player object; disable
// the component to turn the effect off.
public class PlayerSplashEffect : MonoBehaviour
{
    [Header("Префабы (весь визуал настраивается в них)")]
    [Tooltip("Префаб постоянной струи за игроком. Конус эмиссии должен смотреть вдоль локального +Z; Simulation Space — World, иначе шлейф поедет приклеенным к игроку.")]
    [SerializeField] GameObject _sprayPrefab;
    [Tooltip("Префаб разового всплеска при приземлении (опционально). Проигрывается в точке удара, локальный +Z — вдоль нормали поверхности.")]
    [SerializeField] GameObject _landingSplashPrefab;

    [Header("Поведение")]
    [Tooltip("Минимальная скорость скольжения, ниже которой брызг нет, м/с.")]
    [SerializeField] float _minSpraySpeed = 1.5f;
    [Tooltip("Скорость, на которой эмиссия струи выходит на заданную в префабе, м/с.")]
    [SerializeField] float _maxSpraySpeed = 14f;
    [Tooltip("Минимальная скорость удара о горку для всплеска при приземлении, м/с.")]
    [SerializeField] float _landingImpactSpeed = 3f;

    PlayerView _view;
    CapsuleCollider _capsule;
    Rigidbody _rigidbody;

    Transform _sprayRoot;
    ParticleSystem[] _sprayParticles;
    float[] _sprayBaseRates;

    Transform _landingRoot;
    ParticleSystem[] _landingParticles;
    Vector3 _landingBaseScale = Vector3.one;

    bool _wasOnSlide;
    Vector3 _prevVelocity;
    float _rideSprayTimer;
    bool _missingPrefabWarned;

    void Awake()
    {
        _view = GetComponent<PlayerView>();
        _capsule = GetComponent<CapsuleCollider>();
        _rigidbody = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (_view == null || _rigidbody == null)
            return;

        Vector3 velocity = _rigidbody.linearVelocity;
        bool onSlide = _view.IsOnSlide;
        Vector3 normal = _view.SlideNormal;
        Vector3 contact = GetContactPoint(normal);

        // Landing edge: use the PRE-contact velocity — by the time the contact is
        // registered the solver has already absorbed the impact from the current one.
        if (onSlide && !_wasOnSlide)
        {
            float impact = Vector3.Dot(_prevVelocity, -normal);
            if (impact > _landingImpactSpeed)
                PlayLandingSplash(contact, normal, impact);
        }

        UpdateSpray(onSlide, velocity, normal, contact);

        _wasOnSlide = onSlide;
        _prevVelocity = velocity;
    }

    Vector3 GetContactPoint(Vector3 normal)
    {
        if (_capsule == null)
            return transform.position;

        Vector3 center = transform.TransformPoint(_capsule.center);
        return center - normal * _capsule.radius;
    }

    void UpdateSpray(bool onSlide, Vector3 velocity, Vector3 normal, Vector3 contact)
    {
        // "Riding" is contact OR path guidance — raw contact flickers over seams and
        // crests for many physics steps at a time, and the spray kept dying with it.
        // The initial spawn drop is excluded so the plume doesn't burn mid-air.
        bool riding = (onSlide || _view.IsGuided) && _view.HasSlideContactSinceSpawn;
        if (riding)
            _rideSprayTimer = 0.25f;
        else
            _rideSprayTimer -= Time.fixedDeltaTime;

        float speed = velocity.magnitude;
        float strength = _rideSprayTimer > 0f
            ? Mathf.Clamp01((speed - _minSpraySpeed) / Mathf.Max(_maxSpraySpeed - _minSpraySpeed, 0.1f))
            : 0f;

        if (strength <= 0f)
        {
            SetSprayEmission(0f);
            return;
        }

        if (!EnsureSpray())
            return;

        // The spray root is aimed with +Z up along the surface normal, leaned back
        // against the motion — a rooster tail behind the contact point.
        Vector3 forward = Vector3.ProjectOnPlane(velocity, normal);
        Vector3 sprayDir = normal;
        if (forward.sqrMagnitude > 0.01f)
        {
            forward.Normalize();
            sprayDir = (normal - forward * 0.45f).normalized;
        }

        _sprayRoot.SetPositionAndRotation(contact, Quaternion.LookRotation(sprayDir, forward));
        SetSprayEmission(strength);
    }

    bool EnsureSpray()
    {
        if (_sprayRoot != null)
            return true;

        if (_sprayPrefab == null)
        {
            if (!_missingPrefabWarned)
            {
                _missingPrefabWarned = true;
                Debug.LogWarning("PlayerSplashEffect: префаб струи не назначен — брызг не будет. Назначь Spray Prefab в Inspector.", this);
            }

            return false;
        }

        var go = Instantiate(_sprayPrefab, transform);
        go.name = "SlideContactSpray";
        _sprayRoot = go.transform;
        _sprayParticles = go.GetComponentsInChildren<ParticleSystem>(true);

        // The authored emission rates are the 100% mark; the ride speed scales them
        // through the multiplier so the prefab keeps full control of the look.
        _sprayBaseRates = new float[_sprayParticles.Length];
        for (int i = 0; i < _sprayParticles.Length; i++)
        {
            _sprayBaseRates[i] = _sprayParticles[i].emission.rateOverTimeMultiplier;
            _sprayParticles[i].Play();
        }

        return true;
    }

    void SetSprayEmission(float scale)
    {
        if (_sprayParticles == null)
            return;

        for (int i = 0; i < _sprayParticles.Length; i++)
        {
            var emission = _sprayParticles[i].emission;
            emission.rateOverTimeMultiplier = _sprayBaseRates[i] * scale;
        }
    }

    void PlayLandingSplash(Vector3 position, Vector3 normal, float impact)
    {
        if (_landingRoot == null)
        {
            if (_landingSplashPrefab == null)
                return;

            var go = Instantiate(_landingSplashPrefab, transform);
            go.name = "LandingSplash";
            _landingRoot = go.transform;
            _landingBaseScale = _landingRoot.localScale;
            _landingParticles = go.GetComponentsInChildren<ParticleSystem>(true);

            foreach (var ps in _landingParticles)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        Vector3 up = normal.sqrMagnitude > 1e-6f ? normal : Vector3.up;
        _landingRoot.SetPositionAndRotation(position, Quaternion.LookRotation(up));
        // One persistent instance replayed per landing (no Instantiate garbage), scaled
        // with the impact speed.
        _landingRoot.localScale = _landingBaseScale * Mathf.Clamp(impact / 8f, 0.6f, 1.6f);

        foreach (var ps in _landingParticles)
        {
            ps.Clear(true);
            ps.Play();
        }
    }
}
