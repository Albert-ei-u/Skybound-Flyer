using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the playable training scene: a real OpenStreetMap city, the carbon
/// drone model, physical sky and lighting, a chase camera and the flight HUD.
/// </summary>
public static class SkyboundSceneBuilder
{
    private const string DroneModelPath = "Assets/models/carbon_drone/drone/source/fly.glb";
    private const string MusicPath = "Assets/audio/DST-TowerDefenseTheme_1.mp3.ogg";
    private const string GeneratedFolder = "Assets/Generated";
    private const float DroneSize = 1.2f;

    /// <summary>Playable cities: OSM export, scene name, menu label, left-hand traffic.</summary>
    private static readonly (string json, string scene, string label, bool driveOnLeft)[] Cities =
    {
        ("Assets/cities/midtown_manhattan.json", "NewYork", "New York", false),
        ("Assets/cities/london_westminster.json", "London", "London", true),
        ("Assets/cities/kigali.json", "Kigali", "Kigali", false),
    };

    [MenuItem("Skybound/Create Drone Training Scene")]
    public static void CreateDroneTrainingScene()
    {
        var available = new System.Collections.Generic.List<(string json, string scene, string label, bool driveOnLeft)>();
        foreach (var city in Cities) if (File.Exists(city.json)) available.Add(city);
        if (available.Count == 0)
        {
            EditorUtility.DisplayDialog("Skybound", "No city data found in Assets/cities.\n\n" +
                "Run: python tools/export_city.py \"Times Square, New York, USA\" " +
                "--output assets/cities/midtown_manhattan.json --distance 900", "OK");
            return;
        }

        EnsureFolder("Assets/Scenes");
        ResetFolder(GeneratedFolder);
        EnsureFolder($"{GeneratedFolder}/Textures");

        try
        {
            EditorUtility.DisplayProgressBar("Skybound", "Creating materials…", 0.05f);
            Material[] facades =
            {
                FacadeMaterial("GlassTower", new Color(0.35f, 0.45f, 0.55f), new Color(0.12f, 0.18f, 0.25f), 0.9f, 0.85f, 0.6f),
                FacadeMaterial("Brick", new Color(0.55f, 0.3f, 0.22f), new Color(0.12f, 0.14f, 0.17f), 0.35f, 0.1f, 0.2f),
                FacadeMaterial("Limestone", new Color(0.78f, 0.74f, 0.66f), new Color(0.15f, 0.17f, 0.2f), 0.4f, 0.1f, 0.3f),
                FacadeMaterial("Concrete", new Color(0.6f, 0.6f, 0.6f), new Color(0.1f, 0.13f, 0.16f), 0.5f, 0.15f, 0.25f),
            };
            Material roof = SolidMaterial("Roof", new Color(0.32f, 0.32f, 0.33f), 0.1f);
            Material road = RoadMaterial();
            Material pavement = PavementMaterial();
            Material sky = SkyMaterial();

            string[] sceneNames = new string[available.Count];
            string[] labels = new string[available.Count];
            var buildScenes = new EditorBuildSettingsScene[available.Count];
            for (int i = 0; i < available.Count; i++)
            {
                sceneNames[i] = available[i].scene;
                labels[i] = available[i].label;
                buildScenes[i] = new EditorBuildSettingsScene($"Assets/Scenes/{available[i].scene}.unity", true);
            }

            for (int i = 0; i < available.Count; i++)
            {
                var city = available[i];
                EditorUtility.DisplayProgressBar("Skybound", $"Building {city.label} from OpenStreetMap…", (i + 0.5f) / available.Count);
                string meshFolder = $"{GeneratedFolder}/{city.scene}";
                EnsureFolder(meshFolder);

                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                CreateLighting(sky);
                CreateGround(pavement);
                if (city.scene == "Kigali") TintGround(new Color(0.55f, 0.75f, 0.45f)); // green hills
                GameObject cityRoot = CityImporter.Build(city.json, facades, roof, road, meshFolder, out Vector3 spawn);
                RoadNetwork network = cityRoot.GetComponent<RoadNetwork>();
                network.driveOnLeft = city.driveOnLeft;

                GameObject drone = CreateDrone(spawn + Vector3.up * 1f);
                CreateCamera(drone.transform);

                var traffic = new GameObject("CityLife").AddComponent<TrafficSystem>();
                traffic.network = network;
                var missions = new GameObject("Missions").AddComponent<MissionSystem>();
                missions.network = network;
                missions.drone = drone.GetComponent<DroneController>();

                FlightHud hud = CreateHud(drone);
                hud.cityName = city.label;
                hud.missions = missions;
                hud.citySceneNames = sceneNames;
                hud.cityLabels = labels;
                CreateMusic();

                EditorSceneManager.SaveScene(scene, $"Assets/Scenes/{city.scene}.unity");
            }

            EditorBuildSettings.scenes = buildScenes;
            EditorSceneManager.OpenScene($"Assets/Scenes/{available[0].scene}.unity");
            Debug.Log($"Skybound built {available.Count} city scenes: {string.Join(", ", labels)}.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ------------------------------------------------------------------- drone

    private static GameObject CreateDrone(Vector3 position)
    {
        GameObject drone = new GameObject("Drone");
        drone.transform.position = position;

        Rigidbody body = drone.AddComponent<Rigidbody>();
        body.mass = 1.5f;
        body.linearDamping = 0.8f;
        body.angularDamping = 2f;
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        drone.AddComponent<DroneController>();

        GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DroneModelPath);
        if (modelPrefab != null)
        {
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            model.name = "CarbonDroneModel";
            model.transform.SetParent(drone.transform, false);
            FitModel(model.transform);
        }
        else
        {
            Debug.LogWarning($"Drone model not found at {DroneModelPath}. Make sure the glTFast " +
                             "package finished importing, then rebuild the scene.");
            GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placeholder.transform.SetParent(drone.transform, false);
            placeholder.transform.localScale = new Vector3(DroneSize, 0.2f, DroneSize);
            Object.DestroyImmediate(placeholder.GetComponent<Collider>());
        }

        BoxCollider collider = drone.AddComponent<BoxCollider>();
        collider.size = new Vector3(DroneSize, 0.3f, DroneSize);
        return drone;
    }

    /// <summary>Scales and centres an imported model so it is about DroneSize wide.</summary>
    private static void FitModel(Transform model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        float largest = Mathf.Max(bounds.size.x, bounds.size.z);
        if (largest <= 0f) return;

        float scale = DroneSize / largest;
        model.localScale = Vector3.one * scale;
        model.localPosition = -(bounds.center - model.parent.position) * scale;
    }

    private static void CreateCamera(Transform target)
    {
        GameObject cameraObject = new GameObject("DroneCamera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 70f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 3000f;
        camera.allowHDR = true;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.transform.position = target.position + new Vector3(0f, 2.5f, -7f);
        cameraObject.transform.LookAt(target);

        DroneCamera follow = cameraObject.AddComponent<DroneCamera>();
        follow.target = target;
        cameraObject.tag = "MainCamera";
    }

    private static FlightHud CreateHud(GameObject drone)
    {
        var hud = new GameObject("FlightHud").AddComponent<FlightHud>();
        hud.drone = drone.GetComponent<Rigidbody>();
        return hud;
    }

    private static void CreateMusic()
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
        if (clip == null) return;
        AudioSource source = new GameObject("Music").AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.volume = 0.25f;
        source.playOnAwake = true;
    }

    // --------------------------------------------------------------- world look

    private static Material SkyMaterial()
    {
        var sky = new Material(Shader.Find("Skybox/Procedural")) { name = "Sky" };
        sky.SetFloat("_SunSize", 0.035f);
        sky.SetFloat("_AtmosphereThickness", 1.05f);
        sky.SetColor("_SkyTint", new Color(0.5f, 0.6f, 0.75f));
        sky.SetColor("_GroundColor", new Color(0.35f, 0.35f, 0.38f));
        sky.SetFloat("_Exposure", 1.2f);
        AssetDatabase.CreateAsset(sky, $"{GeneratedFolder}/Sky.mat");
        return sky;
    }

    private static void CreateLighting(Material sky)
    {
        GameObject sunObject = new GameObject("Sun");
        Light sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.95f, 0.86f);
        sun.intensity = 1.3f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sunObject.transform.rotation = Quaternion.Euler(38f, -35f, 0f);

        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.72f, 0.78f, 0.85f);
        RenderSettings.fogStartDistance = 300f;
        RenderSettings.fogEndDistance = 2200f;

        QualitySettings.shadowDistance = 450f;
        QualitySettings.shadowCascades = 4;
        QualitySettings.antiAliasing = 4;
    }

    private static void CreateGround(Material pavement)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(400f, 1f, 400f);
        ground.GetComponent<Renderer>().sharedMaterial = pavement;
        GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.BatchingStatic);
    }

    private static void TintGround(Color tint)
    {
        var ground = GameObject.Find("Ground").GetComponent<Renderer>();
        var material = new Material(ground.sharedMaterial) { color = tint };
        AssetDatabase.CreateAsset(material, $"{GeneratedFolder}/Ground_{tint.GetHashCode()}.mat");
        ground.sharedMaterial = material;
    }

    private static Material PavementMaterial()
    {
        Texture2D tex = SaveTexture("Pavement", PavementTexture());
        Material material = SolidMaterial("Pavement", Color.white, 0.15f);
        material.mainTexture = tex;
        material.mainTextureScale = new Vector2(1000f, 1000f); // 4 m slabs across 4 km
        return material;
    }

    // ---------------------------------------------------------------- materials

    private static Material FacadeMaterial(string name, Color wall, Color glass, float windowRatio,
                                           float smoothness, float litChance)
    {
        Texture2D albedo = SaveTexture($"Facade_{name}", FacadeTexture(wall, glass, windowRatio, litChance, false));
        Texture2D emission = SaveTexture($"Facade_{name}_Emission", FacadeTexture(wall, glass, windowRatio, litChance, true));
        Material material = SolidMaterial($"Facade_{name}", Color.white, smoothness);
        material.mainTexture = albedo;
        material.SetFloat("_Metallic", name == "GlassTower" ? 0.6f : 0f);
        material.EnableKeyword("_EMISSION");
        material.SetTexture("_EmissionMap", emission);
        material.SetColor("_EmissionColor", new Color(0.25f, 0.22f, 0.15f));
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material RoadMaterial()
    {
        Material material = SolidMaterial("Asphalt", Color.white, 0.25f);
        material.mainTexture = SaveTexture("Asphalt", AsphaltTexture());
        return material;
    }

    private static Material SolidMaterial(string name, Color color, float smoothness)
    {
        var material = new Material(Shader.Find("Standard")) { name = name, color = color };
        material.SetFloat("_Glossiness", smoothness);
        material.enableInstancing = true;
        AssetDatabase.CreateAsset(material, $"{GeneratedFolder}/{name}.mat");
        return material;
    }

    // ----------------------------------------------------- procedural textures

    /// <summary>One tile = one window bay (3 m) by one floor (3.5 m).</summary>
    private static Texture2D FacadeTexture(Color wall, Color glass, float windowRatio, float litChance, bool emission)
    {
        const int w = 64, h = 64, tilesX = 8, tilesY = 8; // 8x8 bays so lit windows vary
        var tex = new Texture2D(w * tilesX, h * tilesY, TextureFormat.RGBA32, true);
        var random = new System.Random(wall.GetHashCode());
        int marginX = Mathf.RoundToInt(w * (1f - windowRatio) * 0.5f);
        int marginTop = Mathf.RoundToInt(h * 0.18f), marginBottom = Mathf.RoundToInt(h * 0.22f);

        for (int ty = 0; ty < tilesY; ty++)
        for (int tx = 0; tx < tilesX; tx++)
        {
            bool lit = random.NextDouble() < litChance;
            float shade = 0.85f + (float)random.NextDouble() * 0.3f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool isWindow = x >= marginX && x < w - marginX && y >= marginBottom && y < h - marginTop;
                float noise = 0.93f + (float)random.NextDouble() * 0.07f;
                Color c;
                if (emission)
                {
                    c = isWindow && lit ? new Color(1f, 0.85f, 0.55f) : Color.black;
                }
                else if (isWindow)
                {
                    float gradient = Mathf.Lerp(0.8f, 1.25f, (float)y / h); // sky reflection
                    c = glass * gradient * shade;
                }
                else
                {
                    c = wall * noise;
                    if (y < marginBottom && y > marginBottom - 3) c *= 0.7f; // sill shadow
                }
                c.a = 1f;
                tex.SetPixel(tx * w + x, ty * h + y, c);
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>Road tile: across U (edge to edge), along V (one road-width).</summary>
    private static Texture2D AsphaltTexture()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var random = new System.Random(7);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float grain = 0.16f + (float)random.NextDouble() * 0.05f;
            Color c = new Color(grain, grain, grain * 1.05f);
            float u = x / (float)size;
            if (u < 0.03f || u > 0.97f) c = new Color(0.55f, 0.55f, 0.52f);           // curb
            else if (u > 0.06f && u < 0.075f || u > 0.925f && u < 0.94f) c = new Color(0.85f, 0.85f, 0.8f); // edge line
            else if (u > 0.49f && u < 0.51f && y < size / 2) c = new Color(0.95f, 0.78f, 0.15f); // dashed centre
            c.a = 1f;
            tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }

    private static Texture2D PavementTexture()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var random = new System.Random(3);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float v = 0.5f + (float)random.NextDouble() * 0.06f;
            if (x < 2 || y < 2) v *= 0.75f; // slab joints
            tex.SetPixel(x, y, new Color(v, v * 0.98f, v * 0.95f, 1f));
        }
        tex.Apply();
        return tex;
    }

    private static Texture2D SaveTexture(string name, Texture2D texture)
    {
        string path = $"{GeneratedFolder}/Textures/{name}.png";
        File.WriteAllBytes(path, texture.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 8;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ------------------------------------------------------------------ folders

    private static void ResetFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) AssetDatabase.DeleteAsset(path);
        EnsureFolder(path);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
