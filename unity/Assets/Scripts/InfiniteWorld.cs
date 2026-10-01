using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Endless open world around the real city. Terrain is generated in chunks
/// around the drone wherever it flies: flat land at the city edge, then
/// hills, forests, mountains, beaches and an endless ocean. One coast is
/// guaranteed a few kilometres out in the <see cref="coastDirection"/>.
/// Falling into the water counts as a crash.
/// </summary>
public sealed class InfiniteWorld : MonoBehaviour
{
    [SerializeField] public Transform drone;
    [SerializeField] public Material terrainMaterial;
    [SerializeField] public Material waterMaterial;
    [Tooltip("Compass bearing (degrees) in which the nearest ocean lies.")]
    [SerializeField] public float coastDirection = 135f;
    [SerializeField] public int seed = 1;
    [Tooltip("Half-width of the city's ground plane; chunks fully inside it are skipped.")]
    [SerializeField] public float cityHalfSize = 1600f;

    public const float WaterLevel = -0.5f;
    private const float ChunkSize = 400f;
    private const int Resolution = 20;        // cells per chunk side (20 m)
    private const int ViewChunks = 8;         // ~3.2 km draw distance
    private const float ColliderRange = 900f;
    private const float FlatRadius = 1700f;   // city stays flat out to here
    private const float RampWidth = 1300f;

    private sealed class Chunk
    {
        public GameObject Go;
        public MeshCollider Collider;
    }

    private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    private readonly List<Vector2Int> toBuild = new List<Vector2Int>();
    private Transform water;
    private Vector2 coast, noiseOffset;
    private DroneController controller;
    private bool splashed;

    private void Start()
    {
        float a = coastDirection * Mathf.Deg2Rad;
        coast = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        var random = new System.Random(seed);
        noiseOffset = new Vector2(random.Next(1000, 60000), random.Next(1000, 60000));

        if (waterMaterial != null) waterMaterial = new Material(waterMaterial); // animated copy, asset stays untouched
        water = BuildWater();
        if (drone != null)
        {
            controller = drone.GetComponent<DroneController>();
            if (controller != null) controller.WasReset += () => splashed = false;
        }
    }

    private void Update()
    {
        if (drone == null) return;
        Vector3 p = drone.position;

        // Water follows the drone, snapped to its texture tile so ripples don't slide.
        water.position = new Vector3(Mathf.Round(p.x / 50f) * 50f, WaterLevel, Mathf.Round(p.z / 50f) * 50f);

        StreamChunks(p);

        // Splash down in the ocean.
        if (!splashed && p.y < 1.2f && IsOcean(p))
        {
            splashed = true;
            controller?.Crash("The drone fell into the ocean.");
        }
    }

    // ----------------------------------------------------------- queries

    /// <summary>Ground height (m) at a world position, WaterLevel or below means sea.</summary>
    public float HeightAt(float x, float z)
    {
        float d = Mathf.Sqrt(x * x + z * z);
        float blend = Mathf.SmoothStep(0f, 1f, (d - FlatRadius) / RampWidth);
        if (blend <= 0f) return -0.05f;

        float nx = x + noiseOffset.x, nz = z + noiseOffset.y;
        float continent = (Fbm(nx / 3200f, nz / 3200f, 3) - 0.5f) * 2.2f;   // about -1..1
        float hills = Fbm(nx / 650f, nz / 650f, 4);
        float ridges = 1f - Mathf.Abs(Fbm(nx / 1400f, nz / 1400f, 3) * 2f - 1f);
        float h = continent * 140f + 20f
                + hills * hills * 110f * Mathf.Clamp01(continent + 0.6f)
                + ridges * ridges * ridges * 260f * Mathf.Clamp01(continent);

        // The guaranteed coast: land sinks steadily beyond ~2.6 km toward it.
        float along = x * coast.x + z * coast.y;
        h -= Mathf.Max(0f, along - 2600f) * 0.15f;
        h = Mathf.Clamp(h, -60f, 420f);

        h = Mathf.Lerp(-0.05f, h, blend);
        // Keep dry land under the corners of the city's ground plane.
        if (d < cityHalfSize * 1.45f) h = Mathf.Max(h, -0.05f);
        return h;
    }

    public bool IsOcean(Vector3 p) => HeightAt(p.x, p.z) < WaterLevel;

    /// <summary>Short place name for the HUD status line.</summary>
    public string RegionName(Vector3 p, string city)
    {
        float d = new Vector2(p.x, p.z).magnitude;
        if (d < FlatRadius) return city;
        if (IsOcean(p)) return "OPEN OCEAN";
        float h = HeightAt(p.x, p.z);
        if (h < 4f) return "COAST";
        if (h > 180f) return "MOUNTAINS";
        return $"COUNTRYSIDE · {d / 1000f:0.0} KM FROM {city}";
    }

    private static float Fbm(float x, float z, int octaves)
    {
        float sum = 0f, amp = 0.5f, total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Mathf.PerlinNoise(x, z) * amp;
            total += amp;
            x *= 2.03f;
            z *= 2.03f;
            amp *= 0.5f;
        }
        return sum / total;
    }

    // --------------------------------------------------------- streaming

    private void StreamChunks(Vector3 p)
    {
        var centre = new Vector2Int(Mathf.FloorToInt(p.x / ChunkSize), Mathf.FloorToInt(p.z / ChunkSize));

        // Drop chunks that fell behind, and give nearby ones collision.
        var remove = new List<Vector2Int>();
        foreach (var pair in chunks)
        {
            Vector2Int c = pair.Key;
            if (Mathf.Abs(c.x - centre.x) > ViewChunks + 1 || Mathf.Abs(c.y - centre.y) > ViewChunks + 1)
            {
                Destroy(pair.Value.Go);
                remove.Add(c);
                continue;
            }
            Vector3 mid = new Vector3((c.x + 0.5f) * ChunkSize, 0f, (c.y + 0.5f) * ChunkSize);
            bool near = Vector2.Distance(new Vector2(mid.x, mid.z), new Vector2(p.x, p.z)) < ColliderRange;
            pair.Value.Collider.enabled = near;
        }
        foreach (Vector2Int c in remove) chunks.Remove(c);

        // Build missing chunks nearest-first, a few per frame to avoid hitches.
        toBuild.Clear();
        for (int x = -ViewChunks; x <= ViewChunks; x++)
            for (int z = -ViewChunks; z <= ViewChunks; z++)
            {
                var c = new Vector2Int(centre.x + x, centre.y + z);
                if (x * x + z * z > ViewChunks * ViewChunks || chunks.ContainsKey(c)) continue;
                toBuild.Add(c);
            }
        toBuild.Sort((a, b) => (a - centre).sqrMagnitude.CompareTo((b - centre).sqrMagnitude));
        for (int i = 0; i < toBuild.Count && i < 3; i++) chunks[toBuild[i]] = BuildChunk(toBuild[i]);
    }

    private Chunk BuildChunk(Vector2Int c)
    {
        var chunk = new Chunk { Go = new GameObject($"Terrain {c.x},{c.y}") };
        chunk.Go.transform.SetParent(transform, false);

        float x0 = c.x * ChunkSize, z0 = c.y * ChunkSize;
        // Entirely under the city's ground plane: nothing to show.
        bool hidden = Mathf.Abs(x0) <= cityHalfSize && Mathf.Abs(x0 + ChunkSize) <= cityHalfSize &&
                      Mathf.Abs(z0) <= cityHalfSize && Mathf.Abs(z0 + ChunkSize) <= cityHalfSize;
        chunk.Collider = chunk.Go.AddComponent<MeshCollider>();
        chunk.Collider.enabled = false;
        if (hidden) return chunk;

        int n = Resolution + 1;
        var vertices = new Vector3[n * n];
        var colors = new Color[n * n];
        var triangles = new int[Resolution * Resolution * 6];
        float cell = ChunkSize / Resolution;
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float wx = x0 + x * cell, wz = z0 + z * cell;
                float h = HeightAt(wx, wz);
                vertices[z * n + x] = new Vector3(wx, h, wz);
                colors[z * n + x] = GroundColor(wx, wz, h);
            }
        int t = 0;
        for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution; x++)
            {
                int i = z * n + x;
                triangles[t++] = i; triangles[t++] = i + n; triangles[t++] = i + 1;
                triangles[t++] = i + 1; triangles[t++] = i + n; triangles[t++] = i + n + 1;
            }

        var mesh = new Mesh { name = chunk.Go.name };
        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        chunk.Go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = chunk.Go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = terrainMaterial;
        chunk.Collider.sharedMesh = mesh;
        return chunk;
    }

    private Color GroundColor(float x, float z, float h)
    {
        // Slope from neighbouring heights: steep ground shows rock.
        float slope = Mathf.Abs(HeightAt(x + 8f, z) - h) + Mathf.Abs(HeightAt(x, z + 8f) - h);
        float variation = Mathf.PerlinNoise(x / 90f + 7f, z / 90f + 3f) * 0.12f - 0.06f;

        Color sand = new Color(0.82f, 0.76f, 0.56f);
        Color grass = new Color(0.32f, 0.5f, 0.22f);
        Color forest = new Color(0.16f, 0.32f, 0.14f);
        Color rock = new Color(0.45f, 0.42f, 0.4f);
        Color snow = new Color(0.94f, 0.95f, 0.97f);

        Color c;
        if (h < WaterLevel) c = Color.Lerp(sand, new Color(0.3f, 0.35f, 0.3f), Mathf.Clamp01(-h / 25f)); // sea bed
        else if (h < 3f) c = sand;
        else if (h < 70f) c = Color.Lerp(grass, forest, Mathf.PerlinNoise(x / 300f, z / 300f));
        else if (h < 220f) c = Color.Lerp(forest, rock, (h - 70f) / 150f);
        else c = Color.Lerp(rock, snow, Mathf.Clamp01((h - 220f) / 60f));
        if (slope > 9f && h > 3f) c = Color.Lerp(c, rock, Mathf.Clamp01((slope - 9f) / 10f));
        return c * (1f + variation);
    }

    private Transform BuildWater()
    {
        // One big quad that follows the drone: the sea is endless in every direction.
        const float half = 4500f;
        var go = new GameObject("Ocean", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(transform, false);
        var mesh = new Mesh { name = "Ocean" };
        mesh.vertices = new[]
        {
            new Vector3(-half, 0f, -half), new Vector3(-half, 0f, half),
            new Vector3(half, 0f, half), new Vector3(half, 0f, -half),
        };
        float tiles = half * 2f / 50f;
        mesh.uv = new[] { Vector2.zero, new Vector2(0f, tiles), new Vector2(tiles, tiles), new Vector2(tiles, 0f) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(half * 2f, 1f, half * 2f));
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = waterMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    private void LateUpdate()
    {
        // Slowly drifting waves.
        if (waterMaterial != null)
            waterMaterial.mainTextureOffset = new Vector2(Time.time * 0.01f, Time.time * 0.006f);
    }
}
