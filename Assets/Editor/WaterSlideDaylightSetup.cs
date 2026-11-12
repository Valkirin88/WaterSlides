using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;

public static class WaterSlideDaylightSetup
{
    [MenuItem("Water Slides/Apply Clear Daylight")]
    public static void Apply()
    {
        if (Application.isPlaying) return;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.72f, 0.79f, 0.86f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.61f, 0.66f);
        RenderSettings.ambientGroundColor = new Color(0.38f, 0.43f, 0.47f);
        RenderSettings.ambientIntensity = 1f;
        var sun = RenderSettings.sun;
        if (sun != null)
        {
            Undo.RecordObject(sun, "Clear daylight");
            sun.intensity = 1.35f;
            sun.color = new Color(1f, 0.98f, 0.94f);
            sun.shadowStrength = 0.65f;
            EditorUtility.SetDirty(sun);
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("Clear daylight applied: neutral sun and three-color ambient fill; course geometry unchanged.");
    }
}
