using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Turns a panel to face the eyes square on from any direction, upright to the head, so it always reads straight. Disable the component to keep a fixed angle.
public class BillboardPanel : MonoBehaviour
{
    [Header("References")]
    [Tooltip("XRGrabInteractable on this panel. While it's held the hand controls the rotation.")]
    [SerializeField] private XRGrabInteractable grab;

    [Header("Facing")]
    [Tooltip("How quickly the panel turns to face the head. Higher is snappier.")]
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
        var away = transform.position - _head.position;
        if (away.sqrMagnitude < 0.0001f) return;
        var facing = Quaternion.LookRotation(away, _head.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, facing, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
    }
}
