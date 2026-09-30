using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static UiKit;

/// <summary>
/// GTA-style game UI built with uGUI at runtime:
/// - in flight: radar minimap with GPS route and mission blips (bottom-left),
///   cash and mission timer (top-right), objective subtitles (bottom),
///   compass, target arrow, speed/altitude and an attitude indicator;
/// - screens: title, job select (locked missions and levels), pause, help,
///   big map, and MISSION PASSED / WASTED banners.
/// </summary>
public sealed class FlightHud : MonoBehaviour
{
    [SerializeField] public Rigidbody drone;
    [SerializeField] public string cityName = "New York";
    [SerializeField] public MissionSystem missions;
    [SerializeField] public string[] citySceneNames = new string[0];
    [SerializeField] public string[] cityLabels = new string[0];
    [SerializeField] private float batteryMinutes = 12f;

    // Only the minimap camera renders this layer (the GPS route).
    private const int RouteLayer = 31;
    private const float MiniW = 380f, MiniH = 240f, MiniRange = 220f;
    private const float BigSize = 820f, BigRange = 750f;

    private Transform hudRoot;
    private Text speedText, altitudeText, headingText, batteryText, statusText;
    private Text cashText, missionTitle, timerText, subtitle, bannerTitle, bannerDetail, waypointDistance;
    private RectTransform compassStrip, horizonPitch, horizonRoll, waypoint, waypointArrow;
    private RectTransform minimapFrame, playerArrow, timerBox;
    private Image batteryFill;
    private CanvasGroup banner;
    private float bannerTime;
    private Camera minimapCamera;
    private LineRenderer route, course;
    private float bigRange = BigRange;
    private float routeTimer;
    private bool bigMap;
    private readonly List<RectTransform> blips = new List<RectTransform>();

    private GameObject titleScreen, jobScreen, pauseScreen, helpPanel;
    private Transform jobList;
    private float battery = 1f;

    private bool MenuOpen => titleScreen.activeSelf || jobScreen.activeSelf || pauseScreen.activeSelf;

    private void Start()
    {
        // Old scenes (e.g. DroneTraining) have no missions: jump to a real city scene.
        if (missions == null) missions = FindFirstObjectByType<MissionSystem>();
        if (missions == null)
        {
            string city = citySceneNames.Length > 0 ? citySceneNames[0] : "NewYork";
            if (Application.CanStreamedLevelBeLoaded(city))
            {
                SceneManager.LoadScene(city);
                return;
            }
            Debug.LogError("This scene has no missions. Run Skybound > Create Drone Training Scene, then open Assets/Scenes/NewYork.");
        }

        Canvas hud = CreateCanvas(transform, "HUD", 0);
        hudRoot = hud.transform;
        BuildCompass(hudRoot);
        BuildInstruments(hudRoot);
        BuildStatus(hudRoot);
        BuildMissionHud(hudRoot);
        BuildMinimap(hudRoot);
        BuildBanner(hudRoot);

        Canvas menus = CreateCanvas(transform, "Menus", 10);
        BuildHelp(menus.transform);
        BuildTitle(menus.transform);
        BuildJobs(menus.transform);
        BuildPause(menus.transform);

        if (missions != null)
        {
            missions.Banner += OnBanner;
            missions.Finished += () => ShowScreen(jobScreen);
        }
        ShowScreen(titleScreen);
    }

    private void Update()
    {
        if (titleScreen == null) return; // redirected to another scene
        HandleKeys();
        if (drone == null) return;
        UpdateInstruments();
        UpdateMission();
        UpdateMap();
    }

    // ---------------------------------------------------------------- input

    private void HandleKeys()
    {
        bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        if (titleScreen.activeSelf && enter) ShowScreen(jobScreen);
        else if (pauseScreen.activeSelf && enter) ShowScreen(null);

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (jobScreen.activeSelf || pauseScreen.activeSelf) ShowScreen(null);
            else if (!titleScreen.activeSelf) ShowScreen(pauseScreen);
        }
        if (!MenuOpen)
        {
            if (Input.GetKeyDown(KeyCode.J)) ShowScreen(jobScreen);
            if (Input.GetKeyDown(KeyCode.M)) ToggleBigMap();
        }
        if (Input.GetKeyDown(KeyCode.H)) helpPanel.SetActive(!helpPanel.activeSelf);
    }

    /// <summary>Shows one menu screen (or none), pausing the game while a menu is open.</summary>
    private void ShowScreen(GameObject screen)
    {
        titleScreen.SetActive(screen == titleScreen);
        jobScreen.SetActive(screen == jobScreen);
        pauseScreen.SetActive(screen == pauseScreen);
        if (screen == jobScreen) RefreshJobs();
        helpPanel.SetActive(screen == titleScreen);
        hudRoot.gameObject.SetActive(screen == null || screen == pauseScreen);
        Time.timeScale = screen == null ? 1f : 0f;
        Cursor.visible = screen != null;
        Cursor.lockState = CursorLockMode.None;
    }

    // --------------------------------------------------------------- update

    private void UpdateInstruments()
    {
        Transform t = drone.transform;
        Vector3 velocity = drone.linearVelocity;
        float altitude = t.position.y;

        speedText.text = $"{new Vector3(velocity.x, 0f, velocity.z).magnitude * 3.6f:0}<size=14> km/h</size>";
        altitudeText.text = $"{altitude:0}<size=14> m</size>";

        float heading = (t.eulerAngles.y + 360f) % 360f;
        headingText.text = $"{heading:000}°";
        compassStrip.anchoredPosition = new Vector2(-heading * 4f, 0f);
        playerArrow.localRotation = Quaternion.Euler(0f, 0f, -heading);

        float pitch = Mathf.DeltaAngle(0f, t.eulerAngles.x);
        float roll = Mathf.DeltaAngle(0f, t.eulerAngles.z);
        horizonRoll.localRotation = Quaternion.Euler(0f, 0f, -roll);
        horizonPitch.anchoredPosition = new Vector2(0f, pitch * 3f);

        float drain = Time.deltaTime / (batteryMinutes * 60f);
        battery = Mathf.Max(0f, battery - drain * (0.6f + velocity.magnitude / 25f));
        batteryFill.fillAmount = battery;
        batteryFill.color = battery > 0.3f ? new Color(0.3f, 0.75f, 0.35f) : battery > 0.15f ? Color.yellow : Color.red;
        batteryText.text = $"BATTERY {battery * 100f:0}%";

        statusText.text = altitude > 120f ? "<color=#ffcc00>ABOVE 120 m LEGAL CEILING</color>"
                        : battery < 0.15f ? "<color=#ff5555>LOW BATTERY — LAND NOW</color>"
                        : cityName.ToUpperInvariant();
    }

    private void UpdateMission()
    {
        if (bannerTime > 0f)
        {
            bannerTime -= Time.unscaledDeltaTime;
            banner.alpha = Mathf.Clamp01(bannerTime);
        }
        if (missions == null) return;

        cashText.text = $"${missions.Cash:N0}";
        missionTitle.text = missions.Title;
        subtitle.text = missions.Objective;
        timerBox.gameObject.SetActive(missions.Active);
        if (missions.Active)
        {
            int seconds = Mathf.CeilToInt(missions.TimeLeft);
            timerText.text = $"TIME   {seconds / 60}:{seconds % 60:00}";
            timerText.color = missions.TimeLeft < 15f ? new Color(1f, 0.35f, 0.35f) : Color.white;
        }

        Vector3? target = missions.Target;
        waypoint.gameObject.SetActive(target.HasValue);
        if (target.HasValue)
        {
            // Same heading source as the compass, so arrow and compass always agree.
            Vector3 dir = target.Value - drone.position;
            float targetBearing = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float relative = Mathf.DeltaAngle(drone.transform.eulerAngles.y, targetBearing);
            waypointArrow.localRotation = Quaternion.Euler(0f, 0f, -relative);
            waypointDistance.text = $"{Vector3.Distance(drone.position, target.Value):0} m";
        }
    }

    private void UpdateMap()
    {
        float width = bigMap ? BigSize : MiniW, height = bigMap ? BigSize : MiniH;
        // The big map zooms out to fit the whole mission course.
        if (bigMap)
        {
            float farthest = 0f;
            if (missions != null)
                foreach (Vector3 p in missions.AllTargets)
                    farthest = Mathf.Max(farthest, Vector2.Distance(new Vector2(p.x, p.z), new Vector2(drone.position.x, drone.position.z)));
            bigRange = Mathf.Clamp(farthest * 1.15f, 300f, 1500f);
            minimapCamera.orthographicSize = bigRange;
        }
        float range = bigMap ? bigRange : MiniRange;
        float scale = height * 0.5f / range; // UI pixels per metre

        // GPS route along the real roads, refreshed every second.
        Vector3? target = missions != null ? missions.Target : null;
        routeTimer -= Time.unscaledDeltaTime;
        if (target.HasValue && routeTimer <= 0f && missions.network != null)
        {
            routeTimer = 1f;
            List<Vector3> path = missions.network.FindRoute(drone.position, target.Value);
            route.positionCount = path.Count;
            for (int i = 0; i < path.Count; i++) route.SetPosition(i, new Vector3(path[i].x, 1f, path[i].z));
        }
        if (!target.HasValue) route.positionCount = 0;
        route.widthMultiplier = (bigMap ? 16f : 7f) * range / (bigMap ? BigRange : MiniRange);

        // Mission roadmap: line through every remaining ring / stop, in order.
        course.positionCount = 0;
        if (missions != null)
        {
            var points = new List<Vector3>(missions.AllTargets);
            course.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) course.SetPosition(i, new Vector3(points[i].x, 0.5f, points[i].z));
        }
        course.widthMultiplier = route.widthMultiplier * 0.6f;

        // Blips: yellow = next objective, blue = later ones; pinned to the edge when off-map.
        int used = 0;
        if (missions != null)
        {
            foreach (Vector3 point in missions.AllTargets)
            {
                if (used == blips.Count)
                {
                    RectTransform blip = Panel(minimapFrame, "Blip", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f), Color.white);
                    blip.GetComponent<Image>().sprite = Circle();
                    blip.gameObject.AddComponent<UnityEngine.UI.Outline>().effectColor = Color.black;
                    Text number = Label(blip, "", 13, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
                    number.color = Color.black;
                    number.fontStyle = FontStyle.Bold;
                    number.GetComponent<Shadow>().enabled = false;
                    blips.Add(blip);
                }
                Vector3 offset = point - drone.position;
                Vector2 pos = new Vector2(offset.x, offset.z) * scale;
                pos.x = Mathf.Clamp(pos.x, -width / 2f + 12f, width / 2f - 12f);
                pos.y = Mathf.Clamp(pos.y, -height / 2f + 12f, height / 2f - 12f);
                RectTransform b = blips[used];
                b.gameObject.SetActive(true);
                b.SetAsLastSibling();
                b.anchoredPosition = pos;
                b.sizeDelta = Vector2.one * (used == 0 ? 24f : 20f);
                b.GetComponentInChildren<Text>().text = (used + 1).ToString(); // order to fly them in
                b.GetComponent<Image>().color = used == 0 ? Gold : new Color(0.2f, 0.85f, 1f);
                used++;
            }
        }
        for (int i = used; i < blips.Count; i++) blips[i].gameObject.SetActive(false);
        playerArrow.SetAsLastSibling();
    }

    private void ToggleBigMap()
    {
        bigMap = !bigMap;
        minimapFrame.anchorMin = minimapFrame.anchorMax = bigMap ? new Vector2(0.5f, 0.5f) : Vector2.zero;
        minimapFrame.anchoredPosition = bigMap ? Vector2.zero : new Vector2(40f + MiniW / 2f, 60f + MiniH / 2f);
        minimapFrame.sizeDelta = bigMap ? new Vector2(BigSize, BigSize) : new Vector2(MiniW, MiniH);
        minimapCamera.orthographicSize = bigMap ? bigRange : MiniRange;
        minimapCamera.aspect = bigMap ? 1f : MiniW / MiniH;
    }

    private void OnBanner(string headline, string detail, bool success)
    {
        bannerTitle.text = headline;
        bannerTitle.color = success ? Gold : new Color(0.85f, 0.1f, 0.1f);
        bannerDetail.text = detail;
        bannerTime = 3.5f;
        banner.alpha = 1f;
        if (headline != "PACKAGE COLLECTED" && headline != "DELIVERED") battery = 1f; // fresh battery for each job
        routeTimer = 0f;
    }

    // ------------------------------------------------------------ HUD build

    private void BuildCompass(Transform root)
    {
        RectTransform frame = Panel(root, "Compass", new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(640f, 44f), PanelColor);
        frame.gameObject.AddComponent<RectMask2D>();
        compassStrip = new GameObject("Strip", typeof(RectTransform)).GetComponent<RectTransform>();
        compassStrip.SetParent(frame, false);
        compassStrip.sizeDelta = Vector2.zero;
        string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        for (int deg = -360; deg <= 720; deg += 15)
        {
            int wrapped = ((deg % 360) + 360) % 360;
            bool cardinal = wrapped % 45 == 0;
            string label = cardinal ? names[wrapped / 45] : wrapped % 30 == 0 ? wrapped.ToString() : "·";
            Text tick = Label(compassStrip, label, cardinal ? 20 : 13, TextAnchor.MiddleCenter,
                              new Vector2(0.5f, 0.5f), new Vector2(deg * 4f, 0f), new Vector2(60f, 40f));
            tick.color = wrapped == 0 ? new Color(1f, 0.4f, 0.3f) : cardinal ? Color.white : new Color(1f, 1f, 1f, 0.6f);
        }
        Panel(root, "CompassPointer", new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(3f, 10f), Gold);
        headingText = Label(root, "000°", 16, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -82f), new Vector2(100f, 22f));

        waypoint = new GameObject("Waypoint", typeof(RectTransform)).GetComponent<RectTransform>();
        waypoint.SetParent(root, false);
        waypoint.anchorMin = waypoint.anchorMax = new Vector2(0.5f, 1f);
        waypoint.anchoredPosition = new Vector2(0f, -125f);
        Text arrow = Label(waypoint, "▲", 40, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
        arrow.color = Gold;
        waypointArrow = arrow.rectTransform;
        waypointDistance = Label(waypoint, "", 17, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -36f), new Vector2(160f, 24f));
    }

    private void BuildInstruments(Transform root)
    {
        // Compact flight instruments bottom-right, above the timer.
        RectTransform box = Panel(root, "Instruments", new Vector2(1f, 0f), new Vector2(-150f, 110f), new Vector2(260f, 150f), PanelColor);
        speedText = Label(box, "0", 30, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(170f, -35f), new Vector2(200f, 40f));
        altitudeText = Label(box, "0", 30, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(170f, -80f), new Vector2(200f, 40f));
        Label(box, "SPD", 13, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(30f, -35f), new Vector2(40f, 20f)).color = Accent;
        Label(box, "ALT", 13, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(30f, -80f), new Vector2(40f, 20f)).color = Accent;

        RectTransform frame = Panel(box, "Attitude", new Vector2(1f, 0f), new Vector2(-45f, 40f), new Vector2(64f, 64f), Color.white);
        frame.GetComponent<Image>().sprite = Circle();
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        horizonRoll = new GameObject("Roll", typeof(RectTransform)).GetComponent<RectTransform>();
        horizonRoll.SetParent(frame, false);
        horizonPitch = new GameObject("Pitch", typeof(RectTransform)).GetComponent<RectTransform>();
        horizonPitch.SetParent(horizonRoll, false);
        Panel(horizonPitch, "Sky", new Vector2(0.5f, 0.5f), new Vector2(0f, 100f), new Vector2(300f, 200f), new Color(0.2f, 0.5f, 0.85f));
        Panel(horizonPitch, "Ground", new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(300f, 200f), new Color(0.45f, 0.3f, 0.15f));
        Panel(frame, "Wings", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 3f), Gold);
        Label(box, "W fly · S brake · Q/E turn · ↑↓ height", 11, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(115f, 16f), new Vector2(210f, 16f)).color = new Color(1f, 1f, 1f, 0.55f);
    }

    private void BuildStatus(Transform root)
    {
        statusText = Label(root, "", 16, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(200f, -30f), new Vector2(360f, 24f));
        statusText.fontStyle = FontStyle.Bold;
        Label(root, "© OpenStreetMap contributors", 11, TextAnchor.MiddleRight, new Vector2(1f, 0f),
              new Vector2(-120f, 10f), new Vector2(220f, 16f)).color = new Color(1f, 1f, 1f, 0.45f);
    }

    private void BuildMissionHud(Transform root)
    {
        cashText = Label(root, "$0", 44, TextAnchor.MiddleRight, new Vector2(1f, 1f), new Vector2(-190f, -45f), new Vector2(300f, 56f));
        cashText.color = Money;
        cashText.fontStyle = FontStyle.Bold;
        cashText.gameObject.AddComponent<UnityEngine.UI.Outline>().effectColor = Color.black;

        missionTitle = Label(root, "", 18, TextAnchor.MiddleRight, new Vector2(1f, 1f), new Vector2(-230f, -90f), new Vector2(420f, 26f));
        missionTitle.color = Gold;
        missionTitle.fontStyle = FontStyle.Bold;

        timerBox = Panel(root, "Timer", new Vector2(1f, 0f), new Vector2(-150f, 215f), new Vector2(260f, 44f), new Color(0f, 0f, 0f, 0.6f));
        timerText = Label(timerBox, "", 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260f, 44f));
        timerText.fontStyle = FontStyle.Bold;

        // GTA-style subtitle line for the current objective.
        subtitle = Label(root, "", 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(1100f, 40f));
        subtitle.gameObject.AddComponent<UnityEngine.UI.Outline>().effectColor = Color.black;
    }

    private void BuildMinimap(Transform root)
    {
        var texture = new RenderTexture(768, 768, 16) { name = "MinimapTexture" };
        var camObject = new GameObject("MinimapCamera");
        minimapCamera = camObject.AddComponent<Camera>();
        minimapCamera.orthographic = true;
        minimapCamera.orthographicSize = MiniRange;
        minimapCamera.targetTexture = texture;
        minimapCamera.clearFlags = CameraClearFlags.SolidColor;
        minimapCamera.backgroundColor = new Color(0.1f, 0.12f, 0.1f);
        minimapCamera.farClipPlane = 1000f;
        camObject.AddComponent<MinimapFollow>().target = drone != null ? drone.transform : null;
        if (Camera.main != null) Camera.main.cullingMask &= ~(1 << RouteLayer);

        var routeObject = new GameObject("GpsRoute") { layer = RouteLayer };
        route = routeObject.AddComponent<LineRenderer>();
        route.material = new Material(Shader.Find("Sprites/Default"));
        route.startColor = route.endColor = new Color(0.75f, 0.35f, 1f); // GTA purple route
        route.alignment = LineAlignment.TransformZ;
        routeObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // lie flat, face the map camera
        route.numCornerVertices = 2;
        route.positionCount = 0;

        var courseObject = new GameObject("MissionCourse") { layer = RouteLayer };
        course = courseObject.AddComponent<LineRenderer>();
        course.material = route.material;
        course.startColor = course.endColor = new Color(0.2f, 0.85f, 1f, 0.9f); // blue roadmap between rings
        course.alignment = LineAlignment.TransformZ;
        courseObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        course.positionCount = 0;

        minimapFrame = Panel(root, "Minimap", Vector2.zero, new Vector2(40f + MiniW / 2f, 60f + MiniH / 2f), new Vector2(MiniW, MiniH), new Color(0f, 0f, 0f, 0.8f));
        UiKit.Outline(minimapFrame, new Color(0f, 0f, 0f, 0.9f));
        minimapFrame.gameObject.AddComponent<RectMask2D>();
        var map = new GameObject("Map", typeof(RectTransform), typeof(RawImage));
        map.transform.SetParent(minimapFrame, false);
        var mapRect = map.GetComponent<RectTransform>();
        mapRect.anchorMin = Vector2.zero;
        mapRect.anchorMax = Vector2.one;
        mapRect.sizeDelta = Vector2.zero;
        map.GetComponent<RawImage>().texture = texture;
        minimapCamera.aspect = MiniW / MiniH;

        Text arrow = Label(minimapFrame, "▲", 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
        arrow.color = Color.white;
        playerArrow = arrow.rectTransform;
        Label(minimapFrame, "N", 16, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(30f, 20f)).color = new Color(1f, 0.4f, 0.3f);

        // Battery bar under the radar, like the GTA health bar.
        RectTransform bar = Panel(root, "BatteryBar", Vector2.zero, new Vector2(40f + MiniW / 2f, 45f), new Vector2(MiniW, 10f), new Color(0f, 0f, 0f, 0.7f));
        batteryFill = Panel(bar, "Fill", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MiniW - 4f, 6f), Color.green).GetComponent<Image>();
        batteryFill.sprite = White();
        batteryFill.type = Image.Type.Filled;
        batteryFill.fillMethod = Image.FillMethod.Horizontal;
        batteryText = Label(root, "", 12, TextAnchor.MiddleLeft, Vector2.zero, new Vector2(40f + 90f, 28f), new Vector2(180f, 18f));
    }

    private void BuildBanner(Transform root)
    {
        var go = new GameObject("Banner", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(root, false);
        banner = go.GetComponent<CanvasGroup>();
        banner.alpha = 0f;
        banner.blocksRaycasts = false;
        Panel(go.transform, "Band", new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(4000f, 150f), new Color(0f, 0f, 0f, 0.55f));
        bannerTitle = Label(go.transform, "", 78, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 140f), new Vector2(1600f, 100f));
        bannerTitle.fontStyle = FontStyle.Bold;
        bannerTitle.gameObject.AddComponent<UnityEngine.UI.Outline>().effectColor = Color.black;
        bannerDetail = Label(go.transform, "", 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 75f), new Vector2(1600f, 34f));
    }

    // ---------------------------------------------------------- menu build

    private GameObject MakeScreen(Transform root, string name, float dim)
    {
        RectTransform screen = Fill(root, name, new Color(0f, 0f, 0f, dim));
        return screen.gameObject;
    }

    private void BuildTitle(Transform root)
    {
        titleScreen = MakeScreen(root, "TitleScreen", 0.45f);
        Transform t = titleScreen.transform;
        Text title = Label(t, "SKYBOUND", 120, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 330f), new Vector2(1400f, 140f));
        title.fontStyle = FontStyle.BoldAndItalic;
        title.color = Gold;
        title.gameObject.AddComponent<UnityEngine.UI.Outline>().effectColor = Color.black;
        Label(t, $"DRONE CITY  ·  {cityName.ToUpperInvariant()}", 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(1200f, 40f));
        UiKit.Button(t, "PRESS ENTER  ·  CHOOSE A JOB", new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(460f, 56f), new Color(0.75f, 0.55f, 0.05f), () => ShowScreen(jobScreen));
    }

    private void BuildJobs(Transform root)
    {
        jobScreen = MakeScreen(root, "JobScreen", 0.8f);
        Transform t = jobScreen.transform;
        Text header = Label(t, "JOBS", 64, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(360f, -80f), new Vector2(600f, 80f));
        header.fontStyle = FontStyle.BoldAndItalic;
        header.color = Gold;
        Label(t, "Finish a level to unlock the next one. Finish all levels to unlock the next mission.", 20,
              TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(560f, -135f), new Vector2(1000f, 30f)).color = new Color(1f, 1f, 1f, 0.7f);

        jobList = new GameObject("JobList", typeof(RectTransform)).transform;
        jobList.SetParent(t, false);

        UiKit.Button(t, "FREE ROAM", new Vector2(0.5f, 0f), new Vector2(-340f, 70f), new Vector2(260f, 52f), new Color(0.2f, 0.25f, 0.3f), () =>
        {
            missions?.Abort();
            ShowScreen(null);
        });
        UiKit.Button(t, "RESET PROGRESS", new Vector2(0.5f, 0f), new Vector2(-60f, 70f), new Vector2(260f, 52f), new Color(0.45f, 0.1f, 0.1f), () =>
        {
            missions?.ResetProgress();
            RefreshJobs();
        });
        // City switch
        for (int i = 0; i < citySceneNames.Length; i++)
        {
            string scene = citySceneNames[i];
            UiKit.Button(t, cityLabels[i].ToUpperInvariant(), new Vector2(0.5f, 0f), new Vector2(220f + i * 180f, 70f), new Vector2(170f, 52f),
                         cityLabels[i] == cityName ? new Color(0.1f, 0.45f, 0.3f) : new Color(0.15f, 0.2f, 0.28f), () =>
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(scene);
            });
        }
        cashLabelOnJobs = Label(t, "", 40, TextAnchor.MiddleRight, new Vector2(1f, 1f), new Vector2(-200f, -80f), new Vector2(400f, 60f));
        cashLabelOnJobs.color = Money;
        cashLabelOnJobs.fontStyle = FontStyle.Bold;
    }

    private Text cashLabelOnJobs;

    /// <summary>Rebuilds the mission cards so lock states reflect saved progress.</summary>
    private void RefreshJobs()
    {
        foreach (Transform child in jobList) Destroy(child.gameObject);
        if (missions != null) cashLabelOnJobs.text = $"${missions.Cash:N0}";

        var catalog = MissionSystem.Catalog;
        const float cardW = 400f, cardH = 560f, gap = 30f;
        float startX = -(catalog.Length - 1) * (cardW + gap) / 2f;
        for (int m = 0; m < catalog.Length; m++)
        {
            var info = catalog[m];
            bool unlocked = MissionSystem.IsMissionUnlocked(m);
            int done = MissionSystem.Completed(m);
            RectTransform card = Panel(jobList, "Card", new Vector2(0.5f, 0.5f), new Vector2(startX + m * (cardW + gap), 20f),
                                       new Vector2(cardW, cardH), unlocked ? new Color(0.06f, 0.1f, 0.14f, 0.95f) : new Color(0.08f, 0.08f, 0.08f, 0.9f));
            UiKit.Outline(card, unlocked ? new Color(Gold.r, Gold.g, Gold.b, 0.6f) : new Color(1f, 1f, 1f, 0.1f));

            Panel(card, "Stripe", new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(cardW, 8f), unlocked ? Gold : new Color(0.3f, 0.3f, 0.3f));
            Label(card, $"MISSION {m + 1}", 16, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(cardW / 2f, -35f), new Vector2(cardW - 40f, 24f)).color = new Color(1f, 1f, 1f, 0.6f);
            Text name = Label(card, info.Name, 30, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(cardW / 2f, -72f), new Vector2(cardW - 40f, 40f));
            name.fontStyle = FontStyle.Bold;
            name.color = unlocked ? Color.white : new Color(0.5f, 0.5f, 0.5f);
            Text desc = Label(card, info.Description, 18, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(cardW / 2f, -140f), new Vector2(cardW - 40f, 70f));
            desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            desc.color = new Color(1f, 1f, 1f, unlocked ? 0.8f : 0.35f);
            string type = info.Kind == MissionSystem.Kind.Race ? "CHECKPOINT RACE" : "DELIVERY";
            Label(card, $"{type}   ·   {done}/{info.Levels} COMPLETE", 15, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(cardW / 2f, -200f), new Vector2(cardW - 40f, 22f)).color = Accent;

            if (!unlocked)
            {
                Text lockText = Label(card, $"LOCKED\n<size=18>Complete {catalog[m - 1].Name}</size>", 34, TextAnchor.MiddleCenter,
                                      new Vector2(0.5f, 0.5f), new Vector2(0f, -80f), new Vector2(cardW - 40f, 120f));
                lockText.color = new Color(1f, 1f, 1f, 0.5f);
                continue;
            }

            for (int level = 1; level <= info.Levels; level++)
            {
                bool open = MissionSystem.IsLevelUnlocked(m, level);
                bool complete = done >= level;
                int mission = m, lvl = level;
                string text = complete ? $"LEVEL {level}   ·   DONE  (REPLAY)" : open ? $"LEVEL {level}   ·   PLAY" : $"LEVEL {level}   ·   LOCKED";
                Color color = complete ? new Color(0.12f, 0.4f, 0.2f) : open ? new Color(0.75f, 0.55f, 0.05f) : new Color(0.2f, 0.2f, 0.2f);
                Button button = UiKit.Button(card, text, new Vector2(0.5f, 0f), new Vector2(0f, 60f + (info.Levels - level) * 72f),
                                             new Vector2(cardW - 50f, 58f), color, () =>
                {
                    ShowScreen(null);
                    missions.StartMission(mission, lvl);
                });
                button.interactable = open;
            }
        }
    }

    private void BuildPause(Transform root)
    {
        pauseScreen = MakeScreen(root, "PauseScreen", 0.6f);
        Transform t = pauseScreen.transform;
        Text title = Label(t, "PAUSED", 72, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 220f), new Vector2(800f, 90f));
        title.fontStyle = FontStyle.BoldAndItalic;
        title.color = Gold;
        UiKit.Button(t, "RESUME", new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(380f, 58f), new Color(0.75f, 0.55f, 0.05f), () => ShowScreen(null));
        UiKit.Button(t, "JOBS", new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(380f, 58f), new Color(0.15f, 0.2f, 0.28f), () => ShowScreen(jobScreen));
        UiKit.Button(t, "RESET DRONE", new Vector2(0.5f, 0.5f), new Vector2(0f, -50f), new Vector2(380f, 58f), new Color(0.15f, 0.2f, 0.28f), () =>
        {
            drone.GetComponent<DroneController>().ResetDrone();
            ShowScreen(null);
        });
        UiKit.Button(t, "CONTROLS", new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(380f, 58f), new Color(0.15f, 0.2f, 0.28f), () => helpPanel.SetActive(!helpPanel.activeSelf));
    }

    private void BuildHelp(Transform root)
    {
        RectTransform panel = Panel(root, "HowToPlay", new Vector2(0f, 0.5f), new Vector2(330f, -40f), new Vector2(560f, 440f), new Color(0.03f, 0.07f, 0.1f, 0.94f));
        UiKit.Outline(panel, new Color(Accent.r, Accent.g, Accent.b, 0.5f));
        helpPanel = panel.gameObject;
        Text title = Label(panel, "HOW TO PLAY", 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(520f, 36f));
        title.color = Accent;
        title.fontStyle = FontStyle.Bold;

        string[,] rows =
        {
            { "W", "Fly forward (hold Shift to boost)" },
            { "S", "Slow down / brake" },
            { "Q / E", "Turn left / right" },
            { "A / D", "Roll left / right" },
            { "↑ / ↓", "Climb / descend and land" },
            { "J", "Jobs (mission select)" },
            { "M", "Big map" },
            { "R", "Reset drone" },
            { "H", "Show / hide this guide" },
            { "ESC", "Pause" },
        };
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            float y = -72f - i * 30f;
            Label(panel, rows[i, 0], 19, TextAnchor.MiddleRight, new Vector2(0.5f, 1f), new Vector2(-160f, y), new Vector2(110f, 28f)).color = Gold;
            Label(panel, rows[i, 1], 17, TextAnchor.MiddleLeft, new Vector2(0.5f, 1f), new Vector2(80f, y), new Vector2(330f, 28f));
        }
        Text tip = Label(panel, "Follow the purple GPS route on the radar and the yellow arrow.\nHover inside yellow markers · fly through blue rings.", 14,
                         TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 32f), new Vector2(520f, 40f));
        tip.color = new Color(1f, 1f, 1f, 0.7f);
    }
}

/// <summary>Keeps the top-down minimap camera above the drone, north-up.</summary>
public sealed class MinimapFollow : MonoBehaviour
{
    public Transform target;

    private void LateUpdate()
    {
        if (target == null) return;
        transform.SetPositionAndRotation(target.position + Vector3.up * 450f, Quaternion.Euler(90f, 0f, 0f));
    }
}
