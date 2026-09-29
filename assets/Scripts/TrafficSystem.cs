using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// City life: cars driving in their lanes and pedestrians on the sidewalks,
/// all following the real road network. Density grows with the mission level.
/// </summary>
public sealed class TrafficSystem : MonoBehaviour
{
    [SerializeField] public RoadNetwork network;
    [SerializeField] public int carCount = 260;
    [SerializeField] public int pedestrianCount = 450;

    private static readonly Color[] CarColors =
    {
        new Color(0.95f, 0.78f, 0.1f),  // taxi yellow
        new Color(0.95f, 0.78f, 0.1f),
        new Color(0.08f, 0.08f, 0.09f),
        new Color(0.85f, 0.85f, 0.87f),
        new Color(0.55f, 0.57f, 0.6f),
        new Color(0.55f, 0.06f, 0.06f),
        new Color(0.08f, 0.15f, 0.4f),
        new Color(0.9f, 0.9f, 0.92f),
    };

    private static readonly Color[] ClothColors =
    {
        new Color(0.1f, 0.1f, 0.12f), new Color(0.2f, 0.25f, 0.45f), new Color(0.6f, 0.1f, 0.1f),
        new Color(0.85f, 0.85f, 0.8f), new Color(0.35f, 0.3f, 0.2f), new Color(0.15f, 0.4f, 0.25f),
    };

    private static readonly Color[] SkinColors =
    {
        new Color(0.36f, 0.22f, 0.14f), new Color(0.55f, 0.38f, 0.26f), new Color(0.8f, 0.62f, 0.5f),
        new Color(0.95f, 0.8f, 0.68f), new Color(0.25f, 0.16f, 0.1f),
    };

    private sealed class Agent
    {
        public Transform Transform;
        public RoadWalker Walker;
        public float Speed;
        public bool IsCar;
        public float Phase;
        public Transform[] Legs;
    }

    private readonly List<Agent> agents = new List<Agent>();
    private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
    private Material glass, tyre, headlight, taillight;

    private void Start()
    {
        if (network == null || network.RoadCount == 0) return;
        Shader shader = Shader.Find("Standard");
        glass = new Material(shader) { color = new Color(0.05f, 0.08f, 0.12f) };
        glass.SetFloat("_Glossiness", 0.95f);
        tyre = new Material(shader) { color = new Color(0.04f, 0.04f, 0.04f) };
        headlight = Emissive(new Color(1f, 0.95f, 0.8f));
        taillight = Emissive(new Color(0.9f, 0.05f, 0.05f));

        int level = PlayerPrefs.GetInt("skybound_level", 1);
        float density = Mathf.Lerp(0.6f, 1.3f, (level - 1) / 5f);
        for (int i = 0; i < carCount * density; i++) SpawnCar();
        for (int i = 0; i < pedestrianCount * density; i++) SpawnPedestrian();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        foreach (Agent agent in agents)
        {
            Vector3 target = agent.Walker.Target(network);
            Vector3 position = agent.Transform.position;
            Vector3 toTarget = target - position;
            toTarget.y = 0f;

            if (toTarget.magnitude < (agent.IsCar ? 2.5f : 0.8f))
            {
                agent.Walker.Advance(network);
                continue;
            }

            Vector3 step = toTarget.normalized * agent.Speed * dt;
            agent.Transform.position = position + step;
            Quaternion look = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            agent.Transform.rotation = Quaternion.Slerp(agent.Transform.rotation, look, (agent.IsCar ? 4f : 8f) * dt);

            if (!agent.IsCar && agent.Legs != null)
            {
                agent.Phase += dt * agent.Speed * 4f;
                float swing = Mathf.Sin(agent.Phase) * 30f;
                agent.Legs[0].localRotation = Quaternion.Euler(swing, 0f, 0f);
                agent.Legs[1].localRotation = Quaternion.Euler(-swing, 0f, 0f);
            }
        }
    }

    // ------------------------------------------------------------------- spawn

    private void SpawnCar()
    {
        Vector3 start = network.RandomPoint(out int road, out int index);
        float lane = network.Width(road) * 0.25f * (network.driveOnLeft ? -1f : 1f);
        var walker = new RoadWalker { Road = road, Index = index, Forward = Random.value > 0.5f, Offset = lane };
        walker.Advance(network);

        Transform car = BuildCar(CarColors[Random.Range(0, CarColors.Length)]);
        car.position = start + Vector3.up * 0.05f;
        agents.Add(new Agent { Transform = car, Walker = walker, Speed = Random.Range(7f, 13f), IsCar = true });
    }

    private void SpawnPedestrian()
    {
        Vector3 start = network.RandomPoint(out int road, out int index);
        float side = Random.value > 0.5f ? 1f : -1f;
        float offset = side * (network.Width(road) * 0.5f + Random.Range(1.2f, 3f));
        var walker = new RoadWalker { Road = road, Index = index, Forward = Random.value > 0.5f, Offset = offset };

        Transform person = BuildPerson();
        person.position = start + Vector3.right * offset;
        agents.Add(new Agent
        {
            Transform = person, Walker = walker, Speed = Random.Range(1.1f, 1.7f), Phase = Random.value * 6f,
            Legs = new[] { person.Find("LegL"), person.Find("LegR") },
        });
    }

    // ------------------------------------------------------------------ models

    private Transform BuildCar(Color paint)
    {
        var car = new GameObject("Car").transform;
        car.SetParent(transform, false);
        bool van = Random.value < 0.15f;
        float length = van ? 5.2f : 4.5f;

        Part(car, PrimitiveType.Cube, new Vector3(0f, 0.7f, 0f), new Vector3(1.85f, 0.7f, length), Mat(paint));
        Part(car, PrimitiveType.Cube, new Vector3(0f, van ? 1.45f : 1.3f, van ? 0.3f : -0.2f),
             new Vector3(1.7f, van ? 0.9f : 0.6f, van ? length - 0.8f : 2.4f), glass);
        Part(car, PrimitiveType.Cube, new Vector3(0f, van ? 1.92f : 1.62f, van ? 0.3f : -0.2f),
             new Vector3(1.68f, 0.05f, van ? length - 0.9f : 2.1f), Mat(paint)); // roof
        for (int i = 0; i < 4; i++)
        {
            float x = i % 2 == 0 ? -0.85f : 0.85f;
            float z = i < 2 ? length * 0.32f : -length * 0.32f;
            Transform wheel = Part(car, PrimitiveType.Cylinder, new Vector3(x, 0.35f, z), new Vector3(0.7f, 0.12f, 0.7f), tyre);
            wheel.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
        Part(car, PrimitiveType.Cube, new Vector3(-0.6f, 0.8f, length / 2f), new Vector3(0.35f, 0.15f, 0.05f), headlight);
        Part(car, PrimitiveType.Cube, new Vector3(0.6f, 0.8f, length / 2f), new Vector3(0.35f, 0.15f, 0.05f), headlight);
        Part(car, PrimitiveType.Cube, new Vector3(-0.65f, 0.85f, -length / 2f), new Vector3(0.3f, 0.12f, 0.05f), taillight);
        Part(car, PrimitiveType.Cube, new Vector3(0.65f, 0.85f, -length / 2f), new Vector3(0.3f, 0.12f, 0.05f), taillight);

        var collider = car.gameObject.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, 0.9f, 0f);
        collider.size = new Vector3(1.9f, 1.8f, length);
        var body = car.gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        return car;
    }

    private Transform BuildPerson()
    {
        var person = new GameObject("Pedestrian").transform;
        person.SetParent(transform, false);
        float height = Random.Range(0.9f, 1.1f);
        person.localScale = Vector3.one * height;
        Material shirt = Mat(ClothColors[Random.Range(0, ClothColors.Length)]);
        Material trousers = Mat(ClothColors[Random.Range(0, ClothColors.Length)]);
        Material skin = Mat(SkinColors[Random.Range(0, SkinColors.Length)]);

        Part(person, PrimitiveType.Capsule, new Vector3(0f, 1.25f, 0f), new Vector3(0.45f, 0.35f, 0.3f), shirt);
        Part(person, PrimitiveType.Sphere, new Vector3(0f, 1.72f, 0f), new Vector3(0.24f, 0.26f, 0.24f), skin);
        for (int i = 0; i < 2; i++)
        {
            // Legs pivot at the hip so they can swing while walking.
            var hip = new GameObject(i == 0 ? "LegL" : "LegR").transform;
            hip.SetParent(person, false);
            hip.localPosition = new Vector3(i == 0 ? -0.11f : 0.11f, 0.9f, 0f);
            Part(hip, PrimitiveType.Capsule, new Vector3(0f, -0.45f, 0f), new Vector3(0.16f, 0.45f, 0.16f), trousers);
        }
        return person;
    }

    private static Transform Part(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go.transform;
    }

    private Material Mat(Color color)
    {
        if (materials.TryGetValue(color, out Material m)) return m;
        m = new Material(Shader.Find("Standard")) { color = color, enableInstancing = true };
        m.SetFloat("_Glossiness", 0.6f);
        materials[color] = m;
        return m;
    }

    private static Material Emissive(Color color)
    {
        var m = new Material(Shader.Find("Standard")) { color = color };
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", color * 1.5f);
        return m;
    }
}
