using UnityEngine;
using UnityEngine.SceneManagement;
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
    [SerializeField] public MissionSystem missions;
    [SerializeField] public string[] citySceneNames = new string[0];
    [SerializeField] public string[] cityLabels = new string[0];

    private static readonly Color Accent = new Color(0.35f, 1f, 0.75f);
    private static readonly Color PanelColor = new Color(0.02f, 0.05f, 0.08f, 0.55f);

    private Font font;
    private Text speedText, altitudeText, verticalText, headingText, batteryText, statusText;
    private RectTransform compassStrip, horizonPitch, horizonRoll;
    private Image batteryFill;
    private GameObject menuPanel;
    private Text menuTitle;
    private GameObject helpPanel;
    private Text missionTitle, missionObjective, missionTimer, cashText, bannerTitle, bannerDetail;
    private CanvasGroup banner;
    private float bannerTime;
    private RectTransform waypoint, waypointArrow;
    private RectTransform minimapFrame;
    private Camera minimapCamera;
    private bool bigMap;
    private readonly System.Collections.Generic.List<RectTransform> blips = new System.Collections.Generic.List<RectTransform>();
    private const float MiniMapSize = 240f, MiniMapRange = 180f, BigMapSize = 760f, BigMapRange = 700f;
    private Text waypointDistance;
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
        BuildMissionPanel(root);
        BuildWaypoint(root);
        BuildBanner(root);
        BuildHelp(root);
        if (missions != null)
        {
            missions.Banner += OnBanner;
            if (missions.Active) OnBanner(missions.Title, missions.Objective, true); // started before the HUD
        }

        ShowMenu("SKYBOUND", MenuPrompt("ENTER take off"));
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.H)) helpPanel.SetActive(!helpPanel.activeSelf);
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (menuPanel.activeSelf) HideMenu(); else ShowMenu("PAUSED", MenuPrompt("ENTER resume   ·   P restart career"));
        }
        if (menuPanel.activeSelf)
        {
            for (int i = 0; i < citySceneNames.Length && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    Time.timeScale = 1f;
                    SceneManager.LoadScene(citySceneNames[i]);
                    return;
                }
            }
            if (Input.GetKeyDown(KeyCode.P) && missions != null)
            {
                missions.ResetProgress();
                HideMenu();
            }
        }
        if (Input.GetKeyDown(KeyCode.M)) ToggleBigMap();
        UpdateMission();
        UpdateBlips();
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

    // ----------------------------------------------------------------- missions

    private string MenuPrompt(string action)
    {
        var sb = new System.Text.StringBuilder(action);
        if (citySceneNames.Length > 1)
        {
            sb.Append("\nCity:  ");
            for (int i = 0; i < cityLabels.Length; i++) sb.Append($"[{i + 1}] {cityLabels[i]}   ");
        }
        return sb.ToString();
    }

    private void UpdateMission()
    {
        if (missions == null) return;
        missionTitle.text = missions.Title;
        missionObjective.text = missions.Objective;
        cashText.text = $"${missions.Cash:N0}";
        if (missions.Active)
        {
            int t = Mathf.CeilToInt(missions.TimeLeft);
            missionTimer.text = $"{t / 60}:{t % 60:00}";
            missionTimer.color = missions.TimeLeft < 15f ? new Color(1f, 0.3f, 0.3f) : Color.white;
        }
        else
        {
            missionTimer.text = "";
        }

        if (bannerTime > 0f)
        {
            bannerTime -= Time.unscaledDeltaTime;
            banner.alpha = Mathf.Clamp01(bannerTime);
        }

        Camera cam = Camera.main;
        Vector3? target = missions.Target;
        waypoint.gameObject.SetActive(target.HasValue && cam != null && !menuPanel.activeSelf);
        if (!waypoint.gameObject.activeSelf) return;

        // Top-down bearing to the objective relative to where the drone faces.
        Vector3 local = drone.transform.InverseTransformPoint(target.Value);
        float bearing = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        waypointArrow.localRotation = Quaternion.Euler(0f, 0f, -bearing);
        waypointDistance.text = $"{Vector3.Distance(drone.position, target.Value):0} m";
    }

    private void OnBanner(string headline, string detail, bool success)
    {
        bannerTitle.text = headline;
        bannerTitle.color = success ? new Color(1f, 0.82f, 0.2f) : new Color(1f, 0.3f, 0.3f);
        bannerDetail.text = detail;
        bannerTime = 3.5f;
        banner.alpha = 1f;
        if (headline != "PACKAGE COLLECTED") battery = 1f; // fresh battery for each job
    }

    private void BuildMissionPanel(Transform root)
    {
        RectTransform panel = Panel(root, "Mission", new Vector2(0f, 1f), new Vector2(190f, -190f), new Vector2(340f, 110f), PanelColor);
        Outline(panel);
        missionTitle = Label(panel, "", 16, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(170f, -18f), new Vector2(320f, 22f));
        missionTitle.color = new Color(1f, 0.82f, 0.2f);
        missionTitle.fontStyle = FontStyle.Bold;
        missionObjective = Label(panel, "", 15, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(170f, -58f), new Vector2(320f, 44f));
        missionObjective.horizontalOverflow = HorizontalWrapMode.Wrap;
        missionTimer = Label(panel, "", 22, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(170f, -92f), new Vector2(320f, 26f));
        cashText = Label(root, "$0", 34, TextAnchor.MiddleRight, new Vector2(1f, 1f), new Vector2(-150f, -300f), new Vector2(260f, 44f));
        cashText.color = new Color(0.45f, 0.9f, 0.4f);
        cashText.fontStyle = FontStyle.Bold;
    }

    private void BuildWaypoint(Transform root)
    {
        waypoint = new GameObject("Waypoint", typeof(RectTransform)).GetComponent<RectTransform>();
        waypoint.SetParent(root, false);
        waypoint.anchorMin = waypoint.anchorMax = new Vector2(0.5f, 1f);
        waypoint.anchoredPosition = new Vector2(0f, -170f);
        Text arrow = Label(waypoint, "▲", 44, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
        arrow.color = new Color(1f, 0.82f, 0.2f);
        waypointArrow = arrow.rectTransform;
        waypointDistance = Label(waypoint, "", 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -42f), new Vector2(160f, 24f));
    }

    private void BuildBanner(Transform root)
    {
        var go = new GameObject("Banner", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(root, false);
        banner = go.GetComponent<CanvasGroup>();
        banner.alpha = 0f;
        Panel(go.transform, "Band", new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(3000f, 130f), new Color(0f, 0f, 0f, 0.55f));
        bannerTitle = Label(go.transform, "", 64, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), new Vector2(1400f, 80f));
        bannerTitle.fontStyle = FontStyle.Bold;
        bannerDetail = Label(go.transform, "", 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 145f), new Vector2(1400f, 30f));
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

        Text help = Label(root, "W forward   Shift boost   S brake   Q/E turn   A/D roll   ↑/↓ climb/descend   R reset   M map   H help   ESC menu", 14,
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
        cam.orthographicSize = MiniMapRange;
        minimapCamera = cam;
        cam.targetTexture = texture;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.1f, 0.12f, 0.1f);
        var follow = camObject.AddComponent<MinimapFollow>();
        follow.target = drone != null ? drone.transform : null;

        RectTransform frame = Panel(root, "Minimap", new Vector2(1f, 1f), new Vector2(-150f, -150f), new Vector2(240f, 240f), Color.white);
        minimapFrame = frame;
        frame.GetComponent<Image>().sprite = CircleSprite();
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var map = new GameObject("Map", typeof(RectTransform), typeof(RawImage));
        map.transform.SetParent(frame, false);
        RectTransform mapRect = map.GetComponent<RectTransform>();
        mapRect.anchorMin = Vector2.zero;
        mapRect.anchorMax = Vector2.one;
        mapRect.sizeDelta = Vector2.zero; // stretch with the frame when the big map opens
        map.GetComponent<RawImage>().texture = texture;
        Panel(frame, "Player", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f), new Color(1f, 0.3f, 0.2f)).GetComponent<Image>().sprite = CircleSprite();
        Label(root, "N", 18, TextAnchor.MiddleCenter, new Vector2(1f, 1f), new Vector2(-150f, -18f), new Vector2(30f, 24f)).color = new Color(1f, 0.4f, 0.3f);
    }

    private void ToggleBigMap()
    {
        bigMap = !bigMap;
        minimapFrame.anchorMin = minimapFrame.anchorMax = bigMap ? new Vector2(0.5f, 0.5f) : new Vector2(1f, 1f);
        minimapFrame.anchoredPosition = bigMap ? Vector2.zero : new Vector2(-150f, -150f);
        minimapFrame.sizeDelta = Vector2.one * (bigMap ? BigMapSize : MiniMapSize);
        minimapCamera.orthographicSize = bigMap ? BigMapRange : MiniMapRange;
    }

    /// <summary>Mission objectives on the map: yellow = next, blue = later; pinned to the edge when out of range.</summary>
    private void UpdateBlips()
    {
        int used = 0;
        if (missions != null && drone != null)
        {
            float size = bigMap ? BigMapSize : MiniMapSize;
            float range = bigMap ? BigMapRange : MiniMapRange;
            float edge = size * 0.5f - 10f;
            foreach (Vector3 target in missions.AllTargets)
            {
                if (used == blips.Count)
                {
                    RectTransform blip = Panel(minimapFrame, "Blip", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 16f), Color.white);
                    blip.GetComponent<Image>().sprite = CircleSprite();
                    blip.gameObject.AddComponent<UnityEngine.UI.Outline>().effectColor = Color.black;
                    blips.Add(blip);
                }
                Vector3 offset = target - drone.position; // map is north-up, so world x/z map straight to UI x/y
                Vector2 pos = new Vector2(offset.x, offset.z) / range * (size * 0.5f);
                if (pos.magnitude > edge) pos = pos.normalized * edge;
                RectTransform b = blips[used];
                b.gameObject.SetActive(true);
                b.anchoredPosition = pos;
                b.sizeDelta = Vector2.one * (used == 0 ? 18f : 12f);
                b.GetComponent<Image>().color = used == 0 ? new Color(1f, 0.82f, 0.2f) : new Color(0.2f, 0.85f, 1f);
                used++;
            }
        }
        for (int i = used; i < blips.Count; i++) blips[i].gameObject.SetActive(false);
    }

    private void BuildMenu(Transform root)
    {
        RectTransform panel = Panel(root, "Menu", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3000f, 3000f), new Color(0f, 0f, 0f, 0.6f));
        menuPanel = panel.gameObject;
        RectTransform card = Panel(panel, "Card", new Vector2(0.5f, 0.5f), new Vector2(0f, 300f), new Vector2(560f, 260f), new Color(0.03f, 0.07f, 0.1f, 0.92f));
        Outline(card);
        menuTitle = Label(card, "", 56, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(540f, 80f));
        menuTitle.fontStyle = FontStyle.Bold;
        menuTitle.color = Accent;
        Label(card, cityName, 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -135f), new Vector2(540f, 30f));
    }

    private void BuildHelp(Transform root)
    {
        RectTransform panel = Panel(root, "HowToPlay", new Vector2(0.5f, 0.5f), new Vector2(0f, -55f), new Vector2(640f, 410f), new Color(0.03f, 0.07f, 0.1f, 0.92f));
        Outline(panel);
        helpPanel = panel.gameObject;
        Text title = Label(panel, "HOW TO PLAY", 28, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(600f, 40f));
        title.color = Accent;
        title.fontStyle = FontStyle.Bold;

        string[,] rows =
        {
            { "W", "Fly forward (hold Shift to boost)" },
            { "S", "Slow down / brake (reverse when stopped)" },
            { "Q / E", "Turn left / right" },
            { "A / D", "Roll left / right (slide sideways)" },
            { "↑ / ↓", "Climb / descend and land" },
            { "R", "Reset drone to the start point" },
            { "H", "Show / hide this guide" },
            { "M", "Big map with mission markers" },
            { "ESC", "Pause menu (1/2/3 change city)" },
        };
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            float y = -80f - i * 30f;
            Label(panel, rows[i, 0], 20, TextAnchor.MiddleRight, new Vector2(0.5f, 1f), new Vector2(-170f, y), new Vector2(120f, 28f)).color = Accent;
            Label(panel, rows[i, 1], 18, TextAnchor.MiddleLeft, new Vector2(0.5f, 1f), new Vector2(90f, y), new Vector2(360f, 28f));
        }
        Label(panel, "Missions: follow the yellow arrow. Land in yellow markers, fly through blue rings. Don't crash!", 15,
              TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(600f, 24f)).color = new Color(1f, 1f, 1f, 0.7f);
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
        if (helpPanel != null) helpPanel.SetActive(true);
        Time.timeScale = 0f;
    }

    private void HideMenu()
    {
        menuPanel.SetActive(false);
        helpPanel.SetActive(false);
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
