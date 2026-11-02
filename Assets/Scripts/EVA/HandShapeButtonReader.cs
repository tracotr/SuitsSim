using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

// A button that's pressed while the hand holds a hand shape, e.g. pressing the thumb down from a thumbs up to teleport.
// Plug it into an interactor's input as an Object Reference. Runs before the interaction manager so a press is seen the same frame.
[DefaultExecutionOrder(-200)]
public class HandShapeButtonReader : MonoBehaviour, IXRInputButtonReader
{
    [Header("References")]
    [Tooltip("Hand tracking events of the hand to read.")]
    [SerializeField] private XRHandTrackingEvents handTrackingEvents;

    [Tooltip("Hand shape that counts as pressed.")]
    [SerializeField] private XRHandShape pressedShape;

    // runtime values
    private XRHandJointsUpdatedEventArgs _latest;
    private bool _pressed;
    private int _changedFrame = -1;

    private void OnEnable()
    {
        handTrackingEvents.jointsUpdated.AddListener(OnJointsUpdated);
    }

    private void OnDisable()
    {
        handTrackingEvents.jointsUpdated.RemoveListener(OnJointsUpdated);
        _latest = null;
        SetPressed(false);
    }

    private void OnJointsUpdated(XRHandJointsUpdatedEventArgs args) => _latest = args;

    private void Update()
    {
        bool tracked = _latest != null && _latest.subsystem.running && _latest.hand.isTracked;
        SetPressed(tracked && pressedShape.CheckConditions(_latest));
    }

    private void SetPressed(bool pressed)
    {
        if (pressed == _pressed) return;
        _pressed = pressed;
        _changedFrame = Time.frameCount;
    }

    public bool ReadIsPerformed() => _pressed;

    public bool ReadWasPerformedThisFrame() => _pressed && _changedFrame == Time.frameCount;

    public bool ReadWasCompletedThisFrame() => !_pressed && _changedFrame == Time.frameCount;

    public float ReadValue() => _pressed ? 1f : 0f;

    public bool TryReadValue(out float value)
    {
        value = ReadValue();
        return true;
    }
}
