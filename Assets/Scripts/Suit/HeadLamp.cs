using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// A helmet-side lamp, keeps light at a fixed spot beside the head and pointing where the head looks. Grab it to switch it on or off.
public class HeadLamp : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Main Camera under the XR Origin.")]
    [SerializeField] private Transform head;

    [Tooltip("The lamp's light.")]
    [SerializeField] private Light lamp;

    [Tooltip("XRSimpleInteractable on this object; selecting it switches the lamp.")]
    [SerializeField] private XRSimpleInteractable interactable;

    [Header("Mount")]
    [Tooltip("Where the lamp sits relative to the head, in metres (x = right, y = up, z = forward).")]
    [SerializeField] private Vector3 offset = new(0.12f, 0.04f, 0.02f);

    [Tooltip("Extra tilt of the beam relative to where the head looks, in degrees (x = down).")]
    [SerializeField] private Vector3 aim = new(10f, 0f, 0f);

    private void OnEnable()
    {
        interactable.selectEntered.AddListener(ToggleLamp);
    }

    private void OnDisable()
    {
        interactable.selectEntered.RemoveListener(ToggleLamp);
    }

    private void Update()
    {
        transform.SetPositionAndRotation(head.TransformPoint(offset), head.rotation * Quaternion.Euler(aim));
    }

    private void ToggleLamp(SelectEnterEventArgs args) => lamp.enabled = !lamp.enabled;
}
