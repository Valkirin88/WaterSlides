using System.Collections.Generic;
using Suimono.Core;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WaterSlides
{
    public enum WaterFlowAlignment
    {
        AlongSlide,
        AcrossSlide,
        ManualWorld
    }

    // Вода через Suimono: custom mesh по пути горки + эффект водопада на выходе.
    // На объекте курса — шаблон; на каждой горке — своя копия.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class WaterSlideWater : MonoBehaviour
    {
        const string SuimonoSurfacePath = "Assets/SUIMONO - WATER SYSTEM 2/PREFABS/SUIMONO_Surface.prefab";
        const string SuimonoModulePath = "Assets/SUIMONO - WATER SYSTEM 2/PREFABS/SUIMONO_Module.prefab";
        const string WaterfallFxPath = "Assets/SUIMONO - WATER SYSTEM 2/EFFECTS/fx_waterfall.prefab";

        [Header("Surface")]
        [Tooltip("Показывать воду Suimono внутри трубы / жёлоба.")]
        public bool EnableWater = true;

        [Tooltip("Уровень заполнения: 0 — дно, 1 — почти полная труба.")]
        [Range(0.1f, 0.95f)] public float WaterFill = 0.194f;

        [Tooltip("Скорость течения (Suimono flowSpeed).")]
        [Range(0.01f, 2f)] public float FlowSpeed = 0.469f;

        [Tooltip("Высота волн на поверхности.")]
        [Range(0f, 1.5f)] public float WaveHeight = 0.152f;

        [Tooltip("Масштаб волн.")]
        [Range(0.1f, 6f)] public float WaveScale = 4.7f;

        [Header("Waves — Direction")]
        [Tooltip("Вдоль горки — течение вниз по жёлобу. Поперёк — от борта к борту.")]
        public WaterFlowAlignment FlowAlignment = WaterFlowAlignment.AcrossSlide;

        [Tooltip("Угол Suimono (только Manual). 0 ≈ вдоль, 90 ≈ поперёк.")]
        [Range(0f, 360f)] public float ManualFlowDirection = 282f;

        [Tooltip("Тонкая подстройка направления (градусы).")]
        [Range(-180f, 180f)] public float FlowDirectionOffset = 62f;

        [Header("Waves — Stretch")]
        [Tooltip("Растяжение волн ВДОЛЬ горки (больше = мельче/чаще вдоль).")]
        [Min(0.1f)] public float WaveStretchAlong = 0.1f;

        [Tooltip("Растяжение волн ПОПЕРЁК горки (больше = мельче/чаще поперёк).")]
        [Min(0.1f)] public float WaveStretchAcross = 15f;

        [Header("Waterfall")]
        [Tooltip("Эффект водопада Suimono на выходе трубы.")]
        public bool EnableWaterfall = true;

        [Header("Suimono Prefabs")]
        [Tooltip("Suimono работает только с Built-in Render Pipeline. Проект уже настроен на него.")]
        public GameObject SuimonoSurfacePrefab;

        [Tooltip("Префаб SUIMONO_Module (один на сцену). Подставляется автоматически в Editor, если пусто.")]
        public GameObject SuimonoModulePrefab;

        [Tooltip("Префаб fx_waterfall. Подставляется автоматически в Editor, если пусто.")]
        public GameObject WaterfallEffectPrefab;

        GameObject _surfaceRoot;
        SuimonoObject _suimonoObject;
        Mesh _waterMesh;

        Transform _waterfallRoot;
        ParticleSystem _waterfallParticles;

        public bool IsSlideWater => TryGetComponent<WaterSlideGenerator>(out _);

        void Reset()
        {
            ApplySharedDefaults();
            AutoAssignPrefabs();
        }

        public static void ApplySharedDefaults(WaterSlideWater target)
        {
            target.EnableWater = true;
            target.WaterFill = 0.194f;
            target.FlowSpeed = 0.469f;
            target.WaveHeight = 0.152f;
            target.WaveScale = 4.7f;
            target.FlowAlignment = WaterFlowAlignment.AcrossSlide;
            target.ManualFlowDirection = 282f;
            target.FlowDirectionOffset = 62f;
            target.WaveStretchAlong = 0.1f;
            target.WaveStretchAcross = 15f;
            target.EnableWaterfall = true;
        }

        public void ApplySharedDefaults() => ApplySharedDefaults(this);

        void OnValidate()
        {
            AutoAssignPrefabs();
            if (IsSlideWater)
                Rebuild();
        }

        public void AutoAssignPrefabs()
        {
#if UNITY_EDITOR
            if (SuimonoSurfacePrefab == null)
                SuimonoSurfacePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SuimonoSurfacePath);
            if (SuimonoModulePrefab == null)
                SuimonoModulePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SuimonoModulePath);
            if (WaterfallEffectPrefab == null)
                WaterfallEffectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterfallFxPath);
#endif
        }

        public static void EnsureModuleInScene(Transform parent, GameObject modulePrefab)
        {
            if (modulePrefab == null || FindAnyObjectByType<SuimonoModule>() != null)
                return;

            var moduleGo = Instantiate(modulePrefab, parent);
            moduleGo.name = "SUIMONO_Module";
        }

        public void CopyFrom(WaterSlideWater source)
        {
            if (source == null)
                return;

            EnableWater = source.EnableWater;
            WaterFill = source.WaterFill;
            FlowSpeed = source.FlowSpeed;
            WaveHeight = source.WaveHeight;
            WaveScale = source.WaveScale;
            FlowAlignment = source.FlowAlignment;
            ManualFlowDirection = source.ManualFlowDirection;
            FlowDirectionOffset = source.FlowDirectionOffset;
            WaveStretchAlong = source.WaveStretchAlong;
            WaveStretchAcross = source.WaveStretchAcross;
            EnableWaterfall = source.EnableWaterfall;
            SuimonoSurfacePrefab = source.SuimonoSurfacePrefab;
            SuimonoModulePrefab = source.SuimonoModulePrefab;
            WaterfallEffectPrefab = source.WaterfallEffectPrefab;
        }

        [ContextMenu("Rebuild Water")]
        public void Rebuild()
        {
            if (!TryGetComponent<WaterSlideGenerator>(out var slide))
                return;
            if (slide.ControlPoints == null || slide.ControlPoints.Count < 2)
                return;

            Rebuild(slide.BuildMeshSettings(), slide.GetWorldControlPoints(), slide);
        }

        public void Rebuild(
            WaterSlideMeshBuilder.Settings meshSettings,
            List<Vector3> worldPoints,
            WaterSlideGenerator slide)
        {
            AutoAssignPrefabs();

            if (!EnableWater || SuimonoSurfacePrefab == null)
            {
                SetActive(false);
                return;
            }

            EnsureSuimonoSurface();
            ConfigureSuimonoSurface(meshSettings, worldPoints, slide);
            ConfigureWaterfall(meshSettings, worldPoints);
            SetActive(true);
        }

        void EnsureSuimonoSurface()
        {
            if (_surfaceRoot != null)
                return;

            _surfaceRoot = Instantiate(SuimonoSurfacePrefab, transform);
            _surfaceRoot.name = "SuimonoWater";
            _surfaceRoot.transform.localPosition = Vector3.zero;
            _surfaceRoot.transform.localRotation = Quaternion.identity;
            _surfaceRoot.transform.localScale = Vector3.one;

            _suimonoObject = _surfaceRoot.GetComponent<SuimonoObject>();

            var scaleChild = _surfaceRoot.transform.Find("Suimono_ObjectScale");
            if (scaleChild != null)
                scaleChild.gameObject.SetActive(false);
        }

        void ConfigureSuimonoSurface(
            WaterSlideMeshBuilder.Settings meshSettings,
            List<Vector3> worldPoints,
            WaterSlideGenerator slide)
        {
            if (_suimonoObject == null)
                return;

            if (_waterMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(_waterMesh);
                else
                    DestroyImmediate(_waterMesh);
                _waterMesh = null;
            }

            _waterMesh = WaterSlideMeshBuilder.BuildWaterSurface(worldPoints, meshSettings, WaterFill);
            if (_waterMesh == null)
            {
                SetActive(false);
                return;
            }

            _waterMesh.name = $"{slide.name}_SuimonoWater";

            _suimonoObject.typeIndex = 1;
            _suimonoObject.enableCustomMesh = true;
            _suimonoObject.customMesh = _waterMesh;
            _suimonoObject.customWaves = true;
            _suimonoObject.useBeaufortScale = false;
            _suimonoObject.flowSpeed = FlowSpeed;
            _suimonoObject.waveHeight = WaveHeight;
            _suimonoObject.waveScale = WaveScale;
            _suimonoObject.lgWaveHeight = 0f;
            _suimonoObject.turbulenceFactor = 0.15f;
            _suimonoObject.enableTess = false;
            _suimonoObject.enableInteraction = false;
            _suimonoObject.underwaterDepth = 1f;

            float pathLength = 0f;
            var processed = WaterSlideMeshBuilder.BuildProcessedPath(worldPoints, meshSettings);
            for (int i = 1; i < processed.Count; i++)
                pathLength += Vector3.Distance(processed[i], processed[i - 1]);

            WaterSlideMeshBuilder.ComputeWaterChord(meshSettings, WaterFill, out _, out float halfChord);
            float pathWidth = Mathf.Max(halfChord * 2f, 0.5f);

            // UV.x = ширина, UV.y = длина пути — так Suimono даёт волны вдоль жёлоба при flow ≈ 0°.
            _suimonoObject.cmScaleX = Mathf.Max(pathWidth / (WaveScale * 2f), 0.35f) * WaveStretchAcross;
            _suimonoObject.cmScaleY = Mathf.Max(pathLength / (WaveScale * 4f), 1f) * WaveStretchAlong;

            float baseDir = FlowAlignment switch
            {
                WaterFlowAlignment.AlongSlide => 0f,
                WaterFlowAlignment.AcrossSlide => 90f,
                _ => ManualFlowDirection
            };
            _suimonoObject.flowDirection = baseDir + FlowDirectionOffset;
        }

        void ConfigureWaterfall(WaterSlideMeshBuilder.Settings meshSettings, List<Vector3> worldPoints)
        {
            if (!EnableWaterfall || WaterfallEffectPrefab == null)
            {
                if (_waterfallRoot != null)
                    _waterfallRoot.gameObject.SetActive(false);
                return;
            }

            if (!WaterSlideMeshBuilder.TryGetWaterExitInfo(worldPoints, meshSettings, WaterFill,
                    out var surfaceCenter, out _, out _, out var flowDir))
            {
                if (_waterfallRoot != null)
                    _waterfallRoot.gameObject.SetActive(false);
                return;
            }

            if (_waterfallRoot == null)
            {
                var fxGo = Instantiate(WaterfallEffectPrefab, transform);
                fxGo.name = "SuimonoWaterfall";
                _waterfallRoot = fxGo.transform;
                _waterfallParticles = fxGo.GetComponent<ParticleSystem>();
            }

            _waterfallRoot.gameObject.SetActive(true);
            _waterfallRoot.position = surfaceCenter;
            if (flowDir.sqrMagnitude > 1e-4f)
                _waterfallRoot.rotation = Quaternion.LookRotation(flowDir.normalized, Vector3.up);

            if (_waterfallParticles != null && Application.isPlaying && !_waterfallParticles.isPlaying)
                _waterfallParticles.Play();
        }

        void SetActive(bool active)
        {
            if (_surfaceRoot != null)
                _surfaceRoot.SetActive(active);
            if (!active && _waterfallRoot != null)
                _waterfallRoot.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_waterMesh == null)
                return;
            if (Application.isPlaying)
                Destroy(_waterMesh);
            else
                DestroyImmediate(_waterMesh);
        }
    }
}
