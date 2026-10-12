using System;
using System.Collections.Generic;
using UnityEngine;

// Runs Procedure assets against the TSS, urgent error procedures pause the current one and resume it after.
[DefaultExecutionOrder(-50)]
public class ProcedureRunner : MonoBehaviour
{
    [Header("Start")]
    [Tooltip("Started on Play. Leave empty to start idle.")]
    [SerializeField] private Procedure startProcedure;

    [Tooltip("CAPCOM setup before the start procedure: all switches off, then Start EVA (telemetry runs during egress).")]
    [SerializeField] private bool resetSwitchesOnStart = true;

    [Header("Urgent procedures")]
    [Tooltip("Started when the TSS raises the fan error.")]
    [SerializeField] private Procedure fanErrorProcedure;

    [Tooltip("Started when the TSS raises the oxy error.")]
    [SerializeField] private Procedure oxyErrorProcedure;

    [Tooltip("Started when the TSS raises the power error.")]
    [SerializeField] private Procedure powerErrorProcedure;

    [Tooltip("Started when the TSS raises the scrubber error.")]
    [SerializeField] private Procedure scrubberErrorProcedure;

    public event Action Changed;
    public event Action<string> Warning;
    public event Action<TssSwitch> UnexpectedSwitch;
    public event Action<Procedure> Completed;

    // runtime values
    public static ProcedureRunner Instance { get; private set; }
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
    private ProcedureStep _watchedStep;
    private readonly Stack<Procedure> _urgent = new();
    private readonly Dictionary<Procedure, int> _progress = new();

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        _sim = TssSim.Instance;
        _sim.SwitchChanged += OnSwitchChanged;
        _sim.ErrorChanged += OnErrorChanged;
    }

    private void OnDisable()
    {
        WatchValues(null);
        if (!_sim) return;
        _sim.SwitchChanged -= OnSwitchChanged;
        _sim.ErrorChanged -= OnErrorChanged;
    }

    private void Start()
    {
        if (!startProcedure) return;
        if (resetSwitchesOnStart)
        {
            _sim.ResetSwitches();
            _sim.StartEva();
        }
        Begin(startProcedure);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Continues the procedure where it was left, a normal one picked during an urgent one waits until it's done.
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

    public void Confirm()
    {
        if (CurrentStep is { CanConfirm: true }) Advance();
    }

    public void Skip()
    {
        if (CurrentStep != null) Advance();
    }

    public void Back()
    {
        if (!Current || Index == 0) return;
        _progress[Current]--;
        CurrentStep.StartStep();
        WatchValues(CurrentStep);
        Changed?.Invoke();
    }

    public void Stop()
    {
        _urgent.Clear();
        _normal = null;
        _progress.Clear();
        WatchValues(null);
        Changed?.Invoke();
    }

    private void OnSwitchChanged(TssSwitch s, bool on)
    {
        var step = CurrentStep;
        if (step == null) return;

        if (!step.ExpectsSwitch(s))
        {
            Warning?.Invoke($"Unexpected: {TssSim.Label(s)} → {(on ? "On" : "Off")}");
            UnexpectedSwitch?.Invoke(s);
        }
        else if (step.CompletesOnItsOwn)
            Advance();
    }

    // A repeat error starts its procedure over.
    private void OnErrorChanged(TssErrorFlag flag, bool active)
    {
        if (!active) return;
        var p = flag switch
        {
            TssErrorFlag.Fan => fanErrorProcedure,
            TssErrorFlag.Oxy => oxyErrorProcedure,
            TssErrorFlag.Power => powerErrorProcedure,
            _ => scrubberErrorProcedure,
        };
        if (p) Push(p, restart: true);
    }

    private void OnWatchedValueChanged(float value)
    {
        if (CurrentStep is { CompletesOnItsOwn: true }) Advance();
    }

    // Listens only to the telemetry values the current step waits on.
    private void WatchValues(ProcedureStep step)
    {
        if (_watchedStep == step) return;
        if (_watchedStep != null && _sim)
            foreach (var r in _watchedStep.valueRequirements) _sim.Eva1.Get(r.value).OnValueChanged -= OnWatchedValueChanged;
        _watchedStep = step;
        if (_watchedStep != null)
            foreach (var r in _watchedStep.valueRequirements) _sim.Eva1.Get(r.value).OnValueChanged += OnWatchedValueChanged;
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

    // Steps that are already met pass straight through, and finishing a procedure resumes the one it paused.
    private void Enter()
    {
        while (Current)
        {
            var step = CurrentStep;
            if (step == null) { Finish(); continue; }
            step.StartStep();
            if (!step.CompletesOnItsOwn) break;
            _progress[Current]++;
        }
        WatchValues(CurrentStep);
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
}
