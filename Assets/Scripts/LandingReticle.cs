using UnityEngine;
using WaterSlides;

// Прицел приземления. Две подсказки игроку в полёте:
//  * КОЛЬЦО ВХОДА — обруч в сечении начала следующей горки (куда надо попасть);
//  * ТОЧКА ПРИЗЕМЛЕНИЯ — где тело упадёт при текущей скорости (баллистический
//    прогноз PlayerView.TryPredictLanding) и дуга полёта до неё.
// Цвет: зелёный — прогноз попадает на горку, красный — промах (упадёт мимо).
// Компоненты и LineRenderer'ы создаёт меню Water Slides/Setup Controls.
public class LandingReticle : MonoBehaviour
{
    [Tooltip("Игрок. Пусто — находится в сцене сам.")]
    [SerializeField] PlayerView _player;
    [Tooltip("Показывать кольцо входа следующей горки. Выключено по просьбе пользователя (28.10.2026): остаются только маркер приземления и дуга полёта.")]
    [SerializeField] bool _showEntryRing = false;
    [Tooltip("LineRenderer кольца входа следующей горки.")]
    [SerializeField] LineRenderer _entryRing;
    [Tooltip("LineRenderer точки приземления (маленькое кольцо на поверхности).")]
    [SerializeField] LineRenderer _landingMarker;
    [Tooltip("LineRenderer дуги полёта от тела до точки приземления.")]
    [SerializeField] LineRenderer _flightArc;
    [Tooltip("Горизонт прогноза полёта, с.")]
    [SerializeField] float _horizon = 6f;
    [Tooltip("Число сегментов колец.")]
    [SerializeField, Range(8, 64)] int _segments = 36;
    [Tooltip("Радиус кольца точки приземления, м.")]
    [SerializeField] float _markerRadius = 0.5f;
    [Tooltip("Минимальный радиус кольца входа, м (если жёлоб уже).")]
    [SerializeField] float _minRingRadius = 0.9f;
    [Tooltip("Цвет кольца входа, пока тело едет по горке (цели ещё нет).")]
    [SerializeField] Color _ringIdle = new(1f, 1f, 1f, 0.55f);
    [Tooltip("Цвет подсказок, когда прогноз попадает на горку.")]
    [SerializeField] Color _good = new(0.35f, 1f, 0.45f, 0.95f);
    [Tooltip("Цвет подсказок, когда прогноз падает мимо.")]
    [SerializeField] Color _bad = new(1f, 0.3f, 0.25f, 0.95f);
    [Tooltip("Сколько секунд после отрыва подсказки ещё не показываются (короткие подскоки с перегибов не считаются полётом).")]
    [SerializeField] float _showDelay = 0.12f;
    [Tooltip("Толщина линий колец как доля расстояния до камеры: на экране кольцо всегда одной толщины (0.006 × 40 м = 24 см). Ниже Min Line Width не тоньше.")]
    [SerializeField] float _lineWidthPerMeter = 0.006f;
    [Tooltip("Минимальная толщина линий колец, м.")]
    [SerializeField] float _minLineWidth = 0.08f;
    [Tooltip("Камера для расчёта толщины. Пусто — Camera.main.")]
    [SerializeField] Camera _camera;

    Vector3[] _ringPoints;
    Vector3[] _markerPoints;
    readonly Vector3[] _arcPoints = new Vector3[64];

    void LateUpdate()
    {
        if (_player == null)
            _player = FindFirstObjectByType<PlayerView>();
        if (_player == null)
        {
            Hide(_entryRing); Hide(_landingMarker); Hide(_flightArc);
            return;
        }

        bool airborne = !_player.IsOnSlide && _player.HasSlideContactSinceSpawn && _player.TimeSinceSlideContact > _showDelay;
        var next = _player.NextSlide;

        bool hasLanding = false;
        bool onTarget = false;
        Vector3 landPoint = default, landNormal = Vector3.up;
        if (airborne)
        {
            hasLanding = _player.TryPredictLanding(_horizon, out landPoint, out landNormal, out var landSlide);
            // Совпадения с любым коллайдером горки недостаточно: иначе даже посадка
            // на посторонний участок трассы всегда подсвечивалась зелёным. Подсвечиваем
            // текущую горку (короткий подскок) и следующую по маршруту.
            onTarget = hasLanding && landSlide != null
                && (landSlide == _player.CurrentSlide || landSlide == _player.NextSlide);
            // На перелёте к следующей горке без прыжка не долететь (10.11.2026):
            // дуга красная, пока игрок не прыгнет в воздухе — это и есть подсказка.
            if (_player.NeedsJumpToReachNext)
                onTarget = false;
        }

        // ---- Кольцо входа ---- (по умолчанию выключено: пользователь попросил убрать 28.10.2026)
        if (_showEntryRing && next != null && next.RideFrames != null && next.RideFrames.Count > 1)
        {
            var f0 = next.RideFrames[0];
            Vector3 up = f0.Up.sqrMagnitude > 1e-6f ? f0.Up.normalized : Vector3.up;
            Vector3 fwd = f0.Forward.sqrMagnitude > 1e-6f ? f0.Forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(up, fwd).normalized;
            float radius = Mathf.Max(next.RideHalfWidth, _minRingRadius);
            // Открытый жёлоб: дно на −Depth от кадра, обруч стоит на дне. Труба: кадр в центре.
            Vector3 center = next.Enclosure == SlideEnclosure.Open
                ? f0.Position + up * (radius - next.FlumeDepth)
                : f0.Position;
            DrawCircle(_entryRing, ref _ringPoints, center, right, up, radius);
            SetColor(_entryRing, airborne ? (onTarget ? _good : _bad) : _ringIdle);
        }
        else
            Hide(_entryRing);

        // ---- Точка приземления и дуга ----
        if (airborne && hasLanding)
        {
            Vector3 n = landNormal.sqrMagnitude > 1e-6f ? landNormal.normalized : Vector3.up;
            Vector3 t1 = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
            Vector3 t2 = Vector3.Cross(n, t1);
            DrawCircle(_landingMarker, ref _markerPoints, landPoint + n * 0.05f, t1, t2, _markerRadius);
            SetColor(_landingMarker, onTarget ? _good : _bad);
            DrawArc(landPoint, onTarget ? _good : _bad);
        }
        else
        {
            Hide(_landingMarker);
            if (airborne)
                DrawArc(null, _bad);
            else
                Hide(_flightArc);
        }
    }

    void DrawArc(Vector3? end, Color color)
    {
        if (_flightArc == null)
            return;
        var rb = _player.Rigidbody;
        if (rb == null)
        {
            Hide(_flightArc);
            return;
        }
        Vector3 pos = _player.BodyCenter;
        Vector3 vel = rb.linearVelocity;
        Vector3 g = Physics.gravity * _player.GravityMultiplier;
        const float step = 0.08f;
        int count = 0;
        for (int i = 0; i < _arcPoints.Length; i++)
        {
            _arcPoints[count++] = pos;
            if (end.HasValue && count > 2 && Vector3.Dot(end.Value - pos, vel) <= 0f)
                break;
            pos += vel * step + g * (0.5f * step * step);
            vel += g * step;
            if (count * step > _horizon)
                break;
        }
        if (end.HasValue && count < _arcPoints.Length)
            _arcPoints[count++] = end.Value;
        _flightArc.enabled = true;
        _flightArc.positionCount = count;
        _flightArc.SetPositions(_arcPoints);
        SetColor(_flightArc, color);
    }

    void DrawCircle(LineRenderer line, ref Vector3[] buffer, Vector3 center, Vector3 axisA, Vector3 axisB, float radius)
    {
        if (line == null)
            return;
        if (buffer == null || buffer.Length != _segments)
            buffer = new Vector3[_segments];
        for (int i = 0; i < _segments; i++)
        {
            float a = i * Mathf.PI * 2f / _segments;
            buffer[i] = center + axisA * (Mathf.Cos(a) * radius) + axisB * (Mathf.Sin(a) * radius);
        }
        line.enabled = true;
        line.loop = true;
        line.positionCount = _segments;
        line.SetPositions(buffer);

        // Толщина по расстоянию до камеры: иначе кольцо на следующей горке (30–60 м)
        // тоньше пикселя и его не видно.
        if (_camera == null)
            _camera = Camera.main;
        float distance = _camera != null ? Vector3.Distance(_camera.transform.position, center) : 20f;
        float width = Mathf.Max(distance * _lineWidthPerMeter, _minLineWidth);
        line.startWidth = width;
        line.endWidth = width;
    }

    static void SetColor(LineRenderer line, Color color)
    {
        if (line == null)
            return;
        line.startColor = color;
        line.endColor = color;
    }

    static void Hide(LineRenderer line)
    {
        if (line != null)
            line.enabled = false;
    }
}
