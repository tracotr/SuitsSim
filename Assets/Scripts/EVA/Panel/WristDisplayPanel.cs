using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Base for every display panel. It shows TSS data and redraws each sim tick. Subclass it and fill in Refresh for each kind of data.
// On the wrist, where World Panel is set, grabbing it pulls out a world copy instead of moving it. World copies close with their X.
public abstract class WristDisplayPanel : MonoBehaviour
{
    [Header("Panel")]
    [Tooltip("XRGrabInteractable on this panel.")]
    [SerializeField] private XRGrabInteractable grab;

    [Tooltip("Set on the wrist copy only: the panel prefab to pull out when grabbed. Leave empty on the prefab so world copies just move.")]
    [SerializeField] private WristDisplayPanel worldPanel;

    [Tooltip("The X that closes a pulled-out copy. It is hidden on the wrist.")]
    [SerializeField] private XRSimpleInteractable closeButton;

    protected virtual void Awake()
    {
        grab.trackPosition = grab.trackRotation = grab.trackScale = false;
        grab.unparentTransformOnGrab = false;
        closeButton.gameObject.SetActive(false);
    }

    protected virtual void OnEnable()
    {
        TssSim.Instance.Ticked += Refresh;
        grab.selectEntered.AddListener(PullOut);
        closeButton.selectEntered.AddListener(Close);
        Refresh();
    }

    // TssSim may already be gone when play mode stops.
    protected virtual void OnDisable()
    {
        if (TssSim.Instance) TssSim.Instance.Ticked -= Refresh;
        grab.selectEntered.RemoveListener(PullOut);
        closeButton.selectEntered.RemoveListener(Close);
    }

    // A pulled-out copy moves freely and doesn't pull out copies of its own.
    private void MakeWorldCopy()
    {
        worldPanel = null;
        grab.trackPosition = grab.trackRotation = grab.trackScale = true;
        grab.unparentTransformOnGrab = true;
        closeButton.gameObject.SetActive(true);
    }

    private void Close(SelectEnterEventArgs args) => Destroy(gameObject);

    // Redraws the panel from the latest data.
    protected abstract void Refresh();

    // Spawns the world copy in this panel's place and moves the grab onto it, held where it was pinched.
    private void PullOut(SelectEnterEventArgs args)
    {
        if (!worldPanel) return;
        var copy = Instantiate(worldPanel, transform.position, transform.rotation);
        copy.transform.localScale = transform.lossyScale;
        copy.MakeWorldCopy();
        args.manager.SelectExit(args.interactorObject, grab);
        args.manager.SelectEnter(args.interactorObject, copy.grab);
    }
}
