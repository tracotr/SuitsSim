using System;
using SuitsSim.Core;
using UnityEngine;
using UnityEngine.Events;

namespace SuitsSim.Panels
{
    /// <summary>
    /// A push button that can be held, e.g. the task board's SFA, CSR or ALT buttons.
    /// Several LTV procedures depend on holding one button while moving a switch, so
    /// press and release are separate events.
    ///
    /// XR wiring on the same object's XRSimpleInteractable:
    ///   Select Entered -> PanelButton.Press,  Select Exited -> PanelButton.Release
    /// </summary>
    [AddComponentMenu("SUITS Sim/Panel Button")]
    public class PanelButton : MonoBehaviour, IPanelControl
    {
        public string controlId = "PDD.SFA";
        [Tooltip("Model the cap so its local +Z points out of the panel.")]
        public Transform cap;
        public float travel = 0.006f;
        public UnityEvent onPressed;
        public UnityEvent onReleased;

        public static event Action<PanelButton> AnyPressed;
        public static event Action<PanelButton> AnyReleased;

        public string ControlId => controlId;
        public bool IsHeld { get; private set; }
        public float HeldSeconds => IsHeld ? Time.time - pressedAt : 0f;

        Vector3 capRest;
        float pressedAt;

        void Awake()
        {
            if (cap != null) capRest = cap.localPosition;
        }

        void OnEnable() => PanelRegistry.Register(this);

        void OnDisable()
        {
            if (IsHeld) Release();
            PanelRegistry.Unregister(this);
        }

        /// <summary>Desktop click: DesktopRig calls Press on mouse down and Release on mouse up.</summary>
        public void Activate() => Press();

        public void Press()
        {
            if (IsHeld) return;
            IsHeld = true;
            pressedAt = Time.time;
            if (cap != null) cap.localPosition = capRest + Vector3.back * travel;
            Log("PRESS");
            onPressed?.Invoke();
            AnyPressed?.Invoke(this);
        }

        public void Release()
        {
            if (!IsHeld) return;
            IsHeld = false;
            if (cap != null) cap.localPosition = capRest;
            Log($"RELEASE {Time.time - pressedAt:F1}s");
            onReleased?.Invoke();
            AnyReleased?.Invoke(this);
        }

        void Log(string value)
        {
            var root = SimRoot.Instance;
            if (root != null) root.Log.Event("button", controlId, value);
        }
    }
}
