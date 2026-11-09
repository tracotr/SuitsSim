using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Samples.GestureSample;

public class WristDisplay: MonoBehaviour
{
    [SerializeField] private Transform panels;
    [Tooltip("Turn around the wrist bone's Z in degrees for the left hand. The right hand uses the negative of this.")]
    [SerializeField] private float panelRotation = 80;

    [SerializeField] private XRHandTrackingEvents leftHand;
    [SerializeField] private XRHandTrackingEvents rightHand;

    [SerializeField] private Transform leftWrist;
    [SerializeField] private Transform rightWrist;

    [SerializeField] private StaticHandGesture wristGesture;

    [Header("Reaching")]
    [Tooltip("When the other hand is closer than this to the panels, in metres, they freeze in place so the wrist's tracking jitter can't move them.")]
    [SerializeField] private float freezeDistance = 0.2f;

    [Tooltip("How long the panels stay up after the gesture ends, in seconds, so a tracking dropout doesn't hide them.")]
    [SerializeField] private float hideDelay = 0.5f;

    // runtime values
    private Transform _reachingWrist;
    private Vector3 _panelsLocalPosition;
    private Quaternion _panelsLocalRotation;
    private bool _gestureHeld;
    private bool _frozen;
    private float _hideAt;

    private void OnEnable()
    {
        wristGesture.gesturePerformed.AddListener(OnGesturePerformed);
        wristGesture.gestureEnded.AddListener(OnGestureEnded);
    }

    private void OnDisable()
    {
        wristGesture.gesturePerformed.RemoveListener(OnGesturePerformed);
        wristGesture.gestureEnded.RemoveListener(OnGestureEnded);
    }

    private void Start()
    {
        // default set our panels off since they are controlled by our gesture.
        panels.gameObject.SetActive(false);
        panels.GetLocalPositionAndRotation(out _panelsLocalPosition, out _panelsLocalRotation);

        // Determine which hand/wrist we want to use based on our users handedness
        bool left = UserHandedness.Instance.OffHand == Handedness.Left;
        var hand = left ? leftHand : rightHand;
        var wrist = left ? leftWrist : rightWrist;
        _reachingWrist = left ? rightWrist : leftWrist;

        // Set our transform parent and local position to wrist, the hands are mirrored so the right hand turns the other way.
        transform.SetParent(wrist, false);
        transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 0f, left ? panelRotation : -panelRotation));

        // tell our wrist gesture to look at the specific hand.
        wristGesture.handTrackingEvents = hand;
        wristGesture.enabled = true;
    }

    // Freezes the panels while the other hand reaches for them, and only hides them once the gesture has been gone a moment.
    private void LateUpdate()
    {
        if (!panels.gameObject.activeSelf) return;

        bool reaching = Vector3.Distance(_reachingWrist.position, panels.position) < freezeDistance;
        if (reaching != _frozen) SetFrozen(reaching);

        if (!_gestureHeld && !_frozen && Time.time >= _hideAt) panels.gameObject.SetActive(false);
    }

    private void OnGesturePerformed()
    {
        _gestureHeld = true;
        panels.gameObject.SetActive(true);
    }

    private void OnGestureEnded()
    {
        _gestureHeld = false;
        _hideAt = Time.time + hideDelay;
    }

    // Frozen panels leave the wrist and hold their spot in the world, then snap back onto the wrist.
    private void SetFrozen(bool frozen)
    {
        _frozen = frozen;
        if (frozen)
        {
            panels.SetParent(null, true);
            return;
        }
        panels.SetParent(transform, false);
        panels.SetLocalPositionAndRotation(_panelsLocalPosition, _panelsLocalRotation);
    }
}
