using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Samples.GestureSample;

public class WristDisplay: MonoBehaviour
{
    [SerializeField] private Transform panels;
    [Tooltip("Turn around the wrist bone's Z, in degrees, for the left hand; the right hand uses the mirror (negative).")]
    [SerializeField] private float panelRotation = 80;

    [SerializeField] private XRHandTrackingEvents leftHand;
    [SerializeField] private XRHandTrackingEvents rightHand;

    [SerializeField] private Transform leftWrist;
    [SerializeField] private Transform rightWrist;

    [SerializeField] private StaticHandGesture wristGesture;

    private void Start()
    {
        // default set our panels off since they are controlled by our gesture.
        panels.gameObject.SetActive(false);

        // Determine which hand/wrist we want to use based on our users handedness
        bool left = UserHandedness.Instance.OffHand == Handedness.Left;
        var hand = left ? leftHand : rightHand;
        var wrist = left ? leftWrist : rightWrist;

        // Set our transform parent and local position to wrist, the hands are mirrored so the right hand turns the other way.
        transform.SetParent(wrist, false);
        transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 0f, left ? panelRotation : -panelRotation));

        // tell our wrist gesture to look at the specific hand.
        wristGesture.handTrackingEvents = hand;
        wristGesture.enabled = true;
    }
}
