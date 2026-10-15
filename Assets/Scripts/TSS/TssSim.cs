using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static TssSwitch;

public enum TssSwitch
{
    [InspectorName("UIA/EVA 1/EMU Power")] UiaEva1Power,
    [InspectorName("UIA/EVA 1/O2")] UiaEva1Oxy,
    [InspectorName("UIA/EVA 1/Water Supply")] UiaEva1WaterSupply,
    [InspectorName("UIA/EVA 1/Water Waste")] UiaEva1WaterWaste,

    [InspectorName("UIA/EVA 2/EMU Power")] UiaEva2Power,
    [InspectorName("UIA/EVA 2/O2")] UiaEva2Oxy,
    [InspectorName("UIA/EVA 2/Water Supply")] UiaEva2WaterSupply,
    [InspectorName("UIA/EVA 2/Water Waste")] UiaEva2WaterWaste,

    [InspectorName("UIA/O2 Vent")] UiaOxyVent,
    [InspectorName("UIA/Depress Pump")] UiaDepress,

    [InspectorName("DCU/EVA 1/Batt")] DcuEva1Batt,
    [InspectorName("DCU/EVA 1/OXY")] DcuEva1Oxy,
    [InspectorName("DCU/EVA 1/Batt Source")] DcuEva1BattSource,
    [InspectorName("DCU/EVA 1/Fan")] DcuEva1Fan,
    [InspectorName("DCU/EVA 1/Pump")] DcuEva1Pump,
    [InspectorName("DCU/EVA 1/CO2")] DcuEva1Co2,
}

public enum TssError
{
    None, O2Low, O2High, FanLow
}

public enum TssErrorFlag
{
    Fan, Oxy, Power, Scrubber
}

// The simulated TSS: EV1 telemetry stepped once per second from the UIA / DCU switches
[DefaultExecutionOrder(-100)]
public class TssSim : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Starting telemetry values (Assets/Resources/EvaData.asset).")]
    [SerializeField] private EvaData defaultEvaData;

    [Header("EVA")]
    [Tooltip("Start the EVA as soon as play mode begins.")]
    [SerializeField] private bool startOnPlay = false;

    [Tooltip("Seconds into the EVA when a random suit fault is thrown. -1 = never.")]
    [SerializeField] private int randomErrorAt = -1;

    public event Action Ticked;
    public event Action<TssSwitch, bool> SwitchChanged;
    public event Action<TssErrorFlag, bool> ErrorChanged;

    // runtime values
    public static TssSim Instance { get; private set; }
    public bool StartOnPlay => startOnPlay;
    public EvaData Eva1 { get; private set; }
    public bool Running { get; private set; }
    public TssError CurrentError { get; private set; }
    public float PosX { get; private set; }
    public float PosY { get; private set; }
    public float Heading { get; private set; }
    public bool UiaConnected => GetSwitch(UiaEva1Power) && GetSwitch(DcuEva1Batt);

    private readonly HashSet<TssSwitch> _on = new();
    private readonly HashSet<TssErrorFlag> _errors = new();
    private int _simTime;
    private float _tickTimer;

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(this); return; }
        Instance = this;

        Eva1 = Instantiate(defaultEvaData);
        Eva1.Init();
        Running = startOnPlay;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        _tickTimer += Time.deltaTime;
        if (_tickTimer < 1f) return;
        _tickTimer -= 1f;
        Advance(1);
    }

    // Runs this many one-second ticks right now.
    public void Advance(int seconds)
    {
        for (int i = 0; i < seconds; i++)
        {
            Tick();
            Ticked?.Invoke();
        }
    }

    public bool GetSwitch(TssSwitch tssSwitch) => _on.Contains(tssSwitch);

    public void SetSwitch(TssSwitch tssSwitch, bool value)
    {
        if (!(value ? _on.Add(tssSwitch) : _on.Remove(tssSwitch))) return;
        SwitchChanged?.Invoke(tssSwitch, value);
    }

    public bool IsErrorActive(TssErrorFlag flag) => _errors.Contains(flag);

    // The switch's Inspector name, e.g. "DCU/EVA 1/Fan (PRI)".
    public static string Label(TssSwitch tssSwitch) =>
        typeof(TssSwitch).GetField(tssSwitch.ToString())?.GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? tssSwitch.ToString();

    public void SetImu(float x, float y, float heading)
    {
        PosX = x;
        PosY = y;
        Heading = heading;
    }

    public void StartEva() => Running = true;

    // Stops the EVA and puts telemetry back to its starting values, switches are kept.
    public void ResetEva()
    {
        Running = false;
        _simTime = 0;
        CurrentError = TssError.None;
        Eva1.ResetValues();
    }

    public void ResetSwitches()
    {
        foreach (TssSwitch tssSwitch in Enum.GetValues(typeof(TssSwitch))) SetSwitch(tssSwitch, false);
    }

    public void ThrowError(TssError type)
    {
        CurrentError = type;
        Eva1.ThrowError(this);
    }

    private void SetError(TssErrorFlag flag, bool active)
    {
        if (active ? _errors.Add(flag) : _errors.Remove(flag)) ErrorChanged?.Invoke(flag, active);
    }

    // Same order as increment_simulation() in TSS2026 data.c.
    private void Tick()
    {
        Eva1.UpdateActiveStates(this);
        Eva1.UpdateOxygen(this);
        Eva1.UpdateFans(this);
        Eva1.UpdateScrubbers(this);
        Eva1.UpdateUiaConnected(this);

        foreach (TssErrorFlag flag in Enum.GetValues(typeof(TssErrorFlag)))
            SetError(flag, Eva1.HasError(flag, this));

        if (!Running) return;

        _simTime++;
        if (CurrentError == TssError.None && _simTime == randomErrorAt)
            ThrowError((TssError)UnityEngine.Random.Range(1, 4));
        Eva1.Step();
    }
}
