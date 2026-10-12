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

    [Header("Settings")]
    [Tooltip("Seconds the hand takes to slide onto the lever and back.")]
    [SerializeField] private float blendTime = 0.08f;

    [Tooltip("Metres your real hand can pull away from the grab point before it lets go of the lever.")]
    [SerializeField] private float releaseDistance = 0.12f;

    // runtime values
    private XRLever _lever;
    private Vector3 _grabPointOnHandle;
    private Vector3 _offset;
    private Vector3[] _restPositions;

    private void Awake()
    {
        _restPositions = new Vector3[handVisuals.Length];
        for (int i = 0; i < handVisuals.Length; i++) _restPositions[i] = handVisuals[i].localPosition;
    }

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
        _offset = Vector3.zero;
        MoveVisuals();
    }

    private void LateUpdate()
    {
        if (_lever && Vector3.Distance(GrabPoint(), PinchPoint()) > releaseDistance) ReleaseLever();

        var target = _lever ? GrabPoint() - PinchPoint() : Vector3.zero;
        if (_offset == Vector3.zero && target == Vector3.zero) return;

        _offset = Vector3.Lerp(_offset, target, 1f - Mathf.Exp(-Time.deltaTime / blendTime));
        if (!_lever && _offset.sqrMagnitude < 1e-8f) _offset = Vector3.zero;
        MoveVisuals();
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (args.interactableObject is not XRLever lever) return;
        _lever = lever;
        _grabPointOnHandle = lever.handle.InverseTransformPoint(PinchPoint());
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (ReferenceEquals(args.interactableObject, _lever)) _lever = null;
    }

    // Ends the grab, so the lever snaps to its nearest end and the hand slides back.
    private void ReleaseLever()
    {
        interactor.interactionManager.SelectExit((IXRSelectInteractor)interactor, _lever);
        _lever = null;
    }

    private Vector3 GrabPoint() => _lever.handle.TransformPoint(_grabPointOnHandle);

    private Vector3 PinchPoint() => interactor.GetAttachTransform(_lever).position;

    private void MoveVisuals()
    {
        for (int i = 0; i < handVisuals.Length; i++)
            handVisuals[i].localPosition = _restPositions[i] + handVisuals[i].parent.InverseTransformVector(_offset);
    }
}
