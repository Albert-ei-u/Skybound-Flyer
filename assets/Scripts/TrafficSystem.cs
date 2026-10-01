using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// City life: cars driving in their lanes and pedestrians on the sidewalks,
/// all following the real road network. Density grows with the mission level.
/// People walk with swinging arms and legs, some stand around in groups
/// chatting, and anyone the drone buzzes at head height runs away.
/// Real human models (Assets/models/people, e.g. Mixamo FBX with a walk
/// animation) replace the built-in bodies when the scene builder finds them.
/// </summary>
public sealed class TrafficSystem : MonoBehaviour
{
    [SerializeField] public RoadNetwork network;
    [SerializeField] public int carCount = 260;
    [SerializeField] public int pedestrianCount = 450;
    [Tooltip("Optional real human models; the built-in bodies are used when empty.")]
    [SerializeField] public GameObject[] personModels = new GameObject[0];
    [Tooltip("Walk animation controller for each person model (same order, may be null).")]
    [SerializeField] public RuntimeAnimatorController[] personAnimations = new RuntimeAnimatorController[0];

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

    private static readonly Color[] HairColors =
    {
        new Color(0.05f, 0.04f, 0.03f), new Color(0.25f, 0.15f, 0.08f), new Color(0.6f, 0.45f, 0.25f),
        new Color(0.85f, 0.75f, 0.5f), new Color(0.55f, 0.55f, 0.55f),
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
        public Transform[] Legs, Arms;
        public bool Standing;
        public float Panic;       // seconds left running from the drone
        public Animator Animator;
    }

    private readonly List<Agent> agents = new List<Agent>();
    private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
    private Material glass, tyre, headlight, taillight;
    private Transform drone;

    private void Start()
    {
        if (network == null || network.RoadCount == 0) return;
        Shader shader = Shader.Find("Standard");
        glass = new Material(shader) { color = new Color(0.05f, 0.08f, 0.12f) };
        glass.SetFloat("_Glossiness", 0.95f);
        tyre = new Material(shader) { color = new Color(0.04f, 0.04f, 0.04f) };
        headlight = Emissive(new Color(1f, 0.95f, 0.8f));
        taillight = Emissive(new Color(0.9f, 0.05f, 0.05f));

        var controller = FindFirstObjectByType<DroneController>();
        if (controller != null) drone = controller.transform;

        int level = PlayerPrefs.GetInt("skybound_level", 1);
        float density = Mathf.Lerp(0.6f, 1.3f, (level - 1) / 5f);
        for (int i = 0; i < carCount * density; i++) SpawnCar();
        for (int i = 0; i < pedestrianCount * density; i++)
        {
            // About one in six people stand in small groups instead of walking.
            if (Random.value < 0.16f) SpawnGroup();
            else SpawnPedestrian();
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        Vector3 dronePos = drone != null ? drone.position : Vector3.one * 1e6f;
        foreach (Agent agent in agents)
        {
            if (!agent.IsCar && UpdatePanic(agent, dronePos, dt)) continue;
            if (agent.Standing)
            {
                // Idle: talk with the hands.
                agent.Phase += dt;
                if (agent.Arms != null) agent.Arms[0].localRotation = Quaternion.Euler(Mathf.Sin(agent.Phase * 1.7f) * 12f - 10f, 0f, 0f);
                continue;
            }

            Vector3 target = agent.Walker.Target(network);
            Vector3 position = agent.Transform.position;
            Vector3 toTarget = target - position;
            toTarget.y = 0f;

            if (toTarget.magnitude < (agent.IsCar ? 2.5f : 0.8f))
            {
                agent.Walker.Advance(network);
                continue;
            }

            float speed = agent.Speed * (agent.Panic > 0f ? 3f : 1f);
            Vector3 step = toTarget.normalized * speed * dt;
            agent.Transform.position = position + step;
            Quaternion look = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            agent.Transform.rotation = Quaternion.Slerp(agent.Transform.rotation, look, (agent.IsCar ? 4f : 8f) * dt);

            if (!agent.IsCar) Animate(agent, speed, dt);
        }
    }

    private static void Animate(Agent agent, float speed, float dt)
    {
        if (agent.Animator != null)
        {
            agent.Animator.speed = speed / 1.4f;
            return;
        }
        if (agent.Legs == null) return;
        agent.Phase += dt * speed * 4f;
        float swing = Mathf.Sin(agent.Phase) * (agent.Panic > 0f ? 45f : 30f);
        agent.Legs[0].localRotation = Quaternion.Euler(swing, 0f, 0f);
        agent.Legs[1].localRotation = Quaternion.Euler(-swing, 0f, 0f);
        // Arms swing opposite to the legs; running pumps them harder.
        float arm = swing * (agent.Panic > 0f ? 1.3f : 0.8f);
        agent.Arms[0].localRotation = Quaternion.Euler(-arm, 0f, 0f);
        agent.Arms[1].localRotation = Quaternion.Euler(arm, 0f, 0f);
    }

    /// <summary>
    /// A drone buzzing low overhead makes people run. Walkers sprint along the
    /// sidewalk; groups scatter. Returns true when this agent was moved here.
    /// </summary>
    private static bool UpdatePanic(Agent agent, Vector3 dronePos, float dt)
    {
        Vector3 away = agent.Transform.position - dronePos;
        bool scared = dronePos.y < agent.Transform.position.y + 6f && new Vector2(away.x, away.z).sqrMagnitude < 64f;
        if (scared) agent.Panic = 3f;
        if (agent.Panic <= 0f) return false;

        agent.Panic -= dt;
        if (!agent.Standing) return false;

        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
        agent.Transform.position += away.normalized * 4f * dt;
        agent.Transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        Animate(agent, 4f, dt);
        if (agent.Panic <= 0f && agent.Animator != null) agent.Animator.speed = 0f; // settle where they ended up
        return true;
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

        Agent agent = NewPerson();
        agent.Walker = walker;
        agent.Speed = Random.Range(1.1f, 1.7f);
        agent.Transform.position = start + Vector3.right * offset;
        agents.Add(agent);
    }

    /// <summary>Two to four people standing in a circle on the sidewalk, chatting.</summary>
    private void SpawnGroup()
    {
        Vector3 start = network.RandomPoint(out int road, out int _);
        float side = Random.value > 0.5f ? 1f : -1f;
        Vector3 centre = start + Vector3.right * side * (network.Width(road) * 0.5f + 2.5f);
        int count = Random.Range(2, 5);
        for (int i = 0; i < count; i++)
        {
            float a = i * Mathf.PI * 2f / count + Random.value * 0.4f;
            Agent agent = NewPerson();
            agent.Standing = true;
            agent.Transform.position = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.8f;
            agent.Transform.rotation = Quaternion.LookRotation(centre - agent.Transform.position, Vector3.up);
            if (agent.Animator != null) agent.Animator.speed = 0f; // hold a still pose
            agents.Add(agent);
        }
    }

    private Agent NewPerson()
    {
        var agent = new Agent { Phase = Random.value * 6f };
        if (personModels != null && personModels.Length > 0)
        {
            int i = Random.Range(0, personModels.Length);
            RuntimeAnimatorController walk = personAnimations != null && i < personAnimations.Length ? personAnimations[i] : null;
            agent.Transform = ModelPerson(personModels[i], walk, out agent.Animator);
        }
        else
        {
            agent.Transform = BuildPerson();
            agent.Legs = new[] { agent.Transform.Find("LegL"), agent.Transform.Find("LegR") };
            agent.Arms = new[] { agent.Transform.Find("ArmL"), agent.Transform.Find("ArmR") };
        }
        return agent;
    }

    private Transform ModelPerson(GameObject model, RuntimeAnimatorController walk, out Animator animator)
    {
        var person = new GameObject("Pedestrian").transform;
        person.SetParent(transform, false);
        GameObject body = Instantiate(model, person, false);
        foreach (Collider c in body.GetComponentsInChildren<Collider>()) Destroy(c);

        // Scale every model to a real human height (1.6-1.9 m).
        Renderer[] renderers = body.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            if (b.size.y > 0.01f) body.transform.localScale *= Random.Range(1.6f, 1.9f) / b.size.y;
        }

        animator = body.GetComponentInChildren<Animator>();
        if (animator == null && walk != null) animator = body.AddComponent<Animator>();
        if (animator != null)
        {
            if (walk != null) animator.runtimeAnimatorController = walk;
            animator.applyRootMotion = false; // the traffic system moves people along the sidewalk
            animator.cullingMode = AnimatorCullingMode.CullCompletely;
        }
        return person;
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

        Material hair = Mat(HairColors[Random.Range(0, HairColors.Length)]);
        Material shoes = Mat(new Color(0.07f, 0.07f, 0.07f));
        float shoulders = Random.value > 0.5f ? 0.46f : 0.38f; // body type

        // Torso, hips and neck.
        Part(person, PrimitiveType.Capsule, new Vector3(0f, 1.28f, 0f), new Vector3(shoulders, 0.32f, 0.26f), shirt);
        Part(person, PrimitiveType.Capsule, new Vector3(0f, 0.98f, 0f), new Vector3(shoulders * 0.85f, 0.14f, 0.24f), trousers);
        Part(person, PrimitiveType.Cylinder, new Vector3(0f, 1.6f, 0f), new Vector3(0.1f, 0.05f, 0.1f), skin);

        // Head with short hair, long hair or a cap.
        Part(person, PrimitiveType.Sphere, new Vector3(0f, 1.74f, 0f), new Vector3(0.21f, 0.25f, 0.23f), skin);
        float style = Random.value;
        if (style < 0.2f)
        {
            Material cap = Mat(ClothColors[Random.Range(0, ClothColors.Length)]);
            Part(person, PrimitiveType.Sphere, new Vector3(0f, 1.82f, -0.01f), new Vector3(0.23f, 0.12f, 0.25f), cap);
            Part(person, PrimitiveType.Cube, new Vector3(0f, 1.8f, 0.13f), new Vector3(0.2f, 0.015f, 0.1f), cap);
        }
        else
        {
            Part(person, PrimitiveType.Sphere, new Vector3(0f, 1.8f, -0.02f), new Vector3(0.23f, 0.17f, 0.24f), hair);
            if (style > 0.65f) Part(person, PrimitiveType.Capsule, new Vector3(0f, 1.62f, -0.1f), new Vector3(0.2f, 0.14f, 0.08f), hair);
        }
        Material eye = Mat(new Color(0.05f, 0.05f, 0.05f));
        Part(person, PrimitiveType.Sphere, new Vector3(-0.05f, 1.76f, 0.105f), Vector3.one * 0.03f, eye);
        Part(person, PrimitiveType.Sphere, new Vector3(0.05f, 1.76f, 0.105f), Vector3.one * 0.03f, eye);

        // Legs pivot at the hip and arms at the shoulder, so both swing while walking.
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            var hip = new GameObject(i == 0 ? "LegL" : "LegR").transform;
            hip.SetParent(person, false);
            hip.localPosition = new Vector3(side * 0.1f, 0.92f, 0f);
            Part(hip, PrimitiveType.Capsule, new Vector3(0f, -0.44f, 0f), new Vector3(0.15f, 0.44f, 0.15f), trousers);
            Part(hip, PrimitiveType.Cube, new Vector3(0f, -0.88f, 0.05f), new Vector3(0.12f, 0.08f, 0.26f), shoes);

            var shoulder = new GameObject(i == 0 ? "ArmL" : "ArmR").transform;
            shoulder.SetParent(person, false);
            shoulder.localPosition = new Vector3(side * (shoulders * 0.5f + 0.05f), 1.47f, 0f);
            Part(shoulder, PrimitiveType.Capsule, new Vector3(0f, -0.28f, 0f), new Vector3(0.11f, 0.28f, 0.11f), shirt);
            Part(shoulder, PrimitiveType.Sphere, new Vector3(0f, -0.6f, 0f), Vector3.one * 0.09f, skin);
        }

        // Some carry a backpack or a shopping bag.
        float extra = Random.value;
        if (extra < 0.2f)
            Part(person, PrimitiveType.Cube, new Vector3(0f, 1.3f, -0.2f), new Vector3(0.3f, 0.38f, 0.14f), Mat(ClothColors[Random.Range(0, ClothColors.Length)]));
        else if (extra < 0.3f)
            Part(person.Find("ArmR"), PrimitiveType.Cube, new Vector3(0f, -0.72f, 0f), new Vector3(0.08f, 0.26f, 0.22f), Mat(new Color(0.9f, 0.88f, 0.8f)));
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
