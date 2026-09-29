using UnityEngine;

/// <summary>
/// Stabilized third-person camera. It follows the drone while keeping the
/// horizon aligned to world-up instead of inheriting the drone's roll.
/// </summary>
public sealed class DroneCamera : MonoBehaviour
{
    [SerializeField] public Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 7f, -18f);
    [SerializeField] private float positionSmoothing = 7f;
    [SerializeField] private float lookSmoothing = 10f;

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = target.TransformPoint(offset);
        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            positionSmoothing * Time.deltaTime
        );

        Vector3 lookPoint = target.position + Vector3.up * 1.5f;
        Quaternion desiredRotation = Quaternion.LookRotation(
            lookPoint - transform.position,
            Vector3.up
        );
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRotation,
            lookSmoothing * Time.deltaTime
        );
    }
}
