using System.Collections.Generic;
using UnityEngine;

namespace SuitsSim.Panels
{
    /// <summary>Anything the tester can operate on a panel.</summary>
    public interface IPanelControl
    {
        string ControlId { get; }
        /// <summary>Toggle a switch or press a button. Used by DesktopRig clicks.</summary>
        void Activate();
    }

    /// <summary>Lookup of live panel controls by id, for scenario conditions and procedures.</summary>
    public static class PanelRegistry
    {
        static readonly Dictionary<string, IPanelControl> controls = new Dictionary<string, IPanelControl>();

        public static IEnumerable<PanelSwitch> Switches
        {
            get { foreach (var c in controls.Values) if (c is PanelSwitch s) yield return s; }
        }

        public static void Register(IPanelControl control)
        {
            if (controls.TryGetValue(control.ControlId, out var existing) && !ReferenceEquals(existing, control))
                Debug.LogWarning($"Duplicate panel control id '{control.ControlId}'; the later one wins.", control as Object);
            controls[control.ControlId] = control;
        }

        public static void Unregister(IPanelControl control)
        {
            if (controls.TryGetValue(control.ControlId, out var existing) && ReferenceEquals(existing, control))
                controls.Remove(control.ControlId);
        }

        public static PanelSwitch Switch(string id) =>
            controls.TryGetValue(id, out var c) ? c as PanelSwitch : null;

        /// <summary>True if the switch exists and is in the given position.</summary>
        public static bool Is(string id, bool on)
        {
            var s = Switch(id);
            return s != null && s.State == on;
        }

        public static void ResetAll()
        {
            foreach (var s in new List<PanelSwitch>(Switches)) s.ResetToDefault();
        }

        public static void PushAll()
        {
            foreach (var s in Switches) s.Push();
        }
    }
}
