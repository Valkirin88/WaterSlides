using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

// Счёт на экране. Очки капают за пройденные метры на горке; быстрая езда (выше
// доли потолка) и ускоряющие полосы умножают — скорость становится наградой, а
// тормоз — ценой за надёжность. Рекорд хранится в PlayerPrefs. Объекты и ссылки
// создаёт меню Water Slides/Setup Controls. Подсказка по управлению была —
// убрана 28.10.2026 по просьбе пользователя (меню Remove Controls Hint).
public class RideHud : MonoBehaviour
{
    [Tooltip("Игрок. Пусто — находится в сцене сам.")]
    [SerializeField] PlayerView _player;
    [Tooltip("Текст счёта.")]
    [SerializeField] Text _scoreText;
    [Tooltip("Текст рекорда.")]
    [SerializeField] Text _bestText;
    [Tooltip("Текст скорости и множителя.")]
    [SerializeField] Text _speedText;
    Text _scoreCaption;
    Image _scorePanel;
    Image _speedPanel;
    Image _speedGaugeFill;
    Image _starIcon;
    Image _riskBadge;
    Text _riskBadgeLabel;
    float _riskBadgeShownAt;
    Button _pauseButton;
    GameObject _pauseOverlay;
    Text _pauseBestText;
    RectTransform _safeArea;
    Rect _appliedSafeArea;
    Vector2Int _appliedScreen;
    bool _paused;
    float _displaySpeed;
    float _displaySpeedVel;
    static RideHud s_instance;

    // Для InputController: пока пауза, касания не идут в игру.
    public static bool IsPaused => s_instance != null && s_instance._paused;

    // Точка экрана (пиксели) над кнопкой HUD — такое касание не рулит и не прыгает.
    public static bool IsScreenPointOnHud(Vector2 screenPoint)
    {
        if (s_instance == null || s_instance._pauseButton == null)
            return false;
        var rect = (RectTransform)s_instance._pauseButton.transform;
        return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);
    }
    [Tooltip("Очков за метр пути по горке на базовой скорости.")]
    [SerializeField] float _pointsPerMeter = 1f;
    [Tooltip("Множитель очков на ускоряющей полосе (и секунду после неё).")]
    [SerializeField] float _boostMultiplier = 3f;
    [Tooltip("Зоны шкалы скорости по опасности ближайшего поворота (PlayerView.CornerDanger: 1 — на текущей скорости этот поворот не удержать даже пальцем до упора внутрь — вынесет). Ниже этой доли — зелёная зона; от неё до 1 — жёлтая (идут двойные очки); от 1 — красная (вылет). Цифра скорости красится в цвет зоны.")]
    [SerializeField, Range(0f, 1f)] float _cornerWarnFraction = 0.8f;
    // Цвета зон шкалы (и цифры скорости в жёлтой/красной зоне).
    static readonly Color ZoneGreen = new(0.33f, 0.92f, 0.12f);
    static readonly Color ZoneYellow = new(1f, 0.86f, 0.08f);
    static readonly Color ZoneRed = new(1f, 0.22f, 0.15f);
    // Где на шкале начинаются жёлтая и красная зоны (доля длины) и какой
    // опасности соответствует полная шкала.
    const float GaugeYellowAt = 0.6f;
    const float GaugeRedAt = 0.85f;
    const float GaugeFullDanger = 1.3f;
    float _gaugeLevel;
    float _gaugeLevelVel;
    [Tooltip("Сглаживание цифры скорости и шкалы, с. Без него км/ч менялись каждый кадр на ±1–2 и цифра мельтешила.")]
    [SerializeField] float _speedDisplaySmoothTime = 0.15f;
    [Tooltip("Множитель очков в жёлтой и красной зонах шкалы (скорость близко к пределу ближайшего поворота или выше него): награда за риск, складывается с ×3 ускоряющей полосы. 1 — выключить.")]
    [SerializeField] float _dangerMultiplier = 2f;

    const string BestKey = "WaterSlides.BestScore";

    float _score;
    float _best;
    bool _subscribed;
    int _shownScore = -1, _shownBest = -1, _shownSpeed = -1, _shownDangerStage = -1;
    float _shownMultiplier = -1f;

    public float Score => _score;

    void Awake()
    {
        s_instance = this;
        _best = PlayerPrefs.GetFloat(BestKey, 0f);

        BuildHudPresentation();

        // Страховка: текст без шрифта не рисуется вовсе.
        Font fallback = null;
        foreach (var text in new[] { _scoreText, _bestText, _speedText })
        {
            if (text == null || text.font != null)
                continue;
            fallback ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = fallback;
        }
    }

    // Собирает оформление поверх уже существующих текстовых полей из сцены.
    // Так HUD остаётся совместимым с повторным запуском Setup Controls и
    // автоматически подстраивается под экран через CanvasScaler.
    void BuildHudPresentation()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas == null)
            return;
        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();
        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        // Всё, что у краёв экрана, живёт в контейнере безопасной зоны: на
        // телефонах с вырезом камеры/скруглёнными углами панели не уходят под них.
        var root = EnsureSafeArea();
        ApplySafeArea();
        // Match the reference proportions in the 1080-wide portrait layout.
        var scaler = GetComponent<CanvasScaler>();
        if (scaler != null) scaler.matchWidthOrHeight = 0f;
        if (_scoreText != null)
        {
            _scorePanel = EnsurePanel(root, "Score Pill", new Vector2(0f, 1f), new Vector2(34f, -36f), new Vector2(310f, 98f), Color.white);
            var oldCaption = root.Find("HUD Star");
            if (oldCaption != null) oldCaption.gameObject.SetActive(false);
            var star = EnsureImage(root, "HUD Star Icon", HudStarSprite);
            _starIcon = star;
            PlaceImage(star, new Vector2(0f, 1f), new Vector2(52f, -38f), new Vector2(86f, 86f), Color.white);
            _scoreText.transform.SetParent(root, false);
            PlaceText(_scoreText, new Vector2(0f, 1f), new Vector2(151f, -39f), new Vector2(182f, 84f), TextAnchor.MiddleLeft, 58, Color.white);
            if (_bestText != null) _bestText.gameObject.SetActive(false);

            // Плашка бонуса красной зоны «×2 ОЧКИ»: крупная огненная капсула прямо под
            // панелью очков, по её ширине. Маленький кружок «×2» на углу (первый
            // вариант 06.11.2026) читался плохо — пользователь попросил крупнее.
            // Пивот в центре — пульс от центра.
            _riskBadge = EnsureImage(root, "HUD Risk Badge", HudBadgeSprite);
            var badgeRect = _riskBadge.rectTransform;
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            badgeRect.anchoredPosition = new Vector2(34f + 155f, -36f - 98f - 14f - 48f);
            badgeRect.sizeDelta = new Vector2(288f, 96f);
            _riskBadge.color = Color.white;
            _riskBadgeLabel = EnsureCaption(_riskBadge.transform, "Risk Badge Label", "×2 ОЧКИ", new Vector2(0.5f, 0.5f), new Vector2(0f, 1f),
                new Vector2(288f, 90f), 54, Color.white, TextAnchor.MiddleCenter);
            var labelRect = _riskBadgeLabel.rectTransform;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = new Vector2(0f, 1f);
            _riskBadge.gameObject.SetActive(false);
        }

        if (_speedText != null)
        {
            var speedGroup = root.Find("HUD Speed Group") as RectTransform;
            if (speedGroup == null)
            {
                speedGroup = new GameObject("HUD Speed Group", typeof(RectTransform)).GetComponent<RectTransform>();
                speedGroup.SetParent(root, false);
            }
            speedGroup.anchorMin = speedGroup.anchorMax = new Vector2(0.53f, 1f);
            speedGroup.pivot = new Vector2(0.5f, 1f);
            speedGroup.anchoredPosition = new Vector2(0f, -30f);
            speedGroup.sizeDelta = new Vector2(346f, 126f);
            // Без наклона: в образце плашка ровная, «фигурная» — её форма.
            speedGroup.localRotation = Quaternion.identity;
            _speedPanel = EnsureImage(speedGroup, "Speed Pill", HudSpeedPanelSprite);
            _speedPanel.type = Image.Type.Simple;
            PlaceImage(_speedPanel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(346f, 126f), Color.white);
            _speedText.transform.SetParent(speedGroup, false);
            PlaceText(_speedText, new Vector2(0.5f, 0.5f), new Vector2(48f, 20f), new Vector2(222f, 70f), TextAnchor.MiddleCenter, 58, Color.white);
            var arc = EnsureImage(speedGroup, "HUD Speedometer Arc", HudSpeedometerSprite);
            PlaceImage(arc, new Vector2(0.5f, 0.5f), new Vector2(-104f, 15f), new Vector2(85f, 85f), Color.white);
            var gaugeBack = EnsureImage(speedGroup, "HUD Speed Gauge Back", HudGaugeBackSprite);
            PlaceImage(gaugeBack, new Vector2(0.5f, 0.5f), new Vector2(0f, -32f), new Vector2(274f, 30f), Color.white);
            _speedGaugeFill = EnsureImage(speedGroup, "HUD Speed Gauge Fill", HudGaugeSprite);
            PlaceImage(_speedGaugeFill, new Vector2(0.5f, 0.5f), new Vector2(0f, -32f), new Vector2(274f, 30f), Color.white);
            _speedGaugeFill.type = Image.Type.Filled;
            _speedGaugeFill.fillMethod = Image.FillMethod.Horizontal;
            _speedGaugeFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        }

        _pauseButton = EnsureButton(root, "HUD Pause Button", HudCircleSprite, new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(114f, 114f),
            Color.white, PauseGame);
        var oldPauseGlyph = _pauseButton.transform.Find("Pause Glyph");
        if (oldPauseGlyph != null) oldPauseGlyph.gameObject.SetActive(false);
        for (int i = 0; i < 2; i++)
        {
            var bar = EnsureImage(_pauseButton.transform, "Pause Bar " + i, HudPauseBarSprite);
            PlaceImage(bar, new Vector2(0.5f, 0.5f), new Vector2(i == 0 ? -14f : 14f, 0f), new Vector2(12f, 50f), Color.white);
        }
        BuildPauseOverlay(transform);

        if (_scorePanel != null) _scorePanel.transform.SetAsFirstSibling();
        if (_speedPanel != null) _speedPanel.transform.SetAsFirstSibling();
        if (_pauseButton != null) _pauseButton.transform.SetAsLastSibling();
        if (_pauseOverlay != null) _pauseOverlay.transform.SetAsLastSibling();
    }

    Image EnsurePanel(Transform parent, string objectName, Vector2 anchor, Vector2 position, Vector2 size, Color tint)
    {
        var child = parent.Find(objectName);
        GameObject go;
        if (child != null) go = child.gameObject;
        else
        {
            go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
        }
        var image = go.GetComponent<Image>();
        image.sprite = HudPillSprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 2.24f;
        image.color = tint;
        image.raycastTarget = false;
        var outline = go.GetComponent<Outline>();
        if (outline == null) outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0.26f, 0.85f, 1f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;
        PlaceImage(image, anchor, position, size, tint);
        return image;
    }

    Image EnsureImage(Transform parent, string objectName, Sprite sprite)
    {
        var child = parent.Find(objectName);
        GameObject go;
        if (child != null) go = child.gameObject;
        else
        {
            go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
        }
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    static void PlaceImage(Image image, Vector2 anchor, Vector2 position, Vector2 size, Color color)
    {
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        image.color = color;
    }

    Text EnsureCaption(Transform parent, string objectName, string caption, Vector2 anchor, Vector2 position, Vector2 size, int fontSize, Color color, TextAnchor alignment)
    {
        var child = parent.Find(objectName);
        GameObject go;
        if (child != null) go = child.gameObject;
        else
        {
            go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
        }
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = caption;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    Button EnsureButton(Transform parent, string objectName, Sprite sprite, Vector2 anchor, Vector2 position, Vector2 size, Color color, UnityEngine.Events.UnityAction action)
    {
        var child = parent.Find(objectName);
        GameObject go;
        if (child != null) go = child.gameObject;
        else
        {
            go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
        }
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = true;
        PlaceImage(image, anchor, position, size, color);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
        return button;
    }

    void BuildPauseOverlay(Transform root)
    {
        var child = root.Find("HUD Pause Overlay");
        if (child == null)
        {
            var go = new GameObject("HUD Pause Overlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(root, false);
            child = go.transform;
        }
        _pauseOverlay = child.gameObject;
        var backdrop = _pauseOverlay.GetComponent<Image>();
        backdrop.color = new Color(0.01f, 0.04f, 0.09f, 0.72f);
        backdrop.raycastTarget = true;
        var full = (RectTransform)child;
        full.anchorMin = Vector2.zero;
        full.anchorMax = Vector2.one;
        full.offsetMin = full.offsetMax = Vector2.zero;

        const string city = "Assets/Scenes/NightCity.unity";
        const string lagoon = "Assets/Scenes/SampleScene.unity";
        bool inCity = gameObject.scene.path == city;
        string otherLevel = inCity ? lagoon : city;
        int otherLevelIndex = SceneUtility.GetBuildIndexByScenePath(otherLevel);
        bool canSwitch = otherLevelIndex >= 0;
        var card = EnsurePanel(child, "Pause Card", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, canSwitch ? 420f : 360f), Color.white);
        var title = EnsureCaption(child, "Pause Title", "ПАУЗА", new Vector2(0.5f, 0.5f), new Vector2(0f, 82f), new Vector2(440f, 80f), 56,
            Color.white, TextAnchor.MiddleCenter);
        _pauseBestText = EnsureCaption(child, "Pause Best", "", new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), new Vector2(440f, 50f), 32,
            new Color(1f, 0.86f, 0.3f), TextAnchor.MiddleCenter);
        var resume = EnsureButton(child, "Resume Button", HudPillSprite, new Vector2(0.5f, 0.5f), new Vector2(0f, -82f), new Vector2(300f, 76f),
            Color.white, ResumeGame);
        EnsureCaption(resume.transform, "Resume Label", "ПРОДОЛЖИТЬ", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(270f, 64f), 28,
            new Color(0.04f, 0.25f, 0.42f), TextAnchor.MiddleCenter);
        if (canSwitch)
        {
            var levels = EnsureButton(child, "Other Level", HudPillSprite, new Vector2(.5f, .5f), new Vector2(0f, -157f),
                new Vector2(365f, 60f), new Color(.83f, .92f, 1f), () =>
                {
                    CommitBest();
                    ResumeGame();
                    SceneManager.LoadScene(otherLevelIndex);
                });
            EnsureCaption(levels.transform, "Level Label", inCity ? "ТРОПИЧЕСКАЯ ЛАГУНА" : "НОЧНОЙ МЕГАПОЛИС",
                new Vector2(.5f, .5f), Vector2.zero, new Vector2(345f, 54f), 25,
                new Color(.04f, .25f, .42f), TextAnchor.MiddleCenter);
        }
        card.transform.SetAsFirstSibling();
        title.transform.SetAsLastSibling();
        resume.transform.SetAsLastSibling();
        _pauseBestText.transform.SetAsLastSibling();
        _pauseOverlay.SetActive(false);
    }

    void EnsureSpeedometer(Transform root)
    {
        var arc = EnsureImage(root, "HUD Speedometer Arc", HudSpeedometerSprite);
        PlaceImage(arc, new Vector2(0.59f, 1f), new Vector2(-152f, -38f), new Vector2(64f, 64f), Color.white);
        var oldNeedle = root.Find("HUD Speedometer Needle");
        if (oldNeedle != null) oldNeedle.gameObject.SetActive(false);
    }

    void PauseGame()
    {
        if (_pauseOverlay == null) return;
        _paused = true;
        Time.timeScale = 0f;
        CommitBest();
        if (_pauseBestText != null)
        {
            int best = Mathf.FloorToInt(_best);
            _pauseBestText.text = best > 0 ? "РЕКОРД  " + best.ToString("N0") : "";
        }
        _pauseOverlay.SetActive(true);
    }

    void ResumeGame()
    {
        _paused = false;
        Time.timeScale = 1f;
        if (_pauseOverlay != null) _pauseOverlay.SetActive(false);
    }

    static void PlaceText(Text text, Vector2 anchor, Vector2 position, Vector2 size, TextAnchor alignment, int fontSize, Color color)
    {
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        text.alignment = alignment;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.resizeTextForBestFit = false;
        text.supportRichText = true;
        foreach (var outline in text.GetComponents<Outline>())
            outline.enabled = false;
        foreach (var shadow in text.GetComponents<Shadow>())
        {
            if (shadow is Outline) continue;
            shadow.effectDistance = new Vector2(0f, -2f);
            shadow.effectColor = new Color(0.02f, 0.16f, 0.3f, 0.4f);
        }
        text.raycastTarget = false;
    }

    static Sprite _hudPauseBarSprite;
    static Sprite HudPauseBarSprite
    {
        get
        {
            if (_hudPauseBarSprite != null) return _hudPauseBarSprite;
            var texture = new Texture2D(32, 128, TextureFormat.RGBA32, false);
            var pixels = new Color[32 * 128];
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 32; x++)
            {
                float dx = Mathf.Max(8f - x, 0f, x - 23f);
                float dy = Mathf.Max(8f - y, 0f, y - 119f);
                pixels[y * 32 + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(8.5f - Mathf.Sqrt(dx * dx + dy * dy)));
            }
            texture.SetPixels(pixels); texture.Apply();
            _hudPauseBarSprite = Sprite.Create(texture, new Rect(0, 0, 32, 128), new Vector2(0.5f, 0.5f), 100f);
            return _hudPauseBarSprite;
        }
    }

    // ---- Фигурная плашка скорости и изогнутая шкала (по образцу) ----
    // Плашка: верх прямой, нижний край — плавная дуга (слева ниже, вправо
    // поднимается), концы скруглены. Шкала повторяет изгиб: левый край ниже и
    // толще, вправо поднимается и сужается. Рисуются в 2× размере UI с
    // суперсэмплингом 3×3. Наклон всей плашки (4°, было до 06.11.2026) убран —
    // в образце фигурной была форма, а не поворот.
    const int SpeedPanelW = 692, SpeedPanelH = 252;   // UI 346×126
    const int GaugeW = 548, GaugeH = 60;              // UI 274×30

    // Внутри ли точка (x, t — от левого/верхнего края, текстуры) плашки,
    // утопленной внутрь на d.
    static bool InSpeedPanel(float x, float t, float d)
    {
        const float pad = 4f;
        float u = Mathf.Clamp01(x / SpeedPanelW);
        float top = pad + d;
        float bottom = SpeedPanelH - pad - 20f * Mathf.Pow(u, 1.4f) - d;
        if (t < top || t > bottom) return false;
        float hh = (bottom - top) * 0.5f, cy = (top + bottom) * 0.5f;
        float left = pad + d + hh, right = SpeedPanelW - pad - d - hh;
        if (x < left) { float dx = (x - left) / hh, dy = (t - cy) / hh; return dx * dx + dy * dy <= 1f; }
        if (x > right) { float dx = (x - right) / hh, dy = (t - cy) / hh; return dx * dx + dy * dy <= 1f; }
        return true;
    }

    static Sprite _hudSpeedPanelSprite;
    static Sprite HudSpeedPanelSprite
    {
        get
        {
            if (_hudSpeedPanelSprite != null) return _hudSpeedPanelSprite;
            var texture = new Texture2D(SpeedPanelW, SpeedPanelH, TextureFormat.RGBA32, false) { name = "HUD Speed Panel" };
            var pixels = new Color[SpeedPanelW * SpeedPanelH];
            for (int y = 0; y < SpeedPanelH; y++)
            for (int x = 0; x < SpeedPanelW; x++)
            {
                float t = SpeedPanelH - 1 - y;
                int outer = 0, rim = 0, body = 0;
                for (int sy = 0; sy < 3; sy++)
                for (int sx = 0; sx < 3; sx++)
                {
                    float px = x + (sx + 0.5f) / 3f, pt = t + (sy + 0.5f) / 3f;
                    if (InSpeedPanel(px, pt, 0f)) outer++;
                    if (InSpeedPanel(px, pt, 6f)) rim++;
                    if (InSpeedPanel(px, pt, 24f)) body++;
                }
                if (outer == 0) { pixels[y * SpeedPanelW + x] = Color.clear; continue; }
                float sheen = 1f - t / (SpeedPanelH - 1f);
                Color bodyCol = Color.Lerp(new Color(0.04f, 0.33f, 0.72f), new Color(0.16f, 0.58f, 0.98f), sheen);
                bodyCol = Color.Lerp(bodyCol, Color.white, Mathf.SmoothStep(0f, 1f, (sheen - 0.55f) / 0.3f) * 0.22f);
                Color rimCol = Color.Lerp(new Color(0.3f, 0.78f, 1f), new Color(0.72f, 0.96f, 1f), sheen);
                Color edgeCol = new Color(0.03f, 0.22f, 0.5f);
                // Смесь слоёв по покрытию: обводка → ободок → тело.
                Color c = Color.Lerp(edgeCol, rimCol, rim / 9f);
                c = Color.Lerp(c, bodyCol, body / 9f);
                c.a = outer / 9f * 0.96f;
                pixels[y * SpeedPanelW + x] = c;
            }
            texture.SetPixels(pixels); texture.Apply();
            _hudSpeedPanelSprite = Sprite.Create(texture, new Rect(0, 0, SpeedPanelW, SpeedPanelH), new Vector2(0.5f, 0.5f), 100f);
            return _hudSpeedPanelSprite;
        }
    }

    // Изогнутая шкала: центр линии и толщина по u (0 — левый край).
    static bool InGauge(float x, float t, out float u, out float v)
    {
        u = Mathf.Clamp01(x / GaugeW);
        float center = GaugeH * 0.5f + 9f - 18f * Mathf.Pow(u, 0.8f) + 0f;
        float half = (26f - 8f * u) * 0.5f;
        v = Mathf.Clamp01((t - (center - half)) / (2f * half));
        float r = half, left = 2f + r, right = GaugeW - 2f - r;
        if (x < left || x > right)
        {
            float ex = x < left ? left : right;
            float uc = ex / GaugeW;
            float cc = GaugeH * 0.5f + 9f - 18f * Mathf.Pow(uc, 0.8f);
            float hc = (26f - 8f * uc) * 0.5f;
            float dx = x - ex, dy = t - cc;
            return dx * dx + dy * dy <= hc * hc;
        }
        return Mathf.Abs(t - center) <= half;
    }

    static Sprite BuildGaugeSprite(bool back)
    {
        var texture = new Texture2D(GaugeW, GaugeH, TextureFormat.RGBA32, false) { name = back ? "HUD Zone Gauge Back" : "HUD Zone Gauge" };
        var pixels = new Color[GaugeW * GaugeH];
        var navy = new Color(0.02f, 0.18f, 0.32f);
        for (int y = 0; y < GaugeH; y++)
        for (int x = 0; x < GaugeW; x++)
        {
            float t = GaugeH - 1 - y;
            int cover = 0; float u = 0f, v = 0f;
            for (int sy = 0; sy < 3; sy++)
            for (int sx = 0; sx < 3; sx++)
                if (InGauge(x + (sx + 0.5f) / 3f, t + (sy + 0.5f) / 3f, out u, out v)) cover++;
            if (cover == 0) { pixels[y * GaugeW + x] = Color.clear; continue; }
            InGauge(x + 0.5f, t + 0.5f, out u, out v);
            Color zone = GaugeZoneColor(u);
            Color c;
            if (back)
                // Подложка: тёмно-синяя с приглушёнными цветами зон — границы
                // зон видны и на незаполненной части шкалы.
                c = Color.Lerp(navy, zone, 0.3f);
            else
                // Блик по верхней части полоски — объём, как в образце.
                c = Color.Lerp(zone, Color.white, Mathf.Clamp01((0.45f - v) / 0.45f) * 0.35f);
            c.a = cover / 9f * (back ? 0.92f : 1f);
            pixels[y * GaugeW + x] = c;
        }
        texture.SetPixels(pixels); texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, GaugeW, GaugeH), new Vector2(0.5f, 0.5f), 100f);
    }

    static Sprite _hudGaugeBackSprite;
    static Sprite HudGaugeBackSprite => _hudGaugeBackSprite != null ? _hudGaugeBackSprite : (_hudGaugeBackSprite = BuildGaugeSprite(true));

    static Sprite _hudGaugeSprite;
    static Sprite HudGaugeSprite => _hudGaugeSprite != null ? _hudGaugeSprite : (_hudGaugeSprite = BuildGaugeSprite(false));

    static Sprite _hudWhitePillSprite;
    static Sprite _hudStarSprite;
    static Sprite HudStarSprite
    {
        get
        {
            if (_hudStarSprite != null) return _hudStarSprite;
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HUD Golden Star" };
            var vertices = new Vector2[10];
            for (int i = 0; i < vertices.Length; i++)
            {
                float angle = (90f + i * 36f) * Mathf.Deg2Rad;
                vertices[i] = new Vector2(64f, 64f) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (i % 2 == 0 ? 59f : 28f);
            }
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var point = new Vector2(x + 0.5f, y + 0.5f);
                bool inside = false;
                float distance = float.MaxValue;
                for (int i = 0, j = 9; i < 10; j = i++)
                {
                    var a = vertices[i]; var b = vertices[j];
                    if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                        inside = !inside;
                    var edge = b - a;
                    distance = Mathf.Min(distance, Vector2.Distance(point, a + edge * Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude)));
                }
                Color fill = Color.Lerp(new Color(1f, 0.47f, 0.015f), new Color(1f, 0.94f, 0.35f), y / 127f);
                float angleFromTop = Mathf.Repeat(Mathf.Atan2(point.y - 64f, point.x - 64f) * Mathf.Rad2Deg - 90f, 360f);
                int facet = Mathf.FloorToInt(angleFromTop / 36f);
                fill = Color.Lerp(fill, facet % 2 == 0 ? new Color(1f, 0.98f, 0.52f) : new Color(1f, 0.57f, 0.02f), 0.28f);
                if (distance < 4f) fill = Color.Lerp(new Color(0.9f, 0.39f, 0.01f), new Color(1f, 0.97f, 0.64f), y / 127f);
                fill.a = Mathf.Clamp01(0.5f + (inside ? distance : -distance));
                pixels[y * size + x] = fill;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            _hudStarSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _hudStarSprite;
        }
    }

    static Sprite HudWhitePillSprite
    {
        get
        {
            if (_hudWhitePillSprite != null) return _hudWhitePillSprite;
            var source = HudPillSprite.texture;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            var pixels = source.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(1f, 1f, 1f, pixels[i].a);
            texture.SetPixels(pixels);
            texture.Apply();
            _hudWhitePillSprite = Sprite.Create(texture, new Rect(0, 0, source.width, source.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
            return _hudWhitePillSprite;
        }
    }

    static Sprite _hudPillSprite;
    static Sprite HudPillSprite
    {
        get
        {
            if (_hudPillSprite != null) return _hudPillSprite;
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HUD Blue Pill" };
            var pixels = new Color[size * size];
            const float radius = 120f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - x, 0f, x - (size - 1 - radius));
                float dy = Mathf.Max(radius - y, 0f, y - (size - 1 - radius));
                float edge = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                float sheen = y / (size - 1f);
                // Светлее и с глянцем, как в образце: синий градиент, блик по
                // верхней части, толстый голубой ободок и тонкая тёмная обводка.
                Color fill = Color.Lerp(new Color(0.04f, 0.33f, 0.72f), new Color(0.16f, 0.58f, 0.98f), sheen);
                fill = Color.Lerp(fill, Color.white, Mathf.SmoothStep(0f, 1f, (sheen - 0.55f) / 0.3f) * 0.22f);
                if (edge > -14f) fill = Color.Lerp(new Color(0.3f, 0.78f, 1f), new Color(0.72f, 0.96f, 1f), sheen);
                if (edge > -4f) fill = new Color(0.03f, 0.22f, 0.5f);
                pixels[y * size + x] = new Color(fill.r, fill.g, fill.b, Mathf.Clamp01(0.5f - edge) * 0.96f);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            _hudPillSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(120, 120, 120, 120));
            return _hudPillSprite;
        }
    }

    // Огненная капсула плашки бонуса 3:1: от красного снизу к оранжево-жёлтому
    // сверху, светлый ободок — как у кнопки паузы.
    static Sprite _hudBadgeSprite;
    static Sprite HudBadgeSprite
    {
        get
        {
            if (_hudBadgeSprite != null) return _hudBadgeSprite;
            const int w = 288, h = 96;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "HUD Risk Badge" };
            var pixels = new Color[w * h];
            float radius = h * 0.47f, cy = (h - 1) * 0.5f;
            float leftC = radius + 2f, rightC = w - radius - 3f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float cx = Mathf.Clamp(x, leftC, rightC);
                float dx = x - cx, dy = y - cy;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                Color fill = Color.Lerp(new Color(0.86f, 0.12f, 0.05f), new Color(1f, 0.62f, 0.1f), y / (h - 1f));
                if (distance > radius - 5f) fill = new Color(1f, 0.9f, 0.55f);
                fill.a = Mathf.Clamp01(radius - distance);
                pixels[y * w + x] = fill;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            _hudBadgeSprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            return _hudBadgeSprite;
        }
    }

    static Sprite _hudCircleSprite;
    static Sprite HudCircleSprite
    {
        get
        {
            if (_hudCircleSprite != null) return _hudCircleSprite;
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HUD Circle" };
            var pixels = new Color[size * size];
            float center = (size - 1) * 0.5f, radius = size * 0.48f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center, dy = y - center;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float sheen = y / (size - 1f);
                Color fill = Color.Lerp(new Color(0.04f, 0.33f, 0.72f), new Color(0.16f, 0.58f, 0.98f), sheen);
                fill = Color.Lerp(fill, Color.white, Mathf.SmoothStep(0f, 1f, (sheen - 0.55f) / 0.3f) * 0.22f);
                if (distance > radius - 14f) fill = Color.Lerp(new Color(0.3f, 0.78f, 1f), new Color(0.72f, 0.96f, 1f), sheen);
                if (distance > radius - 4f) fill = new Color(0.03f, 0.22f, 0.5f);
                fill.a = Mathf.Clamp01(radius - distance);
                pixels[y * size + x] = fill;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            _hudCircleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _hudCircleSprite;
        }
    }

    static Sprite _hudSpeedometerSprite;
    static Sprite HudSpeedometerSprite
    {
        get
        {
            if (_hudSpeedometerSprite != null) return _hudSpeedometerSprite;
            const int size = 192;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HUD Speedometer Arc" };
            var pixels = new Color[size * size];
            float centerX = (size - 1) * 0.5f, centerY = 54f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - centerX, dy = y - centerY;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                bool arc = radius >= 75f && radius <= 87f && angle >= 0f && angle <= 180f;
                float tickAngle = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 30f) * 30f));
                bool tick = radius >= 57f && radius <= 75f && angle >= 0f && angle <= 180f && tickAngle < 4f;
                var point = new Vector2(dx, dy);
                var tip = new Vector2(42f, 54f);
                float along = Mathf.Clamp01(Vector2.Dot(point, tip) / tip.sqrMagnitude);
                bool needle = (point - tip * along).sqrMagnitude < 31.5f;
                pixels[y * size + x] = arc || tick || needle || radius < 9f ? Color.white : Color.clear;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            _hudSpeedometerSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _hudSpeedometerSprite;
        }
    }

    // Шкала показывает не км/ч, а близость к вылету на ближайшем повороте:
    // зелёная зона — безопасно, жёлтая (с доли Corner Warn Fraction) — двойные
    // очки, красная (опасность ≥ 1) — на этой скорости поворот не удержать.
    // Кусочно-линейно: [0, warn] → [0, GaugeYellowAt], [warn, 1] → [.., GaugeRedAt],
    // [1, GaugeFullDanger] → [.., 1]. В воздухе опасность не считается — шкала
    // медленно опадает, а не падает в ноль мгновенно.
    void UpdateSpeedGauge(float danger, bool riding)
    {
        if (_speedGaugeFill == null) return;
        float warn = Mathf.Clamp(_cornerWarnFraction > 0.001f ? _cornerWarnFraction : 0.8f, 0.05f, 0.99f);
        float level;
        if (danger <= warn) level = danger / warn * GaugeYellowAt;
        else if (danger <= 1f) level = Mathf.Lerp(GaugeYellowAt, GaugeRedAt, (danger - warn) / (1f - warn));
        else level = Mathf.Lerp(GaugeRedAt, 1f, Mathf.Clamp01((danger - 1f) / (GaugeFullDanger - 1f)));
        float smooth = riding ? 0.12f : 0.6f;
        _gaugeLevel = Mathf.SmoothDamp(_gaugeLevel, Mathf.Clamp01(level), ref _gaugeLevelVel, smooth, Mathf.Infinity, Time.unscaledDeltaTime);
        _speedGaugeFill.fillAmount = Mathf.Max(_gaugeLevel, 0.04f);
        _speedGaugeFill.color = Color.white;
    }

    // «1 240» — разряды через пробел, как в образце, независимо от языка устройства.
    static string FormatScore(int value) =>
        value.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');

    // Цвет зоны шкалы в точке t (доля длины): мягкие переходы на границах зон.
    static Color GaugeZoneColor(float t)
    {
        const float blend = 0.035f;
        Color c = Color.Lerp(ZoneGreen, ZoneYellow, Mathf.SmoothStep(0f, 1f, (t - (GaugeYellowAt - blend)) / (2f * blend)));
        return Color.Lerp(c, ZoneRed, Mathf.SmoothStep(0f, 1f, (t - (GaugeRedAt - blend)) / (2f * blend)));
    }

    RectTransform EnsureSafeArea()
    {
        if (_safeArea != null)
            return _safeArea;
        var child = transform.Find("HUD Safe Area");
        if (child == null)
        {
            child = new GameObject("HUD Safe Area", typeof(RectTransform)).transform;
            child.SetParent(transform, false);
        }
        _safeArea = (RectTransform)child;
        _safeArea.pivot = new Vector2(0.5f, 0.5f);
        _safeArea.offsetMin = _safeArea.offsetMax = Vector2.zero;
        return _safeArea;
    }

    void ApplySafeArea()
    {
        if (_safeArea == null)
            return;
        var screen = new Vector2Int(Screen.width, Screen.height);
        Rect area = Screen.safeArea;
        if (area == _appliedSafeArea && screen == _appliedScreen)
            return;
        _appliedSafeArea = area;
        _appliedScreen = screen;
        float w = Mathf.Max(screen.x, 1), h = Mathf.Max(screen.y, 1);
        _safeArea.anchorMin = new Vector2(area.xMin / w, area.yMin / h);
        _safeArea.anchorMax = new Vector2(area.xMax / w, area.yMax / h);
        _safeArea.offsetMin = _safeArea.offsetMax = Vector2.zero;
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
        if (_subscribed && _player != null)
            _player.PlayReset -= OnPlayReset;
        if (_paused)
            Time.timeScale = 1f;
    }

    void OnPlayReset()
    {
        CommitBest();
        _score = 0f;
    }

    void CommitBest()
    {
        if (_score > _best)
        {
            _best = _score;
            PlayerPrefs.SetFloat(BestKey, _best);
            PlayerPrefs.Save();
        }
    }

    void Update()
    {
        if (_player == null)
            _player = FindFirstObjectByType<PlayerView>();
        if (_player != null && !_subscribed)
        {
            _player.PlayReset += OnPlayReset;
            _subscribed = true;
        }
        ApplySafeArea();

        // Esc на ПК и «Назад» на Android (приходит как Escape) — пауза/продолжить.
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (_paused) ResumeGame();
            else PauseGame();
        }

        // Опасность ближайшего поворота: 2 — слишком быстро (красный), 1 — близко к
        // пределу (предупреждающий цвет), 0 — нормально (цвет по множителю).
        // Гистерезис 0.08: на границе значение дрожит (0.98 ↔ 1.01 каждый шаг физики),
        // без него цвет мигал бы.
        float danger = _player != null ? _player.CornerDanger : 0f;
        const float Hyst = 0.08f;
        int dangerStage = _shownDangerStage < 0 ? 0 : _shownDangerStage;
        float warnOn = _cornerWarnFraction > 0.001f ? _cornerWarnFraction : 1f;
        if (danger >= 1f)
            dangerStage = 2;
        else if (dangerStage == 2 && danger < 1f - Hyst)
            dangerStage = danger >= warnOn ? 1 : 0;
        if (dangerStage < 2)
        {
            if (danger >= warnOn)
                dangerStage = 1;
            else if (dangerStage == 1 && danger < warnOn - Hyst)
                dangerStage = 0;
        }

        float multiplier = 1f;
        float speed = 0f;
        bool riskBonus = false;
        if (_player != null)
        {
            bool riding = _player.IsOnSlide;
            speed = riding ? _player.RideSpeed : (_player.Rigidbody != null ? _player.Rigidbody.linearVelocity.magnitude : 0f);
            if (riding)
            {
                if (_player.BoostActive)
                    multiplier = _boostMultiplier;
                // Жёлтая и красная зоны шкалы: риск вознаграждается двойными
                // очками. (Прежний ×2 «за скорость выше 19 м/с» убран 06.11.2026 —
                // двойные очки теперь привязаны к зонам шкалы по просьбе пользователя.)
                if (dangerStage >= 1 && _dangerMultiplier > 1f)
                {
                    multiplier *= _dangerMultiplier;
                    riskBonus = true;
                }
                _score += _player.RideSpeed * Time.deltaTime * _pointsPerMeter * multiplier;
                if (_score > _best)
                    _best = _score;
            }
        }

        int scoreInt = Mathf.FloorToInt(_score);
        if (_scoreText != null && scoreInt != _shownScore)
        {
            _shownScore = scoreInt;
            _scoreText.text = FormatScore(scoreInt);
        }
        int bestInt = Mathf.FloorToInt(_best);
        if (_bestText != null && bestInt != _shownBest)
        {
            _shownBest = bestInt;
            _bestText.text = bestInt > 0 ? "РЕКОРД  " + bestInt.ToString("N0") : "";
        }
        _displaySpeed = _speedDisplaySmoothTime > 0f
            ? Mathf.SmoothDamp(_displaySpeed, speed, ref _displaySpeedVel, _speedDisplaySmoothTime, Mathf.Infinity, Time.unscaledDeltaTime)
            : speed;
        if (_paused)
            _displaySpeedVel = 0f;
        int kmh = Mathf.RoundToInt(_displaySpeed * 3.6f);
        if (_speedText != null && (kmh != _shownSpeed || dangerStage != _shownDangerStage
            || !Mathf.Approximately(multiplier, _shownMultiplier)))
        {
            _shownSpeed = kmh;
            _shownMultiplier = multiplier;
            _shownDangerStage = dangerStage;
            _speedText.text = $"{kmh} <size=30>км/ч</size>";
            _speedText.color = dangerStage == 2 ? ZoneRed : dangerStage == 1 ? ZoneYellow : Color.white;
        }
        UpdateSpeedGauge(danger, _player != null && _player.IsOnSlide);
        UpdateRiskBadge(riskBonus);
    }

    // Плашка «×N ОЧКИ» под панелью очков, пульс звезды и золотые цифры счёта, пока
    // идёт бонус красной зоны. Появляется «хлопком» (0.4 → 1 за ~0.15 с).
    void UpdateRiskBadge(bool active)
    {
        if (_riskBadge == null)
            return;
        float t = Time.unscaledTime;
        if (active && !_riskBadge.gameObject.activeSelf)
        {
            _riskBadge.gameObject.SetActive(true);
            _riskBadgeShownAt = t;
            if (_riskBadgeLabel != null)
                _riskBadgeLabel.text = "×" + _dangerMultiplier.ToString("0.#") + " ОЧКИ";
        }
        else if (!active && _riskBadge.gameObject.activeSelf)
            _riskBadge.gameObject.SetActive(false);

        float pulse = active ? Mathf.Sin(t * 12f) * 0.5f + 0.5f : 0f;
        if (active)
        {
            float pop = Mathf.Clamp01((t - _riskBadgeShownAt) / 0.15f);
            float popScale = Mathf.Lerp(0.4f, 1f, 1f - (1f - pop) * (1f - pop));
            _riskBadge.rectTransform.localScale = Vector3.one * (popScale * (1f + 0.08f * pulse));
        }
        if (_starIcon != null)
            _starIcon.rectTransform.localScale = Vector3.one * (1f + 0.12f * pulse);
        if (_scoreText != null)
            _scoreText.color = active ? Color.Lerp(new Color(1f, 0.86f, 0.3f), Color.white, pulse * 0.35f) : Color.white;
    }

    void OnApplicationPause(bool pause)
    {
        if (pause)
            CommitBest();
    }

    void OnApplicationQuit()
    {
        CommitBest();
    }
}
