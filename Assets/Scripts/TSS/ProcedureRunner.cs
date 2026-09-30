using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Runs Procedure assets against TssSim. Steps advance as soon as their switches and values are met,
// unexpected switch flips raise a warning, and TSS errors pause the current procedure for an urgent one.
// Progress is kept per procedure, so switching away and back never loses your place.
[DefaultExecutionOrder(-50)]
public class ProcedureRunner : MonoBehaviour
{
    public static ProcedureRunner Instance { get; private set; }

    [Tooltip("Started on Play. Leave empty to start idle.")]
    public Procedure startProcedure;
    
    [Tooltip("CAPCOM's reset: all switches off before the start procedure begins.")]
    public bool resetSwitchesOnStart = true;

    [Header("Urgent procedures, started when the TSS raises the error")]
    public Procedure fanErrorProcedure;
    public Procedure oxyErrorProcedure;
    public Procedure powerErrorProcedure;
    public Procedure scrubberErrorProcedure;

    // Procedure, step or pause state changed. All the UI needs to redraw.
    public event Action Changed;
    
    // A switch was flipped that the current step didn't ask for, or a start was deferred.
    public event Action<string> Warning;
    public event Action<Procedure> Completed;

    public Procedure Current => _urgent.Count > 0 ? _urgent.Peek() : _normal;
    public int Index => Current ? StepOf(Current) : 0;
    public ProcedureStep CurrentStep => Current && Index < Current.steps.Count ? Current.steps[Index] : null;
    public bool IsUrgent => Current && Current.priority == ProcedurePriority.Urgent;

    // Procedures waiting underneath the current one, next to resume first.
    public IEnumerable<Procedure> Paused
    {
        get
        {
            bool top = true;
            foreach (var p in _urgent)
            {
                if (!top) yield return p;
                top = false;
            }
            if (_urgent.Count > 0 && _normal) yield return _normal;
        }
    }

    public int StepOf(Procedure p) => _progress.TryGetValue(p, out var i) ? i : 0;

    private TssSim _sim;
    private Procedure _normal;
    private readonly Stack<Procedure> _urgent = new();
    private readonly Dictionary<Procedure, int> _progress = new();
    private bool _fanWas, _oxyWas, _powerWas, _scrubberWas;

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        _sim = TssSim.Instance;
        if (startProcedure)
        {
            if (resetSwitchesOnStart) _sim.ResetSwitches();
            Begin(startProcedure);
        }
        _sim.SwitchChanged += OnSwitchChanged;
        _sim.Ticked += OnTicked;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (!_sim) return;
        _sim.SwitchChanged -= OnSwitchChanged;
        _sim.Ticked -= OnTicked;
    }

    // Continues `p` from where it was left (or step 0). Urgent procedures take over right away;
    // a normal one picked during an urgent procedure starts once the urgent ones are done.
    public void Begin(Procedure p)
    {
        if (!p) return;
        if (p.priority == ProcedurePriority.Urgent) { Push(p, restart: false); return; }

        _progress.TryAdd(p, 0);
        _normal = p;
        if (IsUrgent)
        {
            Warning?.Invoke($"Finish {Current.title} first. {p.title} starts after.");
            Changed?.Invoke();
        }
        else Enter();
    }

    public void Restart(Procedure p)
    {
        if (!p) return;
        _progress[p] = 0;
        Begin(p);
    }

    // Finishes the current step, only if it's a manual one.
    public void Confirm()
    {
        if (CurrentStep is { IsManual: true }) Advance();
    }

    // Testing: moves on without checking the step.
    public void Skip()
    {
        if (CurrentStep != null) Advance();
    }

    // Testing: goes back one step. If that step is already met it advances again on the next tick.
    public void Back()
    {
        if (!Current || Index == 0) return;
        _progress[Current]--;
        CurrentStep.Begin(_sim);
        Changed?.Invoke();
    }

    public void Stop()
    {
        _urgent.Clear();
        _normal = null;
        _progress.Clear();
        Changed?.Invoke();
    }

    private void OnSwitchChanged(TssSwitch s, bool on)
    {
        var step = CurrentStep;
        if (step == null) return; // idle: free play on the boards

        if (!step.Expects(s, _sim))
            Warning?.Invoke($"Unexpected: {Label(s)} → {(on ? "On" : "Off")}");
        else if (step.IsMet(_sim))
            Advance();
    }

    private void OnTicked()
    {
        Watch(_sim.FanError, ref _fanWas, fanErrorProcedure);
        Watch(_sim.OxyError, ref _oxyWas, oxyErrorProcedure);
        Watch(_sim.PowerError, ref _powerWas, powerErrorProcedure);
        Watch(_sim.ScrubberError, ref _scrubberWas, scrubberErrorProcedure);

        if (CurrentStep != null && CurrentStep.IsMet(_sim)) Advance(); // wait steps
    }

    // Starts the error's procedure when its flag turns on. A repeat error starts it over.
    private void Watch(bool now, ref bool was, Procedure p)
    {
        if (now && !was && p) Push(p, restart: true);
        was = now;
    }

    private void Push(Procedure p, bool restart)    
    {
        if (_urgent.Contains(p)) return;
        if (restart || !_progress.ContainsKey(p)) _progress[p] = 0;
        _urgent.Push(p);
        Enter();
    }

    private void Advance()
    {
        _progress[Current]++;
        Enter();
    }

    // Sets up the current step and passes straight through any that are already met
    // (e.g. "Verify OXY – PRI"). Finishing a procedure resumes whatever it paused.
    private void Enter()
    {
        while (Current)
        {
            var step = CurrentStep;
            if (step == null) { Finish(); continue; }
            step.Begin(_sim);
            if (!step.IsMet(_sim)) break;
            _progress[Current]++;
        }
        Changed?.Invoke();
    }

    private void Finish()
    {
        var done = Current;
        _progress.Remove(done);
        if (_urgent.Count > 0) _urgent.Pop();
        else _normal = null;
        Completed?.Invoke(done);
    }

    // The switch's Inspector name, e.g. "DCU/EVA 1/Fan (PRI)".
    private static string Label(TssSwitch s) =>
        typeof(TssSwitch).GetField(s.ToString())?.GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? s.ToString();

    // Play mode > right-click > Self Test. Plays the start procedure by flipping each required switch,
    // throws a fan fault once the fan is on PRI, and checks that the fan procedure takes over and the
    // start procedure resumes at the same step, all with zero warnings. Resets everything after.
    [ContextMenu("Self Test")]
    private void SelfTest()
    {
        string failure = RunSelfTest();
        if (failure == null) Debug.Log("ProcedureRunner self test passed");
        else Debug.LogError("ProcedureRunner self test failed: " + failure);
    }

    private string RunSelfTest()
    {
        if (!_sim || !startProcedure) return "needs Play mode and a Start Procedure";
        var sim = _sim;
        var warnings = new List<string>();
        Action<string> onWarning = warnings.Add;
        Warning += onWarning;
        try
        {
            Stop();
            sim.ResetSwitches();
            sim.ResetEva();
            sim.StartEva();
            Begin(startProcedure);

            bool thrown = false;
            for (int guard = 0; Current && guard < 5000; guard++)
            {
                if (!thrown && fanErrorProcedure && Current == startProcedure && sim.GetSwitch(TssSwitch.DcuEva1Fan))
                {
                    thrown = true;
                    int resumeAt = Index;
                    sim.ThrowError(TssError.FanLow);
                    sim.Advance(1);
                    if (Current != fanErrorProcedure) return "fan error didn't start the fan procedure";
                    while (Current == fanErrorProcedure && guard++ < 5000) PlayStep();
                    if (Current != startProcedure || Index != resumeAt)
                        return $"expected {startProcedure.title} to resume at step {resumeAt + 1}, got {(Current ? Current.title : "nothing")} step {Index + 1}";
                    continue;
                }
                PlayStep();
            }

            if (Current) return $"stuck at {Current.title} step {Index + 1}: {CurrentStep?.text}";
            if (warnings.Count > 0) return "warnings: " + string.Join("; ", warnings);
            if (fanErrorProcedure && !thrown) return "fan never went to PRI, so the fan fault wasn't tested";
            return null;
        }
        finally
        {
            Warning -= onWarning;
            Stop();
            sim.ResetSwitches();
            sim.ResetEva();
            if (sim.startOnPlay) sim.StartEva();
            Begin(startProcedure);
        }
    }

    // Does what the current step asks: Confirm a manual step, flip its switches, or wait a second.
    private void PlayStep()
    {
        var p = Current;
        int i = Index;
        var step = CurrentStep;
        if (step.IsManual) Confirm();
        else
            foreach (var r in step.switchRequirements)
                _sim.SetSwitch(r.requiredSwitch, r.state == SwitchState.Flip ? !_sim.GetSwitch(r.requiredSwitch) : r.state == SwitchState.On);
        if (Current == p && Index == i) _sim.Advance(1);
    }
}
