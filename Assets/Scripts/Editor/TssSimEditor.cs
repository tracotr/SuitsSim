using System;
using UnityEditor;
using UnityEngine;

// Play mode view of the TSS: status, switches you can click to flip, telemetry, and test buttons.
[CustomEditor(typeof(TssSim))]
public class TssSimEditor : Editor
{
    private bool _showSwitches = true, _showTelemetry = true;

    public override bool RequiresConstantRepaint() => Application.isPlaying;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var sim = (TssSim)target;

        EditorGUILayout.Space();
        if (!Application.isPlaying || sim.Eva1 == null)
        {
            EditorGUILayout.HelpBox("Enter Play mode for live data and test buttons.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Live", EditorStyles.boldLabel);
        bool anyError = false;
        foreach (TssErrorFlag f in Enum.GetValues(typeof(TssErrorFlag))) anyError |= sim.IsErrorActive(f);
        EditorGUILayout.HelpBox(Status(sim), anyError ? MessageType.Warning : MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Start EVA")) sim.StartEva();
            if (GUILayout.Button("Reset EVA")) sim.ResetEva();
            if (GUILayout.Button("Reset Switches")) sim.ResetSwitches();
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Throw O2 Low")) sim.ThrowError(TssError.O2Low);
            if (GUILayout.Button("Throw O2 High")) sim.ThrowError(TssError.O2High);
            if (GUILayout.Button("Throw Fan Low")) sim.ThrowError(TssError.FanLow);
        }
        if (GUILayout.Button("Self Test")) SelfTest(sim);

        _showSwitches = EditorGUILayout.Foldout(_showSwitches, "Switches (click to flip)", true);
        if (_showSwitches)
        {
            foreach (TssSwitch s in Enum.GetValues(typeof(TssSwitch)))
            {
                bool on = sim.GetSwitch(s);
                bool now = EditorGUILayout.ToggleLeft(TssSim.Label(s), on);
                if (now != on) sim.SetSwitch(s, now);
            }
        }

        _showTelemetry = EditorGUILayout.Foldout(_showTelemetry, "Telemetry", true);
        if (_showTelemetry)
        {
            foreach (var f in sim.Eva1.All())
                EditorGUILayout.LabelField(f.name, f.Active ? $"{f.Value:0.###}" : $"{f.Value:0.###}   (idle)");
        }
    }

    private static string Status(TssSim sim)
    {
        string flags = "";
        foreach (TssErrorFlag f in Enum.GetValues(typeof(TssErrorFlag)))
            if (sim.IsErrorActive(f)) flags += f.ToString().ToUpper() + " ";
        return $"{(sim.Running ? "Running" : "Stopped")}   EVA time {sim.Eva1.elapsedTime.Value:0} s   fault: {sim.CurrentError}\n" +
               (flags.Length > 0 ? "ERRORS: " + flags : "No error flags");
    }

    // Runs egress O2 prep and a fan fault through the sim, then puts everything back to how Play started.
    private static void SelfTest(TssSim sim)
    {
        var run = ProcedureRunner.Instance;
        if (run) run.Stop();
        sim.ResetSwitches();
        sim.ResetEva();
        try
        {
            sim.StartEva();
            sim.SetSwitch(TssSwitch.UiaEva1Power, true);
            sim.SetSwitch(TssSwitch.DcuEva1Batt, true);
            sim.SetSwitch(TssSwitch.DcuEva1Oxy, true);
            sim.SetSwitch(TssSwitch.DcuEva1Fan, true);
            sim.SetSwitch(TssSwitch.UiaOxyVent, true);
            sim.Advance(30);
            Check(sim.Eva1.primaryOxygenPressure.Value < 10 && sim.Eva1.secondaryOxygenPressure.Value < 10, "vent drains both tanks < 10 psi");
            sim.SetSwitch(TssSwitch.UiaOxyVent, false);
            sim.SetSwitch(TssSwitch.UiaEva1Oxy, true);
            sim.Advance(45);
            Check(sim.Eva1.primaryOxygenPressure.Value >= 2999, "primary tank fills to 3000 psi");
            sim.SetSwitch(TssSwitch.UiaEva1Oxy, false);
            sim.Advance(20);
            Check(Mathf.Approximately(sim.Eva1.suitPressureOxygen.Value, 4f), "suit O2 pressurizes to 4 psi");

            sim.ThrowError(TssError.FanLow);
            sim.Advance(1);
            Check(sim.IsErrorActive(TssErrorFlag.Fan) && Mathf.Approximately(sim.Eva1.primaryFanRpm.Value, 0f), "fan fault raises the fan error");
            sim.SetSwitch(TssSwitch.DcuEva1Fan, false);
            sim.Advance(1);
            Check(!sim.IsErrorActive(TssErrorFlag.Fan) && Mathf.Approximately(sim.Eva1.secondaryFanRpm.Value, 30000f), "secondary fan clears the fan error");
            Debug.Log("TssSim self test passed");
        }
        catch (Exception e)
        {
            Debug.LogError("TssSim self test failed: " + e.Message);
        }
        finally
        {
            ProcedureRunnerEditor.ResetToStart(run, sim);
        }
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception(what);
    }
}
