using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WaterSlides;

namespace WaterSlides.Editor
{
    // Схема управления «одним пальцем»: одним пунктом меню создаёт в открытой
    // сцене прицел приземления (LandingReticle + три LineRenderer), HUD со счётом
    // (RideHud на Canvas), материалы кольца/дуги и ускоряющих полос, задаёт курсу
    // полосы и перегенерирует трассу. Повторный запуск ничего не дублирует.
    // Текстовую подсказку по управлению на экране пользователь убрал (28.10.2026):
    // Setup Controls её больше не создаёт и удаляет старую; отдельный пункт
    // Remove Controls Hint делает только это, не трогая трассу и настройки игрока.
    public static class WaterSlideControlsSetup
    {
        const string BoostMaterialPath = "Assets/Effects/BoostStrip.mat";
        const string LineMaterialPath = "Assets/Effects/ReticleLine.mat";
        const string ReticleObjectName = "Landing Reticle";
        const string HudObjectName = "Ride HUD";

        const string HintObjectName = "Hint";

        [MenuItem("Water Slides/Remove Controls Hint")]
        public static void RemoveControlsHint()
        {
            var hud = Object.FindFirstObjectByType<RideHud>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogWarning("Water Slides: Ride HUD в сцене не найден — подсказку удалять нечего.");
                return;
            }
            bool removed = RemoveHint(hud.gameObject);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            Debug.Log(removed ? "Water Slides: подсказка по управлению удалена из HUD." : "Water Slides: подсказки в HUD уже нет.");
        }

        // Удаляет текст подсказки (дочерний объект HUD) вместе с его компонентами.
        static bool RemoveHint(GameObject hud)
        {
            var hint = hud.transform.Find(HintObjectName);
            if (hint == null)
                return false;
            Undo.DestroyObjectImmediate(hint.gameObject);
            EditorUtility.SetDirty(hud);
            return true;
        }

        [MenuItem("Water Slides/Setup Controls")]
        public static void SetupControls()
        {
            var boostMaterial = EnsureBoostMaterial();
            var lineMaterial = EnsureLineMaterial();
            EnsureReticle(lineMaterial);
            EnsureHud();
            ConfigureCourse(boostMaterial);
            ConfigurePlayer();

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("Water Slides: управление настроено (прицел приземления, HUD, ускоряющие полосы, трасса перегенерирована).");
        }

        static Material EnsureBoostMaterial()
        {
            var shader = Shader.Find("WaterSlides/BoostStrip");
            if (shader == null)
            {
                Debug.LogWarning("Water Slides: шейдер WaterSlides/BoostStrip не найден — полосы не будут рисоваться.");
                return null;
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(BoostMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "BoostStrip" };
                AssetDatabase.CreateAsset(material, BoostMaterialPath);
            }
            else if (material.shader != shader)
                material.shader = shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material EnsureLineMaterial()
        {
            // Sprites/Default: цвет из вершин LineRenderer, прозрачность, без света.
            var shader = Shader.Find("Sprites/Default");
            var material = AssetDatabase.LoadAssetAtPath<Material>(LineMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "ReticleLine" };
                AssetDatabase.CreateAsset(material, LineMaterialPath);
            }
            else if (material.shader != shader)
                material.shader = shader;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        static void EnsureReticle(Material lineMaterial)
        {
            var reticle = Object.FindFirstObjectByType<LandingReticle>(FindObjectsInactive.Include);
            GameObject go;
            if (reticle != null)
                go = reticle.gameObject;
            else
            {
                go = new GameObject(ReticleObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Create Landing Reticle");
                reticle = go.AddComponent<LandingReticle>();
            }

            var ring = EnsureLine(go.transform, "EntryRing", lineMaterial, 0.18f);
            var marker = EnsureLine(go.transform, "LandingMarker", lineMaterial, 0.12f);
            var arc = EnsureLine(go.transform, "FlightArc", lineMaterial, 0.07f);

            var so = new SerializedObject(reticle);
            so.FindProperty("_player").objectReferenceValue = Object.FindFirstObjectByType<PlayerView>(FindObjectsInactive.Include);
            so.FindProperty("_entryRing").objectReferenceValue = ring;
            so.FindProperty("_landingMarker").objectReferenceValue = marker;
            so.FindProperty("_flightArc").objectReferenceValue = arc;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(go);
        }

        static LineRenderer EnsureLine(Transform parent, string name, Material material, float width)
        {
            var child = parent.Find(name);
            GameObject go;
            if (child != null)
                go = child.gameObject;
            else
            {
                go = new GameObject(name);
                go.transform.SetParent(parent, false);
            }
            if (!go.TryGetComponent(out LineRenderer line))
                line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = 0;
            line.startWidth = width;
            line.endWidth = width;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;
            line.enabled = false;
            EditorUtility.SetDirty(go);
            return line;
        }

        static void EnsureHud()
        {
            var hud = Object.FindFirstObjectByType<RideHud>(FindObjectsInactive.Include);
            GameObject go;
            if (hud != null)
                go = hud.gameObject;
            else
            {
                go = new GameObject(HudObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Create Ride HUD");
                go.AddComponent<Canvas>();
                go.AddComponent<CanvasScaler>();
                hud = go.AddComponent<RideHud>();
            }

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // Встроенный шрифт uGUI в Unity 6 — LegacyRuntime.ttf, живёт в builtin
            // resources (НЕ в builtin extra: AssetDatabase.GetBuiltinExtraResource его
            // не находил, тексты оставались без шрифта и не рисовались).
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                Debug.LogWarning("Water Slides: встроенный шрифт LegacyRuntime.ttf не найден — тексты HUD останутся без шрифта.");

            var score = EnsureText(go.transform, "Score", font, 110, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(900f, 140f), TextAnchor.MiddleCenter, FontStyle.Bold);
            var best = EnsureText(go.transform, "Best", font, 42, new Vector2(0.5f, 1f), new Vector2(0f, -265f), new Vector2(900f, 60f), TextAnchor.MiddleCenter, FontStyle.Normal);
            var speed = EnsureText(go.transform, "Speed", font, 52, new Vector2(0.5f, 1f), new Vector2(0f, -330f), new Vector2(900f, 70f), TextAnchor.MiddleCenter, FontStyle.Bold);
            RemoveHint(go);
            score.text = "0";
            best.text = "";
            speed.text = "";

            var so = new SerializedObject(hud);
            so.FindProperty("_player").objectReferenceValue = Object.FindFirstObjectByType<PlayerView>(FindObjectsInactive.Include);
            so.FindProperty("_scoreText").objectReferenceValue = score;
            so.FindProperty("_bestText").objectReferenceValue = best;
            so.FindProperty("_speedText").objectReferenceValue = speed;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(go);
        }

        static Text EnsureText(Transform parent, string name, Font font, int size, Vector2 anchor, Vector2 anchoredPos, Vector2 sizeDelta, TextAnchor alignment, FontStyle style)
        {
            var child = parent.Find(name);
            GameObject go;
            if (child != null)
                go = child.gameObject;
            else
            {
                go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
            }
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;

            if (!go.TryGetComponent(out Text text))
                text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            if (!go.TryGetComponent(out Outline outline))
                outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0.1f, 0.25f, 0.85f);
            outline.effectDistance = new Vector2(3f, -3f);
            // Outline наследует Shadow: ищем именно тень, иначе каждый запуск добавлял бы новую.
            Shadow shadow = null;
            foreach (var s in go.GetComponents<Shadow>())
                if (s.GetType() == typeof(Shadow)) { shadow = s; break; }
            if (shadow == null)
                shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(0f, -6f);
            EditorUtility.SetDirty(go);
            return text;
        }

        static void ConfigureCourse(Material boostMaterial)
        {
            var course = Object.FindFirstObjectByType<WaterSlideCourse>(FindObjectsInactive.Include);
            if (course == null)
            {
                Debug.LogWarning("Water Slides: WaterSlideCourse в сцене не найден — полосы не настроены.");
                return;
            }
            Undo.RecordObject(course, "Setup Controls");
            course.BoostStripMaterial = boostMaterial;
            course.BoostStripsPerSlide = new IntRange { Min = 1, Max = 2 };
            course.BoostStripLength = 6f;
            EditorUtility.SetDirty(course);
            course.RegenerateStable();
        }

        static void ConfigurePlayer()
        {
            var player = Object.FindFirstObjectByType<PlayerView>(FindObjectsInactive.Include);
            if (player == null)
                return;
            var so = new SerializedObject(player);
            // Автотормоз — ассист на 60 %: остальное игрок добирает тормозом пальцем.
            // Раньше в сцене стояло 1 (сам тормозил до безопасной скорости, но
            // выключался при любом вводе).
            so.FindProperty("_autoCornerBrake").floatValue = 0.6f;
            // 10 / 6 — острый руль (28.10.2026); раньше здесь стояли устаревшие 6 / 4
            // и повторный Setup Controls молча откатывал бы настройку сцены.
            so.FindProperty("_lateralTargetGain").floatValue = 10f;
            so.FindProperty("_maxSlideSteerSpeed").floatValue = 6f;
            // 0 — без потолка скорости (просьба пользователя 28.10.2026).
            so.FindProperty("_maxSlideSpeed").floatValue = 0f;
            so.FindProperty("_boostAcceleration").floatValue = 14f;
            // 0: с надбавкой +4 м/с тело срывалось с перегибов (трасса построена под потолок).
            so.FindProperty("_boostSpeedBonus").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(player);
        }
    }
}
