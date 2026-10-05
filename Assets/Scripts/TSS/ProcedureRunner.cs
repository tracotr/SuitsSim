using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
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
    
    [Tooltip("CAPCOM setup before the start procedure: all switches off, then Start EVA (telemetry runs during egress).")]
    public bool resetSwitchesOnStart = true;
    [Tooltip("Complete Egress (context menu): pause before each step so you can watch the levers.")]
    public float stepDelay = 0.3f;

    [Header("Urgent procedures, started when the TSS raises the error")]
    public Procedure fanErrorProcedure;
    public Procedure oxyErrorProcedure;
    public Procedure powerErrorProcedure;
    public Procedure scrubberErrorProcedure;

    [Header("Live view (Play mode, read-only)")]
    [SerializeField] private string currentProcedure;
    [SerializeField, TextArea(8, 40)] private string stepList;

    // Procedure, step or pause state changed. All the UI needs to redraw.
    public event Action Changed;
    
    // A switch was flipped that the current step didn't ask for, or a start was deferred.
    public event Action<string> Warning;
    // The switch behind an "Unexpected" warning, so the board can point at it.
    public event Action<TssSwitch> UnexpectedSwitch;
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
        Changed += RefreshLiveView;
    }

    private void Start()
    {
        _sim = TssSim.Instance;
        if (startProcedure)
        {
            if (resetSwitchesOnStart)
            {
                // CAPCOM before egress: all switches down/back, then START EVA telemetry (values only move while running).
                _sim.ResetSwitches();
                _sim.StartEva();
            }
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

    // Finishes the current step if it's manual, or a confirm step whose requirements are met.
    [ContextMenu("Confirm Step")]
    public void Confirm()
    {
        if (CurrentStep != null && CurrentStep.CanConfirm(_sim)) Advance();
    }

    // Testing: moves on without checking the step.
    [ContextMenu("Skip Step")]
    public void Skip()
    {
        if (CurrentStep != null) Advance();
    }

    // Testing: goes back one step. If that step is already met it advances again on the next tick.
    [ContextMenu("Back Step")]
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
        {
            Warning?.Invoke($"Unexpected: {TssSim.Label(s)} → {(on ? "On" : "Off")}");
            UnexpectedSwitch?.Invoke(s);
        }
        else if (step.CompletesOnItsOwn(_sim))
            Advance();
    }

    private void OnTicked()
    {
        Watch(_sim.FanError, ref _fanWas, fanErrorProcedure);
        Watch(_sim.OxyError, ref _oxyWas, oxyErrorProcedure);
        Watch(_sim.PowerError, ref _powerWas, powerErrorProcedure);
        Watch(_sim.ScrubberError, ref _scrubberWas, scrubberErrorProcedure);

        if (CurrentStep != null && CurrentStep.CompletesOnItsOwn(_sim)) Advance(); // wait steps
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
            if (!step.CompletesOnItsOwn(_sim)) break;
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

    // Fills the Inspector's live view: current procedure, what's paused, and every step
    // (✓ done, ▶ current, "(Confirm)" when the current step is waiting for the tester).
    private void RefreshLiveView()
    {
        if (!Current)
        {
            currentProcedure = "(idle)";
            stepList = "";
            return;
        }

        var paused = new StringBuilder();
        foreach (var p in Paused) paused.Append($"{p.title} {StepOf(p) + 1}/{p.steps.Count}, ");
        currentProcedure = $"{(IsUrgent ? "URGENT  " : "")}{Current.title}  —  step {Index + 1}/{Current.steps.Count}"
            + (paused.Length > 0 ? $"   (paused: {paused.ToString().TrimEnd(',', ' ')})" : "");

        var sb = new StringBuilder();
        string header = null;
        for (int i = 0; i < Current.steps.Count; i++)
        {
            var s = Current.steps[i];
            if (s.header != header) { sb.AppendLine($"[{s.header}]"); header = s.header; }
            string mark = i < Index ? "✓" : i == Index ? "▶" : "  ";
            string confirm = i == Index && (s.IsManual || s.requireConfirm) ? "   (Confirm)" : "";
            sb.AppendLine($"{mark} {i + 1,2}. {s.text}{confirm}");
        }
        stepList = sb.ToString();
    }

    // Play mode > right-click > Self Test. Plays the start procedure by flipping each required switch,
    // throws a fan fault once the fan is on PRI, and checks that the fan procedure takes over and the
    // start procedure resumes at the same step, all with zero warnings. Resets everything after.
    [ContextMenu("Self Test")]
    private void SelfTest()
    {
        Debug.Log("ProcedureRunner self test: starting" + (startProcedure ? $" ({startProcedure.title})" : ""));
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
        // Log every step as the test reaches it, with the sim seconds the previous one took.
        Procedure lastP = null;
        int lastI = -1, lastTick = 0, ticks = 0;
        Action onTick = () => ticks++;
        Action onChanged = () =>
        {
            if (Current == lastP && Index == lastI) return;
            string waited = ticks > lastTick ? $"  (waited {ticks - lastTick} s)" : "";
            lastP = Current; lastI = Index; lastTick = ticks;
            Debug.Log(Current
                ? $"  {(IsUrgent ? "URGENT " : "")}{Current.title} {Index + 1}/{Current.steps.Count}: {CurrentStep?.text}{waited}"
                : $"  done{waited}");
        };
        Changed += onChanged;
        sim.Ticked += onTick;
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
                    Debug.Log($"  -- throwing Fan Low at {Current.title} step {Index + 1}");
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
            Changed -= onChanged;
            sim.Ticked -= onTick;
            Stop();
            sim.ResetSwitches();
            sim.ResetEva();
            if (sim.startOnPlay) sim.StartEva();
            Begin(startProcedure);
        }
    }

    // Play mode > right-click > Complete Egress. Plays the start procedure live on the boards: each lever
    // flips for real, wait steps fast-forward the sim one second per frame. Leaves the finished state up.
    [ContextMenu("Complete Egress")]
    private void CompleteEgress()
    {
        if (!_sim || !startProcedure) { Debug.LogWarning("Complete Egress needs Play mode and a Start Procedure"); return; }
        StopAllCoroutines();
        StartCoroutine(PlayLive(startProcedure));
    }

    private IEnumerator PlayLive(Procedure p)
    {
        var warnings = new List<string>();
        Action<string> onWarning = warnings.Add;
        Warning += onWarning;
        Debug.Log($"Complete Egress: playing {p.title}");
        Stop();
        _sim.ResetSwitches();
        _sim.ResetEva();
        _sim.StartEva();
        Begin(p);

        string failure = null;
        while (Current == p)
        {
            int i = Index;
            var step = CurrentStep;
            Debug.Log($"  {p.title} {i + 1}/{p.steps.Count}: {step.text}");
            yield return new WaitForSeconds(stepDelay);

            foreach (var r in step.switchRequirements)
                _sim.SetSwitch(r.requiredSwitch, r.state == SwitchState.Flip ? !_sim.GetSwitch(r.requiredSwitch) : r.state == SwitchState.On);

            int waited = 0;
            while (Current == p && Index == i && waited < 600)
            {
                if (step.CanConfirm(_sim)) { Confirm(); break; } // stands in for the tester's Confirm
                _sim.Advance(1);
                waited++;
                yield return null;
            }
            if (Current == p && Index == i) { failure = $"stuck at step {i + 1}: {step.text}"; break; }
            if (waited > 0) Debug.Log($"    waited {waited} s");
            if (Current == p && Index > i + 1) Debug.Log($"    steps {i + 2}–{Index} already met, passed automatically");
        }

        Warning -= onWarning;
        if (failure != null) Debug.LogError("Complete Egress failed: " + failure);
        else if (Current) Debug.LogWarning($"Complete Egress: interrupted by {Current.title}");
        else if (warnings.Count > 0) Debug.LogWarning("Complete Egress: done with warnings: " + string.Join("; ", warnings));
        else Debug.Log("Complete Egress: done, no warnings");
    }

    // Does what the current step asks: flip its switches, Confirm it if it's waiting for that, or wait a second.
    private void PlayStep()
    {
        var p = Current;
        int i = Index;
        var step = CurrentStep;
        foreach (var r in step.switchRequirements)
            _sim.SetSwitch(r.requiredSwitch, r.state == SwitchState.Flip ? !_sim.GetSwitch(r.requiredSwitch) : r.state == SwitchState.On);
        if (Current == p && Index == i && step.CanConfirm(_sim)) Confirm();
        if (Current == p && Index == i) _sim.Advance(1);
    }
}
