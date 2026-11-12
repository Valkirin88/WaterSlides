using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterSlides.Editor
{
    // Applies a tropical lagoon look using the project’s existing Suimono palm and rock assets.
    public static class TropicalArchipelagoSetup
    {
        const string PanoramaSourcePath = "Assets/WaterSlides/Environment/TropicalArchipelagoPanorama.png";
        const string PanoramaPath = "Assets/WaterSlides/Environment/TropicalArchipelagoEquirect.png";
        const string SkyboxPath = "Assets/WaterSlides/Environment/TropicalArchipelagoSkybox.mat";
        const string OceanPath = "Assets/WaterSlides/Environment/TropicalOcean.mat";
        const string IslandRootName = "Tropical Archipelago";
        const float SeaDepthBelowTrack = 100f;

        [MenuItem("Water Slides/Apply Reference Image Backdrop")]
        public static void ApplyReferenceImageBackdrop()
        {
            // Use the full surrounding panorama; the perspective reference remains preserved.
            const string path = "Assets/WaterSlides/Environment/TropicalLagoon360_Detailed.png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 8192;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            const string materialPath = "Assets/WaterSlides/Environment/ReferenceBackdrop.mat";
            var shader = Shader.Find("WaterSlides/ReferenceBackdrop");
            if (shader == null) return;
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.shader = shader;
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Debug.Log($"Panorama loaded: {material.mainTexture.width} x {material.mainTexture.height}");
            EditorUtility.SetDirty(material);
            RenderSettings.skybox = material;
            var ground = Object.FindFirstObjectByType<EndlessGround>(FindObjectsInactive.Include);
            if (ground != null)
            {
                var renderer = ground.GetComponent<MeshRenderer>();
                if (renderer != null) { renderer.enabled = false; EditorUtility.SetDirty(renderer); }
            }
            var islands = GameObject.Find(IslandRootName);
            if (islands != null) { islands.SetActive(false); EditorUtility.SetDirty(islands); }
            if (Camera.main != null) { Camera.main.clearFlags = CameraClearFlags.Skybox; EditorUtility.SetDirty(Camera.main); }
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        static readonly string[] RockPaths =
        {
            "Assets/SUIMONO - WATER SYSTEM 2/_DEMO/TERRAIN/Objects/Rocks/Prefabs/rock01.prefab",
            "Assets/SUIMONO - WATER SYSTEM 2/_DEMO/TERRAIN/Objects/Rocks/Prefabs/rock02.prefab",
            "Assets/SUIMONO - WATER SYSTEM 2/_DEMO/TERRAIN/Objects/Rocks/Prefabs/rock03.prefab",
            "Assets/SUIMONO - WATER SYSTEM 2/_DEMO/TERRAIN/Objects/Rocks/Prefabs/rock04.prefab",
            "Assets/SUIMONO - WATER SYSTEM 2/_DEMO/TERRAIN/Objects/Rocks/Prefabs/rock05.prefab"
        };

        static readonly string[] PalmPaths =
        {
            "Assets/SUIMONO - WATER SYSTEM 2/_DEMO/TERRAIN/Objects/Palm Trees/Prefabs/PalmTreeSingle.prefab",
            "Assets/SUIMONO - WATER SYSTEM 2/_DEMO/TERRAIN/Objects/Palm Trees/Prefabs/PalmTreeSingleBent.prefab",
            "Assets/SUIMONO - WATER SYSTEM 2/_DEMO/TERRAIN/Objects/Palm Trees/Prefabs/PalmTreesDual.prefab",
            "Assets/SUIMONO - WATER SYSTEM 2/_DEMO/TERRAIN/Objects/Palm Trees/Prefabs/PalmTreesDualBent.prefab"
        };

        [MenuItem("Water Slides/Setup Tropical Archipelago")]
        public static void Setup()
        {
            var panorama = PreparePanorama();
            var skybox = PrepareSkybox(panorama);
            var ocean = PrepareOceanMaterial();
            ApplySkyAndOcean(skybox, ocean);
            RebuildIslands();

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("Water Slides: tropical archipelago environment set up (panorama sky, lagoon sea, islands, palms, rocks).");
        }

        [MenuItem("Water Slides/Apply Panoramic Backdrop")]
        public static void ApplyPanoramicBackdrop()
        {
            const string path = "Assets/WaterSlides/Environment/TropicalLagoon360.png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { Debug.LogError("Panoramic backdrop image is missing."); return; }
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            var panorama = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            RenderSettings.skybox = PrepareSkybox(panorama);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Water Slides/Align Existing Islands With Sea")]
        public static void AlignExistingIslands()
        {
            var root = GameObject.Find(IslandRootName);
            if (root == null) return;
            Undo.RegisterFullObjectHierarchyUndo(root, "Align existing island scenery");
            foreach (Transform island in root.transform)
                island.localScale = Vector3.one * 4f;
            var course = Object.FindFirstObjectByType<WaterSlideCourse>();
            if (root.transform.childCount > 0 && course != null)
            {
                var template = root.transform.GetChild(0).gameObject;
                int index = 0;
                foreach (var slide in course.Slides)
                {
                    var frames = slide != null ? slide.RideFrames : null;
                    if (frames == null || frames.Count < 2) continue;
                    foreach (float fraction in new[] { 0.08f, 0.3f, 0.65f })
                    {
                        var frame = frames[Mathf.Clamp(Mathf.RoundToInt(fraction * (frames.Count - 1)), 0, frames.Count - 1)];
                        string name = "Shore Cluster " + index++;
                        var existing = root.transform.Find(name);
                        var cluster = existing != null ? existing.gameObject : Object.Instantiate(template, root.transform);
                        if (existing == null) Undo.RegisterCreatedObjectUndo(cluster, "Add shoreline scenery");
                        cluster.name = name;
                        Vector3 right = Vector3.Cross(Vector3.up, frame.Forward).normalized;
                        cluster.transform.position = frame.Position + right * (index % 2 == 0 ? 65f : -65f) - Vector3.up * SeaDepthBelowTrack;
                        cluster.transform.localScale = Vector3.one * 4f;
                    }
                }
            }
            if (root.GetComponent<TropicalIslandWaterline>() == null)
                Undo.AddComponent<TropicalIslandWaterline>(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveOpenScenes();
        }

        static Texture2D PreparePanorama()
        {
            // Keep the authored panorama when applying the environment again.
            var authored = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/WaterSlides/Environment/TropicalLagoon360.png");
            if (authored != null) return authored;
            AssetDatabase.ImportAsset(PanoramaSourcePath);
            if (AssetImporter.GetAtPath(PanoramaSourcePath) is TextureImporter sourceImporter)
            {
                sourceImporter.textureType = TextureImporterType.Default;
                sourceImporter.sRGBTexture = true;
                sourceImporter.isReadable = true;
                sourceImporter.wrapMode = TextureWrapMode.Clamp;
                sourceImporter.filterMode = FilterMode.Bilinear;
                sourceImporter.mipmapEnabled = false;
                sourceImporter.textureCompression = TextureImporterCompression.Uncompressed;
                sourceImporter.maxTextureSize = 4096;
                sourceImporter.textureShape = TextureImporterShape.Texture2D;
                sourceImporter.SaveAndReimport();
            }

            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(PanoramaSourcePath);
            if (source == null || !source.isReadable)
            {
                Debug.LogWarning("Water Slides: the lagoon reference image could not be read to build its panorama.");
                return source;
            }

            // The supplied wide image already contains the desired island scene.
            // Fit it to the 2:1 skybox canvas by a small vertical crop; do not stretch
            // or smear its edges around the unseen hemisphere.
            const int width = 2048;
            const int height = 1024;
            var sourcePixels = source.GetPixels();
            int sourceWidth = source.width;
            int sourceHeight = source.height;
            float sourceAspect = (float)sourceWidth / sourceHeight;
            float cropHeight = Mathf.Clamp01(sourceAspect / 2f);
            float cropOffset = (1f - cropHeight) * 0.5f;
            var pixels = new Color[width * height];

            for (int y = 0; y < height; y++)
            {
                float sourceV = cropOffset + (y + 0.5f) / height * cropHeight;
                for (int x = 0; x < width; x++)
                {
                    float sourceU = (x + 0.5f) / width;
                    pixels[y * width + x] = SampleBilinear(sourcePixels, sourceWidth, sourceHeight, sourceU, sourceV);
                }
            }

            var panorama = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "Tropical Archipelago Equirectangular Panorama",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4
            };
            panorama.SetPixels(pixels);
            panorama.Apply(false, false);
            File.WriteAllBytes(Path.GetFullPath(PanoramaPath), panorama.EncodeToPNG());
            Object.DestroyImmediate(panorama);
            AssetDatabase.ImportAsset(PanoramaPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(PanoramaPath) is TextureImporter panoramaImporter)
            {
                panoramaImporter.sRGBTexture = true;
                panoramaImporter.isReadable = false;
                panoramaImporter.wrapMode = TextureWrapMode.Clamp;
                panoramaImporter.filterMode = FilterMode.Bilinear;
                panoramaImporter.mipmapEnabled = false;
                panoramaImporter.textureCompression = TextureImporterCompression.Uncompressed;
                panoramaImporter.maxTextureSize = 2048;
                panoramaImporter.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(PanoramaPath);
        }

        static Color SampleBilinear(Color[] pixels, int width, int height, float u, float v)
        {
            float x = Mathf.Clamp01(u) * (width - 1);
            float y = Mathf.Clamp01(v) * (height - 1);
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int y1 = Mathf.Min(y0 + 1, height - 1);
            Color bottom = Color.Lerp(pixels[y0 * width + x0], pixels[y0 * width + x1], x - x0);
            Color top = Color.Lerp(pixels[y1 * width + x0], pixels[y1 * width + x1], x - x0);
            return Color.Lerp(bottom, top, y - y0);
        }

        static Material PrepareSkybox(Texture2D panorama)
        {
            var shader = Shader.Find("Skybox/Panoramic");
            if (shader == null || panorama == null)
            {
                Debug.LogWarning("Water Slides: panoramic skybox shader or panorama image is unavailable.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            if (material == null)
            {
                material = new Material(shader) { name = "TropicalArchipelagoSkybox" };
                AssetDatabase.CreateAsset(material, SkyboxPath);
            }
            else if (material.shader != shader)
                material.shader = shader;

            material.SetTexture("_MainTex", panorama);
            // Keep the sky reference vivid while avoiding a washed-out look in the mobile simulator.
            material.SetColor("_Tint", Color.white);
            material.SetFloat("_Exposure", 0.42f);
            material.SetFloat("_Rotation", 0f);
            material.SetFloat("_ImageType", 0f);
            material.SetFloat("_Layout", 0f);
            material.SetFloat("_MirrorOnBack", 1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material PrepareOceanMaterial()
        {
            var shader = Shader.Find("WaterSlides/TropicalOcean");
            if (shader == null)
            {
                Debug.LogWarning("Water Slides: WaterSlides/TropicalOcean shader was not imported yet.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(OceanPath);
            if (material == null)
            {
                material = new Material(shader) { name = "TropicalOcean" };
                AssetDatabase.CreateAsset(material, OceanPath);
            }
            else if (material.shader != shader)
                material.shader = shader;

            material.SetColor("_DeepColor", new Color(0.01f, 0.28f, 0.65f));
            material.SetColor("_ShallowColor", new Color(0.015f, 0.68f, 0.83f));
            material.SetColor("_FoamColor", new Color(0.65f, 0.94f, 1f));
            material.SetFloat("_WaveScale", 3.5f);
            material.SetFloat("_WaveSpeed", 0.18f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        static void ApplySkyAndOcean(Material skybox, Material ocean)
        {
            if (skybox != null)
            {
                RenderSettings.skybox = skybox;
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientIntensity = 0.8f;
                DynamicGI.UpdateEnvironment();
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.46f, 0.75f, 0.88f);
            RenderSettings.fogStartDistance = 1600f;
            RenderSettings.fogEndDistance = 5200f;

            var camera = Camera.main;
            if (camera != null)
                camera.clearFlags = CameraClearFlags.Skybox;

            var sun = RenderSettings.sun;
            if (sun == null)
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.type == LightType.Directional) { sun = light; break; }
            if (sun != null)
            {
                RenderSettings.sun = sun;
                sun.color = new Color(1f, 0.94f, 0.8f);
                sun.intensity = 0.9f;
                sun.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
            }

            var ground = Object.FindFirstObjectByType<EndlessGround>(FindObjectsInactive.Include);
            if (ground != null)
            {
                var renderer = ground.GetComponent<MeshRenderer>();
                if (renderer != null && ocean != null)
                    renderer.sharedMaterial = ocean;
                var so = new SerializedObject(ground);
                so.FindProperty("_depthBelowTarget").floatValue = SeaDepthBelowTrack;
                so.FindProperty("_followHeight").boolValue = true;
                so.FindProperty("_textureTileSize").floatValue = 1200f;
                so.FindProperty("_fogColor").colorValue = RenderSettings.fogColor;
                so.FindProperty("_fogStart").floatValue = 1600f;
                so.FindProperty("_fogEnd").floatValue = 5200f;
                so.FindProperty("_cameraFarPlane").floatValue = 12000f;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(ground);
            }
        }

        static void RebuildIslands()
        {
            var course = Object.FindFirstObjectByType<WaterSlideCourse>(FindObjectsInactive.Include);
            if (course == null)
            {
                Debug.LogWarning("Water Slides: course not found; sky and ocean were set up without islands.");
                return;
            }

            var oldRoot = GameObject.Find(IslandRootName);
            if (oldRoot != null)
                Undo.DestroyObjectImmediate(oldRoot);
            var root = new GameObject(IslandRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Tropical Archipelago");

            var rocks = LoadPrefabs(RockPaths);
            var palms = LoadPrefabs(PalmPaths);
            if (rocks.Count == 0 || palms.Count == 0)
            {
                Debug.LogWarning("Water Slides: palm or rock prefabs were not found; no islands were placed.");
                return;
            }

            var random = new System.Random(8204);
            int islandIndex = 0;
            foreach (var slide in course.Slides)
            {
                var frames = slide != null ? slide.RideFrames : null;
                if (frames == null || frames.Count < 2)
                    continue;

                float totalLength = 0f;
                for (int i = 1; i < frames.Count; i++)
                    totalLength += Vector3.Distance(frames[i - 1].Position, frames[i].Position);
                int islandsOnSlide = Mathf.Max(1, Mathf.FloorToInt(totalLength / 850f));
                for (int n = 0; n < islandsOnSlide && islandIndex < 28; n++)
                {
                    float along = (n + 1f) / (islandsOnSlide + 1f);
                    int frameIndex = Mathf.Clamp(Mathf.RoundToInt(along * (frames.Count - 1)), 0, frames.Count - 1);
                    var frame = frames[frameIndex];
                    Vector3 forward = frame.Forward.sqrMagnitude > 1e-5f ? frame.Forward.normalized : Vector3.forward;
                    Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                    if (right.sqrMagnitude < 1e-5f) right = Vector3.right;
                    float side = islandIndex % 2 == 0 ? 1f : -1f;
                    float offset = Mathf.Lerp(90f, 210f, (float)random.NextDouble());
                    Vector3 center = frame.Position + right * (side * offset);
                    float seaY = frame.Position.y - SeaDepthBelowTrack;
                    CreateIsland(root.transform, $"Island {islandIndex + 1:00}", center, seaY, rocks, palms, random);
                    islandIndex++;
                }
                if (islandIndex >= 28) break;
            }

            EditorUtility.SetDirty(root);
            Debug.Log($"Water Slides: placed {islandIndex} tropical island clusters around the course.");
        }

        static List<GameObject> LoadPrefabs(string[] paths)
        {
            var result = new List<GameObject>();
            foreach (var path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) result.Add(prefab);
            }
            return result;
        }

        static void CreateIsland(Transform parent, string name, Vector3 center, float seaY, List<GameObject> rocks, List<GameObject> palms, System.Random random)
        {
            var island = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(island, "Create Island");
            island.transform.SetParent(parent, false);
            island.transform.position = new Vector3(center.x, seaY, center.z);

            int rockCount = 3 + random.Next(3);
            for (int i = 0; i < rockCount; i++)
            {
                var prefab = rocks[random.Next(rocks.Count)];
                var rock = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(rock, "Place Island Rock");
                rock.transform.SetParent(island.transform, false);
                rock.transform.localPosition = new Vector3(RandomRange(random, -8f, 8f), RandomRange(random, 1.5f, 3f), RandomRange(random, -6f, 6f));
                rock.transform.localRotation = Quaternion.Euler(0f, RandomRange(random, 0f, 360f), 0f);
                rock.transform.localScale *= RandomRange(random, 0.75f, 1.2f);
            }

            int palmCount = 1 + random.Next(2);
            for (int i = 0; i < palmCount; i++)
            {
                var prefab = palms[random.Next(palms.Count)];
                var palm = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(palm, "Place Island Palm");
                palm.transform.SetParent(island.transform, false);
                palm.transform.localPosition = new Vector3(RandomRange(random, -7f, 7f), 2.3f, RandomRange(random, -6f, 6f));
                palm.transform.localRotation = Quaternion.Euler(0f, RandomRange(random, 0f, 360f), 0f);
                palm.transform.localScale *= RandomRange(random, 0.85f, 1.3f);
            }
        }

        static float RandomRange(System.Random random, float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
    }
}
