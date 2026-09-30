using UnityEngine;

// Keeps the DCU strapped to the chest: follows the head's position and facing (yaw only),
// ignoring pitch/roll so looking down at it doesn't move it.
[RequireComponent(typeof(Rigidbody))]
public class ChestMount : MonoBehaviour
{
    public Transform head;                          // XR Origin > Camera Offset > Main Camera
    public Vector3 offset = new(0f, -0.45f, 0.3f);  // metres below / in front of the eyes
    public float tilt = 35f;                        // degrees the top faces up toward you
    public float turnSpeed = 4f;                    // how fast it swings to your facing

    Rigidbody _rb;
    Vector3 _facing;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _facing = Flat();
    }

    // Head forward flattened onto the ground. Looking straight down, forward is useless, so use head.up.
    Vector3 Flat()
    {
        var f = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (f.sqrMagnitude < 0.1f) f = Vector3.ProjectOnPlane(head.up, Vector3.up);
        return f.normalized;
    }

    void FixedUpdate()
    {
        _facing = Vector3.Slerp(_facing, Flat(), turnSpeed * Time.fixedDeltaTime);
        var yaw = Quaternion.LookRotation(_facing, Vector3.up);
        _rb.MovePosition(head.position + yaw * offset);
        _rb.MoveRotation(yaw * Quaternion.Euler(tilt, 0f, 0f));
    }
}