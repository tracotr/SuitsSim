using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// The close button on a world panel; pinch the X to remove it. The wrist copy hides its X.
public class WorldDisplayPanel : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Pinch to remove this panel.")]
    [SerializeField] private XRSimpleInteractable closeButton;

    private void OnEnable()
    {
        closeButton.selectEntered.AddListener(Close);
    }

    private void OnDisable()
    {
        closeButton.selectEntered.RemoveListener(Close);
    }

    private void Close(SelectEnterEventArgs args) => Destroy(gameObject);
}
