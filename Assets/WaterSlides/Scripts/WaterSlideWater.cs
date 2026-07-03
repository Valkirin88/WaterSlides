using System.Collections;
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
    [DefaultExecutionOrder(200)]
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

        [Header("Suimono — Close Range")]
        [Tooltip("Резерв для transRenderDistance, если снова включить прозрачность Suimono. На горках прозрачность отключена — вода рисуется напрямую.")]
        [Min(3f)] public float SuimonoTransRenderDistance = 8f;

        const float DefaultSuimonoTransRenderDistance = 8f;

        [Header("Diagnostics")]
        [Tooltip("Дамп состояния воды в консоль через 1 и 6 секунд после старта игры (диагностика невидимой воды).")]
        public bool LogWaterDiagnostics = true;

        [Tooltip("Отладка: подменить материал воды на ярко-розовый Unlit прямо в игре. Если розовая лента видна в Game-виде — меш/камера/слои в порядке и проблема в шейдере Suimono; если нет — в геометрии.")]
        public bool DebugUnlitWater;

        int _diagnosticsShots;
        Material _debugUnlitMaterial;
        Material _suimonoMaterialBackup;

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
            target.SuimonoTransRenderDistance = DefaultSuimonoTransRenderDistance;
        }

        public void ApplySharedDefaults() => ApplySharedDefaults(this);

        void OnValidate()
        {
            AutoAssignPrefabs();
            if (!IsSlideWater)
                return;

#if UNITY_EDITOR
            // Rebuild() instantiates prefabs; doing that inside OnValidate raises
            // "SendMessage cannot be called during OnValidate" for every slide. OnValidate
            // also fires while ENTERING play mode, so the deferral must be unconditional.
            EditorApplication.delayCall += () =>
            {
                if (this != null)
                    Rebuild();
            };
#else
            Rebuild();
#endif
        }

        void OnEnable()
        {
            if (!Application.isPlaying || !IsSlideWater)
                return;

            EnsureSuimonoSurface();
            StartCoroutine(RefreshSuimonoWhenReady());
        }

        void Start()
        {
            if (!IsSlideWater || !Application.isPlaying)
                return;

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
#else
            TryCopyPrefabsFromCoursePreset();
#endif
        }

        void TryCopyPrefabsFromCoursePreset()
        {
            var course = GetComponentInParent<WaterSlideCourse>();
            if (course == null)
                return;

            var preset = course.WaterPreset;
            if (preset == null || preset == this)
                return;

            if (SuimonoSurfacePrefab == null)
                SuimonoSurfacePrefab = preset.SuimonoSurfacePrefab;
            if (SuimonoModulePrefab == null)
                SuimonoModulePrefab = preset.SuimonoModulePrefab;
            if (WaterfallEffectPrefab == null)
                WaterfallEffectPrefab = preset.WaterfallEffectPrefab;
        }

        public static void EnsureModuleInScene(Transform parent, GameObject modulePrefab)
        {
            if (modulePrefab == null)
                return;

            SuimonoModule module = FindAnyObjectByType<SuimonoModule>();
            if (module == null)
            {
                var moduleGo = Instantiate(modulePrefab, parent);
                moduleGo.name = "SUIMONO_Module";
                module = moduleGo.GetComponent<SuimonoModule>();
            }

            if (module == null)
                return;

            BindModuleCamera(module);
            ApplySlideModuleSettings(module, parent);
        }

        static void ApplySlideModuleSettings(SuimonoModule module, Transform context)
        {
            if (module == null)
                return;

            var course = context != null ? context.GetComponentInParent<WaterSlideCourse>() : null;
            var preset = course != null ? course.WaterPreset : null;
            ConfigureModule(module, preset);
        }

        public static void ConfigureModule(SuimonoModule module, WaterSlideWater preset = null)
        {
            BindModuleCamera(module);
            if (module == null)
                return;

            // Slide water is viewed from centimeters away. Suimono's transparency RT path
            // (enableTransparency + transRenderDistance) fades the surface to the trans buffer
            // up close — on thin ribbon meshes that buffer is empty and water disappears.
            module.enableTransparency = false;
            module.enableCaustics = false;

            float transDistance = preset != null
                ? preset.SuimonoTransRenderDistance
                : DefaultSuimonoTransRenderDistance;
            module.transRenderDistance = Mathf.Clamp(transDistance, 3f, 50f);
        }

        public static void BindModuleCamera(SuimonoModule module)
        {
            if (module == null)
                return;

            var mainCamera = Camera.main;
            if (mainCamera == null)
                return;

            var cameraTransform = mainCamera.transform;
            module.setCamera = cameraTransform;
            module.mainCamera = cameraTransform;
            module.setTrack = cameraTransform;
        }

        public static void RefreshAllInScene()
        {
            if (!Application.isPlaying)
                return;

            var module = FindAnyObjectByType<SuimonoModule>();
            if (module != null)
            {
                BindModuleCamera(module);
                ConfigureModule(module, FindAnyObjectByType<WaterSlideCourse>()?.WaterPreset);
            }

            foreach (var water in FindObjectsByType<WaterSlideWater>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!water.isActiveAndEnabled || !water.IsSlideWater || !water.EnableWater)
                    continue;

                water.RegisterWithModule();
            }
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
            SuimonoTransRenderDistance = source.SuimonoTransRenderDistance;
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

            if (Application.isPlaying)
                RegisterWithModule();
        }

        public void RegisterWithModule()
        {
            if (!Application.isPlaying || !EnableWater)
                return;

            EnsureSuimonoSurface();
            if (_suimonoObject == null)
                return;

            EnsureModuleInScene(transform, SuimonoModulePrefab);
            ApplyDynamicSuimonoSettings();
            ApplyMeshToSuimonoRenderer();

            var module = FindAnyObjectByType<SuimonoModule>();
            if (module == null)
                return;

            BindModuleCamera(module);
            var course = FindAnyObjectByType<WaterSlideCourse>();
            ConfigureModule(module, course != null ? course.WaterPreset : null);
            module.RegisterSurface(_suimonoObject);
        }

        void EnsureSuimonoSurface()
        {
            // The cached references are not serialized: after a domain reload they are null
            // while the child surface still exists. Adopt it instead of spawning a duplicate.
            if (_surfaceRoot == null)
            {
                var existing = transform.Find("SuimonoWater");
                if (existing != null)
                    _surfaceRoot = existing.gameObject;
            }

            if (_surfaceRoot == null)
            {
                _surfaceRoot = Instantiate(SuimonoSurfacePrefab, transform);
                _surfaceRoot.name = "SuimonoWater";
                _surfaceRoot.transform.localPosition = Vector3.zero;
                _surfaceRoot.transform.localRotation = Quaternion.identity;
                _surfaceRoot.transform.localScale = Vector3.one;
            }

            if (_suimonoObject == null)
                _suimonoObject = _surfaceRoot.GetComponent<SuimonoObject>();

            var scaleChild = _surfaceRoot.transform.Find("Suimono_ObjectScale");
            if (scaleChild != null && scaleChild.gameObject.activeSelf)
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

            WaterSlideMeshBuilder.ExpandMeshBounds(_waterMesh, 2f);
            _waterMesh.name = $"{slide.name}_SuimonoWater";

            _suimonoObject.enableCustomMesh = true;
            _suimonoObject.customMesh = _waterMesh;
            ApplyDynamicSuimonoSettings();
            ApplyMeshToSuimonoRenderer();
        }

        // Everything SuimonoObject can stomp on its own (PresetLoad in its Start/ReloadData
        // rewrites waveScale, waveHeight, flowSpeed, flowDirection from the preset file).
        void ApplyDynamicSuimonoSettings()
        {
            _suimonoObject.typeIndex = 1;
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

            // Same preset → same wave density on every slide (meters in UV, not per-slide length/width).
            _suimonoObject.cmScaleX = Mathf.Max(1f / (WaveScale * 2f), 0.05f) * WaveStretchAcross;
            _suimonoObject.cmScaleY = Mathf.Max(1f / (WaveScale * 4f), 0.05f) * WaveStretchAlong;

            _suimonoObject.flowDirection = ComputeFlowDirection();
        }

        float ComputeFlowDirection()
        {
            float baseDir = FlowAlignment switch
            {
                WaterFlowAlignment.AlongSlide => 0f,
                WaterFlowAlignment.AcrossSlide => 90f,
                _ => ManualFlowDirection
            };
            return baseDir + FlowDirectionOffset;
        }

        void LateUpdate()
        {
            // Two snapshots: right after spawn and once Suimono has fully initialized —
            // the state often differs and the second shot shows what stomped the first.
            if (Application.isPlaying && IsSlideWater && LogWaterDiagnostics && _diagnosticsShots < 2
                && Time.timeSinceLevelLoad > (_diagnosticsShots == 0 ? 1f : 6f))
            {
                _diagnosticsShots++;
                DumpWaterDiagnostics();
            }

            if (_suimonoObject == null || !EnableWater)
                return;

            // A freshly instantiated Suimono surface runs Start → ReloadData → PresetLoad and
            // overwrites our wave settings with its preset file (also happens on every script
            // reload). That is why "Generate Course" reverted the waves while the fields on
            // this component stayed correct. Detect the stomp and re-assert.
            if (!Mathf.Approximately(_suimonoObject.waveScale, WaveScale)
                || !Mathf.Approximately(_suimonoObject.waveHeight, WaveHeight)
                || !Mathf.Approximately(_suimonoObject.flowSpeed, FlowSpeed)
                || !Mathf.Approximately(_suimonoObject.flowDirection, ComputeFlowDirection())
                || !_suimonoObject.customWaves)
            {
                ApplyDynamicSuimonoSettings();
            }

            if (Application.isPlaying && IsSlideWater)
                ForceSlideWaterVisible();
        }

        void ForceSlideWaterVisible()
        {
            _suimonoObject.enableCustomMesh = true;
            if (_waterMesh != null)
                _suimonoObject.customMesh = _waterMesh;

            ApplyMeshToSuimonoRenderer();

            var suimonoObj = _surfaceRoot != null ? _surfaceRoot.transform.Find("Suimono_Object") : null;
            if (suimonoObj == null)
                return;

            var renderer = suimonoObj.GetComponent<Renderer>();
            if (renderer == null)
                return;

            renderer.enabled = true;
            renderer.forceRenderingOff = false;

            // Suimono moves the surface to its own layer at runtime; if the active camera's
            // culling mask was ever saved without that layer, the water exists but never
            // draws in the Game view (Scene view is unaffected — it renders all layers).
            var cam = Camera.main;
            if (cam != null && (cam.cullingMask & (1 << suimonoObj.gameObject.layer)) == 0)
                cam.cullingMask |= 1 << suimonoObj.gameObject.layer;

            if (ApplyDebugUnlitMaterial(renderer))
                return;

            var material = renderer.sharedMaterial;
            if (material == null)
                return;

            material.SetFloat("suimono_trans_on", 0f);
            material.DisableKeyword("SUIMONO_TRANS_ON");
        }

        // Splits the "invisible water" search space in one run: an Unlit ribbon that DOES
        // show up proves mesh/layer/camera are fine and the Suimono shader eats the pixels.
        bool ApplyDebugUnlitMaterial(Renderer renderer)
        {
            if (DebugUnlitWater)
            {
                if (_debugUnlitMaterial == null)
                {
                    var unlit = Shader.Find("Unlit/Color");
                    if (unlit == null)
                        return false;
                    _debugUnlitMaterial = new Material(unlit) { color = new Color(1f, 0f, 0.85f, 1f) };
                }

                if (renderer.sharedMaterial != _debugUnlitMaterial)
                {
                    _suimonoMaterialBackup = renderer.sharedMaterial;
                    renderer.sharedMaterial = _debugUnlitMaterial;
                }

                return true;
            }

            if (_suimonoMaterialBackup != null && renderer.sharedMaterial == _debugUnlitMaterial)
            {
                renderer.sharedMaterial = _suimonoMaterialBackup;
                _suimonoMaterialBackup = null;
            }

            return false;
        }

        // One-shot state dump for the "water invisible in Game view" class of bugs: every
        // fact needed to tell apart layer/culling, material, mesh and module problems.
        void DumpWaterDiagnostics()
        {
            var cam = Camera.main;
            var suimonoObj = _surfaceRoot != null ? _surfaceRoot.transform.Find("Suimono_Object") : null;
            var renderer = suimonoObj != null ? suimonoObj.GetComponent<Renderer>() : null;
            var material = renderer != null ? renderer.sharedMaterial : null;
            var module = FindAnyObjectByType<SuimonoModule>();

            string rendInfo = renderer == null
                ? "renderer=NULL"
                : $"rendEnabled={renderer.enabled} isVisible={renderer.isVisible} layer={LayerMask.LayerToName(renderer.gameObject.layer)} bounds={renderer.bounds.center:F1}/{renderer.bounds.size:F1}";
            string matInfo = material == null
                ? "mat=NULL"
                : $"shader={material.shader.name} queue={material.renderQueue} transOn={(material.HasProperty("suimono_trans_on") ? material.GetFloat("suimono_trans_on").ToString("F0") : "—")}";
            string camInfo = cam == null
                ? "cam=NULL"
                : $"cam={cam.name} pos={cam.transform.position:F1} far={cam.farClipPlane} maskHasLayer={(renderer != null ? ((cam.cullingMask & (1 << renderer.gameObject.layer)) != 0).ToString() : "—")} depthMode={cam.depthTextureMode}";
            string meshInfo = _waterMesh == null ? "mesh=NULL" : $"meshVerts={_waterMesh.vertexCount}";
            string moduleInfo = module == null
                ? "module=NULL"
                : $"moduleTrans={module.enableTransparency} moduleCam={(module.setCamera != null ? module.setCamera.name : "NULL")}";

            var meshFilter = suimonoObj != null ? suimonoObj.GetComponent<MeshFilter>() : null;
            string suimonoInfo = _suimonoObject == null
                ? "suiObj=NULL"
                : $"suiType={_suimonoObject.typeIndex} customMesh={_suimonoObject.enableCustomMesh}/{(_suimonoObject.customMesh != null ? _suimonoObject.customMesh.vertexCount.ToString() : "NULL")} filterMesh={(meshFilter != null && meshFilter.sharedMesh != null ? meshFilter.sharedMesh.vertexCount.ToString() : "NULL")}";

            Debug.Log($"[WaterDiag {name} t={Time.timeSinceLevelLoad:F1}] enableWater={EnableWater} rootActive={(_surfaceRoot != null ? _surfaceRoot.activeInHierarchy.ToString() : "NULL")} | {rendInfo} | {matInfo} | {meshInfo} | {suimonoInfo} | {camInfo} | {moduleInfo}", this);
        }

        void ApplyMeshToSuimonoRenderer()
        {
            if (_surfaceRoot == null || _waterMesh == null)
                return;

            var suimonoObj = _surfaceRoot.transform.Find("Suimono_Object");
            if (suimonoObj == null)
                return;

            var meshFilter = suimonoObj.GetComponent<MeshFilter>();
            if (meshFilter != null)
                meshFilter.mesh = _waterMesh;

            var meshCollider = suimonoObj.GetComponent<MeshCollider>();
            if (meshCollider != null)
                meshCollider.sharedMesh = _waterMesh;
        }

        IEnumerator RefreshSuimonoWhenReady()
        {
            for (int i = 0; i < 120; i++)
            {
                if (!EnableWater)
                    yield break;

                EnsureSuimonoSurface();
                if (_suimonoObject == null)
                {
                    yield return null;
                    continue;
                }

                EnsureModuleInScene(transform, SuimonoModulePrefab);
                var module = FindAnyObjectByType<SuimonoModule>();
                if (module != null && Camera.main != null)
                {
                    RegisterWithModule();
                    yield break;
                }

                yield return null;
            }
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
