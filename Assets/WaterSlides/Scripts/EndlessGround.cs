using UnityEngine;

namespace WaterSlides
{
    // «Земля», до которой нельзя долететь: огромная плоскость с 2D-картинкой ландшафта
    // (вид сверху), которая держится на фиксированной глубине ПОД игроком и ездит за ним
    // по горизонтали шагом в период текстуры — картинка остаётся приколоченной к миру,
    // а край плоскости никогда не показывается. Трасса спускается бесконечно, земля
    // спускается вместе с ней. Заодно настраивает туман и дальность камеры, чтобы
    // плоскость растворялась в дымке у горизонта.
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class EndlessGround : MonoBehaviour
    {
        [Header("Слежение")]
        [Tooltip("За кем следовать (обычно игрок). Если пусто — находится PlayerView в сцене.")]
        [SerializeField] Transform _target;

        [Tooltip("На сколько метров земля ниже игрока. Чем больше — тем выше кажется горка.")]
        [SerializeField, Min(20f)] float _depthBelowTarget = 260f;

        [Tooltip("Следовать за игроком по высоте (земля никогда не приближается). Выкл — плоскость стоит на высоте этого объекта.")]
        [SerializeField] bool _followHeight = true;

        [Header("Плоскость")]
        [Tooltip("Размер квадратной плоскости земли, м. Край должен быть дальше конца тумана.")]
        [SerializeField, Min(500f)] float _planeSize = 16000f;

        [Tooltip("Сколько метров мира покрывает один повтор картинки. Картинка повторяется зеркально (швов не видно), поэтому период по миру — вдвое больше.")]
        [SerializeField, Min(50f)] float _textureTileSize = 1200f;

        [Header("Туман и камера")]
        [Tooltip("Включать линейный туман цвета неба, чтобы край земли и горизонт тонули в дымке.")]
        [SerializeField] bool _applyFog = true;

        [Tooltip("Цвет тумана (дымка у горизонта, под цвет неба).")]
        [SerializeField] Color _fogColor = new(0.74f, 0.83f, 0.93f, 1f);

        [Tooltip("Расстояние, с которого начинается туман, м.")]
        [SerializeField, Min(0f)] float _fogStart = 900f;

        [Tooltip("Расстояние, на котором туман полностью скрывает объекты, м. Должно быть меньше половины размера плоскости.")]
        [SerializeField, Min(10f)] float _fogEnd = 6000f;

        [Tooltip("Дальняя плоскость отсечения главной камеры, м (в Play-режиме). Должна быть больше конца тумана.")]
        [SerializeField, Min(100f)] float _cameraFarPlane = 12000f;

        MeshFilter _meshFilter;
        MeshRenderer _renderer;
        Mesh _mesh;
        float _builtSize = -1f;
        float _builtTile = -1f;

        void OnEnable()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            EnsureMesh();
            ApplyFog();
            Follow();
        }

        void Start()
        {
            if (!Application.isPlaying)
                return;

            var cam = Camera.main;
            if (cam != null && cam.farClipPlane < _cameraFarPlane)
                cam.farClipPlane = _cameraFarPlane;
        }

        void OnValidate()
        {
            _fogEnd = Mathf.Max(_fogEnd, _fogStart + 10f);
            _cameraFarPlane = Mathf.Max(_cameraFarPlane, _fogEnd + 100f);
            if (isActiveAndEnabled)
            {
                EnsureMesh();
                ApplyFog();
            }
        }

        void LateUpdate()
        {
            Follow();
        }

        void OnDestroy()
        {
            if (_mesh == null)
                return;
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
        }

        Transform ResolveTarget()
        {
            if (_target != null)
                return _target;

            var player = FindFirstObjectByType<PlayerView>(FindObjectsInactive.Include);
            if (player != null)
                _target = player.MovementTransform;
            return _target;
        }

        void Follow()
        {
            var target = ResolveTarget();
            if (target == null)
                return;

            // Шаг слежения кратен периоду картинки: она не плывёт вместе с игроком, а
            // остаётся на месте в мире.
            float period = _textureTileSize;
            var p = target.position;
            float x = Mathf.Round(p.x / period) * period;
            float z = Mathf.Round(p.z / period) * period;
            float y = _followHeight ? p.y - _depthBelowTarget : transform.position.y;
            transform.position = new Vector3(x, y, z);
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        void EnsureMesh()
        {
            if (_meshFilter == null)
                _meshFilter = GetComponent<MeshFilter>();
            if (_meshFilter == null)
                return;

            if (_mesh != null && _meshFilter.sharedMesh == _mesh
                && Mathf.Approximately(_builtSize, _planeSize) && Mathf.Approximately(_builtTile, _textureTileSize))
                return;

            if (_mesh == null)
                _mesh = new Mesh { name = "EndlessGround" };

            float half = _planeSize * 0.5f;
            // UV в «тайлах»: плоскость центрирована на кратной периоду точке, поэтому
            // UV = мировая координата / размер тайла, и картинка приколочена к миру.
            float repeats = _planeSize / _textureTileSize;
            _mesh.Clear();
            _mesh.vertices = new[]
            {
                new Vector3(-half, 0f, -half),
                new Vector3(-half, 0f, half),
                new Vector3(half, 0f, half),
                new Vector3(half, 0f, -half)
            };
            _mesh.uv = new[]
            {
                new Vector2(-repeats * 0.5f, -repeats * 0.5f),
                new Vector2(-repeats * 0.5f, repeats * 0.5f),
                new Vector2(repeats * 0.5f, repeats * 0.5f),
                new Vector2(repeats * 0.5f, -repeats * 0.5f)
            };
            _mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _mesh.RecalculateBounds();
            _meshFilter.sharedMesh = _mesh;
            _builtSize = _planeSize;
            _builtTile = _textureTileSize;
        }

        void ApplyFog()
        {
            if (!_applyFog)
                return;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.fogStartDistance = _fogStart;
            RenderSettings.fogEndDistance = _fogEnd;
        }
    }
}
