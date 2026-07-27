using System.Collections;
using UnityEngine;
using WaterSlides;

[DefaultExecutionOrder(100)]
public class EntryPoint : MonoBehaviour
{
    [Tooltip("Игрок, которым управляет игра. Обязателен: без него игрок не создаётся.")]
    [SerializeField] PlayerView _playerView;
    [Tooltip("Трасса из горок. Если пусто, находится в сцене автоматически.")]
    [SerializeField] WaterSlideCourse _course;
    [Tooltip("Высота спавна игрока над началом первой горки, м.")]
    [SerializeField] float _spawnHeightAboveStart = 2.5f;
    [Tooltip("Сколько кадров ждать готовности трассы к спавну, прежде чем создать игрока принудительно.")]
    [SerializeField] int _maxWaitFrames = 300;

    InputController _inputController;
    PlayerController _playerController;
    bool _spawned;
    // Lowest ride point of the whole course minus a margin; falling below it means the
    // player left the course for good (tunnelled through a mesh or flew into the void).
    float _fallRespawnY = float.NegativeInfinity;

    void Awake()
    {
        if (_course == null)
            _course = FindFirstObjectByType<WaterSlideCourse>();

        if (_playerView == null)
        {
            Debug.LogError("EntryPoint: assign PlayerView in the Inspector.");
            return;
        }

        _inputController = new InputController();
        _playerController = new PlayerController(_playerView, _inputController);
        _playerView.PrepareForSpawn();

        // Плавность кадра. Прежняя связка vSyncCount=0 + targetFrameRate=60 давала
        // «подёргивание» движения: без синхронизации с развёрткой потолок FPS держит
        // софт-ограничитель Unity, который засыпает неточно → времена кадров скачут, и
        // коэффициент интерполяции Rigidbody дёргается каждый кадр. На быстром полёте
        // (25–35 м/с) это читается как рывки ровного баллистического тела.
        // vSync=1 = кадр синхронизирован с монитором и идёт равномерно; расхождение с
        // 50 Гц физикой сглаживает интерполяция Rigidbody. targetFrameRate при активном
        // vSync игнорируется, оставлен запасным потолком на случай отключённой синхронизации.
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = 60;
    }

    void Start()
    {
        if (_playerView == null)
            return;

        StartCoroutine(SpawnWhenCourseReady());
    }

    IEnumerator SpawnWhenCourseReady()
    {
        float bodyClearance = _playerView != null ? _playerView.SpawnBodyClearance : 0.35f;

        for (int i = 0; i < _maxWaitFrames; i++)
        {
            if (_course != null && _course.IsReadyForPlayerSpawn(_spawnHeightAboveStart, bodyClearance))
                break;

            yield return null;
        }

        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();

        SpawnPlayer();
    }

    void Update()
    {
        _inputController?.Update();
        CheckFallRecovery();
    }

    // Safety net, fail-visible: any way of leaving the course downwards (tunnelling
    // through a collider at speed, flying off into the void) ends with a respawn at the
    // start and a console warning naming the position — instead of an endless silent fall.
    void CheckFallRecovery()
    {
        if (!_spawned || _playerView == null)
            return;

        Vector3 pos = _playerView.MovementTransform.position;
        if (pos.y >= _fallRespawnY)
            return;

        Debug.LogWarning($"EntryPoint: игрок упал ниже трассы (y={pos.y:F1} < {_fallRespawnY:F1}) в позиции {pos} — возвращаю на старт. Если это происходит сразу после появления, значит тело прошло сквозь коллайдер первой горки — сообщи об этом.");
        _playerController.SpawnAboveFirstSlide(_course, _spawnHeightAboveStart, _playerView.SpawnBodyClearance);
        _playerView.BeginPlay();
    }

    void CacheFallRespawnHeight()
    {
        float minY = float.MaxValue;
        foreach (var slide in _course.Slides)
        {
            if (slide == null)
                continue;

            var frames = slide.RideFrames;
            for (int i = 0; i < frames.Count; i++)
                minY = Mathf.Min(minY, frames[i].Position.y);
        }

        // 25 m of grace below the lowest ride point: real jumps and wall flyouts dip a
        // few metres below the slides; only a genuine departure goes deeper.
        _fallRespawnY = minY < float.MaxValue ? minY - 25f : float.NegativeInfinity;
    }

    void SpawnPlayer()
    {
        if (_playerView == null)
            return;

        if (_course == null)
            _course = FindFirstObjectByType<WaterSlideCourse>();

        if (_course == null)
        {
            Debug.LogError("EntryPoint: WaterSlideCourse not found — player stays frozen until a course exists.");
            return;
        }

        if (!_course.TryGetFirstSlideSpawn(
                out var position,
                out var forward,
                _spawnHeightAboveStart,
                _playerView.SpawnBodyClearance))
        {
            Debug.LogError("EntryPoint: could not resolve spawn on first slide — player stays frozen.");
            return;
        }

        _playerController.SpawnAboveFirstSlide(_course, _spawnHeightAboveStart, _playerView.SpawnBodyClearance);
        _spawned = true;
        CacheFallRespawnHeight();
        EnsurePlayerCamera();
        WaterSlides.WaterSlideWater.RefreshAllInScene();
        _playerView.BeginPlay();
        Debug.Log($"EntryPoint: player spawned at {position}, slide forward {forward}.");
    }

    void EnsurePlayerCamera()
    {
        var cam = _playerView.GetComponentInChildren<Camera>(true);
        if (cam == null)
            return;

        if (!cam.TryGetComponent<PlayerCamera>(out var playerCamera))
        {
            Debug.LogError("EntryPoint: на камере игрока нет компонента PlayerCamera — добавь его в Inspector.");
            return;
        }

        playerCamera.SetTarget(_playerView.MovementTransform);
    }

    void OnDisable()
    {
        // Keep the player frozen if spawn never completed — avoids dropping from the scene pose.
    }
}
