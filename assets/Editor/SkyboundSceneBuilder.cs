using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the playable training scenes: one per real OpenStreetMap city in
/// Assets/cities, each with the drone hangar models, an endless world of
/// countryside and ocean around the city, level music, physical sky and
/// lighting, a chase camera and the flight HUD.
/// </summary>
public static class SkyboundSceneBuilder
{
    private const string DroneModelPath = "Assets/models/carbon_drone/drone/source/fly.glb";
    private const string SecondDroneModelPath = "Assets/models/skybound_drone.glb";
    private const string MusicPath = "Assets/audio/DST-TowerDefenseTheme_1.mp3.ogg";
    private const string LevelMusicFolder = "Assets/audio/levels";
    private const string PeopleFolder = "Assets/models/people";
    private const float CityHalfSize = 1600f; // city ground plane: 3.2 km square
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
        // Any other city file (e.g. from Skybound > Download Real City…) gets its own scene too.
        if (Directory.Exists("Assets/cities"))
        {
            foreach (string file in Directory.GetFiles("Assets/cities", "*.json"))
            {
                string json = file.Replace('\\', '/');
                if (available.Exists(c => c.json == json)) continue;
                available.Add(ReadCityHeader(json));
            }
        }
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
            Material terrain = TerrainMaterial();
            Material water = WaterMaterial();
            AudioClip[] levelTracks = LoadLevelTracks();
            (GameObject[] people, RuntimeAnimatorController[] walks) = LoadPeople();

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

                Transform droneBase = CreateDroneBase(spawn);
                GameObject drone = CreateDrone(spawn + Vector3.up * (PadHeight + 0.6f));
                CreateCamera(drone.transform);

                var traffic = new GameObject("CityLife").AddComponent<TrafficSystem>();
                traffic.network = network;
                traffic.personModels = people;
                traffic.personAnimations = walks;
                var missions = new GameObject("Missions").AddComponent<MissionSystem>();
                missions.network = network;
                missions.drone = drone.GetComponent<DroneController>();
                missions.droneBase = droneBase;

                var garage = new GameObject("Hangar").AddComponent<DroneGarage>();
                garage.drone = drone.GetComponent<DroneController>();

                var world = new GameObject("EndlessWorld").AddComponent<InfiniteWorld>();
                world.drone = drone.transform;
                world.terrainMaterial = terrain;
                world.waterMaterial = water;
                world.cityHalfSize = CityHalfSize;
                world.seed = city.scene.GetHashCode();
                world.coastDirection = Mathf.Abs(city.scene.GetHashCode() % 360); // each city has its own coastline

                FlightHud hud = CreateHud(drone);
                hud.cityName = city.label;
                hud.missions = missions;
                hud.citySceneNames = sceneNames;
                hud.cityLabels = labels;
                hud.garage = garage;
                hud.world = world;
                CreateMusic();

                var audio = new GameObject("GameAudio").AddComponent<GameAudio>();
                audio.missions = missions;
                audio.drone = drone.GetComponent<DroneController>();
                audio.freeRoamTheme = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
                audio.levelTracks = levelTracks;

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

    private const float PadRadius = 8f;
    private const float PadHeight = 0.3f;

    /// <summary>
    /// Helipad where the drone starts and must land to finish a job. The spot
    /// comes from CityImporter.FindOpenGround, clear of roads and buildings.
    /// </summary>
    private static Transform CreateDroneBase(Vector3 position)
    {
        Material concrete = PadMaterial("PadConcrete", new Color(0.32f, 0.33f, 0.35f), Color.black);
        Material marking = PadMaterial("PadMarking", Color.white, new Color(0.9f, 0.9f, 0.9f) * 0.6f);
        Material lights = PadMaterial("PadLights", new Color(0.2f, 1f, 0.4f), new Color(0.2f, 1f, 0.4f) * 2.5f);

        var root = new GameObject("DroneBase").transform;
        root.position = position;

        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "Helipad";
        pad.transform.SetParent(root, false);
        pad.transform.localPosition = new Vector3(0f, PadHeight * 0.5f, 0f);
        pad.transform.localScale = new Vector3(PadRadius * 2f, PadHeight * 0.5f, PadRadius * 2f);
        pad.GetComponent<Renderer>().sharedMaterial = concrete;

        // White "H": two uprights and a crossbar, just above the pad surface.
        void Stripe(Vector3 offset, Vector3 scale)
        {
            GameObject s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(s.GetComponent<Collider>());
            s.transform.SetParent(root, false);
            s.transform.localPosition = offset + Vector3.up * (PadHeight + 0.01f);
            s.transform.localScale = scale;
            s.GetComponent<Renderer>().sharedMaterial = marking;
        }
        Stripe(new Vector3(-2.2f, 0f, 0f), new Vector3(1f, 0.02f, 6f));
        Stripe(new Vector3(2.2f, 0f, 0f), new Vector3(1f, 0.02f, 6f));
        Stripe(Vector3.zero, new Vector3(3.4f, 0.02f, 1f));

        // Green edge lights so the pad is easy to find from the air.
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI * 2f / 12f;
            GameObject light = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(light.GetComponent<Collider>());
            light.transform.SetParent(root, false);
            light.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (PadRadius - 0.5f) + Vector3.up * PadHeight;
            light.transform.localScale = Vector3.one * 0.4f;
            light.GetComponent<Renderer>().sharedMaterial = lights;
        }
        return root;
    }

    private static Material PadMaterial(string name, Color color, Color emission)
    {
        string path = $"{GeneratedFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Material material = SolidMaterial(name, color, 0.2f);
        if (emission != Color.black)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
        }
        return material;
    }

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

        // Imported hangar models; DroneGarage builds the other drones from parts at runtime.
        if (!AddDroneModel(drone.transform, DroneModelPath, "Model_Carbon", DroneSize, true))
        {
            Debug.LogWarning($"Drone model not found at {DroneModelPath}. Make sure the glTFast " +
                             "package finished importing, then rebuild the scene.");
        }
        AddDroneModel(drone.transform, SecondDroneModelPath, "Model_Skybound", 1.1f, false);

        BoxCollider collider = drone.AddComponent<BoxCollider>();
        collider.size = new Vector3(DroneSize, 0.3f, DroneSize);
        return drone;
    }

    private static bool AddDroneModel(Transform drone, string path, string name, float size, bool active)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return false;
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        model.name = name;
        model.transform.SetParent(drone, false);
        FitModel(model.transform, size);
        model.SetActive(active);
        return true;
    }

    /// <summary>Scales and centres an imported model so it is about `size` wide.</summary>
    private static void FitModel(Transform model, float size)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        float largest = Mathf.Max(bounds.size.x, bounds.size.z);
        if (largest <= 0f) return;

        float scale = size / largest;
        model.localScale = Vector3.one * scale;
        model.localPosition = -(bounds.center - model.parent.position) * scale;
    }

    private static void CreateCamera(Transform target)
    {
        GameObject cameraObject = new GameObject("DroneCamera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 70f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 3600f; // see mountains and ocean in the endless world
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

    // ------------------------------------------------------------- city list

    /// <summary>Scene name, menu label and traffic side for a downloaded city file.</summary>
    private static (string json, string scene, string label, bool driveOnLeft) ReadCityHeader(string json)
    {
        string label = Path.GetFileNameWithoutExtension(json);
        bool left = false;
        try
        {
            var root = (System.Collections.Generic.Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(json));
            if (root.TryGetValue("label", out object l) && l is string ls && ls != "") label = ls;
            else if (root.TryGetValue("place", out object p) && p is string ps && ps != "") label = ps.Split(',')[0];
            if (root.TryGetValue("drive_on_left", out object d) && d is bool b) left = b;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Could not read {json}: {e.Message}");
        }
        var scene = new System.Text.StringBuilder();
        foreach (char c in label) if (char.IsLetterOrDigit(c)) scene.Append(c);
        return (json, scene.Length > 0 ? scene.ToString() : "City", label, left);
    }

    // ------------------------------------------------------------ audio, people

    /// <summary>Optional level music: Assets/audio/levels/m1_l1.ogg … m4_l3.ogg (any audio format).</summary>
    private static AudioClip[] LoadLevelTracks()
    {
        var tracks = new AudioClip[MissionSystem.Catalog.Length * 3];
        if (!AssetDatabase.IsValidFolder(LevelMusicFolder)) return tracks;
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { LevelMusicFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant(); // e.g. "m2_l3"
            if (name.Length >= 5 && name[0] == 'm' && name.Contains("_l") &&
                int.TryParse(name.Substring(1, name.IndexOf('_') - 1), out int m) &&
                int.TryParse(name.Substring(name.IndexOf("_l") + 2), out int l) &&
                m >= 1 && l >= 1 && l <= 3 && (m - 1) * 3 + l - 1 < tracks.Length)
            {
                tracks[(m - 1) * 3 + l - 1] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
        }
        return tracks;
    }

    /// <summary>
    /// Optional real people: every model in Assets/models/people (FBX from Mixamo
    /// etc.). If the file contains a walk animation it is set to loop and gets
    /// its own animator controller.
    /// </summary>
    private static (GameObject[], RuntimeAnimatorController[]) LoadPeople()
    {
        var models = new System.Collections.Generic.List<GameObject>();
        var walks = new System.Collections.Generic.List<RuntimeAnimatorController>();
        if (!AssetDatabase.IsValidFolder(PeopleFolder)) return (models.ToArray(), walks.ToArray());

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { PeopleFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is ModelImporter importer && importer.defaultClipAnimations.Length > 0)
            {
                ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
                bool changed = false;
                foreach (var clip in clips) if (!clip.loopTime) { clip.loopTime = true; changed = true; }
                if (changed)
                {
                    importer.clipAnimations = clips;
                    importer.SaveAndReimport();
                }
            }

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) continue;
            AnimationClip walk = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip c && !c.name.StartsWith("__preview__")) { walk = c; break; }

            RuntimeAnimatorController controller = null;
            if (walk != null)
            {
                string controllerPath = $"{GeneratedFolder}/Walk_{Path.GetFileNameWithoutExtension(path)}.controller";
                controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPathWithClip(controllerPath, walk);
            }
            models.Add(model);
            walks.Add(controller);
        }
        if (models.Count > 0) Debug.Log($"Skybound: {models.Count} people models found in {PeopleFolder}.");
        return (models.ToArray(), walks.ToArray());
    }

    // --------------------------------------------------------------- world look

    private static Material TerrainMaterial()
    {
        Shader shader = Shader.Find("Skybound/VertexColorTerrain");
        if (shader == null) shader = Shader.Find("Standard");
        var material = new Material(shader) { name = "Terrain" };
        material.SetFloat("_Glossiness", 0.05f);
        AssetDatabase.CreateAsset(material, $"{GeneratedFolder}/Terrain.mat");
        return material;
    }

    private static Material WaterMaterial()
    {
        Material material = SolidMaterial("Ocean", Color.white, 0.93f);
        material.mainTexture = SaveTexture("Ocean", WaterTexture());
        material.SetFloat("_Metallic", 0.1f);
        return material;
    }

    /// <summary>One 50 m tile of soft wave pattern in deep sea blues.</summary>
    private static Texture2D WaterTexture()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        Color deep = new Color(0.04f, 0.19f, 0.3f), light = new Color(0.1f, 0.33f, 0.45f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x / (float)size * Mathf.PI * 2f, v = y / (float)size * Mathf.PI * 2f;
            // Sums of whole-period waves so the tile repeats seamlessly.
            float w = Mathf.Sin(u * 3f + Mathf.Sin(v * 2f)) * 0.5f + Mathf.Sin(v * 5f + u) * 0.3f + Mathf.Sin((u + v) * 7f) * 0.2f;
            tex.SetPixel(x, y, Color.Lerp(deep, light, w * 0.5f + 0.5f));
        }
        tex.Apply();
        return tex;
    }

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
        RenderSettings.fogStartDistance = 400f;
        RenderSettings.fogEndDistance = 3300f;

        QualitySettings.shadowDistance = 450f;
        QualitySettings.shadowCascades = 4;
        QualitySettings.antiAliasing = 4;
    }

    private static void CreateGround(Material pavement)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(CityHalfSize / 5f, 1f, CityHalfSize / 5f); // plane is 10 m per unit
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
        material.mainTextureScale = new Vector2(CityHalfSize / 2f, CityHalfSize / 2f); // 4 m slabs
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
