using UnityEngine;

// Keeps the DCU strapped to the chest. It sits below and in front of the head and only turns once the head looks well away,
// so glancing around while using the switches doesn't move it. Keep it under Camera Offset so teleports and snap turns carry it along.
public class DCUChestMount : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Main Camera under the XR Origin.")]
    [SerializeField] private Transform head;

    [Header("Mount")]
    [Tooltip("Where the DCU sits from the head, in metres, facing the chest's way (y = up, z = forward).")]
    [SerializeField] private Vector3 offset = new(0f, -0.35f, 0.25f);

    [Tooltip("How far the head can turn left or right, in degrees, before the chest turns to follow.")]
    [SerializeField] private float deadZone = 20f;

    [Tooltip("How quickly the chest turns to catch up with the head. Higher is snappier.")]
    [SerializeField] private float turnSpeed = 4f;

    [Tooltip("How quickly the DCU follows the head's position. Higher is snappier.")]
    [SerializeField] private float followSpeed = 10f;
    
    [Tooltip("The offset rotation of the dcu")]
    [SerializeField] private float dcuRotationOffset = -90f;

    // runtime values
    private float _yaw;
    private bool _turning;

    private void OnEnable()
    {
        _yaw = HeadYaw();
        transform.SetLocalPositionAndRotation(head.localPosition + Quaternion.Euler(0f, _yaw, 0f) * offset, Quaternion.Euler(0f, _yaw, 0f));
    }

    private void Update()
    {
        float headYaw = HeadYaw() + dcuRotationOffset;
        // if we're past our deadzone, let the dcu mount turn
        if (Mathf.Abs(Mathf.DeltaAngle(_yaw, headYaw)) > deadZone) 
            _turning = true;
        
        // while we're turning, lerp the angle between where we are looking and where the mount is by our turn speed
        if (_turning)
        {
            _yaw = Mathf.LerpAngle(_yaw, headYaw, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
            if (Mathf.Abs(Mathf.DeltaAngle(_yaw, headYaw)) < 1f) 
                _turning = false;
        }
        
        // Set our new facing and target angles to our dcu chest mount so it actually movoes
        var facing = Quaternion.Euler(0f, _yaw, 0f);
        var target = head.localPosition + facing * offset;
        transform.localPosition = Vector3.Lerp(transform.localPosition, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
        transform.localRotation = facing;
    }

    // The head's left/right heading
    private float HeadYaw()
    {
        var forward = head.localRotation * Vector3.forward;
        return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }
}
