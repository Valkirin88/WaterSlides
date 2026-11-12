using UnityEngine;
using UnityEngine.InputSystem;

// Ввод «одним пальцем» (мобильная схема) + клавиатура для редактора.
//  * Палец (или левая кнопка мыши) на экране: его ГОРИЗОНТАЛЬНОЕ положение задаёт
//    целевое поперечное положение в жёлобе (LateralTarget, −1 — левая стенка,
//    +1 — правая). В воздухе то же положение работает как стрейф (TurnAxis).
//  * Второй палец (или правая кнопка мыши, или S) — тормоз.
//  * Короткий тап без движения — прыжок.
//  * Клавиатура: A/D плавно двигают ту же цель (положение запоминается, как
//    ползунок), W / стрелка вверх — газ (разгон выше потолка, см. PlayerView._throttleSpeedBonus), пробел — прыжок.
public class InputController
{
    public struct State
    {
        // Стрейф в воздухе, −1..1.
        public float TurnAxis;
        // Газ (+1) / тормоз (−1). Тормоз пальцем и S приходят сюда как −1.
        public float ThrottleAxis;
        public bool JumpPressed;
        // Целевое поперечное положение в жёлобе, −1..1 (доля ширины пола).
        public float LateralTarget;
        // false — цель не задана (например, ввод пишет зонд автотестов) и путевая
        // модель рулит по TurnAxis по-старому.
        public bool HasLateralTarget;
        public bool Brake;
    }

    public State Current { get; private set; }

    // Доля ширины экрана, в которой палец пробегает весь жёлоб: центральные 70 %,
    // у самых краёв палец «упирается» в стенку.
    const float ScreenSpanFraction = 0.7f;
    // Скорость хода цели от клавиш A/D, долей ширины в секунду.
    const float KeyboardTargetRate = 4f;
    // Тап: отпустить раньше этого времени и сдвинуть меньше этого расстояния.
    const float TapMaxDuration = 0.22f;
    const float TapMaxMovePixels = 24f;
    // Сглаживание цели (06.11.2026, жалоба «персонаж дёрганый»): палец задаёт
    // сырую цель, наружу отдаётся цель, догоняющая её за ~SmoothTime и не
    // быстрее MaxRate ширин/с. Касание в другом месте экрана больше не
    // перебрасывает тело рывком, а дрожание пальца не доходит до руля.
    const float TargetSmoothTime = 0.12f;
    const float TargetMaxRate = 5f;
    // Мёртвая зона стрейфа в воздухе (доля ширины); дальше ось растёт от нуля
    // плавно, без скачка до 0.12 на границе.
    const float AirDeadZone = 0.1f;

    float _lateralTarget;
    float _smoothedTarget;
    float _smoothedTargetVel;
    bool _jumpLatched;
    bool _pointerBlocked;
    readonly System.Collections.Generic.HashSet<int> _blockedTouches = new();
    bool _pointerWasDown;
    float _pointerDownTime;
    Vector2 _pointerDownPos;
    bool _pointerMoved;

    public void Update()
    {
        float turn = 0f;
        float throttle = 0f;
        bool jump = false;
        bool brake = false;
        bool pointerDown = false;
        Vector2 pointerPos = default;
        int pointerCount = 0;

        // ---- Касания ----
        var touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            var touches = touchscreen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                if (!touch.press.isPressed)
                {
                    _blockedTouches.Remove(i);
                    continue;
                }
                // Палец (слот касания стабилен на всё касание), начавший касание на кнопке HUD или на экране паузы, до
                // отпускания не рулит и не прыгает.
                if (touch.press.wasPressedThisFrame
                    && (RideHud.IsPaused || RideHud.IsScreenPointOnHud(touch.position.ReadValue())))
                    _blockedTouches.Add(i);
                if (_blockedTouches.Contains(i))
                    continue;
                if (pointerCount == 0)
                    pointerPos = touch.position.ReadValue();
                pointerCount++;
            }
            pointerDown = pointerCount > 0;
        }

        // ---- Мышь (редактор / ПК); в Device Simulator мышь уже приходит касанием ----
        var mouse = Mouse.current;
        if (!pointerDown && mouse != null && mouse.leftButton.isPressed)
        {
            if (mouse.leftButton.wasPressedThisFrame)
                _pointerBlocked = RideHud.IsPaused || RideHud.IsScreenPointOnHud(mouse.position.ReadValue());
            if (!_pointerBlocked)
            {
                pointerDown = true;
                pointerCount = 1;
                pointerPos = mouse.position.ReadValue();
            }
        }
        if (mouse != null && mouse.rightButton.isPressed)
            brake = true;
        if (pointerCount >= 2)
            brake = true;

        // ---- Клавиатура ----
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            float keyTurn = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                keyTurn -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                keyTurn += 1f;
            turn = keyTurn;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                throttle += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                brake = true;
            jump = keyboard.spaceKey.wasPressedThisFrame;

            if (!pointerDown && Mathf.Abs(keyTurn) > 0.01f)
                _lateralTarget = Mathf.Clamp(_lateralTarget + keyTurn * KeyboardTargetRate * Time.unscaledDeltaTime, -1f, 1f);
        }

        // ---- Положение пальца → цель ----
        if (pointerDown)
        {
            float width = Mathf.Max(Screen.width, 1);
            float x = (pointerPos.x / width - 0.5f) * 2f / ScreenSpanFraction;
            _lateralTarget = Mathf.Clamp(x, -1f, 1f);

            if (!_pointerWasDown)
            {
                _pointerDownTime = Time.unscaledTime;
                _pointerDownPos = pointerPos;
                _pointerMoved = false;
            }
            else if ((pointerPos - _pointerDownPos).sqrMagnitude > TapMaxMovePixels * TapMaxMovePixels)
                _pointerMoved = true;
        }
        else if (_pointerWasDown)
        {
            // Отпустили: короткий тап без движения — прыжок.
            if (!_pointerMoved && Time.unscaledTime - _pointerDownTime < TapMaxDuration)
                jump = true;
        }
        _pointerWasDown = pointerDown;

        // Пауза: ввод игры не слушается (тап по «Продолжить» не должен прыгать).
        if (RideHud.IsPaused)
        {
            jump = false;
            brake = false;
            throttle = 0f;
            turn = 0f;
        }

        float dt = Time.unscaledDeltaTime;
        _smoothedTarget = Mathf.SmoothDamp(_smoothedTarget, _lateralTarget, ref _smoothedTargetVel, TargetSmoothTime, TargetMaxRate, dt);
        if (pointerDown)
        {
            float mag = Mathf.Abs(_smoothedTarget);
            turn = mag > AirDeadZone ? Mathf.Sign(_smoothedTarget) * (mag - AirDeadZone) / (1f - AirDeadZone) : 0f;
        }

        // Прыжок держится, пока его не заберёт шаг физики (ConsumeJump): при
        // частоте кадров выше 50 Гц кадр с тапом мог не попасть ни в один
        // FixedUpdate, и прыжок терялся.
        if (jump)
            _jumpLatched = true;

        if (brake)
            throttle = -1f;

        Current = new State
        {
            TurnAxis = turn,
            ThrottleAxis = throttle,
            JumpPressed = _jumpLatched,
            LateralTarget = _smoothedTarget,
            HasLateralTarget = true,
            Brake = brake
        };
    }

    // Возврат цели в центр (респавн).
    public void ResetLateralTarget()
    {
        _lateralTarget = 0f;
        _smoothedTarget = 0f;
        _smoothedTargetVel = 0f;
    }

    // Шаг физики забрал прыжок.
    public void ConsumeJump()
    {
        _jumpLatched = false;
        var state = Current;
        state.JumpPressed = false;
        Current = state;
    }
}
