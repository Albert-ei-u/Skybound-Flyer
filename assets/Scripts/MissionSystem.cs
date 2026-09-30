using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GTA-style jobs. The catalog has several missions, each with levels.
/// Mission 1 level 1 starts unlocked; finishing a level unlocks the next
/// level, and finishing a mission's last level unlocks the next mission.
/// Jobs are placed on the real streets and progress is saved.
/// </summary>
public sealed class MissionSystem : MonoBehaviour
{
    [SerializeField] public RoadNetwork network;
    [SerializeField] public DroneController drone;

    public enum Kind { Race, Delivery }

    public sealed class MissionInfo
    {
        public string Name, Description;
        public Kind Kind;
        public int Levels;
    }

    public static readonly MissionInfo[] Catalog =
    {
        new MissionInfo { Name = "FIRST FLIGHT", Kind = Kind.Race, Levels = 3,
            Description = "Learn to fly. Pass through the rings above the streets." },
        new MissionInfo { Name = "EXPRESS DELIVERY", Kind = Kind.Delivery, Levels = 3,
            Description = "Pick up parcels and deliver them across the city." },
        new MissionInfo { Name = "SKYLINE RACE", Kind = Kind.Race, Levels = 3,
            Description = "High, fast rings between the skyscrapers. Beat the clock." },
        new MissionInfo { Name = "MEDICAL EMERGENCY", Kind = Kind.Delivery, Levels = 3,
            Description = "Rush medicine to several hospitals. Every second counts." },
    };

    public int Cash { get; private set; }
    public int CurrentMission { get; private set; } = -1;
    public int CurrentLevel { get; private set; }
    public string Title { get; private set; } = "";
    public string Objective { get; private set; } = "";
    public float TimeLeft { get; private set; }
    public bool Active { get; private set; }
    public Vector3? Target => Active && targets.Count > 0 ? targets[0].position : (Vector3?)null;

    /// <summary>Every remaining objective, in order (for map blips).</summary>
    public IEnumerable<Vector3> AllTargets
    {
        get { if (Active) foreach (Transform t in targets) yield return t.position; }
    }

    /// <summary>Raised with (headline, detail, success) for the big banner.</summary>
    public event System.Action<string, string, bool> Banner;

    /// <summary>Raised after a job is passed, so the HUD can reopen the job list.</summary>
    public event System.Action Finished;

    private static readonly string[] Cargo =
    {
        "a phone repair part", "fresh coffee", "legal documents", "a birthday cake",
        "a spare house key", "concert tickets", "a lost passport", "a laptop",
    };

    private readonly List<Transform> targets = new List<Transform>();
    private Kind kind;
    private float holdTimer;
    private int reward, stopsLeft;
    private Material beaconMaterial, ringMaterial, beamMaterial;

    private void Awake()
    {
        Cash = PlayerPrefs.GetInt("skybound_cash", 0);
        beaconMaterial = Glow(new Color(1f, 0.85f, 0.1f, 0.35f));
        ringMaterial = Glow(new Color(0.2f, 0.9f, 1f, 0.95f));
        ringMaterial.SetColor("_EmissionColor", new Color(0.2f, 0.9f, 1f) * 3f); // bright even in shadow
        beamMaterial = Glow(new Color(0.2f, 0.9f, 1f, 0.18f));
        Objective = "Free roam. Press J to choose a job.";
    }

    private void Start()
    {
        if (drone != null) drone.Crashed += () => Fail("WASTED", "The drone crashed.");
    }

    // ------------------------------------------------------------ progress

    /// <summary>How many levels of a mission are complete (0..Levels).</summary>
    public static int Completed(int mission) => PlayerPrefs.GetInt($"skybound_m{mission}", 0);

    public static bool IsMissionUnlocked(int mission) =>
        mission == 0 || Completed(mission - 1) >= Catalog[mission - 1].Levels;

    public static bool IsLevelUnlocked(int mission, int level) =>
        IsMissionUnlocked(mission) && Completed(mission) >= level - 1;

    public void ResetProgress()
    {
        for (int m = 0; m < Catalog.Length; m++) PlayerPrefs.DeleteKey($"skybound_m{m}");
        PlayerPrefs.DeleteKey("skybound_cash");
        PlayerPrefs.Save();
        Cash = 0;
        Abort();
    }

    // ---------------------------------------------------------------- flow

    /// <summary>Start a job (level is 1-based). Ignored if it is still locked.</summary>
    public void StartMission(int mission, int level)
    {
        if (!IsLevelUnlocked(mission, level)) return;
        CancelInvoke();
        ClearTargets();
        drone.ResetDrone();
        CurrentMission = mission;
        CurrentLevel = level;
        MissionInfo info = Catalog[mission];
        kind = info.Kind;

        // Difficulty 1..12 across the whole career.
        int difficulty = mission * 3 + level;
        float t = difficulty / 12f;
        Vector3 origin = drone.transform.position;
        reward = 200 + difficulty * 150;
        Title = $"{info.Name} · LEVEL {level}";

        if (kind == Kind.Delivery)
        {
            stopsLeft = mission == 3 ? level + 1 : 1; // medical: several drop-offs
            Vector3 pickup = PointNear(origin, 120f + difficulty * 25f);
            targets.Add(Beacon(pickup));
            float distance = Vector3.Distance(origin, pickup);
            Vector3 last = pickup;
            for (int i = 0; i < stopsLeft; i++)
            {
                Vector3 drop = PointNear(last, 250f + difficulty * 40f);
                targets.Add(Beacon(drop));
                distance += Vector3.Distance(last, drop);
                last = drop;
            }
            string cargo = mission == 3 ? "medicine" : Cargo[Random.Range(0, Cargo.Length)];
            Objective = $"Go to the <color=#ffd133>yellow marker</color> and pick up {cargo}.";
            TimeLeft = distance / Mathf.Lerp(9f, 18f, t) + 30f;
        }
        else
        {
            int count = 3 + level * 2;
            float minHeight = mission == 2 ? 40f : 12f;
            Vector3 last = origin;
            float distance = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = PointNear(last, 110f + difficulty * 10f);
                p.y = Random.Range(minHeight, minHeight + 15f + difficulty * 4f);
                targets.Add(Ring(p, last));
                distance += Vector3.Distance(last, p);
                last = p;
            }
            Objective = $"Fly through the <color=#33d9ff>blue rings</color>. {count} to go.";
            TimeLeft = distance / Mathf.Lerp(8f, 18f, t) + 20f;
        }

        HighlightNext();
        Active = true;
        Banner?.Invoke(info.Name, $"LEVEL {level}", true);
    }

    /// <summary>Leave the current job without passing or failing (free roam).</summary>
    public void Abort()
    {
        CancelInvoke();
        Active = false;
        ClearTargets();
        CurrentMission = -1;
        Title = "";
        Objective = "Free roam. Press J to choose a job.";
    }

    private void Update()
    {
        if (!Active || drone == null) return;

        TimeLeft -= Time.deltaTime;
        if (TimeLeft <= 0f)
        {
            Fail("MISSION FAILED", "You ran out of time.");
            return;
        }

        Vector3 dronePos = drone.transform.position;
        Transform target = targets[0];
        if (kind == Kind.Race)
        {
            if (Vector3.Distance(dronePos, target.position) < 10f) ReachTarget();
        }
        else
        {
            // Descend into the marker and hold for a moment to pick up / drop off.
            Vector3 flat = dronePos - target.position;
            bool inside = new Vector2(flat.x, flat.z).magnitude < 6f && flat.y < 8f;
            holdTimer = inside ? holdTimer + Time.deltaTime : 0f;
            if (holdTimer > 1f)
            {
                holdTimer = 0f;
                ReachTarget();
            }
        }

        foreach (Transform t in targets) t.Rotate(0f, 60f * Time.deltaTime, 0f);
    }

    private void ReachTarget()
    {
        Destroy(targets[0].gameObject);
        targets.RemoveAt(0);

        if (targets.Count == 0)
        {
            Pass();
            return;
        }

        if (kind == Kind.Delivery)
        {
            bool first = targets.Count == stopsLeft;
            Objective = targets.Count == 1
                ? "Deliver to the <color=#ffd133>yellow marker</color>."
                : $"Deliver to the <color=#ffd133>yellow marker</color>. {targets.Count} stops left.";
            Banner?.Invoke(first ? "PACKAGE COLLECTED" : "DELIVERED", "", true);
        }
        else
        {
            Objective = $"Fly through the <color=#33d9ff>blue rings</color>. {targets.Count} to go.";
        }
        HighlightNext();
    }

    private void Pass()
    {
        Active = false;
        int bonus = Mathf.RoundToInt(TimeLeft) * 5;
        Cash += reward + bonus;
        PlayerPrefs.SetInt("skybound_cash", Cash);
        if (Completed(CurrentMission) < CurrentLevel) PlayerPrefs.SetInt($"skybound_m{CurrentMission}", CurrentLevel);
        PlayerPrefs.Save();

        string unlocked = CurrentLevel < Catalog[CurrentMission].Levels
            ? $"Level {CurrentLevel + 1} unlocked"
            : CurrentMission + 1 < Catalog.Length ? $"New mission unlocked: {Catalog[CurrentMission + 1].Name}" : "All missions complete!";
        Objective = "";
        Banner?.Invoke("MISSION PASSED", $"+${reward + bonus}   ·   {unlocked}", true);
        Invoke(nameof(RaiseFinished), 4f);
    }

    private void RaiseFinished() => Finished?.Invoke();

    private void Fail(string headline, string reason)
    {
        if (!Active) return;
        Active = false;
        ClearTargets();
        Objective = "";
        Banner?.Invoke(headline, reason + "  Retrying…", false);
        Invoke(nameof(Retry), 3.5f);
    }

    private void Retry() => StartMission(CurrentMission, CurrentLevel);

    // -------------------------------------------------------------- markers

    private Vector3 PointNear(Vector3 from, float distance)
    {
        Vector3 best = from;
        float bestError = float.MaxValue;
        for (int i = 0; i < 40; i++)
        {
            Vector3 p = network.RandomPoint(out _, out _);
            float error = Mathf.Abs(Vector3.Distance(new Vector3(from.x, 0f, from.z), p) - distance);
            if (error < bestError) { bestError = error; best = p; }
        }
        return best;
    }

    private Transform Beacon(Vector3 position)
    {
        var beacon = new GameObject("MissionMarker").transform;
        beacon.position = position;
        GameObject column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(column.GetComponent<Collider>());
        column.transform.SetParent(beacon, false);
        column.transform.localPosition = new Vector3(0f, 40f, 0f);
        column.transform.localScale = new Vector3(8f, 40f, 8f);
        column.GetComponent<Renderer>().sharedMaterial = beaconMaterial;
        column.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        AddRing(beacon, new Vector3(0f, 0.3f, 0f), 6f, Quaternion.identity);
        return beacon;
    }

    private Transform Ring(Vector3 position, Vector3 from)
    {
        var ring = new GameObject("Checkpoint").transform;
        ring.position = position;
        Vector3 dir = position - from;
        dir.y = 0f;
        AddRing(ring, Vector3.zero, 10f, Quaternion.LookRotation(dir.sqrMagnitude > 0.1f ? dir : Vector3.forward) * Quaternion.Euler(90f, 0f, 0f));

        // Light beam from the street up through the ring so it can be spotted from far away.
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(beam.GetComponent<Collider>());
        beam.transform.SetParent(ring, false);
        beam.transform.localScale = new Vector3(2.5f, 150f, 2.5f);
        beam.GetComponent<Renderer>().sharedMaterial = beamMaterial;
        beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ring;
    }

    private void AddRing(Transform parent, Vector3 offset, float radius, Quaternion rotation)
    {
        var go = new GameObject("Ring", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = offset;
        go.transform.localRotation = rotation;
        go.GetComponent<MeshFilter>().sharedMesh = Torus(radius, 1.3f);
        go.GetComponent<MeshRenderer>().sharedMaterial = ringMaterial;
    }

    private void HighlightNext()
    {
        for (int i = 0; i < targets.Count; i++)
        {
            targets[i].gameObject.SetActive(kind == Kind.Race ? i < 2 : i == 0);
        }
    }

    private void ClearTargets()
    {
        foreach (Transform t in targets) if (t != null) Destroy(t.gameObject);
        targets.Clear();
    }

    private static Material Glow(Color color)
    {
        var m = new Material(Shader.Find("Standard")) { color = color };
        m.SetFloat("_Mode", 3f); // transparent
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_ALPHABLEND_ON");
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(color.r, color.g, color.b) * 1.2f);
        m.renderQueue = 3000;
        return m;
    }

    private static Mesh Torus(float radius, float thickness)
    {
        const int major = 48, minor = 10;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i <= major; i++)
        {
            float a = i * Mathf.PI * 2f / major;
            Vector3 centre = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            for (int j = 0; j <= minor; j++)
            {
                float b = j * Mathf.PI * 2f / minor;
                vertices.Add(centre + (centre.normalized * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b)) * thickness);
                if (i < major && j < minor)
                {
                    int k = i * (minor + 1) + j, n = k + minor + 1;
                    triangles.AddRange(new[] { k, n, k + 1, k + 1, n, n + 1 });
                }
            }
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        return mesh;
    }
}
