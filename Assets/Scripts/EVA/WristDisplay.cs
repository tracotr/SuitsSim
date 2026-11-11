using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Samples.GestureSample;

// Panels on the back of the off hand's wrist, shown while the wrist gesture is held.
public class WristDisplay : MonoBehaviour
{
    [Header("Panels")]
    [Tooltip("HandSide on this object, set to the off hand.")]
    [SerializeField] private HandSide handSide;

    [SerializeField] private Transform panels;
    [Tooltip("Turn around the wrist bone's Z in degrees for the left hand. The right hand uses the negative of this.")]
    [SerializeField] private float panelRotation = 80;

    [SerializeField] private XRHandTrackingEvents leftHand;
    [SerializeField] private XRHandTrackingEvents rightHand;

    [SerializeField] private StaticHandGesture wristGesture;

    [Header("Reaching")]
    [Tooltip("When the other hand is closer than this to the panels, in metres, they freeze in place so the wrist's tracking jitter can't move them.")]
    [SerializeField] private float freezeDistance = 0.2f;

    [Tooltip("How long the panels stay up after the gesture ends, in seconds, so a tracking dropout doesn't hide them.")]
    [SerializeField] private float hideDelay = 0.15f;

    // runtime values
    private Vector3 _panelsLocalPosition;
    private Quaternion _panelsLocalRotation;
    private bool _gestureHeld;
    private bool _frozen;
    private float _hideAt;

    private void Awake()
    {
        handSide.HandChanged += OnHandChanged;

        // The panels start hidden since the gesture controls them.
        panels.gameObject.SetActive(false);
        panels.GetLocalPositionAndRotation(out _panelsLocalPosition, out _panelsLocalRotation);
    }

    private void OnDestroy() => handSide.HandChanged -= OnHandChanged;

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

    // The hands are mirrored so the right hand turns the other way. The gesture only picks up its hand in OnEnable so it gets toggled.
    private void OnHandChanged(Handedness hand)
    {
        bool left = hand == Handedness.Left;
        if (_frozen) SetFrozen(false);
        _gestureHeld = false;
        panels.gameObject.SetActive(false);

        transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 0f, left ? panelRotation : -panelRotation));

        wristGesture.enabled = false;
        wristGesture.handTrackingEvents = left ? leftHand : rightHand;
        wristGesture.enabled = true;
    }

    // Freezes the panels while the other hand reaches for them, and only hides them once the gesture has been gone a moment.
    private void LateUpdate()
    {
        if (!panels.gameObject.activeSelf) return;

        bool reaching = Vector3.Distance(handSide.OtherHand.position, panels.position) < freezeDistance;
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
