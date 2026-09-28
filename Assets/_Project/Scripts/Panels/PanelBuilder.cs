using SuitsSim.Tss;
using UnityEngine;

namespace SuitsSim.Panels
{
    /// <summary>
    /// Generates a placeholder UIA or DCU: one PanelSwitch per TSS control, laid out in a
    /// grid of cubes with labels. Run "Build switches" from the context menu, then swap the
    /// cubes for real models whenever you like; the ids and commands stay.
    ///
    /// DCU true/false meanings come from the TSS2026 simulation code (data.c), which is what
    /// actually drives telemetry. Note BATT: TSS treats batt.lu = true as UMBILICAL (the UIA
    /// only counts as connected when EMU1 power is on and lu is true), the opposite of the
    /// README's table.
    /// </summary>
    [AddComponentMenu("SUITS Sim/Panel Builder")]
    public class PanelBuilder : MonoBehaviour
    {
        public enum Kind { Uia, Dcu }

        struct Def
        {
            public string id, on, off;
            public uint cmd;
            public Def(string id, uint cmd, string on, string off) { this.id = id; this.cmd = cmd; this.on = on; this.off = off; }
        }

        static readonly Def[] Uia =
        {
            new Def("UIA.EMU1_POWER", TssCommand.UiaEva1Power, "ON", "OFF"),
            new Def("UIA.EV1_SUPPLY", TssCommand.UiaEva1WaterSupply, "OPEN", "CLOSED"),
            new Def("UIA.EV1_WASTE", TssCommand.UiaEva1WaterWaste, "OPEN", "CLOSED"),
            new Def("UIA.EV1_OXYGEN", TssCommand.UiaEva1Oxy, "OPEN", "CLOSED"),
            new Def("UIA.EMU2_POWER", TssCommand.UiaEva2Power, "ON", "OFF"),
            new Def("UIA.EV2_SUPPLY", TssCommand.UiaEva2WaterSupply, "OPEN", "CLOSED"),
            new Def("UIA.EV2_WASTE", TssCommand.UiaEva2WaterWaste, "OPEN", "CLOSED"),
            new Def("UIA.EV2_OXYGEN", TssCommand.UiaEva2Oxy, "OPEN", "CLOSED"),
            new Def("UIA.O2_VENT", TssCommand.UiaOxyVent, "OPEN", "CLOSED"),
            new Def("UIA.DEPRESS", TssCommand.UiaDepress, "ON", "OFF"),
        };

        static readonly Def[] Dcu =
        {
            new Def("DCU.BATT", TssCommand.DcuEva1BattLu, "UMB", "LOCAL"),
            new Def("DCU.BATT_SELECT", TssCommand.DcuEva1BattPs, "PRI", "SEC"),
            new Def("DCU.OXY", TssCommand.DcuEva1Oxy, "PRI", "SEC"),
            new Def("DCU.FAN", TssCommand.DcuEva1Fan, "PRI", "SEC"),
            new Def("DCU.PUMP", TssCommand.DcuEva1Pump, "OPEN", "CLOSED"),
            new Def("DCU.CO2", TssCommand.DcuEva1Co2, "A", "B"),
        };

        public Kind kind = Kind.Uia;
        public int columns = 5;
        public Vector2 spacing = new Vector2(0.12f, 0.16f);
        public Vector3 switchSize = new Vector3(0.03f, 0.06f, 0.03f);

        [ContextMenu("Build switches")]
        public void Build()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);

            var defs = kind == Kind.Uia ? Uia : Dcu;
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var go = new GameObject(d.id);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3((i % columns) * spacing.x, -(i / columns) * spacing.y, 0f);

                var lever = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lever.name = "Lever";
                lever.transform.SetParent(go.transform, false);
                lever.transform.localScale = switchSize;
                // Keep the collider: DesktopRig and XR ray interactors hit it.

                var label = new GameObject("Label").AddComponent<TextMesh>();
                label.transform.SetParent(go.transform, false);
                label.transform.localPosition = new Vector3(0f, -switchSize.y, 0f);
                label.characterSize = 0.01f;
                label.fontSize = 48;
                label.anchor = TextAnchor.UpperCenter;
                label.text = d.id.Substring(d.id.IndexOf('.') + 1).Replace('_', ' ');

                var sw = go.AddComponent<PanelSwitch>();
                sw.controlId = d.id;
                sw.tssCommand = d.cmd;
                sw.sendsToTss = true;
                sw.onLabel = d.on;
                sw.offLabel = d.off;
                sw.resetState = false; // TSS boots every UIA and DCU flag false
                sw.lever = lever.transform;
                sw.indicator = lever.GetComponent<Renderer>();
            }

            if (kind == Kind.Dcu)
            {
                // Umbilical is physical only: no TSS field, but the scenario needs to know it.
                var go = new GameObject("UMBILICAL");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, -((defs.Length - 1) / columns + 1) * spacing.y, 0f);
                var plug = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                plug.transform.SetParent(go.transform, false);
                plug.transform.localScale = new Vector3(0.05f, 0.03f, 0.05f);
                var sw = go.AddComponent<PanelSwitch>();
                sw.controlId = "UMBILICAL";
                sw.sendsToTss = false;
                sw.onLabel = "CONNECTED";
                sw.offLabel = "DISCONNECTED";
                sw.resetState = false;
                sw.indicator = plug.GetComponent<Renderer>();
            }
        }
    }
}
