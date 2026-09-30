using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns a skybound.city.v1 OpenStreetMap export (tools/export_city.py) into
/// real extruded buildings and road surfaces. Geometry is merged into spatial
/// chunks so thousands of buildings render with only a few draw calls.
/// </summary>
public static class CityImporter
{
    private const float ChunkSize = 250f;
    private const float FloorHeight = 3.5f;
    private const float WindowBayWidth = 3f;

    public static GameObject Build(string jsonPath, Material[] facades, Material roof,
                                   Material road, string meshFolder, out Vector3 spawn)
    {
        var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(jsonPath));
        var city = new GameObject("City");
        city.AddComponent<RoadNetwork>();

        var outlines = new List<List<Vector2>>();
        BuildBuildings((List<object>)root["buildings"], facades, roof, meshFolder, city.transform, outlines);
        BuildRoads((List<object>)root["roads"], road, meshFolder, city.transform, out Vector3 roadSpawn);

        if (!FindOpenGround(city.GetComponent<RoadNetwork>(), outlines, out spawn))
        {
            Debug.LogWarning("No open ground found for the drone base; starting on the nearest road.");
            spawn = roadSpawn;
        }
        return city;
    }

    // ---------------------------------------------------------------- buildings

    private sealed class MeshBuffer
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Walls = new List<int>();
        public readonly List<int> Roofs = new List<int>();
    }

    private static void BuildBuildings(List<object> buildings, Material[] facades, Material roof,
                                       string meshFolder, Transform parent, List<List<Vector2>> outlines)
    {
        // One buffer per (chunk, facade style) so each chunk gets varied facades.
        var buffers = new Dictionary<(Vector2Int, int), MeshBuffer>();

        foreach (object entry in buildings)
        {
            var building = (Dictionary<string, object>)entry;
            List<Vector2> outline = ReadRing((List<object>)building["coordinates"]);
            if (outline.Count < 3) continue;
            outlines.Add(outline);

            float height = ToFloat(building["height"]);
            height = float.IsNaN(height) ? 12f : Mathf.Clamp(height, 4f, 450f);
            Vector2 centre = Centroid(outline);
            var chunk = new Vector2Int(Mathf.FloorToInt(centre.x / ChunkSize),
                                       Mathf.FloorToInt(centre.y / ChunkSize));
            // Taller buildings get glass towers; low-rise get brick/concrete.
            int style = height > 60f ? 0 : (Mathf.Abs(Mathf.RoundToInt(centre.x * 7 + centre.y * 13)) % (facades.Length - 1)) + 1;

            if (!buffers.TryGetValue((chunk, style), out MeshBuffer buffer))
            {
                buffer = new MeshBuffer();
                buffers[(chunk, style)] = buffer;
            }
            AddExtrusion(buffer, outline, height);
        }

        var group = new GameObject("Buildings").transform;
        group.SetParent(parent, false);
        foreach (var pair in buffers)
        {
            MeshBuffer b = pair.Value;
            var mesh = new Mesh { name = $"Buildings_{pair.Key.Item1.x}_{pair.Key.Item1.y}_{pair.Key.Item2}" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(b.Vertices);
            mesh.SetUVs(0, b.Uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(b.Walls, 0);
            mesh.SetTriangles(b.Roofs, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMesh(mesh, meshFolder);

            var go = new GameObject(mesh.name);
            go.transform.SetParent(group, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { facades[pair.Key.Item2], roof };
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        }
    }

    private static void AddExtrusion(MeshBuffer b, List<Vector2> ring, float height)
    {
        if (SignedArea(ring) < 0f) ring.Reverse(); // make counter-clockwise

        // Walls: flat-shaded quads; UVs in window bays so textures tile per floor.
        float perimeter = 0f;
        for (int i = 0; i < ring.Count; i++)
        {
            Vector2 a = ring[i];
            Vector2 c = ring[(i + 1) % ring.Count];
            float length = Vector2.Distance(a, c);
            if (length < 0.01f) continue;

            int start = b.Vertices.Count;
            b.Vertices.Add(new Vector3(a.x, 0f, a.y));
            b.Vertices.Add(new Vector3(c.x, 0f, c.y));
            b.Vertices.Add(new Vector3(c.x, height, c.y));
            b.Vertices.Add(new Vector3(a.x, height, a.y));
            float u0 = perimeter / WindowBayWidth;
            float u1 = (perimeter + length) / WindowBayWidth;
            float v1 = height / FloorHeight;
            b.Uvs.Add(new Vector2(u0, 0f));
            b.Uvs.Add(new Vector2(u1, 0f));
            b.Uvs.Add(new Vector2(u1, v1));
            b.Uvs.Add(new Vector2(u0, v1));
            // Counter-clockwise ring seen from above -> outward faces need this order.
            b.Walls.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            perimeter += length;
        }

        // Roof: ear-clipped polygon.
        int roofStart = b.Vertices.Count;
        foreach (Vector2 p in ring)
        {
            b.Vertices.Add(new Vector3(p.x, height, p.y));
            b.Uvs.Add(p / 10f);
        }
        foreach (int index in Triangulate(ring))
        {
            b.Roofs.Add(roofStart + index);
        }
    }

    // --------------------------------------------------------------- drone base

    private const float BaseCell = 4f;          // search grid resolution (m)
    private const float BaseSearchRadius = 700f;
    private const float RoadClearance = 12f;    // metres from the road edge
    private const float BuildingClearance = 14f; // metres from any wall

    /// <summary>
    /// Finds the open ground closest to the city centre that is well clear of
    /// every road and building, so the drone base never sits on a street.
    /// </summary>
    private static bool FindOpenGround(RoadNetwork network, List<List<Vector2>> outlines, out Vector3 spot)
    {
        int size = Mathf.CeilToInt(BaseSearchRadius * 2f / BaseCell);
        var blocked = new bool[size, size];

        Vector2 CellCentre(int x, int z) =>
            new Vector2(-BaseSearchRadius + (x + 0.5f) * BaseCell, -BaseSearchRadius + (z + 0.5f) * BaseCell);

        // Visits every cell within `margin` of the box (min, max) and blocks those the test accepts.
        void BlockAround(Vector2 min, Vector2 max, float margin, System.Func<Vector2, bool> test)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((min.x - margin + BaseSearchRadius) / BaseCell));
            int x1 = Mathf.Min(size - 1, Mathf.FloorToInt((max.x + margin + BaseSearchRadius) / BaseCell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((min.y - margin + BaseSearchRadius) / BaseCell));
            int z1 = Mathf.Min(size - 1, Mathf.FloorToInt((max.y + margin + BaseSearchRadius) / BaseCell));
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                    if (!blocked[x, z] && test(CellCentre(x, z))) blocked[x, z] = true;
        }

        for (int road = 0; road < network.RoadCount; road++)
        {
            float reach = network.Width(road) * 0.5f + RoadClearance;
            for (int i = 0; i + 1 < network.PointCount(road); i++)
            {
                Vector3 a3 = network.Point(road, i), b3 = network.Point(road, i + 1);
                Vector2 a = new Vector2(a3.x, a3.z), b = new Vector2(b3.x, b3.z);
                BlockAround(Vector2.Min(a, b), Vector2.Max(a, b), reach,
                            p => DistanceToSegment(p, a, b) <= reach);
            }
        }

        foreach (List<Vector2> ring in outlines)
        {
            Vector2 min = ring[0], max = ring[0];
            foreach (Vector2 p in ring) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            BlockAround(min, max, BuildingClearance, p =>
            {
                if (InsidePolygon(p, ring)) return true;
                for (int i = 0; i < ring.Count; i++)
                    if (DistanceToSegment(p, ring[i], ring[(i + 1) % ring.Count]) <= BuildingClearance) return true;
                return false;
            });
        }

        // Nearest free cell to the centre. Skip the edge of the search area,
        // where roads and buildings just outside it were never checked.
        spot = Vector3.zero;
        float best = float.MaxValue;
        int border = Mathf.CeilToInt(BuildingClearance / BaseCell) + 1;
        for (int x = border; x < size - border; x++)
        {
            for (int z = border; z < size - border; z++)
            {
                if (blocked[x, z]) continue;
                Vector2 c = CellCentre(x, z);
                if (c.sqrMagnitude < best) { best = c.sqrMagnitude; spot = new Vector3(c.x, 0f, c.y); }
            }
        }
        return best < float.MaxValue;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    private static bool InsidePolygon(Vector2 p, List<Vector2> ring)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            if ((ring[i].y > p.y) != (ring[j].y > p.y) &&
                p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x)
                inside = !inside;
        }
        return inside;
    }

    // -------------------------------------------------------------------- roads

    private static void BuildRoads(List<object> roads, Material material, string meshFolder, Transform parent,
                                   out Vector3 spawn)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        var centreline = new List<Vector3>();
        var starts = new List<int>();
        var widths = new List<float>();

        void AddLine(List<Vector2> line, float width)
        {
            AddRoadStrip(line, width, vertices, uvs, triangles);
            if (line.Count < 2) return;
            starts.Add(centreline.Count);
            widths.Add(width);
            foreach (Vector2 p in line) centreline.Add(new Vector3(p.x, 0f, p.y));
        }

        foreach (object entry in roads)
        {
            var road = (Dictionary<string, object>)entry;
            float width = RoadWidth((string)road["highway"]);
            var coords = (List<object>)road["coordinates"];
            // MultiLineString exports are nested one level deeper.
            if (coords.Count > 0 && coords[0] is List<object> first && first.Count > 0 && first[0] is List<object>)
            {
                foreach (object part in coords) AddLine(ReadLine((List<object>)part), width);
            }
            else
            {
                AddLine(ReadLine(coords), width);
            }
        }

        RoadNetwork network = parent.GetComponent<RoadNetwork>();
        network.points = centreline.ToArray();
        network.roadStarts = starts.ToArray();
        network.roadWidths = widths.ToArray();

        // Spawn on the road point nearest the city centre so the drone starts in the open.
        spawn = Vector3.zero;
        float best = float.MaxValue;
        foreach (Vector3 v in vertices)
        {
            if (v.sqrMagnitude < best) { best = v.sqrMagnitude; spawn = v; }
        }

        var mesh = new Mesh { name = "Roads", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        SaveMesh(mesh, meshFolder);

        var go = new GameObject("Roads");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    private static void AddRoadStrip(List<Vector2> line, float width, List<Vector3> vertices,
                                     List<Vector2> uvs, List<int> triangles)
    {
        if (line.Count < 2) return;
        const float y = 0.05f; // just above the ground to avoid z-fighting
        float distance = 0f;
        int start = vertices.Count;
        for (int i = 0; i < line.Count; i++)
        {
            Vector2 dir = i == 0 ? line[1] - line[0]
                        : i == line.Count - 1 ? line[i] - line[i - 1]
                        : (line[i + 1] - line[i - 1]);
            dir.Normalize();
            Vector2 side = new Vector2(-dir.y, dir.x) * (width * 0.5f);
            if (i > 0) distance += Vector2.Distance(line[i], line[i - 1]);

            vertices.Add(new Vector3(line[i].x - side.x, y, line[i].y - side.y));
            vertices.Add(new Vector3(line[i].x + side.x, y, line[i].y + side.y));
            uvs.Add(new Vector2(0f, distance / width));
            uvs.Add(new Vector2(1f, distance / width));
            if (i > 0)
            {
                int k = start + (i - 1) * 2;
                triangles.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 });
            }
        }
    }

    private static float RoadWidth(string highway)
    {
        if (highway.Contains("motorway") || highway.Contains("trunk")) return 18f;
        if (highway.Contains("primary")) return 16f;
        if (highway.Contains("secondary")) return 13f;
        if (highway.Contains("tertiary")) return 11f;
        return 9f;
    }

    // ------------------------------------------------------------------ helpers

    private static void SaveMesh(Mesh mesh, string folder)
    {
        AssetDatabase.CreateAsset(mesh, $"{folder}/{mesh.name}.asset");
    }

    private static List<Vector2> ReadRing(List<object> coords)
    {
        List<Vector2> ring = ReadLine(coords);
        if (ring.Count > 1 && (ring[0] - ring[ring.Count - 1]).sqrMagnitude < 0.0001f)
        {
            ring.RemoveAt(ring.Count - 1); // OSM rings repeat the first point
        }
        return ring;
    }

    private static List<Vector2> ReadLine(List<object> coords)
    {
        var points = new List<Vector2>(coords.Count);
        foreach (object c in coords)
        {
            var pair = (List<object>)c;
            points.Add(new Vector2(ToFloat(pair[0]), ToFloat(pair[1])));
        }
        return points;
    }

    private static float ToFloat(object value) =>
        value is double d ? (float)d : float.Parse(value.ToString(), CultureInfo.InvariantCulture);

    private static float SignedArea(List<Vector2> ring)
    {
        float area = 0f;
        for (int i = 0; i < ring.Count; i++)
        {
            Vector2 a = ring[i], c = ring[(i + 1) % ring.Count];
            area += a.x * c.y - c.x * a.y;
        }
        return area * 0.5f;
    }

    private static Vector2 Centroid(List<Vector2> ring)
    {
        Vector2 sum = Vector2.zero;
        foreach (Vector2 p in ring) sum += p;
        return sum / ring.Count;
    }

    /// <summary>Ear clipping for a counter-clockwise simple polygon.</summary>
    private static List<int> Triangulate(List<Vector2> ring)
    {
        var result = new List<int>();
        var indices = new List<int>();
        for (int i = 0; i < ring.Count; i++) indices.Add(i);

        int guard = ring.Count * ring.Count;
        while (indices.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < indices.Count; i++)
            {
                int ia = indices[(i + indices.Count - 1) % indices.Count];
                int ib = indices[i];
                int ic = indices[(i + 1) % indices.Count];
                Vector2 a = ring[ia], b = ring[ib], c = ring[ic];
                if (Cross(b - a, c - b) <= 0f) continue; // reflex corner

                bool contains = false;
                foreach (int other in indices)
                {
                    if (other == ia || other == ib || other == ic) continue;
                    if (InTriangle(ring[other], a, b, c)) { contains = true; break; }
                }
                if (contains) continue;

                // Unity front faces are clockwise when viewed from above (+Y).
                result.AddRange(new[] { ia, ic, ib });
                indices.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break; // degenerate footprint, keep what we have
        }
        if (indices.Count == 3) result.AddRange(new[] { indices[0], indices[2], indices[1] });
        return result;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
        Cross(b - a, p - a) >= 0f && Cross(c - b, p - b) >= 0f && Cross(a - c, p - c) >= 0f;
}

/// <summary>Small JSON reader (objects, arrays, numbers, strings, bools, null).</summary>
public static class MiniJson
{
    public static object Parse(string json)
    {
        int i = 0;
        return ReadValue(json, ref i);
    }

    private static object ReadValue(string s, ref int i)
    {
        SkipWhitespace(s, ref i);
        char c = s[i];
        if (c == '{') return ReadObject(s, ref i);
        if (c == '[') return ReadArray(s, ref i);
        if (c == '"') return ReadString(s, ref i);
        if (Match(s, i, "true")) { i += 4; return true; }
        if (Match(s, i, "false")) { i += 5; return false; }
        if (Match(s, i, "null")) { i += 4; return null; }
        if (Match(s, i, "NaN")) { i += 3; return double.NaN; }

        int start = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, object> ReadObject(string s, ref int i)
    {
        var result = new Dictionary<string, object>();
        i++;
        while (true)
        {
            SkipWhitespace(s, ref i);
            if (s[i] == '}') { i++; return result; }
            string key = ReadString(s, ref i);
            SkipWhitespace(s, ref i);
            i++; // ':'
            result[key] = ReadValue(s, ref i);
            SkipWhitespace(s, ref i);
            if (s[i] == ',') i++;
        }
    }

    private static List<object> ReadArray(string s, ref int i)
    {
        var result = new List<object>();
        i++;
        while (true)
        {
            SkipWhitespace(s, ref i);
            if (s[i] == ']') { i++; return result; }
            result.Add(ReadValue(s, ref i));
            SkipWhitespace(s, ref i);
            if (s[i] == ',') i++;
        }
    }

    private static string ReadString(string s, ref int i)
    {
        var sb = new StringBuilder();
        i++;
        while (s[i] != '"')
        {
            if (s[i] == '\\')
            {
                i++;
                char e = s[i];
                if (e == 'u') { sb.Append((char)System.Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
            }
            else sb.Append(s[i]);
            i++;
        }
        i++;
        return sb.ToString();
    }

    private static bool Match(string s, int i, string word) =>
        string.CompareOrdinal(s, i, word, 0, word.Length) == 0;

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }
}
