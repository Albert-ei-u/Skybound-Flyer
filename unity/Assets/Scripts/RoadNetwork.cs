using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Real OSM road centrelines baked into the scene by the city importer.
/// Traffic, pedestrians and missions navigate on it at runtime.
/// </summary>
public sealed class RoadNetwork : MonoBehaviour
{
    [SerializeField] public Vector3[] points;
    [SerializeField] public int[] roadStarts;   // index of each road's first point in points
    [SerializeField] public float[] roadWidths;
    [SerializeField] public bool driveOnLeft;  // UK-style traffic

    private Dictionary<Vector2Int, List<int>> junctions;

    public int RoadCount => roadStarts?.Length ?? 0;

    public int PointCount(int road) =>
        (road + 1 < roadStarts.Length ? roadStarts[road + 1] : points.Length) - roadStarts[road];

    public Vector3 Point(int road, int index) => points[roadStarts[road] + index];

    public float Width(int road) => roadWidths[road];

    /// <summary>Picks the next road leaving the junction at the end of <paramref name="road"/>.</summary>
    public bool NextRoad(int road, bool forward, out int nextRoad, out bool nextForward)
    {
        BuildJunctions();
        Vector3 end = forward ? Point(road, PointCount(road) - 1) : Point(road, 0);
        nextRoad = road;
        nextForward = !forward; // dead end: turn around

        if (!junctions.TryGetValue(Key(end), out List<int> options)) return false;
        var candidates = new List<int>();
        foreach (int option in options)
        {
            if (option != road) candidates.Add(option);
        }
        if (candidates.Count == 0) return false;

        nextRoad = candidates[Random.Range(0, candidates.Count)];
        nextForward = (Point(nextRoad, 0) - end).sqrMagnitude <
                      (Point(nextRoad, PointCount(nextRoad) - 1) - end).sqrMagnitude;
        return true;
    }

    /// <summary>A random point on a road, used to place missions and spawns.</summary>
    public Vector3 RandomPoint(out int road, out int index)
    {
        road = Random.Range(0, RoadCount);
        index = Random.Range(0, PointCount(road));
        return Point(road, index);
    }

    private void BuildJunctions()
    {
        if (junctions != null) return;
        junctions = new Dictionary<Vector2Int, List<int>>();
        for (int r = 0; r < RoadCount; r++)
        {
            AddJunction(Point(r, 0), r);
            AddJunction(Point(r, PointCount(r) - 1), r);
        }
    }

    private void AddJunction(Vector3 p, int road)
    {
        Vector2Int key = Key(p);
        if (!junctions.TryGetValue(key, out List<int> list)) junctions[key] = list = new List<int>();
        if (!list.Contains(road)) list.Add(road);
    }

    private static Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));
}

/// <summary>
/// Moves an agent along the road network with a lateral lane offset.
/// Shared by cars (lane) and pedestrians (sidewalk).
/// </summary>
public struct RoadWalker
{
    public int Road, Index;
    public bool Forward;
    public float Offset; // metres to the right of travel (negative = left)

    public Vector3 Target(RoadNetwork net)
    {
        int count = net.PointCount(Road);
        int from = Mathf.Clamp(Forward ? Index - 1 : Index + 1, 0, count - 1);
        Vector3 a = net.Point(Road, from);
        Vector3 b = net.Point(Road, Index);
        Vector3 dir = b - a;
        if (dir.sqrMagnitude < 0.01f) return b;
        Vector3 right = Vector3.Cross(Vector3.up, dir.normalized);
        return b + right * Offset;
    }

    /// <summary>Advance to the next point; returns false if the agent turned around.</summary>
    public void Advance(RoadNetwork net)
    {
        int count = net.PointCount(Road);
        int next = Index + (Forward ? 1 : -1);
        if (next >= 0 && next < count)
        {
            Index = next;
            return;
        }
        net.NextRoad(Road, Forward, out int road, out bool forward);
        Road = road;
        Forward = forward;
        Index = forward ? Mathf.Min(1, net.PointCount(road) - 1) : Mathf.Max(0, net.PointCount(road) - 2);
    }
}
