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

    /// <summary>Raised when the drone hits something faster than crashImpactSpeed.</summary>
    public event System.Action Crashed;

    private Rigidbody body;
    private Vector3 startPosition;
    private Quaternion startRotation;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        startPosition = transform.position;
        startRotation = transform.rotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void FixedUpdate()
    {
        // Arrows climb/descend, W flies forward, S brakes, A/D roll, Q/E turn.
        float liftInput = ReadAxis(KeyCode.UpArrow, KeyCode.DownArrow);
        float forwardInput = Input.GetKey(KeyCode.W) ? 1f : 0f;
        bool braking = Input.GetKey(KeyCode.S);
        float rollInput = ReadAxis(KeyCode.D, KeyCode.A);
        float yawInput = ReadAxis(KeyCode.E, KeyCode.Q);

        // Hover assist: cancel gravity so the drone holds altitude when no
        // climb/descend key is pressed.
        body.AddForce(-Physics.gravity * body.mass, ForceMode.Force);
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
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetDrone();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.relativeVelocity.magnitude > crashImpactSpeed) Crashed?.Invoke();
    }

    public void ResetDrone()
    {
        transform.position = resetPoint != null ? resetPoint.position : startPosition;
        transform.rotation = resetPoint != null ? resetPoint.rotation : startRotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    private static float ReadAxis(KeyCode positive, KeyCode negative)
    {
        float value = 0f;
        if (Input.GetKey(positive)) value += 1f;
        if (Input.GetKey(negative)) value -= 1f;
        return value;
    }
}
