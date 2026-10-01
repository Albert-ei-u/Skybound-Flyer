using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Skybound > Download Real City… : fetches buildings and roads for any place
/// on Earth from OpenStreetMap (Overpass API) and saves a skybound.city.v1
/// file in Assets/cities. Then run Skybound > Create Drone Training Scene and
/// the new city appears in the CITIES menu. Needs an internet connection.
/// </summary>
public sealed class OsmCityDownloader : EditorWindow
{
    private const string Endpoint = "https://overpass-api.de/api/interpreter";

    /// <summary>Ready-made real places: label, latitude, longitude, left-hand traffic.</summary>
    private static readonly (string label, double lat, double lon, bool left)[] Presets =
    {
        ("Paris", 48.8584, 2.2945, false),
        ("Tokyo", 35.6595, 139.7005, true),
        ("Dubai", 25.1972, 55.2744, false),
        ("Sydney", -33.8688, 151.2093, true),
        ("San Francisco", 37.7946, -122.3999, false),
        ("Nairobi", -1.2864, 36.8172, true),
        ("Hong Kong", 22.2819, 114.1589, true),
        ("Rio de Janeiro", -22.9068, -43.1729, false),
        ("Cape Town", -33.9249, 18.4241, true),
        ("Singapore", 1.2834, 103.8607, true),
        ("Berlin", 52.5163, 13.3777, false),
        ("Toronto", 43.6426, -79.3871, false),
    };

    private string label = "Paris";
    private double latitude = 48.8584, longitude = 2.2945;
    private bool driveOnLeft;
    private float radius = 900f;
    private Vector2 scroll;

    [MenuItem("Skybound/Download Real City…")]
    private static void Open() => GetWindow<OsmCityDownloader>(true, "Download Real City", true).minSize = new Vector2(420f, 520f);

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Downloads real buildings and streets from OpenStreetMap. Pick a preset or type any " +
                                "coordinates (right-click a spot in Google Maps to copy them). Keep the radius under " +
                                "about 1200 m: bigger areas take long to download and build.", MessageType.Info);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(190f));
        foreach (var p in Presets)
        {
            if (GUILayout.Button(p.label))
            {
                label = p.label;
                latitude = p.lat;
                longitude = p.lon;
                driveOnLeft = p.left;
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        label = EditorGUILayout.TextField("City name", label);
        latitude = EditorGUILayout.DoubleField("Latitude", latitude);
        longitude = EditorGUILayout.DoubleField("Longitude", longitude);
        radius = EditorGUILayout.Slider("Radius (m)", radius, 300f, 1500f);
        driveOnLeft = EditorGUILayout.Toggle("Left-hand traffic", driveOnLeft);

        EditorGUILayout.Space();
        if (GUILayout.Button("Download", GUILayout.Height(36f)))
        {
            string path = Download(label, latitude, longitude, radius, driveOnLeft);
            if (path != null &&
                EditorUtility.DisplayDialog("Skybound", $"Saved {path}.\n\nRebuild the city scenes now?", "Build scenes", "Later"))
            {
                SkyboundSceneBuilder.CreateDroneTrainingScene();
            }
        }
        EditorGUILayout.LabelField("Map data © OpenStreetMap contributors (ODbL).", EditorStyles.miniLabel);
    }

    /// <summary>Downloads one area and writes Assets/cities/&lt;name&gt;.json. Returns the path or null.</summary>
    public static string Download(string label, double lat, double lon, float radius, bool driveOnLeft)
    {
        string r = radius.ToString("0", CultureInfo.InvariantCulture);
        string at = $"{r},{lat.ToString(CultureInfo.InvariantCulture)},{lon.ToString(CultureInfo.InvariantCulture)}";
        string query =
            "[out:json][timeout:180];(" +
            $"way[\"building\"](around:{at});" +
            "way[\"highway\"~\"^(motorway|trunk|primary|secondary|tertiary|unclassified|residential|living_street|service|" +
            $"motorway_link|trunk_link|primary_link|secondary_link|tertiary_link)$\"](around:{at});" +
            ");out body geom;";

        string json;
        try
        {
            json = Fetch(query);
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Skybound", "Download failed:\n" + e.Message +
                                        "\n\nCheck your internet connection, or try again in a minute (the free OSM server may be busy).", "OK");
            return null;
        }
        if (json == null) return null; // cancelled

        EditorUtility.DisplayProgressBar("Skybound", "Converting OpenStreetMap data…", 0.9f);
        try
        {
            string text = Convert(json, label, lat, lon, driveOnLeft, out int buildings, out int roads);
            if (buildings == 0 && roads == 0)
            {
                EditorUtility.DisplayDialog("Skybound", "No buildings or roads found there. Check the coordinates.", "OK");
                return null;
            }
            Directory.CreateDirectory("Assets/cities");
            string path = $"Assets/cities/{FileName(label)}.json";
            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path);
            Debug.Log($"Skybound: {label} saved with {buildings} buildings and {roads} roads → {path}");
            return path;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static string Fetch(string query)
    {
        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(4) })
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SkyboundFlightSimulator/1.0 (student project)");
            var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", query) });
            var task = client.PostAsync(Endpoint, content);
            try
            {
                float t = 0f;
                while (!task.IsCompleted)
                {
                    t = Mathf.Min(0.85f, t + 0.002f);
                    if (EditorUtility.DisplayCancelableProgressBar("Skybound", "Downloading from OpenStreetMap…", t)) return null;
                    System.Threading.Thread.Sleep(50);
                }
                var response = task.Result;
                string body = response.Content.ReadAsStringAsync().Result;
                if (!response.IsSuccessStatusCode) throw new Exception($"HTTP {(int)response.StatusCode}: {body.Substring(0, Math.Min(200, body.Length))}");
                return body;
            }
            catch (AggregateException e)
            {
                throw e.InnerException ?? e;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }

    /// <summary>Overpass JSON → skybound.city.v1 (metres east/north of the centre).</summary>
    private static string Convert(string overpass, string label, double lat0, double lon0, bool driveOnLeft,
                                  out int buildingCount, out int roadCount)
    {
        var root = (Dictionary<string, object>)MiniJson.Parse(overpass);
        var elements = (List<object>)root["elements"];
        double metresPerLat = 110540.0, metresPerLon = 111320.0 * Math.Cos(lat0 * Math.PI / 180.0);

        var roads = new StringBuilder();
        var buildings = new StringBuilder();
        buildingCount = roadCount = 0;

        foreach (object e in elements)
        {
            var way = (Dictionary<string, object>)e;
            if (!way.TryGetValue("geometry", out object g) || !(g is List<object> geometry) || geometry.Count < 2) continue;
            var tags = way.TryGetValue("tags", out object tg) ? (Dictionary<string, object>)tg : new Dictionary<string, object>();

            var coords = new StringBuilder("[");
            for (int i = 0; i < geometry.Count; i++)
            {
                var node = (Dictionary<string, object>)geometry[i];
                double x = (Number(node["lon"]) - lon0) * metresPerLon;
                double y = (Number(node["lat"]) - lat0) * metresPerLat;
                if (i > 0) coords.Append(',');
                coords.Append('[').Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                      .Append(y.ToString("0.##", CultureInfo.InvariantCulture)).Append(']');
            }
            coords.Append(']');

            if (tags.TryGetValue("building", out object kind))
            {
                if (geometry.Count < 4) continue;
                if (buildingCount++ > 0) buildings.Append(',');
                buildings.Append("{\"building\":").Append(Quote(kind.ToString()))
                         .Append(",\"height\":").Append(Height(tags).ToString("0.#", CultureInfo.InvariantCulture))
                         .Append(",\"coordinates\":").Append(coords).Append('}');
            }
            else if (tags.TryGetValue("highway", out object highway))
            {
                if (roadCount++ > 0) roads.Append(',');
                string name = tags.TryGetValue("name", out object n) ? n.ToString() : "";
                roads.Append("{\"name\":").Append(Quote(name)).Append(",\"highway\":").Append(Quote(highway.ToString()))
                     .Append(",\"coordinates\":").Append(coords).Append('}');
            }
        }

        return new StringBuilder()
            .Append("{\"schema\":\"skybound.city.v1\",\"source\":\"OpenStreetMap via Overpass API\",")
            .Append("\"attribution\":\"\\u00a9 OpenStreetMap contributors\",")
            .Append("\"place\":").Append(Quote(label)).Append(",\"label\":").Append(Quote(label))
            .Append(",\"drive_on_left\":").Append(driveOnLeft ? "true" : "false")
            .Append(",\"units\":\"meters\",\"center\":[")
            .Append(lat0.ToString(CultureInfo.InvariantCulture)).Append(',').Append(lon0.ToString(CultureInfo.InvariantCulture))
            .Append("],\"roads\":[").Append(roads).Append("],\"buildings\":[").Append(buildings).Append("]}")
            .ToString();
    }

    /// <summary>Real height from OSM tags, else from the number of floors, else a typical value.</summary>
    private static float Height(Dictionary<string, object> tags)
    {
        if (tags.TryGetValue("height", out object h) && LeadingNumber(h.ToString(), out float metres)) return Mathf.Max(3f, metres);
        if (tags.TryGetValue("building:levels", out object l) && LeadingNumber(l.ToString(), out float levels)) return Mathf.Max(3f, levels * 3.2f + 1f);
        string kind = tags.TryGetValue("building", out object k) ? k.ToString() : "";
        return kind == "house" || kind == "detached" || kind == "garage" || kind == "shed" ? 6f : 12f;
    }

    private static bool LeadingNumber(string text, out float value)
    {
        int end = 0;
        while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.')) end++;
        return float.TryParse(text.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0f;
    }

    private static double Number(object value) =>
        value is double d ? d : double.Parse(value.ToString(), CultureInfo.InvariantCulture);

    private static string Quote(string text)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in text)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < ' ') sb.Append(' ');
            else if (c > 127) sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    public static string FileName(string label)
    {
        var sb = new StringBuilder();
        foreach (char c in label.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString().Trim('_');
    }
}
