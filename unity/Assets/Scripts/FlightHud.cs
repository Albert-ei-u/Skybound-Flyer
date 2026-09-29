using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drone ground-station style HUD built with uGUI at runtime: compass tape,
/// artificial horizon, speed/altitude readouts, battery, minimap and a
/// start/pause menu. Building it in code keeps the scene builder self-contained.
/// </summary>
public sealed class FlightHud : MonoBehaviour
{
    [SerializeField] public Rigidbody drone;
    [SerializeField] public string cityName = "Midtown Manhattan, New York";
    [SerializeField] private float batteryMinutes = 12f;

    private static readonly Color Accent = new Color(0.35f, 1f, 0.75f);
    private static readonly Color PanelColor = new Color(0.02f, 0.05f, 0.08f, 0.55f);

    private Font font;
    private Text speedText, altitudeText, verticalText, headingText, batteryText, statusText;
    private RectTransform compassStrip, horizonPitch, horizonRoll;
    private Image batteryFill;
    private GameObject menuPanel;
    private Text menuTitle;
    private float battery = 1f;
    private float lastAltitude;

    private void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Canvas canvas = CreateCanvas();
        Transform root = canvas.transform;

        BuildCompass(root);
        BuildHorizon(root);
        speedText = BuildTape(root, "SPD", new Vector2(0.5f, 0.5f), new Vector2(-330f, 0f));
        altitudeText = BuildTape(root, "ALT", new Vector2(0.5f, 0.5f), new Vector2(330f, 0f));
        verticalText = Label(root, "", 18, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(330f, -60f), new Vector2(140f, 30f));
        BuildStatusPanel(root);
        BuildMinimap(root);
        BuildMenu(root);

        ShowMenu("SKYBOUND", "Press ENTER to take off");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (menuPanel.activeSelf) HideMenu(); else ShowMenu("PAUSED", "ENTER resume   ·   R reset drone");
        }
        if (menuPanel.activeSelf && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
        {
            HideMenu();
        }
        if (drone == null) return;

        Transform t = drone.transform;
        Vector3 velocity = drone.linearVelocity;
        float altitude = t.position.y;
        float vertical = Time.deltaTime > 0f ? (altitude - lastAltitude) / Time.deltaTime : 0f;
        lastAltitude = altitude;

        speedText.text = $"{new Vector3(velocity.x, 0f, velocity.z).magnitude * 3.6f:0}\n<size=14>km/h</size>";
        altitudeText.text = $"{altitude:0}\n<size=14>m AGL</size>";
        verticalText.text = $"{(vertical >= 0 ? "▲" : "▼")} {Mathf.Abs(vertical):0.0} m/s";

        float heading = (t.eulerAngles.y + 360f) % 360f;
        headingText.text = $"{heading:000}°";
        // 4 px per degree; strip holds three 360° copies so it can wrap.
        compassStrip.anchoredPosition = new Vector2(-heading * 4f, 0f);

        float pitch = Mathf.DeltaAngle(0f, t.eulerAngles.x);
        float roll = Mathf.DeltaAngle(0f, t.eulerAngles.z);
        horizonRoll.localRotation = Quaternion.Euler(0f, 0f, -roll);
        horizonPitch.anchoredPosition = new Vector2(0f, pitch * 4f);

        float drain = Time.deltaTime / (batteryMinutes * 60f);
        battery = Mathf.Max(0f, battery - drain * (0.6f + velocity.magnitude / 25f));
        batteryFill.fillAmount = battery;
        batteryFill.color = battery > 0.3f ? Accent : battery > 0.15f ? Color.yellow : Color.red;
        batteryText.text = $"BAT {battery * 100f:0}%";

        statusText.text = altitude > 120f ? "<color=#ffcc00>ABOVE 120 m LEGAL CEILING</color>"
                        : battery < 0.15f ? "<color=#ff5555>LOW BATTERY — LAND NOW</color>"
                        : "GPS LOCK  ·  18 SAT  ·  LINK 100%";
    }

    // ------------------------------------------------------------------- layout

    private Canvas CreateCanvas()
    {
        var go = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    private void BuildCompass(Transform root)
    {
        RectTransform frame = Panel(root, "Compass", new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(720f, 56f), PanelColor);
        frame.gameObject.AddComponent<RectMask2D>();

        compassStrip = new GameObject("Strip", typeof(RectTransform)).GetComponent<RectTransform>();
        compassStrip.SetParent(frame, false);
        compassStrip.sizeDelta = Vector2.zero;
        string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        for (int deg = -360; deg <= 720; deg += 15)
        {
            int wrapped = ((deg % 360) + 360) % 360;
            bool cardinal = wrapped % 45 == 0;
            string label = cardinal ? names[wrapped / 45] : (wrapped % 30 == 0 ? wrapped.ToString() : "|");
            Text tick = Label(compassStrip, label, cardinal ? 22 : 14, TextAnchor.MiddleCenter,
                              new Vector2(0.5f, 0.5f), new Vector2(deg * 4f, 4f), new Vector2(60f, 40f));
            tick.color = wrapped == 0 ? new Color(1f, 0.4f, 0.3f) : cardinal ? Color.white : new Color(1f, 1f, 1f, 0.6f);
        }

        Panel(root, "CompassPointer", new Vector2(0.5f, 1f), new Vector2(0f, -82f), new Vector2(3f, 14f), Accent);
        headingText = Label(root, "000°", 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(100f, 28f));
        headingText.color = Accent;
    }

    private void BuildHorizon(Transform root)
    {
        // Circular attitude indicator in the lower centre.
        RectTransform frame = Panel(root, "AttitudeIndicator", new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(200f, 200f), Color.white);
        Image mask = frame.GetComponent<Image>();
        mask.sprite = CircleSprite();
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        horizonRoll = new GameObject("Roll", typeof(RectTransform)).GetComponent<RectTransform>();
        horizonRoll.SetParent(frame, false);
        horizonRoll.sizeDelta = new Vector2(200f, 200f);

        horizonPitch = new GameObject("Pitch", typeof(RectTransform)).GetComponent<RectTransform>();
        horizonPitch.SetParent(horizonRoll, false);
        horizonPitch.sizeDelta = Vector2.zero;
        Panel(horizonPitch, "Sky", new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(600f, 500f), new Color(0.2f, 0.5f, 0.85f));
        Panel(horizonPitch, "Ground", new Vector2(0.5f, 0.5f), new Vector2(0f, -250f), new Vector2(600f, 500f), new Color(0.45f, 0.3f, 0.15f));
        Panel(horizonPitch, "Line", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 2f), Color.white);
        for (int p = -30; p <= 30; p += 10)
        {
            if (p == 0) continue;
            Panel(horizonPitch, $"Pitch{p}", new Vector2(0.5f, 0.5f), new Vector2(0f, p * 4f), new Vector2(p % 20 == 0 ? 60f : 30f, 2f), new Color(1f, 1f, 1f, 0.8f));
        }

        // Fixed aircraft symbol.
        Panel(frame, "WingL", new Vector2(0.5f, 0.5f), new Vector2(-40f, 0f), new Vector2(50f, 4f), new Color(1f, 0.8f, 0.1f));
        Panel(frame, "WingR", new Vector2(0.5f, 0.5f), new Vector2(40f, 0f), new Vector2(50f, 4f), new Color(1f, 0.8f, 0.1f));
        Panel(frame, "Dot", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 6f), new Color(1f, 0.8f, 0.1f));
    }

    private Text BuildTape(Transform root, string caption, Vector2 anchor, Vector2 position)
    {
        RectTransform box = Panel(root, caption, anchor, position, new Vector2(130f, 80f), PanelColor);
        Outline(box);
        Label(box, caption, 14, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(120f, 20f)).color = Accent;
        Text value = Label(box, "0", 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(120f, 60f));
        value.supportRichText = true;
        return value;
    }

    private void BuildStatusPanel(Transform root)
    {
        RectTransform panel = Panel(root, "Status", new Vector2(0f, 1f), new Vector2(190f, -70f), new Vector2(340f, 100f), PanelColor);
        Outline(panel);
        Label(panel, cityName.ToUpperInvariant(), 16, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(170f, -18f), new Vector2(320f, 22f)).color = Accent;
        batteryText = Label(panel, "BAT 100%", 16, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(170f, -46f), new Vector2(320f, 22f));

        RectTransform barBack = Panel(panel, "BatteryBack", new Vector2(0f, 1f), new Vector2(240f, -46f), new Vector2(160f, 12f), new Color(1f, 1f, 1f, 0.15f));
        batteryFill = Panel(barBack, "BatteryFill", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160f, 12f), Accent).GetComponent<Image>();
        batteryFill.sprite = WhiteSprite();
        batteryFill.type = Image.Type.Filled;
        batteryFill.fillMethod = Image.FillMethod.Horizontal;

        statusText = Label(panel, "", 14, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(170f, -76f), new Vector2(320f, 22f));
        statusText.supportRichText = true;

        Text help = Label(root, "W/S lift   A/D roll   ↑/↓ pitch   Q/E yaw   R reset   ESC menu", 14,
                          TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(330f, 24f), new Vector2(620f, 24f));
        help.color = new Color(1f, 1f, 1f, 0.6f);
        Label(root, "© OpenStreetMap contributors", 12, TextAnchor.MiddleRight, new Vector2(1f, 0f),
              new Vector2(-140f, 14f), new Vector2(260f, 20f)).color = new Color(1f, 1f, 1f, 0.5f);
    }

    private void BuildMinimap(Transform root)
    {
        var texture = new RenderTexture(512, 512, 16) { name = "MinimapTexture" };
        var camObject = new GameObject("MinimapCamera");
        var cam = camObject.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 180f;
        cam.targetTexture = texture;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.1f, 0.12f, 0.1f);
        var follow = camObject.AddComponent<MinimapFollow>();
        follow.target = drone != null ? drone.transform : null;

        RectTransform frame = Panel(root, "Minimap", new Vector2(1f, 1f), new Vector2(-150f, -150f), new Vector2(240f, 240f), Color.white);
        frame.GetComponent<Image>().sprite = CircleSprite();
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var map = new GameObject("Map", typeof(RectTransform), typeof(RawImage));
        map.transform.SetParent(frame, false);
        map.GetComponent<RectTransform>().sizeDelta = new Vector2(240f, 240f);
        map.GetComponent<RawImage>().texture = texture;
        Panel(frame, "Player", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f), new Color(1f, 0.3f, 0.2f)).GetComponent<Image>().sprite = CircleSprite();
        Label(root, "N", 18, TextAnchor.MiddleCenter, new Vector2(1f, 1f), new Vector2(-150f, -18f), new Vector2(30f, 24f)).color = new Color(1f, 0.4f, 0.3f);
    }

    private void BuildMenu(Transform root)
    {
        RectTransform panel = Panel(root, "Menu", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3000f, 3000f), new Color(0f, 0f, 0f, 0.6f));
        menuPanel = panel.gameObject;
        RectTransform card = Panel(panel, "Card", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 260f), new Color(0.03f, 0.07f, 0.1f, 0.92f));
        Outline(card);
        menuTitle = Label(card, "", 56, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(540f, 80f));
        menuTitle.fontStyle = FontStyle.Bold;
        menuTitle.color = Accent;
        Label(card, cityName, 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -135f), new Vector2(540f, 30f));
    }

    private void ShowMenu(string title, string prompt)
    {
        menuTitle.text = title;
        Transform card = menuPanel.transform.GetChild(0);
        Text hint = card.Find("Hint")?.GetComponent<Text>();
        if (hint == null)
        {
            hint = Label(card, "", 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(540f, 30f));
            hint.name = "Hint";
            hint.color = new Color(1f, 1f, 1f, 0.7f);
        }
        hint.text = prompt;
        menuPanel.SetActive(true);
        Time.timeScale = 0f;
    }

    private void HideMenu()
    {
        menuPanel.SetActive(false);
        Time.timeScale = 1f;
    }

    // ------------------------------------------------------------------ helpers

    private RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private Text Label(Transform parent, string text, int size, TextAnchor alignment, Vector2 anchor, Vector2 position, Vector2 box)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = box;
        var label = go.GetComponent<Text>();
        label.font = font;
        label.text = text;
        label.fontSize = size;
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        go.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
        return label;
    }

    private static void Outline(RectTransform rect)
    {
        var outline = rect.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.5f);
        outline.effectDistance = new Vector2(1f, -1f);
    }

    private static Sprite circle, white;

    private static Sprite CircleSprite()
    {
        if (circle != null) return circle;
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(size / 2f, size / 2f));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(size / 2f - d)));
        }
        tex.Apply();
        circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return circle;
    }

    private static Sprite WhiteSprite()
    {
        if (white != null) return white;
        white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        return white;
    }
}

/// <summary>Keeps the top-down minimap camera above the drone, north-up.</summary>
public sealed class MinimapFollow : MonoBehaviour
{
    public Transform target;

    private void LateUpdate()
    {
        if (target == null) return;
        transform.SetPositionAndRotation(target.position + Vector3.up * 400f, Quaternion.Euler(90f, 0f, 0f));
    }
}
