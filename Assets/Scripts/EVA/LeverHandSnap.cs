using UnityEngine;
using UnityEngine.XR.Content.Interaction;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Keeps the drawn hand on a lever while it's grabbed, so it doesn't drift off as the lever swings.
public class LeverHandSnap : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Near-Far Interactor of this hand.")]
    [SerializeField] private NearFarInteractor interactor;

    [Tooltip("Hand visual roots of this hand (the Quest and Android XR visuals).")]
    [SerializeField] private Transform[] handVisuals;

    // runtime values
    private XRLever _lever;
    private Vector3 _grabPointOnHandle;

    private void OnEnable()
    {
        interactor.selectEntered.AddListener(OnSelectEntered);
        interactor.selectExited.AddListener(OnSelectExited);
    }

    private void OnDisable()
    {
        interactor.selectEntered.RemoveListener(OnSelectEntered);
        interactor.selectExited.RemoveListener(OnSelectExited);
        
        _lever = null;
        
        MoveVisuals(Vector3.zero);
    }

    private void LateUpdate()
    {
        // if we're holding nothing, do nothing
        if (!_lever) return;
        
        var pinchPoint = interactor.GetAttachTransform(_lever).position;
        var pinchOffset = _lever.handle.TransformPoint(_grabPointOnHandle) - pinchPoint;
        MoveVisuals(pinchOffset);
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (args.interactableObject is not XRLever lever) return;
        _lever = lever;
        _grabPointOnHandle = lever.handle.InverseTransformPoint(interactor.GetAttachTransform(lever).position);
    }
    
    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (args.interactableObject is not XRLever) return;
        
        _lever = null;
        MoveVisuals(Vector3.zero);
    }

    private void MoveVisuals(Vector3 offset)
    {
        foreach (var visual in handVisuals)
            visual.position = visual.parent.position + offset;
    }
}
