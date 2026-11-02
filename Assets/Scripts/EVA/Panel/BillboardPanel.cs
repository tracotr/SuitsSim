using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Turns a panel left/right around its own up axis to face the head, keeping its tilt. Disable the component to keep a fixed angle.
public class BillboardPanel : MonoBehaviour
{
    [Header("References")]
    [Tooltip("XRGrabInteractable on this panel; while it's held the hand controls the rotation.")]
    [SerializeField] private XRGrabInteractable grab;

    [Header("Facing")]
    [Tooltip("How quickly the panel turns to face the head; higher is snappier.")]
    [SerializeField] private float turnSpeed = 8f;

    // runtime values
    private Transform _head;

    private void Awake()
    {
        _head = Camera.main.transform;
    }

    private void LateUpdate()
    {
        if (grab.isSelected) return;
        var up = transform.up;
        var away = Vector3.ProjectOnPlane(transform.position - _head.position, up);
        if (away.sqrMagnitude < 0.0001f) return;
        var facing = Quaternion.LookRotation(away, up);
        transform.rotation = Quaternion.Slerp(transform.rotation, facing, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
    }
}
