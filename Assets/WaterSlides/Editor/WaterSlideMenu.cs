using UnityEditor;
using UnityEngine;

namespace WaterSlides.Editor
{
    [CustomEditor(typeof(WaterSlideCourse))]
    public class WaterSlideCourseEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var course = (WaterSlideCourse)target;

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate Course (Randomize)"))
                course.GenerateCourse();

            if (GUILayout.Button("Clear Course"))
                course.ClearCourse();

            if (changed && course.LiveUpdate && !Application.isPlaying)
            {
                // Defer the rebuild: destroying/creating GameObjects during OnInspectorGUI is unsafe.
                EditorApplication.delayCall += () =>
                {
                    if (course != null)
                        course.RegenerateStable();
                };
            }
        }
    }

    public static class WaterSlideMenu
    {
        [MenuItem("GameObject/Water Slides/Water Slide", false, 10)]
        static void CreateSlide()
        {
            var go = new GameObject("Water Slide");
            Undo.RegisterCreatedObjectUndo(go, "Create Water Slide");
            go.AddComponent<WaterSlideGenerator>();

            if (Selection.activeTransform != null)
                go.transform.SetParent(Selection.activeTransform);

            Selection.activeGameObject = go;
        }

        [MenuItem("GameObject/Water Slides/Water Slide Course", false, 11)]
        static void CreateCourse()
        {
            var go = new GameObject("Water Slide Course");
            Undo.RegisterCreatedObjectUndo(go, "Create Water Slide Course");
            var course = go.AddComponent<WaterSlideCourse>();
            course.GenerateCourse();

            if (Selection.activeTransform != null)
                go.transform.SetParent(Selection.activeTransform);

            Selection.activeGameObject = go;
        }

        [MenuItem("GameObject/Water Slides/Demo Scene Setup", false, 20)]
        static void SetupDemoScene()
        {
            var existing = Object.FindFirstObjectByType<WaterSlideDemoSetup>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            var go = new GameObject("WaterSlide Demo");
            Undo.RegisterCreatedObjectUndo(go, "Setup Water Slide Demo");
            go.AddComponent<WaterSlideDemoSetup>();

            Selection.activeGameObject = go;
        }
    }
}
