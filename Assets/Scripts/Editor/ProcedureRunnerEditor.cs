using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Play mode view of the procedure runner: the step list, Confirm / Skip / Back, and test runs.
[CustomEditor(typeof(ProcedureRunner))]
public class ProcedureRunnerEditor : Editor
{
    private static float s_StepDelay = 0.3f;

    public override bool RequiresConstantRepaint() => Application.isPlaying;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var run = (ProcedureRunner)target;
        var sim = TssSim.Instance;

        EditorGUILayout.Space();
        if (!Application.isPlaying || !sim)
        {
            EditorGUILayout.HelpBox("Enter Play mode for the live step list and test buttons.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Live", EditorStyles.boldLabel);
        var cur = run.Current;
        if (!cur) EditorGUILayout.HelpBox("Idle", MessageType.None);
        else
        {
            string paused = string.Join(", ", run.Paused.Select(p => $"{p.title} {run.StepOf(p) + 1}/{p.steps.Count}"));
            EditorGUILayout.HelpBox($"{(run.IsUrgent ? "URGENT  " : "")}{cur.title}  —  step {run.Index + 1}/{cur.steps.Count}" +
                                    (paused.Length > 0 ? $"\nPaused: {paused}" : ""),
                run.IsUrgent ? MessageType.Warning : MessageType.None);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = run.CurrentStep is { CanConfirm: true };
            if (GUILayout.Button("Confirm")) run.Confirm();
            GUI.enabled = run.CurrentStep != null;
            if (GUILayout.Button("Skip")) run.Skip();
            GUI.enabled = cur && run.Index > 0;
            if (GUILayout.Button("Back")) run.Back();
            GUI.enabled = true;
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Self Test")) SelfTest(run, sim);
            if (GUILayout.Button("Complete Egress")) CompleteEgress(run, sim);
            EditorGUIUtility.labelWidth = 70;
            s_StepDelay = EditorGUILayout.FloatField("Step delay", s_StepDelay, GUILayout.Width(110));
            EditorGUIUtility.labelWidth = 0;
        }

        if (!cur) return;
        EditorGUILayout.Space();
        string header = null;
        for (int i = 0; i < cur.steps.Count; i++)
        {
            var s = cur.steps[i];
            if (s.header != header)
            {
                header = s.header;
                EditorGUILayout.LabelField(header, EditorStyles.miniBoldLabel);
            }
            string mark = i < run.Index ? "✓" : i == run.Index ? "▶" : "   ";
            string confirm = i == run.Index && s.IsManual ? "   (Confirm)" : "";
            GUI.color = i < run.Index ? Color.gray : Color.white;
            EditorGUILayout.LabelField($"{mark} {i + 1,2}. {s.text}{confirm}", i == run.Index ? EditorStyles.boldLabel : EditorStyles.label);
            GUI.color = Color.white;
        }
    }

    private static Procedure Field(ProcedureRunner run, string name) =>
        new SerializedObject(run).FindProperty(name).objectReferenceValue as Procedure;

    // Puts the TSS and runner back to how Play started.
    public static void ResetToStart(ProcedureRunner run, TssSim sim)
    {
        if (run) run.Stop();
        sim.ResetSwitches();
        sim.ResetEva();
        if (sim.StartOnPlay) sim.StartEva();
        var start = run ? Field(run, "startProcedure") : null;
        if (!start) return;
        if (new SerializedObject(run).FindProperty("resetSwitchesOnStart").boolValue) sim.StartEva();
        run.Begin(start);
    }

    // Flips the step's switches, confirms it if it's waiting on that, otherwise waits a second.
    private static void PlayStep(ProcedureRunner run, TssSim sim)
    {
        var p = run.Current;
        int i = run.Index;
        var step = run.CurrentStep;
        foreach (var r in step.switchRequirements)
            sim.SetSwitch(r.requiredSwitch, r.state == SwitchState.Flip ? !sim.GetSwitch(r.requiredSwitch) : r.state == SwitchState.On);
        if (run.Current == p && run.Index == i && step.CanConfirm) run.Confirm();
        if (run.Current == p && run.Index == i) sim.Advance(1);
    }

    // Plays the start procedure instantly with a fan fault partway through, and expects zero warnings.
    private static void SelfTest(ProcedureRunner run, TssSim sim)
    {
        var start = Field(run, "startProcedure");
        var fan = Field(run, "fanErrorProcedure");
        if (!start) { Debug.LogWarning("ProcedureRunner self test needs a Start Procedure"); return; }
        Debug.Log($"ProcedureRunner self test: starting ({start.title})");

        var warnings = new List<string>();
        Action<string> onWarning = warnings.Add;
        Procedure lastP = null;
        int lastI = -1, lastTick = 0, ticks = 0;
        Action onTick = () => ticks++;
        Action onChanged = () =>
        {
            if (run.Current == lastP && run.Index == lastI) return;
            string waited = ticks > lastTick ? $"  (waited {ticks - lastTick} s)" : "";
            lastP = run.Current; lastI = run.Index; lastTick = ticks;
            Debug.Log(run.Current
                ? $"  {(run.IsUrgent ? "URGENT " : "")}{run.Current.title} {run.Index + 1}/{run.Current.steps.Count}: {run.CurrentStep?.text}{waited}"
                : $"  done{waited}");
        };
        run.Warning += onWarning;
        run.Changed += onChanged;
        sim.Ticked += onTick;

        string failure = null;
        try
        {
            run.Stop();
            sim.ResetSwitches();
            sim.ResetEva();
            sim.StartEva();
            run.Begin(start);

            bool thrown = false;
            for (int guard = 0; run.Current && guard < 5000 && failure == null; guard++)
            {
                if (!thrown && fan && run.Current == start && sim.GetSwitch(TssSwitch.DcuEva1Fan))
                {
                    thrown = true;
                    int resumeAt = run.Index;
                    Debug.Log($"  -- throwing Fan Low at {start.title} step {resumeAt + 1}");
                    sim.ThrowError(TssError.FanLow);
                    sim.Advance(1);
                    if (run.Current != fan) { failure = "fan error didn't start the fan procedure"; break; }
                    while (run.Current == fan && guard++ < 5000) PlayStep(run, sim);
                    if (run.Current != start || run.Index != resumeAt)
                        failure = $"expected {start.title} to resume at step {resumeAt + 1}, got {(run.Current ? run.Current.title : "nothing")} step {run.Index + 1}";
                    continue;
                }
                PlayStep(run, sim);
            }

            if (failure == null && run.Current) failure = $"stuck at {run.Current.title} step {run.Index + 1}: {run.CurrentStep?.text}";
            if (failure == null && warnings.Count > 0) failure = "warnings: " + string.Join("; ", warnings);
            if (failure == null && fan && !thrown) failure = "fan never went to PRI, so the fan fault wasn't tested";
        }
        finally
        {
            run.Warning -= onWarning;
            run.Changed -= onChanged;
            sim.Ticked -= onTick;
            ResetToStart(run, sim);
        }

        if (failure == null) Debug.Log("ProcedureRunner self test passed");
        else Debug.LogError("ProcedureRunner self test failed: " + failure);
    }

    // Plays the start procedure live on the boards, fast-forwarding wait steps one sim second per frame.
    private static void CompleteEgress(ProcedureRunner run, TssSim sim)
    {
        var start = Field(run, "startProcedure");
        if (!start) { Debug.LogWarning("Complete Egress needs a Start Procedure"); return; }
        run.StopAllCoroutines();
        run.StartCoroutine(PlayLive(run, sim, start));
    }

    private static IEnumerator PlayLive(ProcedureRunner run, TssSim sim, Procedure p)
    {
        var warnings = new List<string>();
        Action<string> onWarning = warnings.Add;
        run.Warning += onWarning;
        Debug.Log($"Complete Egress: playing {p.title}");
        run.Stop();
        sim.ResetSwitches();
        sim.ResetEva();
        sim.StartEva();
        run.Begin(p);

        string failure = null;
        while (run.Current == p)
        {
            int i = run.Index;
            var step = run.CurrentStep;
            Debug.Log($"  {p.title} {i + 1}/{p.steps.Count}: {step.text}");
            yield return new WaitForSeconds(s_StepDelay);

            foreach (var r in step.switchRequirements)
                sim.SetSwitch(r.requiredSwitch, r.state == SwitchState.Flip ? !sim.GetSwitch(r.requiredSwitch) : r.state == SwitchState.On);

            int waited = 0;
            while (run.Current == p && run.Index == i && waited < 600)
            {
                if (step.CanConfirm) { run.Confirm(); break; }
                sim.Advance(1);
                waited++;
                yield return null;
            }
            if (run.Current == p && run.Index == i) { failure = $"stuck at step {i + 1}: {step.text}"; break; }
            if (waited > 0) Debug.Log($"    waited {waited} s");
            if (run.Current == p && run.Index > i + 1) Debug.Log($"    steps {i + 2}–{run.Index} already met, passed automatically");
        }

        run.Warning -= onWarning;
        if (failure != null) Debug.LogError("Complete Egress failed: " + failure);
        else if (run.Current) Debug.LogWarning($"Complete Egress: interrupted by {run.Current.title}");
        else if (warnings.Count > 0) Debug.LogWarning("Complete Egress: done with warnings: " + string.Join("; ", warnings));
        else Debug.Log("Complete Egress: done, no warnings");
    }
}
