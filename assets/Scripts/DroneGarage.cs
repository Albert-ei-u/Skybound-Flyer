using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The hangar: several drones to swap between, each with its own look and
/// handling. Imported models (children named Model_Carbon / Model_Skybound,
/// added by the scene builder) are used when present; the others are built
/// from simple parts. The choice is saved between sessions.
/// </summary>
public sealed class DroneGarage : MonoBehaviour
{
    [SerializeField] public DroneController drone;

    public sealed class Spec
    {
        public string Name, Description, ModelChild;
        public float Lift, Cruise, Turn, Braking, Mass, Size;
        public int Arms;
        public Color Paint;
    }

    public static readonly Spec[] Catalog =
    {
        new Spec { Name = "CARBON X4", ModelChild = "Model_Carbon", Arms = 4, Paint = new Color(0.12f, 0.12f, 0.13f),
            Description = "Balanced carbon-fibre quad. Good at everything.",
            Lift = 22f, Cruise = 32f, Turn = 60f, Braking = 40f, Mass = 1.5f, Size = 1.2f },
        new Spec { Name = "SKYBOUND S1", ModelChild = "Model_Skybound", Arms = 4, Paint = new Color(0.9f, 0.9f, 0.92f),
            Description = "Light camera drone. Climbs fast, turns smoothly.",
            Lift = 28f, Cruise = 30f, Turn = 75f, Braking = 45f, Mass = 1.1f, Size = 1.1f },
        new Spec { Name = "RACER FPV", Arms = 4, Paint = new Color(0.85f, 0.12f, 0.1f),
            Description = "Tiny racing quad. Very fast and twitchy - for the Skyline Race.",
            Lift = 30f, Cruise = 50f, Turn = 110f, Braking = 55f, Mass = 0.8f, Size = 0.8f },
        new Spec { Name = "PHANTOM PRO", Arms = 4, Paint = new Color(0.95f, 0.95f, 0.95f),
            Description = "Steady survey drone with a gimbal camera. Easy to land.",
            Lift = 20f, Cruise = 26f, Turn = 50f, Braking = 60f, Mass = 1.8f, Size = 1.3f },
        new Spec { Name = "HEX LIFTER", Arms = 6, Paint = new Color(0.95f, 0.7f, 0.1f),
            Description = "Six-rotor cargo hauler. Slow but rock solid in wind.",
            Lift = 18f, Cruise = 22f, Turn = 42f, Braking = 50f, Mass = 3f, Size = 1.9f },
    };

    public const string SelectionPref = "skybound_drone";

    public int Selected { get; private set; }

    private readonly List<GameObject> models = new List<GameObject>();
    private readonly List<Transform> props = new List<Transform>();
    private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

    private void Start()
    {
        if (drone == null) drone = FindFirstObjectByType<DroneController>();
        if (drone == null) return;
        for (int i = 0; i < Catalog.Length; i++)
        {
            Transform child = Catalog[i].ModelChild != null ? drone.transform.Find(Catalog[i].ModelChild) : null;
            models.Add(child != null ? child.gameObject : BuildModel(Catalog[i]));
        }
        Select(Mathf.Clamp(PlayerPrefs.GetInt(SelectionPref, 0), 0, Catalog.Length - 1));
    }

    public void Select(int index)
    {
        if (drone == null) return;
        Selected = index;
        PlayerPrefs.SetInt(SelectionPref, index);
        PlayerPrefs.Save();

        Spec spec = Catalog[index];
        for (int i = 0; i < models.Count; i++) models[i].SetActive(i == index);
        drone.Configure(spec.Lift, spec.Cruise, spec.Turn, spec.Braking, spec.Mass);
        var box = drone.GetComponent<BoxCollider>();
        if (box != null)
        {
            // Fit the collider to the model's height so legs and cargo rest on the pad.
            Bounds bounds = new Bounds(drone.transform.position, Vector3.zero);
            foreach (Renderer r in models[index].GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            box.center = new Vector3(0f, drone.transform.InverseTransformPoint(bounds.center).y, 0f);
            box.size = new Vector3(spec.Size, Mathf.Max(0.3f, bounds.size.y), spec.Size);
        }

        props.Clear();
        foreach (Transform t in models[index].GetComponentsInChildren<Transform>())
            if (t.name == "Prop") props.Add(t);
    }

    private void Update()
    {
        // Rotors spin up in flight and idle on the ground.
        if (drone == null) return;
        float rpm = drone.Grounded ? 300f : 2200f;
        for (int i = 0; i < props.Count; i++)
            props[i].Rotate(0f, (i % 2 == 0 ? 1f : -1f) * rpm * Time.deltaTime, 0f, Space.Self);
    }

    // ------------------------------------------------------------ models

    private GameObject BuildModel(Spec spec)
    {
        var root = new GameObject($"Model_{spec.Name}");
        root.transform.SetParent(drone.transform, false);
        float s = spec.Size;
        Material paint = Mat(spec.Paint);
        Material dark = Mat(new Color(0.08f, 0.08f, 0.09f));
        Material blade = Mat(new Color(0.15f, 0.15f, 0.17f, 0.9f));
        Material ledFront = Glow(new Color(0.2f, 1f, 0.4f));
        Material ledBack = Glow(new Color(1f, 0.15f, 0.1f));

        // Body and canopy.
        Part(root.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(s * 0.32f, s * 0.12f, s * 0.42f), paint);
        Part(root.transform, PrimitiveType.Sphere, new Vector3(0f, s * 0.06f, s * 0.05f), new Vector3(s * 0.28f, s * 0.12f, s * 0.34f), paint);
        if (spec.Name == "PHANTOM PRO")
        {
            // Gimbal camera under the nose.
            Part(root.transform, PrimitiveType.Cube, new Vector3(0f, -s * 0.1f, s * 0.14f), new Vector3(s * 0.05f, s * 0.08f, s * 0.05f), dark);
            Part(root.transform, PrimitiveType.Sphere, new Vector3(0f, -s * 0.16f, s * 0.16f), Vector3.one * s * 0.1f, dark);
        }
        if (spec.Name == "HEX LIFTER")
        {
            // Cargo box slung underneath.
            Part(root.transform, PrimitiveType.Cube, new Vector3(0f, -s * 0.14f, 0f), new Vector3(s * 0.3f, s * 0.14f, s * 0.3f), Mat(new Color(0.55f, 0.4f, 0.25f)));
        }

        for (int i = 0; i < spec.Arms; i++)
        {
            // Arms at 45° for quads (X frame), even spacing for hexes.
            float angle = (spec.Arms == 4 ? 45f : 0f) + i * 360f / spec.Arms;
            Quaternion rot = Quaternion.Euler(0f, angle, 0f);
            Vector3 tip = rot * new Vector3(0f, 0f, s * 0.5f);

            Transform arm = Part(root.transform, PrimitiveType.Cube, tip * 0.5f, new Vector3(s * 0.05f, s * 0.04f, s * 0.5f), dark);
            arm.localRotation = rot;
            Part(root.transform, PrimitiveType.Cylinder, tip + Vector3.up * s * 0.03f, new Vector3(s * 0.09f, s * 0.04f, s * 0.09f), dark); // motor

            var prop = new GameObject("Prop").transform;
            prop.SetParent(root.transform, false);
            prop.localPosition = tip + Vector3.up * s * 0.08f;
            Part(prop, PrimitiveType.Cube, Vector3.zero, new Vector3(s * 0.38f, s * 0.008f, s * 0.05f), blade);
            Part(prop, PrimitiveType.Cube, Vector3.zero, new Vector3(s * 0.05f, s * 0.008f, s * 0.38f), blade);

            // Navigation lights: green at the front, red at the back.
            bool front = tip.z > 0.01f;
            Part(root.transform, PrimitiveType.Sphere, tip - Vector3.up * s * 0.02f, Vector3.one * s * 0.035f, front ? ledFront : ledBack);
        }

        // Landing legs.
        for (int side = -1; side <= 1; side += 2)
            Part(root.transform, PrimitiveType.Cube, new Vector3(side * s * 0.16f, -s * 0.1f, 0f), new Vector3(s * 0.02f, s * 0.02f, s * 0.36f), dark);

        root.SetActive(false);
        return root;
    }

    private static Transform Part(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go.transform;
    }

    private Material Mat(Color color)
    {
        if (materials.TryGetValue(color, out Material m)) return m;
        m = new Material(Shader.Find("Standard")) { color = color };
        m.SetFloat("_Glossiness", 0.55f);
        materials[color] = m;
        return m;
    }

    private static Material Glow(Color color)
    {
        var m = new Material(Shader.Find("Standard")) { color = color };
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", color * 3f);
        return m;
    }
}
