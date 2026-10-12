using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Keeps the DCU on the player's chest, and lets them grab it to adjust its height and depth.
[RequireComponent(typeof(Rigidbody))]
public class ChestMount : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to XR Origin main camera")]
    [SerializeField] private Transform head;

    [Tooltip("Kinematic Rigidbody on this object.")]
    [SerializeField] private Rigidbody rb;

    [Tooltip("XRSimpleInteractable on the DCU body that is grabbed to adjust it.")]
    [SerializeField] private XRSimpleInteractable chestMountInteractable;
    
    [Header("Position")]
    [Tooltip("Offset of chest mount from main camera, should be in front of camera, and downwards")]
    [SerializeField] private Vector3 offset = new(0f, -0.45f, 0.3f);

    [Tooltip("Degrees the top of the chest mount faces")]
    [SerializeField] private float tilt = -20f;

    [Tooltip("How fast the chest mount follows the camera")]
    [SerializeField] private float turnSpeed = 4f;

    [Header("Adjusting")]
    [Tooltip("Lowest / highest offset.y of chest mount below the eyes.")]
    [SerializeField] private Vector2 heightRange = new(-0.7f, -0.25f);
    
    [Tooltip("Closest / farthest offset.z of chest mount in front of the eyes.")]
    [SerializeField] private Vector2 depthRange = new(-0.15f, 0.2f);
    
    // runtime values
    private Vector3 _facing;
    private Transform _hand;
    private Vector3 _handStart, _offsetStart;
    private bool Grabbed => _hand;

    private void OnEnable()
    {
        _facing = Flat();
        
        chestMountInteractable.selectEntered.AddListener(OnGrab);
        chestMountInteractable.selectExited.AddListener(OnRelease);
    }

    private void OnDisable()
    {
        chestMountInteractable.selectEntered.RemoveListener(OnGrab);
        chestMountInteractable.selectExited.RemoveListener(OnRelease);
    }

    // Head forward flattened onto the ground, using head up when looking straight down.
    private Vector3 Flat()
    {
        var f = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (f.sqrMagnitude < 0.1f) f = Vector3.ProjectOnPlane(head.up, Vector3.up);
        return f.normalized;
    }
    
    private void OnGrab(SelectEnterEventArgs args)
    {
        if (args.interactorObject is XRPokeInteractor) return;
        _hand = args.interactorObject.GetAttachTransform(chestMountInteractable);
        _handStart = HandLocal();
        _offsetStart = offset;
    }
    
    private void OnRelease(SelectExitEventArgs args)
    {
        if (!_hand || args.interactorObject.GetAttachTransform(chestMountInteractable) != _hand) return;
        _hand = null;
    }

    // Hand position relative to the head in the DCU's facing, so walking or turning while holding it doesn't count.
    private Vector3 HandLocal()
    {
        return Quaternion.Inverse(Quaternion.LookRotation(_facing, Vector3.up)) * (_hand.position - head.position);
    } 

    private void FixedUpdate()
    {
        HandleGrabbed();
        FollowHead();
    }
    
    private void HandleGrabbed()
    {
        if (!Grabbed) return;
        
        var moved = HandLocal() - _handStart;
        offset.y = Mathf.Clamp(_offsetStart.y + moved.y, heightRange.x, heightRange.y);
        offset.z = Mathf.Clamp(_offsetStart.z + moved.z, depthRange.x, depthRange.y);
    }
 
    private void FollowHead()
    {
        _facing = Vector3.Slerp(_facing, Flat(), turnSpeed * Time.fixedDeltaTime);
        var yaw = Quaternion.LookRotation(_facing, Vector3.up);
        rb.MovePosition(head.position + yaw * offset);
        rb.MoveRotation(yaw * Quaternion.Euler(tilt, 0f, 0f));
    }
}
