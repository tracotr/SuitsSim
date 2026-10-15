using UnityEngine;
using UnityEngine.XR.Hands;

// Keeps this object only on the user's dominant hand; the rig has one copy per hand.
public class DominantHandOnly : MonoBehaviour
{
    [Header("Hand")]
    [Tooltip("Which hand this copy belongs to.")]
    [SerializeField] private Handedness hand = Handedness.Right;

    private void Awake()
    {
        gameObject.SetActive(UserHandedness.Instance.Dominant == hand);
    }
}
