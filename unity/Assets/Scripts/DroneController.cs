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
    [SerializeField] private float forwardForce = 14f;
    [SerializeField] private float tiltTorque = 8f;
    [SerializeField] private float yawTorque = 5f;

    [Header("Safety")]
    [SerializeField] private float minimumAltitude = 0.5f;
    [SerializeField] private Transform resetPoint;

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
        float liftInput = ReadAxis(KeyCode.W, KeyCode.S);
        float pitchInput = ReadAxis(KeyCode.UpArrow, KeyCode.DownArrow);
        float rollInput = ReadAxis(KeyCode.D, KeyCode.A);
        float yawInput = ReadAxis(KeyCode.E, KeyCode.Q);

        // W/S controls lift for a drone. The old aircraft throttle mapping
        // will be replaced by a configurable assisted-flight mode later.
        body.AddForce(transform.up * liftInput * liftForce, ForceMode.Force);
        body.AddForce(transform.forward * pitchInput * forwardForce, ForceMode.Force);
        body.AddRelativeTorque(Vector3.right * pitchInput * tiltTorque, ForceMode.Force);
        body.AddRelativeTorque(Vector3.forward * -rollInput * tiltTorque, ForceMode.Force);
        body.AddRelativeTorque(Vector3.up * yawInput * yawTorque, ForceMode.Force);

        if (transform.position.y < minimumAltitude)
        {
            Vector3 safePosition = transform.position;
            safePosition.y = minimumAltitude;
            transform.position = safePosition;
            body.velocity = new Vector3(body.velocity.x, 0f, body.velocity.z);
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetDrone();
        }
    }

    public void ResetDrone()
    {
        transform.position = resetPoint != null ? resetPoint.position : startPosition;
        transform.rotation = resetPoint != null ? resetPoint.rotation : startRotation;
        body.velocity = Vector3.zero;
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
