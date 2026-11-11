using System;
using UnityEngine;
using UnityEngine.XR.Hands;

// Parents this object under the user's dominant or off hand and moves it across whenever UserHandedness changes.
// Scripts that read a specific hand listen to HandChanged from Awake to OnDestroy, because the parenting can deactivate them before it fires.
public class HandSide : MonoBehaviour
{
    [Header("Hand")]
    [Tooltip("Follow the off hand instead of the dominant hand.")]
    [SerializeField] private bool offHand;

    [Tooltip("What this parents under on the left hand.")]
    [SerializeField] private Transform left;

    [Tooltip("What this parents under on the right hand.")]
    [SerializeField] private Transform right;

    public event Action<Handedness> HandChanged;

    // runtime values
    public Handedness Hand { get; private set; }
    public Transform OtherHand { get; private set; }

    // Subscribed for the object's whole life since parenting under an untracked hand deactivates it, and it still has to move across.
    private void Awake() => UserHandedness.Instance.Changed += Attach;

    // UserHandedness may already be gone when play mode stops.
    private void OnDestroy()
    {
        if (UserHandedness.Instance) UserHandedness.Instance.Changed -= Attach;
    }

    // First attach waits for Start so every listener has subscribed in Awake.
    private void Start() => Attach();

    private void Attach()
    {
        Hand = offHand ? UserHandedness.Instance.OffHand : UserHandedness.Instance.Dominant;
        bool isLeft = Hand == Handedness.Left;
        transform.SetParent(isLeft ? left : right, false);
        OtherHand = isLeft ? right : left;
        HandChanged?.Invoke(Hand);
    }
}
