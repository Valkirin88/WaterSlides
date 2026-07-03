using System.Collections;
using UnityEngine;
using WaterSlides;

[DefaultExecutionOrder(100)]
public class EntryPoint : MonoBehaviour
{
    [SerializeField] PlayerView _playerView;
    [SerializeField] WaterSlideCourse _course;
    [SerializeField] float _spawnHeightAboveStart = 2.5f;
    [SerializeField] int _maxWaitFrames = 300;

    InputController _inputController;
    PlayerController _playerController;
    bool _spawned;

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
            playerCamera = cam.gameObject.AddComponent<PlayerCamera>();

        playerCamera.SetTarget(_playerView.MovementTransform);
    }

    void OnDisable()
    {
        // Keep the player frozen if spawn never completed — avoids dropping from the scene pose.
    }
}
