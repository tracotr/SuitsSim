using UnityEngine;
using UnityEngine.XR.Hands;

// The one place that says which hand the user leads with; everything hand-specific asks this.
[DefaultExecutionOrder(-100)]
public class UserHandedness : MonoBehaviour
{
    [Header("Hands")]
    [Tooltip("The user's dominant hand. The other hand holds menus.")]
    [SerializeField] private Handedness dominant = Handedness.Right;

    // runtime values
    public static UserHandedness Instance { get; private set; }
    public Handedness Dominant => dominant;
    public Handedness OffHand => dominant == Handedness.Right ? Handedness.Left : Handedness.Right;

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
