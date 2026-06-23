using UnityEditor;
using UnityEngine;

namespace WaterSlides.Editor
{
    [CustomEditor(typeof(WaterSlideGenerator))]
    public class WaterSlideGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var generator = (WaterSlideGenerator)target;

            EditorGUILayout.Space();
            if (GUILayout.Button("Rebuild Slide"))
                generator.Rebuild();

            if (GUILayout.Button("Add Control Point At End"))
            {
                Undo.RecordObject(generator, "Add Control Point");
                var points = generator.ControlPoints;
                var last = points[^1];
                var prev = points.Count > 1 ? points[^2] : last - Vector3.forward * 5f;
                var delta = last - prev;
                points.Add(last + delta);
                generator.Rebuild();
                EditorUtility.SetDirty(generator);
            }
        }

        void OnSceneGUI()
        {
            var generator = (WaterSlideGenerator)target;
            var points = generator.ControlPoints;
            if (points == null)
                return;

            for (int i = 0; i < points.Count; i++)
            {
                EditorGUI.BeginChangeCheck();
                var world = generator.transform.TransformPoint(points[i]);
                var handleSize = HandleUtility.GetHandleSize(world) * 0.12f;
                var newWorld = Handles.FreeMoveHandle(world, handleSize, Vector3.zero, Handles.SphereHandleCap);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(generator, "Move Control Point");
                    points[i] = generator.transform.InverseTransformPoint(newWorld);
                    generator.Rebuild();
                    EditorUtility.SetDirty(generator);
                }

                Handles.Label(world + Vector3.up * 0.6f, $"P{i}");
            }

            var labelPos = generator.transform.TransformPoint(generator.ControlPoints[0]) + Vector3.up * 2f;
            var typeLabel = generator.Enclosure == SlideEnclosure.Open ? "Open" : "Closed";
            Handles.Label(labelPos, typeLabel);
        }
    }
}
