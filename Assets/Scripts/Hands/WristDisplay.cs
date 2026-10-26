using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Samples.GestureSample;

public class WristDisplay: MonoBehaviour
{
    [SerializeField] private XRHandTrackingEvents leftHand;
    [SerializeField] private XRHandTrackingEvents rightHand;
    
    [SerializeField] private Transform leftWrist;
    [SerializeField] private Transform rightWrist;
    
    [SerializeField] private StaticHandGesture wristGesture;
    
    private void Start()
    {
        // Determine which hand/wrist we want to use based on our users handedness
        var hand = UserHandedness.Instance.OffHand == Handedness.Left ? leftHand : rightHand;
        var wrist = UserHandedness.Instance.OffHand == Handedness.Left ? leftWrist : rightWrist;
        
        // Set our transform parent and local position to wrist
        transform.SetParent(wrist, false);
        transform.localPosition = Vector3.zero;
        
        // tell our wrist gesture to look at the specific hand.
        wristGesture.handTrackingEvents = hand;
        wristGesture.enabled = true;
    }
}
