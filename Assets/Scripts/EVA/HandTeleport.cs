using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.XRHandLocomotion;

// Keeps the one hand teleport interactor on the user's dominant hand. Everything in it that reads a specific hand is pointed at the new one.
public class HandTeleport : MonoBehaviour
{
    [Header("References")]
    [Tooltip("HandSide on this object, set to the dominant hand.")]
    [SerializeField] private HandSide handSide;

    [SerializeField] private XRHandTrackingEvents trackingEvents;
    [SerializeField] private XRRayInteractor rayInteractor;
    [SerializeField] private TrackedPoseDriver poseDriver;
    [SerializeField] private TeleportHandGesture gesture;

    [Header("Left Hand")]
    [Tooltip("The left hand's near-far interactor, turned off while teleporting.")]
    [SerializeField] private NearFarInteractor leftNearFar;
    [SerializeField] private InputActionReference leftPosition;
    [SerializeField] private InputActionReference leftRotation;
    [SerializeField] private InputActionReference leftTrackingState;

    [Header("Right Hand")]
    [Tooltip("The right hand's near-far interactor, turned off while teleporting.")]
    [SerializeField] private NearFarInteractor rightNearFar;
    [SerializeField] private InputActionReference rightPosition;
    [SerializeField] private InputActionReference rightRotation;
    [SerializeField] private InputActionReference rightTrackingState;

    // Awake to OnDestroy since parenting under an untracked hand deactivates this before HandChanged fires.
    private void Awake() => handSide.HandChanged += OnHandChanged;
    private void OnDestroy() => handSide.HandChanged -= OnHandChanged;

    // The gesture and tracking events are toggled so they restart on the new hand. A teleport half done on the old hand is dropped.
    private void OnHandChanged(Handedness hand)
    {
        bool left = hand == Handedness.Left;

        gesture.StopAllCoroutines();
        gesture.enabled = false;
        if (gesture.nearFarInteractor) gesture.nearFarInteractor.enabled = true;
        gesture.nearFarInteractor = left ? leftNearFar : rightNearFar;

        trackingEvents.enabled = false;
        trackingEvents.handedness = hand;
        trackingEvents.enabled = true;

        rayInteractor.handedness = left ? InteractorHandedness.Left : InteractorHandedness.Right;
        poseDriver.positionInput = new InputActionProperty(left ? leftPosition : rightPosition);
        poseDriver.rotationInput = new InputActionProperty(left ? leftRotation : rightRotation);
        poseDriver.trackingStateInput = new InputActionProperty(left ? leftTrackingState : rightTrackingState);

        gesture.enabled = true;
    }
}
