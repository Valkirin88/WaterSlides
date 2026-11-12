using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// -----------------------------------------------------------------------------
// ДИАГНОСТИЧЕСКИЙ ЗОНД (временный, создаётся AgentTestBridge только на время
// автоматического тест-заезда). Симулирует ввод (газ W + руление к стенке),
// каждый физический шаг пишет CSV со состоянием тела и внутренними таймерами
// PlayerView (через reflection, ничего не меняя), а резкие рывки скорости и
// телепорты позиции фиксирует отдельными событиями. Удалить после отладки.
// -----------------------------------------------------------------------------
[DefaultExecutionOrder(200)]
public class AgentCornerProbe : MonoBehaviour
{
    public static bool Running { get; private set; }
    public static bool Finished { get; private set; }
    public static string LastCsvPath { get; private set; }

    public string SteerMode = "alt"; // right | left | alt | none | hold | lean (см. Update)
    public float Duration = 60f;
    public string OutputDir;

    PlayerView _view;
    Rigidbody _rb;
    object _inputController;
    PropertyInfo _inputCurrentProp;
    Type _inputStateType;

    // PlayerView internals (только чтение). Путевая модель: большая часть
    // телеметрии — публичные свойства (RideSpeed/RideLateral/...), reflection
    // остался для пары приватных таймеров.
    FieldInfo _fTimeSince, _fContain, _fSuppress, _fSteerInput, _fCurvature;

    StreamWriter _csv;
    StreamWriter _events;
    float _t;
    Vector3 _prevVel;
    Vector3 _prevPos;
    bool _prevValid;
    bool _prevRiding;
    int _eventCount;
    readonly StringBuilder _sb = new();

    Camera _camera;
    Vector3 _lastRimPoint;
    Vector3 _lastRimUp;
    float _lastRimTime = float.NegativeInfinity;
    float _lastCamEventTime = float.NegativeInfinity;

    void Awake()
    {
        Running = true;
        Finished = false;
        // Игра не должна вставать на паузу, когда окно редактора теряет фокус —
        // тест гоняется под внешним управлением.
        Application.runInBackground = true;
    }

    void Start()
    {
        try
        {
            _view = FindFirstObjectByType<PlayerView>();
            var entry = FindFirstObjectByType<EntryPoint>();
            if (_view == null || entry == null)
            {
                Fail("PlayerView/EntryPoint не найдены в сцене");
                return;
            }

            _rb = _view.Rigidbody;

            var epType = typeof(EntryPoint);
            _inputController = epType.GetField("_inputController", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(entry);
            if (_inputController == null)
            {
                Fail("_inputController недоступен");
                return;
            }
            _inputCurrentProp = _inputController.GetType().GetProperty("Current");
            _inputStateType = typeof(InputController.State);

            var vt = typeof(PlayerView);
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
            _fTimeSince = vt.GetField("_timeSinceSlideContact", F);
            _fContain = vt.GetField("_landingContainTimer", F);
            _fSuppress = vt.GetField("_guideSuppressTimer", F);
            _fSteerInput = vt.GetField("_slideSteerInput", F);
            _fCurvature = vt.GetField("_smoothedCurvature", F);

            Directory.CreateDirectory(OutputDir);
            string stamp = DateTime.Now.ToString("HHmmss");
            LastCsvPath = Path.Combine(OutputDir, $"run_{stamp}.csv");
            _csv = new StreamWriter(LastCsvPath, false, Encoding.UTF8);
            _events = new StreamWriter(Path.Combine(OutputDir, $"run_{stamp}_events.txt"), false, Encoding.UTF8);
            _csv.WriteLine("t;px;py;pz;speed;vx;vy;vz;dv;posSnap;riding;rideSpeed;lateral;latVel;pathS;curv;steer;tSince;contain;suppress;danger");
            _events.WriteLine($"# Тест-заезд (путевая модель): steer={SteerMode}, duration={Duration}");
        }
        catch (Exception e)
        {
            Fail(e.ToString());
        }
    }

    void Fail(string message)
    {
        try
        {
            File.WriteAllText(Path.Combine(OutputDir ?? ".", "probe_error.txt"), message);
        }
        catch { }
        Running = false;
        Finished = true;
        enabled = false;
    }

    // Руление по расписанию: разгон без руля, затем полосы вправо/влево с паузами.
    // В воздухе руль отпускается (как живой игрок): иначе стрейф уводит дугу
    // прыжка мимо приземления и заезд вырождается в падения в пустоту.
    float CurrentTurnAxis()
    {
        // "airhold" — руль зажат ВСЕГДА, включая воздух и стартовое падение:
        // проверка блокировки руления до первого касания горки.
        if (SteerMode == "airhold")
            return 1f;

        // "aim" — на горке руль свободен, в воздухе стрейф доворачивает к ближайшей
        // точке СЛЕДУЮЩЕЙ горки (имитация игрока, который целится в приземление):
        // проверка бесконечной трассы, где без доворота прыжок законно промахивается.
        if (SteerMode == "aim" || SteerMode == "drive" || SteerMode == "hold" || SteerMode == "lean")
            return _view != null && !_view.IsOnSlide ? AimAtNextSlide() : 0f;

        if (_view != null && !_view.IsOnSlide)
            return 0f;

        switch (SteerMode)
        {
            case "right": return _t < 4f ? 0f : 1f;
            case "left": return _t < 4f ? 0f : -1f;
            case "none": return 0f;
            case "gas": return 0f;
            default: // alt
                if (_t < 4f) return 0f;
                float cycle = (_t - 4f) % 30f;
                if (cycle < 12f) return 1f;
                if (cycle < 15f) return 0f;
                if (cycle < 27f) return -1f;
                return 0f;
        }
    }

    float AimAtNextSlide()
    {
        var course = FindFirstObjectByType<WaterSlides.WaterSlideCourse>();
        if (course == null || _view.CurrentSlide == null)
            return 0f;

        var slides = course.Slides;
        int idx = -1;
        for (int i = 0; i < slides.Count; i++)
            if (slides[i] == _view.CurrentSlide) { idx = i; break; }
        if (idx < 0)
            return 0f;

        // Цель — ближайшая к точке «8 м впереди по курсу» точка ТЕКУЩЕЙ или следующей
        // горки: отрыв на перегибе посреди горки должен возвращать на неё же, а не
        // уводить стрейфом к следующей (так заезд 2026-10-26 падал в пустоту).
        Vector3 heading = _view.FlightHeading;
        Vector3 pos = _view.MovementTransform.position;
        Vector3 probe = pos + heading * 8f;
        Vector3 best = Vector3.zero;
        float bestSq = float.MaxValue;
        for (int s = idx; s <= idx + 1 && s < slides.Count; s++)
        {
            foreach (var f in slides[s].RideFrames)
            {
                Vector3 d = f.Position - probe;
                d.y = 0f;
                if (d.sqrMagnitude < bestSq) { bestSq = d.sqrMagnitude; best = f.Position; }
            }
        }

        Vector3 right = Vector3.Cross(Vector3.up, heading).normalized;
        float error = Vector3.Dot(best - pos, right);
        return Mathf.Abs(error) < 0.3f ? 0f : Mathf.Sign(error);
    }

    // "drive" — «внимательный игрок»: на горке тормозит S, если в ближайших ~1.2 с
    // пути есть поворот, который на текущей скорости не удержать (v²·κ > 0.8·сцепление);
    // иначе газ до 15 м/с. Проверка правила «тормозишь — держишься, нет — вылетаешь».
    bool NeedsBrakeAhead(float speed)
    {
        var slide = _view.CurrentSlide;
        if (slide == null || !_view.IsOnSlide)
            return false;

        var frames = slide.RideFrames;
        if (frames == null || frames.Count < 3)
            return false;

        Vector3 pos = _view.MovementTransform.position;
        int start = 0;
        float bestSq = float.MaxValue;
        for (int i = 0; i < frames.Count; i++)
        {
            float d = (frames[i].Position - pos).sqrMagnitude;
            if (d < bestSq) { bestSq = d; start = i; }
        }

        // Перед концом горки — сброс до расчётной скорости прыжка (как внимательный
        // игрок): на потолке скорости вход следующей горки перелетается.
        var course = FindFirstObjectByType<WaterSlides.WaterSlideCourse>();
        if (course != null)
        {
            float exitTarget = course.AssumedExitSpeed * 1.05f;
            if (speed > exitTarget)
            {
                float remaining = 0f;
                for (int i = frames.Count - 1; i > start; i--)
                    remaining += Vector3.Distance(frames[i - 1].Position, frames[i].Position);
                float brakeDist = (speed * speed - exitTarget * exitTarget) / (2f * Mathf.Max(_view.BrakeDeceleration, 1f)) + 3f;
                if (remaining < brakeDist)
                    return true;
            }
        }

        float lookahead = Mathf.Clamp(speed * 1.2f + 4f, 8f, 40f);
        float travelled = 0f, chunk = 0f;
        Vector3 chunkFwd = frames[start].Forward;
        float grip = _view.CorneringGrip * 0.8f;
        for (int j = start + 1; j < frames.Count && travelled < lookahead; j++)
        {
            float step = Vector3.Distance(frames[j - 1].Position, frames[j].Position);
            travelled += step;
            chunk += step;
            if (chunk < 1.2f)
                continue;
            Vector3 up = frames[j].Up;
            Vector3 a = Vector3.ProjectOnPlane(chunkFwd, up);
            Vector3 b = Vector3.ProjectOnPlane(frames[j].Forward, up);
            if (a.sqrMagnitude > 0.04f && b.sqrMagnitude > 0.04f)
            {
                float kappa = Vector3.Angle(a, b) * Mathf.Deg2Rad / chunk;
                if (kappa > 1e-3f && speed * speed * kappa > grip)
                    return true;
            }
            chunk = 0f;
            chunkFwd = frames[j].Forward;
        }

        return false;
    }

    void Update()
    {
        if (Finished || _view == null || _inputCurrentProp == null)
            return;

        // Перезаписываем состояние ввода ПОСЛЕ EntryPoint.Update (порядок 200 > 100):
        // газ до потолка скорости, руль по расписанию. Режим "brake" — вместо руля
        // на t=8..16 зажат тормоз S (проверка провала ниже минимальной скорости).
        float speed = _rb != null ? _rb.linearVelocity.magnitude : 0f;
        bool braking = (SteerMode == "brake" && _t >= 8f && _t < 16f)
            || (SteerMode == "drive" && NeedsBrakeAhead(speed));
        // "gas" — W зажат ПОСТОЯННО (без потолка 15): проверка выноса по инерции
        // на полном газу, когда регулятор поворотов отключён зажатым газом.
        bool fullGas = SteerMode == "gas";
        // "slider" — руль-ползунок (палец): цель поперечного положения качается
        // синусом ±1 с периодом ~8 с, в воздухе стрейф по той же цели.
        bool slider = SteerMode == "slider";
        float sliderTarget = slider ? Mathf.Sin(_t * 0.8f) : 0f;
        // "hold" — палец неподвижно в центре (игрок не рулит): проверка, что без руля
        // в поворотах выносит. "lean" — палец «ложится» внутрь текущего поворота
        // (по сглаженной кривизне пути): проверка, что с рулём держится. В обоих
        // режимах газа нет (у живого игрока его тоже нет), в воздухе — доворот к
        // следующей горке, как в "aim".
        bool hold = SteerMode == "hold";
        bool lean = SteerMode == "lean";
        float leanTarget = 0f;
        if (lean && _view.IsOnSlide && _fCurvature != null)
        {
            float k = (float)_fCurvature.GetValue(_view);
            leanTarget = Mathf.Abs(k) > 0.01f ? Mathf.Sign(k) : 0f;
        }
        bool finger = slider || hold || lean;
        float fingerTarget = slider ? sliderTarget : leanTarget;
        var state = new InputController.State
        {
            TurnAxis = SteerMode == "brake" ? 0f : (slider ? sliderTarget : CurrentTurnAxis()),
            ThrottleAxis = braking ? -1f : (hold || lean ? 0f : (fullGas || speed < 15f ? 1f : 0f)),
            JumpPressed = false,
            LateralTarget = fingerTarget,
            HasLateralTarget = finger
        };
        _inputCurrentProp.SetValue(_inputController, state);
    }

    // Камера двигается в LateUpdate — здесь же меряем её высоту над плоскостью
    // кромки жёлоба. «Камера завалилась за горку» = клиренс ушёл в минус.
    void LateUpdate()
    {
        if (Finished || _view == null || _events == null)
            return;

        if (_camera == null)
        {
            _camera = _view.GetComponentInChildren<Camera>(true);
            if (_camera == null)
                return;
        }

        bool hasRim = _view.TryGetFlumeRimPlane(out Vector3 rimPoint, out Vector3 rimUp);
        if (hasRim)
        {
            _lastRimPoint = rimPoint;
            _lastRimUp = rimUp;
            _lastRimTime = _t;
        }

        bool staleValid = !hasRim && _t - _lastRimTime < 2f;
        if (!hasRim && !staleValid)
            return;

        Vector3 p = hasRim ? rimPoint : _lastRimPoint;
        Vector3 up = hasRim ? rimUp : _lastRimUp;
        float clearance = Vector3.Dot(_camera.transform.position - p, up);

        // Не чаще 4 раз в секунду, только реальные нырки ниже кромки.
        if (clearance < 0.1f && _t - _lastCamEventTime > 0.25f)
        {
            _lastCamEventTime = _t;
            _events.WriteLine(
                $"t={_t:F3} CAM_BELOW_RIM clearance={clearance:F2} rim={(hasRim ? "live" : "stale")} " +
                $"onSlide={(_view.IsOnSlide ? 1 : 0)} camY={_camera.transform.position.y:F2}");
            _eventCount++;
        }

        // Подтягивание камеры к игроку во время езды (полная дистанция ~5.6 м):
        // ближе 4 м при контакте — сработал какой-то из клампов заслонения.
        float camDist = Vector3.Distance(_camera.transform.position, _view.MovementTransform.position + Vector3.up);
        if (camDist < 4f && _view.IsOnSlide && _t - _lastCamEventTime > 0.25f)
        {
            _lastCamEventTime = _t;
            _events.WriteLine($"t={_t:F3} CAM_PULLED_IN dist={camDist:F2} clearance={clearance:F2}");
            _eventCount++;
        }

        // Отставание камеры в полёте: игрок улетел заметно дальше штатной дистанции —
        // камеру где-то держит кламп, а потом она догонит рывком.
        if (camDist > 9f && !_view.IsOnSlide && _t - _lastCamEventTime > 0.25f)
        {
            _lastCamEventTime = _t;
            _events.WriteLine($"t={_t:F3} CAM_LAG dist={camDist:F2}");
            _eventCount++;
        }
    }

    void FixedUpdate()
    {
        if (Finished || _view == null || _rb == null || _csv == null)
            return;

        _t += Time.fixedDeltaTime;
        if (_t >= Duration)
        {
            FinishRun("время вышло");
            return;
        }

        try
        {
            Vector3 pos = _rb.position;
            Vector3 vel = _rb.linearVelocity;
            bool riding = _view.IsOnSlide;

            float dv = 0f, posSnap = 0f;
            if (_prevValid)
            {
                // Ожидаемое изменение скорости за шаг — гравитация; всё сверх — «пинок».
                Vector3 gStep = Physics.gravity * _view.GravityMultiplier * Time.fixedDeltaTime;
                dv = (vel - _prevVel - gStep).magnitude;
                // Ожидаемое смещение — интеграция скорости; всё сверх — телепорт позиции.
                posSnap = (pos - _prevPos - _prevVel * Time.fixedDeltaTime).magnitude;
            }

            float tSince = (float)_fTimeSince.GetValue(_view);
            float contain = (float)_fContain.GetValue(_view);
            float suppress = (float)_fSuppress.GetValue(_view);
            float steerIn = (float)_fSteerInput.GetValue(_view);
            float curvature = (float)_fCurvature.GetValue(_view);
            float rideSpeed = _view.RideSpeed;
            float lateral = _view.RideLateral;
            float latVel = _view.RideLateralVelocity;
            float pathS = _view.RidePathDistance;

            var ci = CultureInfo.InvariantCulture;
            _sb.Clear();
            _sb.Append(_t.ToString("F3", ci)).Append(';')
               .Append(pos.x.ToString("F3", ci)).Append(';').Append(pos.y.ToString("F3", ci)).Append(';').Append(pos.z.ToString("F3", ci)).Append(';')
               .Append(vel.magnitude.ToString("F2", ci)).Append(';')
               .Append(vel.x.ToString("F2", ci)).Append(';').Append(vel.y.ToString("F2", ci)).Append(';').Append(vel.z.ToString("F2", ci)).Append(';')
               .Append(dv.ToString("F2", ci)).Append(';').Append(posSnap.ToString("F3", ci)).Append(';')
               .Append(riding ? 1 : 0).Append(';')
               .Append(rideSpeed.ToString("F2", ci)).Append(';')
               .Append(lateral.ToString("F3", ci)).Append(';').Append(latVel.ToString("F2", ci)).Append(';')
               .Append(pathS.ToString("F2", ci)).Append(';').Append(curvature.ToString("F4", ci)).Append(';')
               .Append(steerIn.ToString("F1", ci)).Append(';')
               .Append(tSince.ToString("F2", ci)).Append(';')
               .Append(contain.ToString("F2", ci)).Append(';').Append(suppress.ToString("F2", ci)).Append(';')
               .Append(_view.CornerDanger.ToString("F2", ci)); // опасность ближайшего поворота (подкраска скорости в HUD)
            _csv.WriteLine(_sb.ToString());

            // Переходы езда↔полёт — главные события новой модели.
            if (_prevValid && riding != _prevRiding)
            {
                _events.WriteLine(riding
                    ? $"t={_t:F3} ATTACH v={vel.magnitude:F1} rideSpeed={rideSpeed:F1} lat={lateral:F2} pathS={pathS:F1}"
                    : $"t={_t:F3} DETACH v={vel.magnitude:F1} lat={lateral:F2} latVel={latVel:F2} pathS={pathS:F1} sup={suppress:F2}");
                _eventCount++;
            }

            // Пинки/телепорты в ЕЗДЕ не считаем: ведение по пути задаёт скорость
            // напрямую (dv штатно большой на пристёгивании и в крутых поворотах).
            // В полёте всё по-старому — там тело свободное и рывков быть не должно.
            // Порог снапа 0.2: выравнивание наклона в полёте вращает тело вокруг
            // центра масс, и пивот (у ног) штатно смещается до ~0.15 м/шаг.
            if (_prevValid && !riding && !_prevRiding && (dv > 2.5f || posSnap > 0.2f))
            {
                _events.WriteLine(
                    $"t={_t:F3} {(posSnap > 0.04f ? "POS_SNAP" : "VEL_KICK")} dv={dv:F2} snap={posSnap:F3} " +
                    $"| lat={lateral:F2} steer={steerIn:F0} tSince={tSince:F2} cont={contain:F2} sup={suppress:F2} " +
                    $"| v: {_prevVel.x:F1},{_prevVel.y:F1},{_prevVel.z:F1} -> {vel.x:F1},{vel.y:F1},{vel.z:F1}");
                _eventCount++;
            }

            _prevVel = vel;
            _prevPos = pos;
            _prevRiding = riding;
            _prevValid = true;
        }
        catch (Exception e)
        {
            Fail(e.ToString());
        }
    }

    void FinishRun(string reason)
    {
        _events?.WriteLine($"# Конец ({reason}), событий: {_eventCount}");
        _csv?.Flush();
        _csv?.Dispose();
        _events?.Flush();
        _events?.Dispose();
        _csv = null;
        _events = null;
        Running = false;
        Finished = true;
    }

    void OnDestroy()
    {
        if (!Finished)
            FinishRun("прервано");
    }
}
