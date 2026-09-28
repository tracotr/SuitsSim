using SuitsSim.Panels;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace SuitsSim.Desktop
{
    /// <summary>
    /// Lets you develop and demo without a headset: WASD to walk, hold right mouse to
    /// look, left click to flip switches and hold buttons. Disables itself when an XR
    /// headset is active, so it can stay in the scene permanently.
    /// Put it on the XR Origin (the object that moves), with <see cref="head"/> = Main Camera.
    /// </summary>
    [AddComponentMenu("SUITS Sim/Desktop Rig")]
    public class DesktopRig : MonoBehaviour
    {
        [SerializeField] Transform head;
        [SerializeField] float walkSpeed = 1.4f;   // brisk EVA walking pace, m/s
        [SerializeField] float runMultiplier = 3f;
        [SerializeField] float lookSensitivity = 0.12f;
        [SerializeField] float eyeHeight = 1.7f;
        [SerializeField] LayerMask groundMask = ~0;
        [SerializeField] float reach = 3f;

        float pitch;
        PanelButton heldButton;

        void Start()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (XRSettings.isDeviceActive) enabled = false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null || head == null) return;

            if (mouse.rightButton.isPressed)
            {
                var d = mouse.delta.ReadValue() * lookSensitivity;
                transform.Rotate(0f, d.x, 0f, Space.World);
                pitch = Mathf.Clamp(pitch - d.y, -80f, 80f);
                head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            var input = new Vector2(
                (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0),
                (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
            if (input.sqrMagnitude > 0f)
            {
                var speed = walkSpeed * (kb.leftShiftKey.isPressed ? runMultiplier : 1f);
                var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                var right = Vector3.Cross(Vector3.up, fwd);
                transform.position += (fwd * input.y + right * input.x).normalized * speed * Time.deltaTime;
            }
            StickToGround();

            if (mouse.leftButton.wasPressedThisFrame) Click(mouse.position.ReadValue());
            if (mouse.leftButton.wasReleasedThisFrame && heldButton != null)
            {
                heldButton.Release();
                heldButton = null;
            }
        }

        void Click(Vector2 screen)
        {
            var cam = head.GetComponent<Camera>();
            if (cam == null) return;
            if (!Physics.Raycast(cam.ScreenPointToRay(screen), out var hit, reach)) return;

            var control = hit.collider.GetComponentInParent<IPanelControl>();
            if (control is PanelButton button)
            {
                button.Press();
                heldButton = button;
            }
            else control?.Activate();
        }

        void StickToGround()
        {
            // The rig origin sits on the floor; the camera is eyeHeight above it.
            head.localPosition = new Vector3(0f, eyeHeight, 0f);
            var from = transform.position + Vector3.up * 50f;
            if (Physics.Raycast(from, Vector3.down, out var hit, 200f, groundMask, QueryTriggerInteraction.Ignore))
                transform.position = new Vector3(transform.position.x, hit.point.y, transform.position.z);
        }
    }
}
