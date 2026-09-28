using System;
using SuitsSim.Core;
using UnityEngine;
using UnityEngine.Events;

namespace SuitsSim.Panels
{
    /// <summary>
    /// A two-position switch on a virtual UIA, DCU or LTV task board. If it has a TSS
    /// command it writes to TSS exactly like the physical panel does; otherwise it is
    /// local state the scenario and repair tracker read (umbilical, task board toggles).
    ///
    /// Hook it to XR: add an XRSimpleInteractable to the same object and wire
    /// Select Entered -> PanelSwitch.Toggle. DesktopRig clicks work without that.
    /// </summary>
    [AddComponentMenu("SUITS Sim/Panel Switch")]
    public class PanelSwitch : MonoBehaviour, IPanelControl
    {
        [Tooltip("Stable id used by procedures and logs, e.g. UIA.DEPRESS, DCU.FAN, POPS.MPS")]
        public string controlId = "UIA.EMU1_POWER";
        public string onLabel = "ON";
        public string offLabel = "OFF";

        [Header("TSS")]
        public bool sendsToTss = true;
        public uint tssCommand;

        [Header("State")]
        [SerializeField] bool state;
        [Tooltip("Position the switch is reset to at scenario start (CAPCOM: UIA down, DCU back).")]
        public bool resetState;

        [Header("Visual")]
        public Transform lever;
        public Vector3 onLocalEuler = new Vector3(-30f, 0f, 0f);
        public Vector3 offLocalEuler = new Vector3(30f, 0f, 0f);
        public Renderer indicator;
        public Color onColor = new Color(0.2f, 0.9f, 0.3f);
        public Color offColor = new Color(0.25f, 0.25f, 0.25f);

        public UnityEvent<bool> onChanged;

        /// <summary>Any switch changed, with who did it (tester or reset).</summary>
        public static event Action<PanelSwitch, ChangeSource> AnyChanged;

        public enum ChangeSource { Tester, Reset }

        public string ControlId => controlId;
        public bool State => state;
        public string StateLabel => state ? onLabel : offLabel;

        MaterialPropertyBlock mpb;

        void OnEnable()
        {
            PanelRegistry.Register(this);
            ApplyVisual();
        }

        void OnDisable() => PanelRegistry.Unregister(this);

        public void Toggle() => Set(!state, ChangeSource.Tester);

        public void Activate() => Toggle();

        public void Set(bool value, ChangeSource source = ChangeSource.Tester)
        {
            if (value == state && source == ChangeSource.Tester) return;
            state = value;
            ApplyVisual();
            Push();

            var root = SimRoot.Instance;
            if (root != null && source == ChangeSource.Tester)
                root.Log.Event("switch", controlId, StateLabel);

            onChanged?.Invoke(state);
            AnyChanged?.Invoke(this, source);
        }

        public void ResetToDefault() => Set(resetState, ChangeSource.Reset);

        /// <summary>Re-sends the current position so TSS matches the virtual panel.</summary>
        public void Push()
        {
            var root = SimRoot.Instance;
            if (sendsToTss && root != null) root.Tss.Send(tssCommand, state);
        }

        void ApplyVisual()
        {
            if (lever != null) lever.localRotation = Quaternion.Euler(state ? onLocalEuler : offLocalEuler);
            if (indicator != null)
            {
                mpb ??= new MaterialPropertyBlock();
                indicator.GetPropertyBlock(mpb);
                var c = state ? onColor : offColor;
                mpb.SetColor("_BaseColor", c); // URP Lit
                mpb.SetColor("_Color", c);     // Built-in
                indicator.SetPropertyBlock(mpb);
            }
        }

        void OnValidate() => ApplyVisual();
    }
}
