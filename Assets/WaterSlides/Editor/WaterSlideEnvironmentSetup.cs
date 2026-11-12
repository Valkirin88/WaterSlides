using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WaterSlides.Editor
{
    // Настройка «бесконечного» окружения в открытой сцене одним пунктом меню:
    // плоскость земли с 2D-картинкой ландшафта (EndlessGround), материал под неё,
    // пресет курса под референс (полосатый жёлоб, разные цвета горок), пресет брызг
    // и перегенерация трассы. Повторный запуск ничего не дублирует — только обновляет.
    public static class WaterSlideEnvironmentSetup
    {
        const string EnvironmentFolder = "Assets/WaterSlides/Environment";
        const string GroundSourcePath = EnvironmentFolder + "/GroundAerial.jpg";
        const string GroundSeamlessPath = EnvironmentFolder + "/GroundAerial_Seamless.png";
        const string GroundMaterialPath = EnvironmentFolder + "/EndlessGround.mat";
        const string GroundObjectName = "Endless Ground";

        [MenuItem("Water Slides/Setup Endless Environment")]
        public static void SetupEndlessEnvironment()
        {
            var texture = PrepareGroundTexture();
            var material = PrepareGroundMaterial(texture);
            EnsureGroundObject(material);
            ApplySplashPreset();
            ApplyPlayerPreset();
            ApplyCoursePreset();

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("Water Slides: бесконечное окружение настроено (земля, туман, брызги, пресет курса, трасса перегенерирована).");
        }

        // Бесшовная версия картинки: «стёганое одеяло» из четырёх копий, сдвинутых
        // на полтайла по X, по Y и по обеим осям, с весами-шатрами в степени 3: у
        // каждой копии вес равен нулю ровно там, где у неё шов, поэтому результат
        // повторяется без швов и без зеркальной симметрии (зеркальный повтор давал
        // калейдоскоп из лесов и озёр).
        [MenuItem("Water Slides/Rebuild Seamless Ground Texture")]
        public static void RebuildSeamlessGroundTexture()
        {
            if (!File.Exists(GroundSourcePath))
            {
                Debug.LogWarning($"Water Slides: нет картинки земли {GroundSourcePath}.");
                return;
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.LoadImage(File.ReadAllBytes(GroundSourcePath));
            int w = source.width, h = source.height;
            var src = source.GetPixels32();
            var dst = new Color32[src.Length];

            // Лоскутная сборка: внутри тайла — оригинал как есть, у краёв — копия,
            // сдвинутая на полтайла (она непрерывна через край тайла). Граница между
            // ними — не плавное смешение, а жёсткий (с перьем в пару процентов) край,
            // гуляющий по периодическому шуму: вид «поля и леса разной формы».
            // Прежнее взвешенное смешение четырёх копий (веса-шатры в кубе) давало на
            // большей части тайла полупрозрачную «кашу» из 2–4 наложенных картинок —
            // игрок видел её как туман, зависящий от места над землёй.
            const float band = 0.2f;     // доля тайла у края, где может лежать сдвинутая копия
            const float feather = 0.04f; // мягкость границы в единицах веса
            const float wobble = 0.6f;   // размах шума границы
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                float wy = Mathf.Clamp01(1f - Mathf.Min(v, 1f - v) / band); // 1 у края, 0 внутри
                int ys = (y + h / 2) % h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float wx = Mathf.Clamp01(1f - Mathf.Min(u, 1f - u) / band);
                    int xs = (x + w / 2) % w;

                    float noise = (Fbm(u, v, 5, 101) - 0.5f) * wobble;
                    float sel = SmoothStep01(0.5f - feather, 0.5f + feather, Mathf.Max(wx, wy) + noise);
                    Color c = Color.Lerp((Color)src[y * w + x], (Color)src[ys * w + xs], sel);
                    c.a = 1f;
                    dst[y * w + x] = c;
                }
            }

            var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.SetPixels32(dst);
            File.WriteAllBytes(GroundSeamlessPath, result.EncodeToPNG());
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(result);
            AssetDatabase.ImportAsset(GroundSeamlessPath);
        }

        static Texture2D PrepareGroundTexture()
        {
            if (!File.Exists(GroundSeamlessPath))
                RebuildSeamlessGroundTexture();
            if (!File.Exists(GroundSeamlessPath))
                return null;

            AssetDatabase.ImportAsset(GroundSeamlessPath);
            if (AssetImporter.GetAtPath(GroundSeamlessPath) is TextureImporter importer)
            {
                bool changed = false;
                if (importer.wrapMode != TextureWrapMode.Repeat) { importer.wrapMode = TextureWrapMode.Repeat; changed = true; }
                if (importer.maxTextureSize < 2048) { importer.maxTextureSize = 2048; changed = true; }
                if (importer.anisoLevel < 16) { importer.anisoLevel = 16; changed = true; }
                if (importer.filterMode != FilterMode.Trilinear) { importer.filterMode = FilterMode.Trilinear; changed = true; }
                if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; changed = true; }
                if (changed)
                    importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(GroundSeamlessPath);
        }

        static Material PrepareGroundMaterial(Texture2D texture)
        {
            // Unlit: картинка уже «освещена» художником; Standard под солнцем силой
            // 4.5 пересвечивал её в кислотно-жёлтый. Туман Unlit/Texture поддерживает.
            var shader = Shader.Find("Unlit/Texture");
            var material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "EndlessGround" };
                AssetDatabase.CreateAsset(material, GroundMaterialPath);
            }
            else if (material.shader != shader)
                material.shader = shader;

            material.mainTexture = texture;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        static void EnsureGroundObject(Material material)
        {
            var ground = Object.FindFirstObjectByType<EndlessGround>(FindObjectsInactive.Include);
            GameObject go;
            if (ground != null)
                go = ground.gameObject;
            else
            {
                go = new GameObject(GroundObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Create Endless Ground");
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                go.AddComponent<EndlessGround>();
            }

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Туман подальше: с плоскостью на 260 м ниже даже взгляд вниз под углом
            // упирается в 1–2 км, и при старте тумана на 900 м земля выцветала.
            var so = new SerializedObject(go.GetComponent<EndlessGround>());
            so.FindProperty("_planeSize").floatValue = 24000f;
            // Туман только у самого горизонта: с 2200 м при взгляде вдоль горки почти
            // вся видимая земля уже уходила в цвет неба и читалась как дымка.
            so.FindProperty("_fogStart").floatValue = 6500f;
            so.FindProperty("_fogEnd").floatValue = 11500f;
            so.FindProperty("_cameraFarPlane").floatValue = 14000f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(go);
        }

        // Только пресет брызг (без перегенерации трассы) — для быстрой подстройки.
        [MenuItem("Water Slides/Apply Splash Preset")]
        public static void ApplySplashPresetMenu()
        {
            ApplySplashPreset();
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }

        // Брызги: крупнее и заметнее капли на экране в полёте, в разы больше брызг
        // из-под персонажа на горке (он всё-таки едет по воде).
        static void ApplySplashPreset()
        {
            var screenDrops = Object.FindFirstObjectByType<FlightScreenDrops>(FindObjectsInactive.Include);
            if (screenDrops != null)
            {
                var so = new SerializedObject(screenDrops);
                so.FindProperty("_maxStrength").floatValue = 0.95f;
                // Tiles < 1 растягивает маску капель крупнее экрана — капли большие;
                // число капель на экране ~ Tiles², так что 0.45 (было 0.7) — их в
                // ~2.4 раза меньше и в 1.5 раза крупнее. Мелкий слой почти убран.
                so.FindProperty("_tiles").floatValue = 0.45f;
                so.FindProperty("_refraction").floatValue = 0.1f;
                so.FindProperty("_fineLayer").floatValue = 0.3f;
                so.FindProperty("_dryTime").floatValue = 3f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
                Debug.LogWarning("Water Slides: FlightScreenDrops в сцене не найден — капли на экране не настроены.");

            var contact = Object.FindFirstObjectByType<SlideContactDrops>(FindObjectsInactive.Include);
            if (contact == null)
            {
                Debug.LogWarning("Water Slides: SlideContactDrops в сцене не найден — брызги персонажа не настроены.");
                return;
            }

            // Брызги — мелкие капли в МИРОВОМ пространстве, веером назад-вверх от ног,
            // с частью скорости тела и гравитацией: шлейф остаётся позади. В локальном
            // пространстве с конусом вперёд они ехали вместе с телом и читались как
            // синий дым, выходящий спереди.
            foreach (var ps in contact.GetComponentsInChildren<ParticleSystem>(true))
            {
                Undo.RecordObject(ps, "Splash preset");
                Undo.RecordObject(ps.transform, "Splash preset");
                ps.transform.localRotation = Quaternion.identity;

                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 12000;
                // Капли мелкие, с разбросом: крупные одинаковые диски читались как
                // «шарики», а не как водяная пыль.
                // Частицы теперь только мелкая взвесь вокруг тела; сами крылья пены —
                // полотна-меши SlideSprayWings (референс: POV-рилсы с прозрачных горок).
                main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.035f);
                // Направление задаёт SlideContactDrops (Velocity over Lifetime, мировые
                // оси), стартовая скорость лишь разбрасывает капли.
                main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3.5f);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
                main.gravityModifier = new ParticleSystem.MinMaxCurve(0.8f);
                // Почти белые, полупрозрачные: насыщенно-голубые непрозрачные капли
                // на светлой воде смотрелись тёмными серо-зелёными шариками.
                // Белая, полупрозрачная (просьба 26.10.2026: «попрозрачнее»). Серость
                // прежних полупрозрачных капель была не от альфы, а от тинта под
                // гамму в Linear-проекте и рождения под водой (см. запись (6)).
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1f, 1f, 1f, 0.45f), new Color(0.92f, 0.97f, 1f, 0.3f));

                // Капля тает: прозрачность и размер к концу жизни уходят в ноль —
                // без этого каждая капля резко «выключалась» в воздухе.
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(gradient);

                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.25f));

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                // Референс (рилс с прозрачной горкой): от тела в обе стороны идут
                // НИЗКИЕ белые крылья пены по поверхности, едущие вместе с райдером,
                // а не столб назад. Ось конуса — вверх по нормали горки (система
                // смотрит +Z назад, +Y вверх; поворот −90° по X кладёт ось на +Y),
                // угол 75° — брызги почти стелются; масштаб по X растягивает веер
                // вбок (поперёк движения), по Z сжимает (вперёд/назад).
                shape.angle = 75f;
                shape.radius = 0.3f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                shape.scale = new Vector3(2.5f, 1f, 0.6f);
                shape.position = Vector3.zero;

                // Часть скорости тела наследуется: капли вылетают вместе с телом и
                // отстают, тормозясь (Limit Velocity) — так выглядит настоящий след.
                // Раньше с 0.45 они «летели впереди тела», но тогда сам эмиттер стоял
                // на 1.57 м впереди (см. запись 2026-10-26 (3)); теперь он под телом.
                var inherit = ps.inheritVelocity;
                inherit.enabled = true;
                inherit.mode = ParticleSystemInheritVelocityMode.Initial;
                inherit.curve = new ParticleSystem.MinMaxCurve(0.35f);

                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.separateAxes = false;
                limit.limit = new ParticleSystem.MinMaxCurve(12f);
                limit.dampen = 0.35f;

                // Вытянутые по движению капли (струйки), а не круглые точки.
                var psr = ps.GetComponent<ParticleSystemRenderer>();
                if (psr != null)
                {
                    Undo.RecordObject(psr, "Splash preset");
                    // Взвесь — круглые точки; вытянутые струйки читались «травой».
                    psr.renderMode = ParticleSystemRenderMode.Billboard;
                    psr.velocityScale = 0f;
                    psr.lengthScale = 1f;
                    psr.maxParticleSize = 0.5f;
                    // Шейдер материала — явно (в .mat стоял builtin fileID 203, что это
                    // за шейдер, по файлу не понять); Legacy Particles в этом проекте
                    // рисуются гарантированно (Built-in RP без URP-ассета).
                    var mat = psr.sharedMaterial;
                    var blended = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                    if (mat != null)
                    {
                        Undo.RecordObject(mat, "Splash preset");
                        if (blended != null && mat.shader != blended)
                            mat.shader = blended;
                        // Legacy-шейдер частиц умножает цвет на 2·_TintColor и рассчитан
                        // на гамму (0.5·2 = 1). Проект в Linear: 0.5 sRGB → 0.214 linear,
                        // ×2 = 0.43 — белые капли выходили серыми даже на белом жёлобе.
                        // 0.735 sRGB ≈ 0.5 linear → ×2 = 1, капли белые.
                        mat.SetColor("_TintColor", new Color(0.735f, 0.735f, 0.735f, 0.5f));
                        // Мягкие частицы: с _InvFade 1 капля в 10 см от жёлоба рисуется
                        // на 10% — весь веер у поверхности был почти невидим. 30 —
                        // растворение только в последних 3 см.
                        mat.SetFloat("_InvFade", 30f);
                        // После ленты воды (Transparent = 3000): иначе вода с GrabPass
                        // рисуется поверх капель и красит их в свой серо-голубой.
                        mat.renderQueue = 3100;
                        EditorUtility.SetDirty(mat);
                        AssetDatabase.SaveAssets();
                    }
                    EditorUtility.SetDirty(psr);
                }

                var emission = ps.emission;
                emission.rateOverTime = new ParticleSystem.MinMaxCurve(2500f);
                EditorUtility.SetDirty(ps);
            }

            // Дрейф, добавляемый SlideContactDrops: назад немного (наследованная
            // скорость уже тянет вперёд, торможение отстаёт), вверх заметно.
            var contactSo = new SerializedObject(contact);
            contactSo.FindProperty("_backSpeed").floatValue = 0.5f;
            contactSo.FindProperty("_upSpeed").floatValue = 1f;
            // Источник НАД поверхностью воды: точка касания — дно жёлоба, а вода
            // стоит на WaterFill 0.12 × глубина ≈ 13–16 см выше; капли, рождённые
            // под водой, просвечивали сквозь ленту серыми.
            contactSo.FindProperty("_sourceLift").floatValue = 0.22f;

            EnsureSprayWings(contact.transform.parent);
            contactSo.ApplyModifiedPropertiesWithoutUndo();
        }

        const string SprayWingMaterialPath = "Assets/Effects/SprayWing.mat";

        // Крылья пены: дочерний объект SprayWings у игрока (MeshFilter + MeshRenderer +
        // SlideSprayWings) и материал на шейдере WaterSlides/SprayWing с шумом воды.
        // Компоненты добавляются здесь, в редакторе, а не в рантайме.
        static void EnsureSprayWings(Transform player)
        {
            if (player == null)
                return;

            var wings = player.Find("SprayWings");
            if (wings == null)
            {
                var go = new GameObject("SprayWings");
                go.transform.SetParent(player, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                go.AddComponent<SlideSprayWings>();
                Undo.RegisterCreatedObjectUndo(go, "Spray wings");
                wings = go.transform;
            }

            var shader = Shader.Find("WaterSlides/SprayWing");
            if (shader == null)
            {
                Debug.LogWarning("Water Slides: шейдер WaterSlides/SprayWing не найден — крылья пены без материала.");
                return;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(SprayWingMaterialPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "SprayWing" };
                AssetDatabase.CreateAsset(mat, SprayWingMaterialPath);
            }
            else if (mat.shader != shader)
                mat.shader = shader;
            if (!File.Exists(WaterNoisePath))
                RebuildWaterNoise();
            mat.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>(WaterNoisePath));
            mat.SetColor("_Color", new Color(1f, 1f, 1f, 0.95f));
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            // Угол между крыльями острее (просьба 27.10.2026): 60° от бокового
            // направления назад → 60° между крыльями (было 35° → 110°).
            var wingsSo = new SerializedObject(wings.GetComponent<SlideSprayWings>());
            wingsSo.FindProperty("_sideAngle").floatValue = 75f; // 30° между крыльями (27.10.2026: «ещё ближе»)
            // Корень крыльев в районе ног (просьба 27.10.2026): +0.6 м от центра тела вперёд.
            wingsSo.FindProperty("_alongOffset").floatValue = 0.6f;
            wingsSo.FindProperty("_tipRate").floatValue = 1500f;
            wingsSo.FindProperty("_tipStart").floatValue = 0.45f; // зона распада длиннее — переход в капли плавнее
            wingsSo.ApplyModifiedPropertiesWithoutUndo();

            var mr = wings.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            EditorUtility.SetDirty(mr);

            EnsureWingTips(wings);
        }

        // Текстура клочка пены для капель крыльев: мягкий диск с рваным краем по
        // шуму Перлина и неровной плотностью внутри. Со случайным поворотом и
        // вращением частицы такие клочья не читаются одинаковыми кружками.
        const string SprayBlobPath = "Assets/Effects/SprayBlob.png";
        static Texture2D EnsureSprayBlobTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(SprayBlobPath);
            if (existing != null)
                return existing;

            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size - 0.5f, v = (y + 0.5f) / size - 0.5f;
                float r = Mathf.Sqrt(u * u + v * v) * 2f; // 0 в центре, 1 на краю
                float n = Mathf.PerlinNoise(u * 6f + 3.7f, v * 6f + 1.3f) * 0.7f
                          + Mathf.PerlinNoise(u * 13f + 9.1f, v * 13f + 5.5f) * 0.3f;
                // Край рвётся шумом: радиус обрыва гуляет 0.55..1.0.
                float edge = Mathf.Lerp(0.55f, 1f, n);
                float a = Mathf.Clamp01((edge - r) / 0.25f);
                a *= 0.7f + 0.3f * n; // неровная плотность внутри
                a = a * a * (3f - 2f * a);
                byte A = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, A);
            }
            tex.SetPixels32(px);
            File.WriteAllBytes(SprayBlobPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(SprayBlobPath);
            if (AssetImporter.GetAtPath(SprayBlobPath) is TextureImporter imp)
            {
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(SprayBlobPath);
        }

        // Капли на концах крыльев (просьба 27.10.2026): две системы частиц WingTip0/1
        // под SprayWings, материал тот же, что у взвеси (PlayerSplash). Скрипт крыльев
        // ведёт их за кончиками; здесь — только создание и внешний вид.
        static void EnsureWingTips(Transform wings)
        {
            var tips = new ParticleSystem[2];
            var splashMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Effects/PlayerSplash.mat");
            // Свой материал капель крыльев: как у взвеси, но с полной альфой тинта —
            // клочья у начала распада должны быть такими же белыми и плотными, как
            // полотно (у взвеси альфа тинта 0.5, капли были вдвое прозрачнее пены).
            const string WingDropPath = "Assets/Effects/WingDrop.mat";
            var dropMat = AssetDatabase.LoadAssetAtPath<Material>(WingDropPath);
            if (dropMat == null && splashMat != null)
            {
                dropMat = new Material(splashMat) { name = "WingDrop" };
                AssetDatabase.CreateAsset(dropMat, WingDropPath);
            }
            if (dropMat != null)
            {
                Undo.RecordObject(dropMat, "Wing tip drops");
                dropMat.SetColor("_TintColor", new Color(0.735f, 0.735f, 0.735f, 1f));
                dropMat.SetFloat("_InvFade", 30f);
                dropMat.renderQueue = 3100;
                // Рваный клок вместо ровного круга: крупные капли у крыла на круглой
                // текстуре читались белыми «шариками» (27.10.2026).
                var blob = EnsureSprayBlobTexture();
                if (blob != null)
                    dropMat.mainTexture = blob;
                EditorUtility.SetDirty(dropMat);
            }
            if (dropMat != null)
                splashMat = dropMat;
            for (int i = 0; i < 2; i++)
            {
                var t = wings.Find("WingTip" + i);
                if (t == null)
                {
                    var go = new GameObject("WingTip" + i);
                    go.transform.SetParent(wings, false);
                    go.AddComponent<ParticleSystem>();
                    Undo.RegisterCreatedObjectUndo(go, "Wing tip drops");
                    t = go.transform;
                }
                var ps = t.GetComponent<ParticleSystem>();
                Undo.RecordObject(ps, "Wing tip drops");
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 3000;
                // Круглые капли с разбросом размера (вытянутые белые читались «опилками»);
                // крупные — клочья пены, только что оторвавшиеся от полотна.
                main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.07f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
                main.gravityModifier = new ParticleSystem.MinMaxCurve(1.6f);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(0.9f, 0.97f, 1f, 0.6f), new Color(0.75f, 0.9f, 1f, 0.4f));
                main.loop = true;
                main.playOnAwake = true;

                var col = ps.colorOverLifetime;
                col.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(gradient);

                // Крупный клок у крыла быстро дробится в мелкую взвесь: размер падает
                // до половины за треть жизни и до 0.1 к концу (расстояние от крыла
                // растёт со временем — крупные брызги рядом с крылом, мелкие вдали).
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                var shrink = new AnimationCurve(new Keyframe(0f, 1f, 0f, -2.8f), new Keyframe(0.3f, 0.45f, -1f, -1f), new Keyframe(1f, 0.08f, -0.25f, 0f));
                size.size = new ParticleSystem.MinMaxCurve(1f, shrink);

                // Форма и собственная эмиссия выключены: капли рождает скрипт
                // (SlideSprayWings.EmitTipDrops) прямо на полотне со своей скоростью.
                var shape = ps.shape;
                shape.enabled = false;
                var velocity = ps.velocityOverLifetime;
                velocity.enabled = false;

                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.separateAxes = false;
                limit.limit = new ParticleSystem.MinMaxCurve(8f);
                limit.dampen = 0.5f;

                // Клочья крутятся — одинаковых силуэтов меньше.
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
                emission.rateOverTimeMultiplier = 0f;

                var psr = ps.GetComponent<ParticleSystemRenderer>();
                if (psr != null)
                {
                    Undo.RecordObject(psr, "Wing tip drops");
                    psr.renderMode = ParticleSystemRenderMode.Billboard;
                    psr.velocityScale = 0f;
                    psr.lengthScale = 1f;
                    psr.maxParticleSize = 0.5f;
                    if (splashMat != null)
                        psr.sharedMaterial = splashMat;
                    EditorUtility.SetDirty(psr);
                }
                EditorUtility.SetDirty(ps);
                tips[i] = ps;
            }

            var so = new SerializedObject(wings.GetComponent<SlideSprayWings>());
            var prop = so.FindProperty("_tipParticles");
            prop.arraySize = 2;
            for (int i = 0; i < 2; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = tips[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Езда под правила «вылететь можно, надо рулить и тормозить»: автотормоз
        // выключен, диапазон скоростей шире (9–19 м/с): на минимуме стенка ещё
        // держит, на потолке крутой поворот (R 7–12) выносит за борт.
        static void ApplyPlayerPreset()
        {
            var player = Object.FindFirstObjectByType<PlayerView>(FindObjectsInactive.Include);
            if (player == null)
            {
                Debug.LogWarning("Water Slides: PlayerView в сцене не найден — пресет езды не применён.");
                return;
            }

            var so = new SerializedObject(player);
            so.FindProperty("_autoCornerBrake").floatValue = 0f;
            so.FindProperty("_minRideSpeed").floatValue = 9f;
            so.FindProperty("_maxSlideSpeed").floatValue = 19f;
            // Руль внутрь должен спорить с центробежной силой (v²/R ≈ 30–40 м/с² на
            // крутом повороте): при 15 он ничего не решал.
            so.FindProperty("_slideSteerForce").floatValue = 30f;
            // Стрейф в воздухе: после случайного выноса за борт надо успеть вернуться
            // в жёлоб за ~0.6 с полёта.
            so.FindProperty("_airStrafeAcceleration").floatValue = 45f;
            so.FindProperty("_maxAirStrafeSpeed").floatValue = 12f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Спуск на спине ногами вперёд НЕ делается поворотом объекта: «Player» в
            // сцене — сам FBX модели, Animator висит на корне, а поворот корня каждый
            // шаг физики перезаписывает PlayerView.AlignBody (так и вышло 26.10.2026:
            // корень крутили на 180°, а персонаж всё равно ехал головой вперёд).
            // Разворот позы делает PlayerView._hipsPoseEuler (180,0,0) через таз.
        }

        const string WaterNoisePath = EnvironmentFolder + "/WaterNoise.png";
        const string WaterMaterialPath = EnvironmentFolder + "/SlideWater.mat";

        // Бесшовный градиентный шум (fbm, периодическая решётка) в R и G — два
        // независимых слоя для струй и пены шейдера WaterSlides/SlideWater.
        [MenuItem("Water Slides/Rebuild Water Noise Texture")]
        public static void RebuildWaterNoise()
        {
            const int size = 512;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float r = Fbm(u, v, 6, 11);
                float g = Fbm(u + 0.31f, v + 0.77f, 5, 29);
                px[y * size + x] = new Color(r, g, 0f, 1f);
            }
            tex.SetPixels32(px);
            File.WriteAllBytes(WaterNoisePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(WaterNoisePath);
            if (AssetImporter.GetAtPath(WaterNoisePath) is TextureImporter imp)
            {
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.sRGBTexture = false;
                imp.mipmapEnabled = true;
                imp.filterMode = FilterMode.Trilinear;
                imp.anisoLevel = 4;
                imp.SaveAndReimport();
            }
        }

        // smoothstep как в GLSL (Mathf.SmoothStep интерполирует между a и b — это другое).
        static float SmoothStep01(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(edge1 - edge0, 1e-6f));
            return t * t * (3f - 2f * t);
        }

        static float Fbm(float u, float v, int period, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int p = period;
            float fu = u, fv = v;
            // Только градиентный шум: шейдер воды рассчитан на его узкое
            // распределение (медиана 0.5, 99-й перцентиль ~0.66) и растягивает
            // контраст. Попытка 26.10.2026 заменить базовую октаву шумом значений
            // (против «решётки» на стекле, которая оказалась артефактом Standard-
            // шейдера) расширила распределение — струи и пена на воде пропали.
            for (int o = 0; o < 4; o++)
            {
                sum += amp * PeriodicNoise(fu * p, fv * p, p, seed + o * 17);
                norm += amp;
                amp *= 0.5f;
                p *= 2;
            }
            return Mathf.Clamp01(sum / norm * 0.5f + 0.5f);
        }

        // Шум значений с периодом p: случайное значение −1..1 в каждом узле,
        // плавная интерполяция; линий «ровно ноль» по решётке не даёт.
        static float PeriodicValueNoise(float x, float y, int p, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
            float v00 = LatticeValue(x0, y0, p, seed), v10 = LatticeValue(x0 + 1, y0, p, seed);
            float v01 = LatticeValue(x0, y0 + 1, p, seed), v11 = LatticeValue(x0 + 1, y0 + 1, p, seed);
            return Mathf.Lerp(Mathf.Lerp(v00, v10, sx), Mathf.Lerp(v01, v11, sx), sy);
        }

        static float LatticeValue(int ix, int iy, int p, int seed)
        {
            ix = ((ix % p) + p) % p;
            iy = ((iy % p) + p) % p;
            uint h = (uint)(ix * 374761393 + iy * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h & 0xFFFF) / 65535f * 2f - 1f;
        }

        // Градиентный шум с периодом p по обеим осям (значение в −1..1).
        static float PeriodicNoise(float x, float y, int p, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
            float n00 = Grad(x0, y0, p, seed, fx, fy);
            float n10 = Grad(x0 + 1, y0, p, seed, fx - 1f, fy);
            float n01 = Grad(x0, y0 + 1, p, seed, fx, fy - 1f);
            float n11 = Grad(x0 + 1, y0 + 1, p, seed, fx - 1f, fy - 1f);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, sx), Mathf.Lerp(n01, n11, sx), sy);
        }

        static float Grad(int ix, int iy, int p, int seed, float dx, float dy)
        {
            ix = ((ix % p) + p) % p;
            iy = ((iy % p) + p) % p;
            uint h = (uint)(ix * 374761393 + iy * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            float a = (h & 0xFFFF) / 65535f * Mathf.PI * 2f;
            return Mathf.Cos(a) * dx + Mathf.Sin(a) * dy;
        }

        static Material PrepareWaterMaterial()
        {
            if (!File.Exists(WaterNoisePath))
                RebuildWaterNoise();
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(WaterNoisePath);
            var shader = Shader.Find("WaterSlides/SlideWater");
            if (shader == null)
            {
                Debug.LogWarning("Water Slides: шейдер WaterSlides/SlideWater не найден.");
                return null;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "SlideWater" };
                AssetDatabase.CreateAsset(mat, WaterMaterialPath);
            }
            else if (mat.shader != shader)
                mat.shader = shader;
            mat.SetTexture("_NoiseTex", noise);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void ApplyCoursePreset()
        {
            var course = Object.FindFirstObjectByType<WaterSlideCourse>();
            if (course == null)
            {
                Debug.LogWarning("Water Slides: в сцене нет WaterSlideCourse — пресет курса не применён.");
                return;
            }

            Undo.RecordObject(course, "Apply Reference Look");
            course.ApplyReferenceLook();

            // Своя вода вместо Suimono на ленте: дешевле и ближе к «быстрому потоку».
            var waterMat = PrepareWaterMaterial();
            var preset = course.WaterPreset;
            if (waterMat != null)
            {
                preset.UseCustomShader = true;
                preset.CustomWaterMaterial = waterMat;
                EditorUtility.SetDirty(preset);
            }
            EditorUtility.SetDirty(course);
            course.RegenerateStable();
        }
    }
}
