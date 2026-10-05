using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Keeps the DCU strapped to the chest: follows the head's position and facing (yaw only),
// ignoring pitch/roll so looking down at it doesn't move it.
// Grab the DCU body (pinch / grip) and move your hand up/down or forward/back to refit it.
[RequireComponent(typeof(Rigidbody))]
public class ChestMount : MonoBehaviour
{
    public Transform head;                          // XR Origin > Camera Offset > Main Camera
    public Vector3 offset = new(0f, -0.45f, 0.3f);  // metres below / in front of the eyes
    public float tilt = 35f;                        // degrees the top faces up toward you
    public float turnSpeed = 4f;                    // how fast it swings to your facing

    [Header("Refit")]
    [Tooltip("XRSimpleInteractable on the DCU body (not the toggles).")]
    public XRSimpleInteractable grabHandle;
    [Tooltip("Lowest / highest offset.y in metres below the eyes.")]
    public Vector2 heightRange = new(-0.7f, -0.25f);
    [Tooltip("Closest / farthest offset.z in metres in front of the eyes.")]
    public Vector2 depthRange = new(-0.15f, 0.2f);

    private Rigidbody _rb;
    private Vector3 _facing;
    private Transform _hand;
    private Vector3 _handStart, _offsetStart;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _facing = Flat();

        if (!grabHandle) return;
        grabHandle.selectEntered.AddListener(OnGrab);
        grabHandle.selectExited.AddListener(OnRelease);
    }

    // Head forward flattened onto the ground. Looking straight down, forward is useless, so use head.up.
    private Vector3 Flat()
    {
        var f = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (f.sqrMagnitude < 0.1f) f = Vector3.ProjectOnPlane(head.up, Vector3.up);
        return f.normalized;
    }

    // Hand relative to the head in the DCU's facing (yaw) frame, so walking, crouching or turning
    // while holding it doesn't count: y = up/down, z = forward/back.
    private Vector3 HandLocal() => Quaternion.Inverse(Quaternion.LookRotation(_facing, Vector3.up)) * (_hand.position - head.position);

    private void OnGrab(SelectEnterEventArgs args)
    {
        if (args.interactorObject is XRPokeInteractor) return; // touching it with a finger isn't a grab
        _hand = args.interactorObject.GetAttachTransform(grabHandle);
        _handStart = HandLocal();
        _offsetStart = offset;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        if (!_hand || args.interactorObject.GetAttachTransform(grabHandle) != _hand) return;
        _hand = null;
    }

    private void FixedUpdate()
    {
        if (_hand)
        {
            var moved = HandLocal() - _handStart;
            offset.y = Mathf.Clamp(_offsetStart.y + moved.y, heightRange.x, heightRange.y);
            offset.z = Mathf.Clamp(_offsetStart.z + moved.z, depthRange.x, depthRange.y);
        }

        _facing = Vector3.Slerp(_facing, Flat(), turnSpeed * Time.fixedDeltaTime);
        var yaw = Quaternion.LookRotation(_facing, Vector3.up);
        _rb.MovePosition(head.position + yaw * offset);
        _rb.MoveRotation(yaw * Quaternion.Euler(tilt, 0f, 0f));
    }
}
