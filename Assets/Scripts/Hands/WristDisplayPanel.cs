using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Base for every display panel: shows TSS data and redraws each sim tick; subclass it and fill in Refresh for each kind of data.
// On the wrist (World Panel set), grabbing it pulls out a world copy instead of moving it.
public abstract class WristDisplayPanel : MonoBehaviour
{
    [Header("Panel")]
    [Tooltip("XRGrabInteractable on this panel.")]
    [SerializeField] private XRGrabInteractable grab;

    [Tooltip("Set on the wrist copy only: the panel prefab to pull out when grabbed. Leave empty on the prefab so world copies just move.")]
    [SerializeField] private WristDisplayPanel worldPanel;

    // The wrist copy stays on the wrist; its grab only hands off to a new world copy.
    protected virtual void Awake()
    {
        if (!worldPanel) return;
        grab.trackPosition = grab.trackRotation = grab.trackScale = false;
        grab.unparentTransformOnGrab = false;
    }

    protected virtual void OnEnable()
    {
        TssSim.Instance.Ticked += Refresh;
        grab.selectEntered.AddListener(PullOut);
        Refresh();
    }

    protected virtual void OnDisable()
    {
        TssSim.Instance.Ticked -= Refresh;
        grab.selectEntered.RemoveListener(PullOut);
    }

    // Redraws the panel from the latest data.
    protected abstract void Refresh();

    // Spawns the world copy in this panel's place and moves the grab onto it, held where it was pinched.
    private void PullOut(SelectEnterEventArgs args)
    {
        if (!worldPanel) return;
        var copy = Instantiate(worldPanel, transform.position, transform.rotation);
        copy.transform.localScale = transform.lossyScale;
        args.manager.SelectExit(args.interactorObject, grab);
        args.manager.SelectEnter(args.interactorObject, copy.grab);
    }
}
