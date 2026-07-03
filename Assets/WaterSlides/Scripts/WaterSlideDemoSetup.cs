using UnityEngine;

namespace WaterSlides
{
    [ExecuteAlways]
    public class WaterSlideDemoSetup : MonoBehaviour
    {
        public bool SetupOnEnable = true;

        void OnEnable()
        {
            if (SetupOnEnable)
                Setup();
        }

        [ContextMenu("Setup Demo")]
        public void Setup()
        {
            EnsureLighting();
            EnsureCourse();
            EnsureCamera();
        }

        void EnsureLighting()
        {
            if (FindAnyObjectByType<Light>() != null)
                return;

            var lightGo = new GameObject("Directional Light");
            lightGo.transform.SetParent(transform);
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
        }

        void EnsureCourse()
        {
            var course = GetComponentInChildren<WaterSlideCourse>();
            if (course == null)
            {
                var courseGo = new GameObject("Water Slide Course");
                courseGo.transform.SetParent(transform);
                courseGo.transform.position = Vector3.zero;
                course = courseGo.AddComponent<WaterSlideCourse>();
                course.SlideCount = 3;
                course.JumpGap = new FloatRange { Min = 5f, Max = 8f };
                course.JumpDrop = new FloatRange { Min = 0.75f, Max = 2f };
                course.SlideLength = new FloatRange { Min = 50f, Max = 60f };
                course.SlideDrop = new FloatRange { Min = 16f, Max = 22f };
                course.AllowLoopsAndSpirals = false;
                course.RandomizeEachGeneration = true;
                course.RandomSeed = 7;
                _ = course.WaterPreset;
                course.GenerateCourse();
            }
        }

        void EnsureCamera()
        {
            // In play mode the player owns Camera.main — do not snap it to the editor demo pose.
            if (Application.isPlaying && FindAnyObjectByType<PlayerView>() != null)
                return;

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                camGo.transform.SetParent(transform);
                cam.transform.position = new Vector3(-15f, 35f, -10f);
                cam.transform.rotation = Quaternion.Euler(35f, 35f, 0f);
            }
        }
    }
}
