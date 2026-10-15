using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// A grabbable panel. Docked on the wrist display, grabbing it pulls out a world copy instead; in the world its close button removes it.
public class SpatialPanel : MonoBehaviour
{
    [Header("References")]
    [Tooltip("XRGrabInteractable on this panel.")]
    [SerializeField] private XRGrabInteractable grab;

    [Tooltip("Pinch to remove this panel; hidden while docked.")]
    [SerializeField] private XRSimpleInteractable closeButton;

    // runtime values
    private SpatialPanel _source;

    private void OnEnable()
    {
        grab.selectEntered.AddListener(OnGrabbed);
        closeButton.selectEntered.AddListener(OnClosed);
    }

    private void OnDisable()
    {
        grab.selectEntered.RemoveListener(OnGrabbed);
        closeButton.selectEntered.RemoveListener(OnClosed);
    }

    // Makes this a wrist copy of the given panel prefab.
    public void Dock(SpatialPanel source)
    {
        _source = source;
        closeButton.gameObject.SetActive(false);
    }

    // A docked panel hands the grab to a new world copy, which lines its grip up with the hand.
    private void OnGrabbed(SelectEnterEventArgs args)
    {
        if (!_source) return;
        var panel = Instantiate(_source, transform.position, transform.rotation);
        args.manager.SelectExit(args.interactorObject, grab);
        args.manager.SelectEnter(args.interactorObject, panel.grab);
    }

    private void OnClosed(SelectEnterEventArgs args) => Destroy(gameObject);
}
