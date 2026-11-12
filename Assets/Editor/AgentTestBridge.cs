using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// -----------------------------------------------------------------------------
// ДИАГНОСТИЧЕСКИЙ МОСТ (временный, для отладки «отбрасывает с кромки»).
// Позволяет внешнему агенту гонять автоматические тест-заезды: он пишет файл
// AgentBridge/cmd.json — мост входит в Play Mode, вешает зонд AgentCornerProbe
// (газ + руление к стенке, лог физики каждый шаг), по завершении выходит из
// Play Mode. Состояние публикуется в AgentBridge/status.json. Ничего не делает,
// пока команд нет. Можно удалить вместе с Assets/Scripts/Testing/ после отладки.
// -----------------------------------------------------------------------------
[InitializeOnLoad]
public static class AgentTestBridge
{
    static readonly string Dir = Path.Combine(Directory.GetCurrentDirectory(), "AgentBridge");
    static readonly string CmdPath = Path.Combine(Dir, "cmd.json");
    static readonly string StatusPath = Path.Combine(Dir, "status.json");
    static double _nextPoll;
    // В EditorPrefs, а не SessionState: после перезапуска редактора SessionState пуст, и мост
    // повторно выполнял последнюю команду из cmd.json (например, запускал тест-заезд).
    const string LastCmdKey = "WaterSlides.AgentBridge.LastCmdId";

    [Serializable]
    class Cmd
    {
        public int id;
        public string action = "";   // "play_test" | "stop" | "refresh" | "tune_player"
        public string steer = "alt"; // "right" | "left" | "alt" | "none"
        public float duration = 60f;
        // tune_player: отрицательное значение — поле не трогать.
        public float autoGrip = -1f;
        public float wallSlope = -1f;
        public float minSpeed = -1f;
        public float maxSpeed = -1f;
        // tune_course: расчётная скорость вылета с трамплинов (AssumedExitSpeed).
        public float exitSpeed = -1f;
        // menu: путь пункта меню редактора (EditorApplication.ExecuteMenuItem).
        public string menuPath = "";
        public string component = "";   // tune: имя типа компонента (например PlayerFlightRagdoll)
        public string field = "";       // tune/tune_course: имя сериализованного поля
        public float value = float.NaN; // tune_course: его новое значение (float/int/bool как 0/1)
        // screenshot: имя файла в AgentBridge/ и задержка (с) после команды; только в Play Mode.
        public string file = "";
        public float delay = 0f;
    }

    // ScreenCapture.CaptureScreenshot в редакторе пишет только ВИДИМЫЙ Game view:
    // после перезапуска редактора раскладка открылась без него, и снимки
    // молча не создавались (27.10.2026). Перед заездом и снимком фокусируем окно.
    // Окно — СИМУЛЯТОР устройства (Device Simulator, встроен в Unity 6), не Game:
    // игра проверяется в симуляторе (решение пользователя 27.10.2026). Game view —
    // только если тип окна симулятора не нашёлся; что открыли — в focus_result.txt.
    static void FocusGameView()
    {
        var type = FindType("UnityEditor.DeviceSimulation.SimulatorWindow")
                   ?? typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        try { File.WriteAllText(Path.Combine(Dir, "focus_result.txt"), type != null ? type.FullName : "no window type"); } catch { }
        if (type != null)
            EditorWindow.GetWindow(type)?.Focus();
    }

    static Type FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = null;
            try { t = asm.GetType(fullName); } catch { }
            if (t != null)
                return t;
        }
        return null;
    }

    static double _screenshotAt = -1;
    static string _screenshotFile;

    static AgentTestBridge()
    {
        Directory.CreateDirectory(Dir);
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < _nextPoll)
            return;
        _nextPoll = EditorApplication.timeSinceStartup + 0.5;

        try
        {
            if (!EditorApplication.isCompiling)
                TryProcessCommand();

            if (_screenshotAt >= 0 && EditorApplication.timeSinceStartup >= _screenshotAt)
            {
                _screenshotAt = -1;
                if (Application.isPlaying)
                    ScreenCapture.CaptureScreenshot(Path.Combine(Dir, _screenshotFile));
            }

            // Тест закончился — выходим из Play Mode (только для НАШИХ запусков).
            if (Application.isPlaying && AgentCornerProbe.Finished && SessionState.GetBool("agent_run_active", false))
            {
                SessionState.SetBool("agent_run_active", false);
                EditorApplication.ExitPlaymode();
            }

            WriteStatus();
        }
        catch (Exception e)
        {
            try { File.WriteAllText(Path.Combine(Dir, "bridge_error.txt"), e.ToString()); } catch { }
        }
    }

    static void TryProcessCommand()
    {
        if (!File.Exists(CmdPath))
            return;

        Cmd cmd;
        try { cmd = JsonUtility.FromJson<Cmd>(File.ReadAllText(CmdPath)); }
        catch { return; }

        if (cmd == null || cmd.id == EditorPrefs.GetInt(LastCmdKey, -1))
            return;

        EditorPrefs.SetInt(LastCmdKey, cmd.id);

        switch (cmd.action)
        {
            case "play_test":
                FocusGameView();
                SessionState.SetString("agent_steer", cmd.steer);
                SessionState.SetFloat("agent_duration", cmd.duration);
                SessionState.SetBool("agent_test_pending", true);
                if (!Application.isPlaying)
                    EditorApplication.EnterPlaymode();
                break;

            case "stop":
                SessionState.SetBool("agent_test_pending", false);
                SessionState.SetBool("agent_run_active", false);
                if (Application.isPlaying)
                    EditorApplication.ExitPlaymode();
                break;

            case "refresh":
                AssetDatabase.Refresh();
                break;

            // Выполнить пункт меню редактора (например, "Water Slides/Setup Endless Environment")
            // и сохранить открытые сцены.
            case "menu":
                if (Application.isPlaying || string.IsNullOrEmpty(cmd.menuPath))
                    break;
                bool ok = EditorApplication.ExecuteMenuItem(cmd.menuPath);
                File.WriteAllText(Path.Combine(Dir, "menu_result.txt"), ok ? "ok" : "not found: " + cmd.menuPath);
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                break;

            // Снимок Game view через delay секунд (в Play Mode) в AgentBridge/<file>.
            case "screenshot":
                if (string.IsNullOrEmpty(cmd.file))
                    break;
                FocusGameView();
                _screenshotFile = cmd.file;
                _screenshotAt = EditorApplication.timeSinceStartup + Mathf.Max(cmd.delay, 0f);
                break;

            // Правка полей WaterSlideCourse в открытой сцене (без перегенерации —
            // её запускать отдельной командой regenerate).
            case "tune_course":
                if (Application.isPlaying)
                    break;
                var tuneCourse = UnityEngine.Object.FindFirstObjectByType<WaterSlides.WaterSlideCourse>();
                if (tuneCourse == null)
                    break;
                var courseSo = new SerializedObject(tuneCourse);
                if (cmd.exitSpeed > 0f)
                    courseSo.FindProperty("AssumedExitSpeed").floatValue = cmd.exitSpeed;
                if (!string.IsNullOrEmpty(cmd.field) && !float.IsNaN(cmd.value))
                {
                    var prop = courseSo.FindProperty(cmd.field);
                    if (prop == null)
                        File.WriteAllText(Path.Combine(Dir, "menu_result.txt"), "no field: " + cmd.field);
                    else if (prop.propertyType == SerializedPropertyType.Float) prop.floatValue = cmd.value;
                    else if (prop.propertyType == SerializedPropertyType.Integer) prop.intValue = Mathf.RoundToInt(cmd.value);
                    else if (prop.propertyType == SerializedPropertyType.Boolean) prop.boolValue = cmd.value > 0.5f;
                }
                courseSo.ApplyModifiedPropertiesWithoutUndo();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tuneCourse.gameObject.scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                break;

            // Перегенерация трассы в открытой сцене: применяет исправленный профиль
            // жёлоба и пересчитывает честность поворотов под ТЕКУЩИЕ физические
            // значения игрока (потолок скорости, сцепление). Стабильный вариант —
            // тот же лэйаут, обновлённая геометрия.
            case "regenerate":
                if (Application.isPlaying)
                    break;
                var course = UnityEngine.Object.FindFirstObjectByType<WaterSlides.WaterSlideCourse>();
                if (course == null)
                    break;
                course.RegenerateStable();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(course.gameObject.scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                break;

            // Правка сериализованных полей PlayerView в ОТКРЫТОЙ сцене (правка файла
            // сцены на диске при запущенном редакторе не подхватилась бы без Reload).
            // Правка любого сериализованного поля первого найденного компонента типа
            // component в открытой сцене: {"action":"tune","component":"PlayerFlightRagdoll","field":"_flightRagdoll","value":1}.
            case "tune":
                if (Application.isPlaying || string.IsNullOrEmpty(cmd.component) || string.IsNullOrEmpty(cmd.field))
                    break;
                var compType = FindType(cmd.component) ?? FindType("WaterSlides." + cmd.component);
                var target = compType != null ? UnityEngine.Object.FindFirstObjectByType(compType) : null;
                if (target == null)
                {
                    File.WriteAllText(Path.Combine(Dir, "menu_result.txt"), "no component: " + cmd.component);
                    break;
                }
                var tso = new SerializedObject(target);
                var tprop = tso.FindProperty(cmd.field);
                if (tprop == null)
                {
                    File.WriteAllText(Path.Combine(Dir, "menu_result.txt"), "no field: " + cmd.field);
                    break;
                }
                if (tprop.propertyType == SerializedPropertyType.Float) tprop.floatValue = cmd.value;
                else if (tprop.propertyType == SerializedPropertyType.Integer) tprop.intValue = Mathf.RoundToInt(cmd.value);
                else if (tprop.propertyType == SerializedPropertyType.Boolean) tprop.boolValue = cmd.value > 0.5f;
                tso.ApplyModifiedPropertiesWithoutUndo();
                File.WriteAllText(Path.Combine(Dir, "menu_result.txt"), "ok " + cmd.component + "." + cmd.field + "=" + cmd.value);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(((Component)target).gameObject.scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                break;

            case "tune_player":
                if (Application.isPlaying)
                    break;
                var view = UnityEngine.Object.FindFirstObjectByType<PlayerView>();
                if (view == null)
                    break;
                var so = new SerializedObject(view);
                if (cmd.autoGrip >= 0f)
                    so.FindProperty("_autoGripFraction").floatValue = cmd.autoGrip;
                if (cmd.wallSlope >= 0f)
                    so.FindProperty("_wallSlope").floatValue = cmd.wallSlope;
                if (cmd.minSpeed >= 0f)
                    so.FindProperty("_minRideSpeed").floatValue = cmd.minSpeed;
                if (cmd.maxSpeed >= 0f)
                    so.FindProperty("_maxSlideSpeed").floatValue = cmd.maxSpeed;
                so.ApplyModifiedPropertiesWithoutUndo();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                break;
        }
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode)
            return;
        if (!SessionState.GetBool("agent_test_pending", false))
            return;

        SessionState.SetBool("agent_test_pending", false);
        SessionState.SetBool("agent_run_active", true);

        // Диагностический зонд создаётся из редактора только на время теста —
        // в геймплейный код и сцену ничего не добавляется.
        var go = new GameObject("AgentCornerProbe");
        var probe = go.AddComponent<AgentCornerProbe>();
        probe.SteerMode = SessionState.GetString("agent_steer", "alt");
        probe.Duration = SessionState.GetFloat("agent_duration", 60f);
        probe.OutputDir = Dir;
    }

    static void WriteStatus()
    {
        string probeState = "absent";
        if (AgentCornerProbe.Finished) probeState = "finished";
        else if (AgentCornerProbe.Running) probeState = "running";

        string json =
            "{\n" +
            $"  \"heartbeat\": \"{DateTime.Now:HH:mm:ss}\",\n" +
            $"  \"playing\": {(Application.isPlaying ? "true" : "false")},\n" +
            $"  \"compiling\": {(EditorApplication.isCompiling ? "true" : "false")},\n" +
            $"  \"lastCmdId\": {EditorPrefs.GetInt(LastCmdKey, -1)},\n" +
            $"  \"probe\": \"{probeState}\",\n" +
            $"  \"lastCsv\": \"{AgentCornerProbe.LastCsvPath?.Replace("\\", "/")}\"\n" +
            "}\n";
        File.WriteAllText(StatusPath, json);
    }
}
