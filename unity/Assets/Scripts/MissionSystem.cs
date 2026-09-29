using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GTA-style mission loop: each level gives a timed job (delivery or
/// checkpoint race) placed on the real streets. Passing pays cash and unlocks
/// the next, harder level; progress is saved between sessions.
/// </summary>
public sealed class MissionSystem : MonoBehaviour
{
    [SerializeField] public RoadNetwork network;
    [SerializeField] public DroneController drone;

    public const int MaxLevel = 10;

    public int Level { get; private set; }
    public int Cash { get; private set; }
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

    private enum Kind { Delivery, Race }

    private static readonly string[] Cargo =
    {
        "medical supplies", "a phone repair part", "fresh coffee", "legal documents",
        "a birthday cake", "a spare house key", "concert tickets", "a lost passport",
    };

    private readonly List<Transform> targets = new List<Transform>();
    private Kind kind;
    private bool carrying;
    private float holdTimer;
    private int reward;
    private Material beaconMaterial, ringMaterial;

    private void Start()
    {
        Level = PlayerPrefs.GetInt("skybound_level", 1);
        Cash = PlayerPrefs.GetInt("skybound_cash", 0);
        beaconMaterial = Glow(new Color(1f, 0.85f, 0.1f, 0.35f));
        ringMaterial = Glow(new Color(0.2f, 0.9f, 1f, 0.8f));
        if (drone != null) drone.Crashed += () => Fail("DRONE DESTROYED", "You hit something too hard.");
        StartMission();
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
            if (Vector3.Distance(dronePos, target.position) < 9f) ReachTarget();
        }
        else
        {
            // Descend into the marker and hold for a moment to pick up / drop off.
            Vector3 flat = dronePos - target.position;
            bool inside = new Vector2(flat.x, flat.z).magnitude < 5f && flat.y < 6f;
            holdTimer = inside ? holdTimer + Time.deltaTime : 0f;
            if (holdTimer > 1.2f)
            {
                holdTimer = 0f;
                ReachTarget();
            }
        }

        foreach (Transform t in targets) t.Rotate(0f, 60f * Time.deltaTime, 0f);
    }

    // ----------------------------------------------------------------- flow

    private void StartMission()
    {
        ClearTargets();
        if (network == null || network.RoadCount == 0) return;

        kind = Level % 2 == 1 ? Kind.Delivery : Kind.Race;
        Vector3 origin = drone.transform.position;
        reward = 250 * Level + 250;

        if (kind == Kind.Delivery)
        {
            string cargo = Cargo[Random.Range(0, Cargo.Length)];
            Vector3 pickup = PointNear(origin, 150f + Level * 40f);
            Vector3 dropoff = PointNear(pickup, 300f + Level * 80f);
            targets.Add(Beacon(pickup));
            targets.Add(Beacon(dropoff));
            carrying = false;
            Title = $"LEVEL {Level} · DELIVERY";
            Objective = $"Fly to the yellow marker and pick up {cargo}.";
            float distance = Vector3.Distance(origin, pickup) + Vector3.Distance(pickup, dropoff);
            TimeLeft = distance / Mathf.Lerp(3.5f, 7f, Level / (float)MaxLevel) + 25f;
        }
        else
        {
            int count = 4 + Level;
            Vector3 last = origin;
            float distance = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = PointNear(last, 120f + Level * 15f);
                p.y = Random.Range(15f, 25f + Level * 6f);
                targets.Add(Ring(p, last));
                distance += Vector3.Distance(last, p);
                last = p;
            }
            Title = $"LEVEL {Level} · CHECKPOINT RACE";
            Objective = $"Fly through all {count} rings before time runs out.";
            TimeLeft = distance / Mathf.Lerp(5f, 9f, Level / (float)MaxLevel) + 15f;
        }

        HighlightNext();
        Active = true;
        Banner?.Invoke(Title, Objective, true);
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

        if (kind == Kind.Delivery && !carrying)
        {
            carrying = true;
            Objective = "Package collected. Deliver it to the marker.";
            Banner?.Invoke("PACKAGE COLLECTED", Objective, true);
        }
        else if (kind == Kind.Race)
        {
            Objective = $"{targets.Count} rings to go.";
        }
        HighlightNext();
    }

    private void Pass()
    {
        Active = false;
        int bonus = Mathf.RoundToInt(TimeLeft) * 5;
        Cash += reward + bonus;
        Level = Mathf.Min(Level + 1, MaxLevel);
        PlayerPrefs.SetInt("skybound_level", Level);
        PlayerPrefs.SetInt("skybound_cash", Cash);
        PlayerPrefs.Save();
        Banner?.Invoke("MISSION PASSED", $"+${reward}  time bonus +${bonus}", true);
        Invoke(nameof(StartMission), 4f);
    }

    private void Fail(string headline, string reason)
    {
        if (!Active) return;
        Active = false;
        ClearTargets();
        Banner?.Invoke(headline, reason + "  Retrying…", false);
        Invoke(nameof(Retry), 3.5f);
    }

    private void Retry()
    {
        drone.ResetDrone();
        StartMission();
    }

    /// <summary>Restart the career from level 1 (used by the pause menu).</summary>
    public void ResetProgress()
    {
        PlayerPrefs.DeleteKey("skybound_level");
        PlayerPrefs.DeleteKey("skybound_cash");
        Level = 1;
        Cash = 0;
        CancelInvoke();
        Active = false;
        Retry();
    }

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
        AddRing(beacon, new Vector3(0f, 0.3f, 0f), 5f, Quaternion.identity);
        return beacon;
    }

    private Transform Ring(Vector3 position, Vector3 from)
    {
        var ring = new GameObject("Checkpoint").transform;
        ring.position = position;
        Vector3 dir = position - from;
        dir.y = 0f;
        AddRing(ring, Vector3.zero, 7f, Quaternion.LookRotation(dir.sqrMagnitude > 0.1f ? dir : Vector3.forward) * Quaternion.Euler(90f, 0f, 0f));
        return ring;
    }

    private void AddRing(Transform parent, Vector3 offset, float radius, Quaternion rotation)
    {
        var go = new GameObject("Ring", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = offset;
        go.transform.localRotation = rotation;
        go.GetComponent<MeshFilter>().sharedMesh = Torus(radius, 0.5f);
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
