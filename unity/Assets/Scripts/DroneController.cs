using UnityEngine;

/// <summary>
/// Basic manual quadcopter controller for the first Unity migration slice.
/// Unity calls FixedUpdate at the physics rate, so forces stay stable across
/// different rendering frame rates.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class DroneController : MonoBehaviour
{
    [Header("Lift and movement")]
    [SerializeField] private float liftForce = 22f;
    [SerializeField] private float cruiseForce = 32f;      // ~90 km/h top speed
    [SerializeField] private float boostMultiplier = 1.8f; // hold Shift
    [SerializeField] private float turnRate = 60f; // degrees per second
    [SerializeField] private float brakingForce = 40f;
    [SerializeField] private float levelSpeed = 4f;

    [Header("Safety")]
    [SerializeField] private float minimumAltitude = 0.5f;
    [SerializeField] private Transform resetPoint;

    [SerializeField] private float crashImpactSpeed = 10f;

    [Header("Arduino joystick")]
    [Tooltip("Serial port such as COM12. Leave empty to find the Arduino automatically.")]
    [SerializeField] private string joystickPort = "";
    [SerializeField] private bool useJoystick = true;

    /// <summary>Raised with a reason when the drone crashes (hard impact, ocean).</summary>
    public event System.Action<string> Crashed;

    /// <summary>Raised after the drone is put back on its start point (R, joystick button, pause menu).</summary>
    public event System.Action WasReset;

    private Rigidbody body;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private ArduinoJoystick joystick;
    private System.Threading.Tasks.Task<ArduinoJoystick> joystickConnect;
    private bool grounded = true; // motors idle while resting on the ground
    private float lastLiftInput;
    private bool reconnectPending;

    /// <summary>PlayerPrefs key for the joystick port chosen in the pause menu ("" = auto).</summary>
    public const string PortPref = "skybound_joystick_port";

    public bool Grounded => grounded;

    /// <summary>One-line joystick state for the menus.</summary>
    public string JoystickStatus =>
        joystick != null && joystick.Connected ? $"Joystick connected on {joystick.PortName}"
        : joystickConnect != null ? "Searching for the Arduino joystick…"
        : "No joystick found (keyboard only)";

    /// <summary>Handling of the drone picked in the hangar.</summary>
    public void Configure(float lift, float cruise, float turn, float braking, float mass)
    {
        liftForce = lift;
        cruiseForce = cruise;
        turnRate = turn;
        brakingForce = braking;
        if (body == null) body = GetComponent<Rigidbody>();
        body.mass = mass;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        startPosition = transform.position;
        startRotation = transform.rotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void OnEnable()
    {
        // Search serial ports off the main thread so startup never freezes.
        if (useJoystick && joystick == null && joystickConnect == null) ConnectJoystick();
    }

    private void ConnectJoystick()
    {
        // The port picked in the pause menu wins over the Inspector field.
        string portName = PlayerPrefs.HasKey(PortPref) ? PlayerPrefs.GetString(PortPref) : joystickPort;
        joystickConnect = System.Threading.Tasks.Task.Run(() => ArduinoJoystick.Open(portName));
    }

    /// <summary>Drops the current joystick and searches again (after the port setting changes).</summary>
    public void ReconnectJoystick()
    {
        if (joystickConnect != null)
        {
            reconnectPending = true; // wait for the running search to release its port
            return;
        }
        joystick?.Dispose();
        joystick = null;
        ConnectJoystick();
    }

    private void OnDisable()
    {
        joystick?.Dispose();
        joystick = null;
        joystickConnect = null;
    }

    private void FixedUpdate()
    {
        // Arrows climb/descend, W flies forward, S brakes, A/D roll, Q/E turn.
        float liftInput = ReadAxis(KeyCode.UpArrow, KeyCode.DownArrow);
        float forwardInput = Input.GetKey(KeyCode.W) ? 1f : 0f;
        bool braking = Input.GetKey(KeyCode.S);
        float rollInput = ReadAxis(KeyCode.D, KeyCode.A);
        float yawInput = ReadAxis(KeyCode.E, KeyCode.Q);

        // Arduino sticks add to the keyboard, so either can fly the drone.
        if (joystick != null && joystick.Connected)
        {
            liftInput = Mathf.Clamp(liftInput + joystick.Lift, -1f, 1f);
            rollInput = Mathf.Clamp(rollInput + joystick.Roll, -1f, 1f);
            yawInput = Mathf.Clamp(yawInput + joystick.Yaw, -1f, 1f);
            forwardInput = Mathf.Max(forwardInput, joystick.Throttle);
            braking |= joystick.Throttle < -0.5f || joystick.Brake;
        }

        lastLiftInput = liftInput;
        if (liftInput > 0f) grounded = false;

        // Hover assist: cancel gravity so the drone holds altitude when no
        // climb/descend key is pressed. On the ground the motors idle, so
        // gravity keeps the drone sitting on the pad until you climb.
        if (!grounded) body.AddForce(-Physics.gravity * body.mass, ForceMode.Force);
        body.AddForce(Vector3.up * liftInput * liftForce, ForceMode.Force);

        // Real drones limit vertical speed (about 6 m/s up, 5 m/s down),
        // which also keeps normal landings below the crash threshold.
        Vector3 v = body.linearVelocity;
        v.y = Mathf.Clamp(v.y, -5f, 6f);
        body.linearVelocity = v;

        bool boost = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float forwardForce = cruiseForce * (boost ? boostMultiplier : 1f);
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        body.AddForce(flatForward * forwardInput * forwardForce, ForceMode.Force);

        Vector3 flatVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        if (braking)
        {
            // Slow down; once nearly stopped, S gently reverses.
            if (flatVelocity.magnitude > 0.5f)
                body.AddForce(-flatVelocity.normalized * brakingForce, ForceMode.Force);
            else
                body.AddForce(-flatForward * cruiseForce * 0.3f, ForceMode.Force);
        }

        // Sideways drift from rolling.
        Vector3 flatRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        body.AddForce(flatRight * rollInput * cruiseForce * 0.5f, ForceMode.Force);

        // Visual tilt toward the direction of travel, then self-level.
        float targetPitch = (forwardInput - (braking ? 1f : 0f)) * 15f;
        float targetRoll = -rollInput * 20f;
        Quaternion level = Quaternion.Euler(targetPitch, transform.eulerAngles.y, targetRoll);
        body.MoveRotation(Quaternion.Slerp(body.rotation, level, levelSpeed * Time.fixedDeltaTime));
        // Direct turn rate: turns only while Q/E is held and stops on release.
        // (Torque made the light drone spin far on a single tap.)
        Vector3 spin = body.angularVelocity;
        spin.y = yawInput * turnRate * Mathf.Deg2Rad;
        body.angularVelocity = spin;

        if (transform.position.y < minimumAltitude)
        {
            Vector3 safePosition = transform.position;
            safePosition.y = minimumAltitude;
            transform.position = safePosition;
            body.linearVelocity = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
        }
    }

    private void Update()
    {
        if (joystickConnect != null && joystickConnect.IsCompleted)
        {
            joystick = joystickConnect.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? joystickConnect.Result : null;
            joystickConnect = null;
            if (reconnectPending)
            {
                reconnectPending = false;
                ReconnectJoystick();
            }
            else if (joystick == null) Debug.Log("No Arduino joystick found; using keyboard.");
        }

        if (Input.GetKeyDown(KeyCode.R) || (joystick != null && joystick.ConsumeResetPress()))
        {
            ResetDrone();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.relativeVelocity.magnitude > crashImpactSpeed) Crash("The drone crashed.");
        CheckTouchdown(collision);
    }

    private void OnCollisionStay(Collision collision) => CheckTouchdown(collision);

    // Resting on something below with no climb input counts as landed.
    private void CheckTouchdown(Collision collision)
    {
        if (lastLiftInput > 0f) return;
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.7f)
            {
                grounded = true;
                return;
            }
        }
    }

    public void Crash(string reason) => Crashed?.Invoke(reason);

    public void ResetDrone()
    {
        transform.position = resetPoint != null ? resetPoint.position : startPosition;
        transform.rotation = resetPoint != null ? resetPoint.rotation : startRotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        grounded = true;
        WasReset?.Invoke();
    }

    private static float ReadAxis(KeyCode positive, KeyCode negative)
    {
        float value = 0f;
        if (Input.GetKey(positive)) value += 1f;
        if (Input.GetKey(negative)) value -= 1f;
        return value;
    }
}
