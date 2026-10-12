using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static TssSwitch;

public enum TssSwitch
{
    [InspectorName("UIA/EVA 1/EMU Power (On)")] UiaEva1Power,
    [InspectorName("UIA/EVA 1/O2 (Open)")] UiaEva1Oxy,
    [InspectorName("UIA/EVA 1/Water Supply (Open)")] UiaEva1WaterSupply,
    [InspectorName("UIA/EVA 1/Water Waste (Open)")] UiaEva1WaterWaste,

    [InspectorName("UIA/EVA 2/EMU Power (On)")] UiaEva2Power,
    [InspectorName("UIA/EVA 2/O2 (Open)")] UiaEva2Oxy,
    [InspectorName("UIA/EVA 2/Water Supply (Open)")] UiaEva2WaterSupply,
    [InspectorName("UIA/EVA 2/Water Waste (Open)")] UiaEva2WaterWaste,

    [InspectorName("UIA/O2 Vent (Open)")] UiaOxyVent,
    [InspectorName("UIA/Depress Pump (On)")] UiaDepress,

    [InspectorName("DCU/EVA 1/Batt (UMB)")] DcuEva1Batt,
    [InspectorName("DCU/EVA 1/OXY (PRI)")] DcuEva1Oxy,
    [InspectorName("DCU/EVA 1/Batt Source (PRI)")] DcuEva1BattSource,
    [InspectorName("DCU/EVA 1/Fan (PRI)")] DcuEva1Fan,
    [InspectorName("DCU/EVA 1/Pump (Open)")] DcuEva1Pump,
    [InspectorName("DCU/EVA 1/CO2 (PRI)")] DcuEva1Co2,
}

public enum TssError
{
    None, O2Low, O2High, FanLow
}

public enum TssErrorFlag
{
    Fan, Oxy, Power, Scrubber
}

// The simulated TSS: EV1 telemetry stepped once per second from the UIA / DCU switches, like TSS2026.
[DefaultExecutionOrder(-100)]
public class TssSim : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Starting telemetry values (Assets/Resources/EvaData.asset).")]
    [SerializeField] private EvaData defaultEvaData;

    [Header("EVA")]
    [Tooltip("Start the EVA as soon as play mode begins.")]
    [SerializeField] private bool startOnPlay = true;

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

    private const float OxyVentRate = 4.3f, OxyFillRate = 2.5f, OxyRate = 0.01f, OxyErrorRate = 0.1f,
        Co2Rate = 0.01f, CoolantFillRate = 5.76f, CoolantDrainRate = 6.78f, CoolantRate = 0.01f;

    private readonly HashSet<TssSwitch> _on = new();
    private readonly HashSet<TssErrorFlag> _errors = new();
    private readonly List<(TssErrorFlag flag, bool active)> _errorChanges = new();
    private int _simTime;
    private float _tickTimer;

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(this); return; }
        Instance = this;

        Eva1 = Instantiate(defaultEvaData);
        foreach (var f in Eva1.All()) f.Init();
        ApplyEvaFormulas(Eva1);
        ComputeFormulas();
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
            foreach (var (flag, active) in _errorChanges) ErrorChanged?.Invoke(flag, active);
            _errorChanges.Clear();
            Ticked?.Invoke();
        }
    }

    public bool GetSwitch(TssSwitch s) => _on.Contains(s);

    public void SetSwitch(TssSwitch s, bool value)
    {
        if (!(value ? _on.Add(s) : _on.Remove(s))) return;
        SwitchChanged?.Invoke(s, value);
    }

    public bool IsErrorActive(TssErrorFlag flag) => _errors.Contains(flag);

    // The switch's Inspector name, e.g. "DCU/EVA 1/Fan (PRI)".
    public static string Label(TssSwitch s) =>
        typeof(TssSwitch).GetField(s.ToString())?.GetCustomAttribute<InspectorNameAttribute>()?.displayName ?? s.ToString();

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
        foreach (var f in Eva1.All()) f.Reset();
        ComputeFormulas();
    }

    public void ResetSwitches()
    {
        foreach (var s in new List<TssSwitch>(_on)) SetSwitch(s, false);
    }

    // O2 faults only start while the DCU is on the primary tank.
    public void ThrowError(TssError type)
    {
        CurrentError = type;
        var e = Eva1;
        bool oxyPri = GetSwitch(DcuEva1Oxy);
        switch (type)
        {
            case TssError.O2Low:
                if (oxyPri) e.suitPressureOxygen.Set(TssFieldAlgo.Decay, OxyErrorRate);
                break;
            case TssError.O2High:
                if (oxyPri) e.suitPressureOxygen.Set(TssFieldAlgo.Growth, OxyErrorRate);
                break;
            case TssError.FanLow:
                e.primaryFanRpm.algo = TssFieldAlgo.Decay;
                e.helmetPressureCo2.Set(TssFieldAlgo.Growth, Co2Rate);
                break;
        }
    }

    private void ApplyEvaFormulas(EvaData eva)
    {
        eva.primaryOxygenPressure.Formula = () => eva.primaryOxygenStorage.Value * 30f;
        eva.secondaryOxygenPressure.Formula = () => eva.secondaryOxygenStorage.Value * 30f;
        eva.suitPressureTotal.Formula = () => eva.suitPressureOxygen.Value + eva.suitPressureCo2.Value + eva.suitPressureOther.Value;
        eva.helmetPressureCo2.Formula = () => eva.suitPressureCo2.Value * 1.5f;
        eva.coolantGasPressure.Formula = () => 0f;
        eva.heartRate.Formula = () => 120f + eva.temperature.Value * 1.55f;
        eva.oxygenConsumption.Formula = () => 0.1f + 0.0001f * eva.heartRate.Value;
        eva.co2Production.Formula = () => 0.11f + 0.0002f * eva.heartRate.Value;
    }

    // Dependent fields start at 0, so fill them in before the first tick.
    private void ComputeFormulas()
    {
        foreach (var f in Eva1.All()) if (f.Formula != null) f.Value = f.Formula();
    }

    private void SetError(TssErrorFlag flag, bool active)
    {
        if (active ? _errors.Add(flag) : _errors.Remove(flag)) _errorChanges.Add((flag, active));
    }

    // Same order as increment_simulation() in TSS2026 data.c.
    private void Tick()
    {
        UpdateActiveStates();
        UpdateO2Error();
        UpdateFanError();
        UpdatePowerError();
        UpdateScrubbers();
        UpdateFanValues();
        if (GetSwitch(UiaEva1Power) && GetSwitch(DcuEva1Batt)) UpdateUiaConnected();
        if (Running) StepValues();
    }

    // The DCU decides which battery, tank and fan are in use, unused ones hold still.
    private void UpdateActiveStates()
    {
        var e = Eva1;
        bool lu = GetSwitch(DcuEva1Batt), ps = GetSwitch(DcuEva1BattSource), oxy = GetSwitch(DcuEva1Oxy), fan = GetSwitch(DcuEva1Fan);
        bool o2Err = CurrentError is TssError.O2Low or TssError.O2High;

        foreach (var f in e.All()) f.Active = true;
        e.primaryBatteryLevel.Active = !lu && ps;
        e.secondaryBatteryLevel.Active = !lu && !ps;
        e.primaryOxygenStorage.Active = oxy || o2Err;
        e.secondaryOxygenStorage.Active = !oxy;
        e.primaryFanRpm.Active = fan || CurrentError == TssError.FanLow;
        e.secondaryFanRpm.Active = !fan;
        e.coolantLiquidPressure.Active = GetSwitch(DcuEva1Pump);
        if (GetSwitch(UiaEva1Power) && lu && GetSwitch(UiaOxyVent))
            e.primaryOxygenStorage.Active = e.secondaryOxygenStorage.Active = true;
    }

    // On the secondary tank suit O2 recovers to 4 psi, on the primary it keeps failing.
    private void UpdateO2Error()
    {
        var suitOxy = Eva1.suitPressureOxygen;
        bool oxy = GetSwitch(DcuEva1Oxy);
        if (!oxy)
        {
            if (CurrentError == TssError.O2Low) { suitOxy.Set(TssFieldAlgo.Growth, OxyRate); suitOxy.max = 4f; }
            if (CurrentError == TssError.O2High) { suitOxy.Set(TssFieldAlgo.Decay, OxyRate); suitOxy.min = 4f; }
        }
        else if (CurrentError == TssError.O2Low) { suitOxy.Set(TssFieldAlgo.Decay, OxyErrorRate); suitOxy.min = 0f; }
        else if (CurrentError == TssError.O2High) { suitOxy.Set(TssFieldAlgo.Growth, OxyErrorRate); suitOxy.max = 4.2f; }
        SetError(TssErrorFlag.Oxy, oxy && CurrentError is TssError.O2Low or TssError.O2High);
    }

    // Switching to the secondary fan clears the helmet CO2.
    private void UpdateFanError()
    {
        bool fan = GetSwitch(DcuEva1Fan);
        bool fanFailed = Eva1.primaryFanRpm.algo == TssFieldAlgo.Decay;
        if (!fan && fanFailed) Eva1.helmetPressureCo2.Set(TssFieldAlgo.Decay, 1000f);
        SetError(TssErrorFlag.Fan, fan && fanFailed);
    }

    private void UpdatePowerError()
    {
        bool onPrimaryBattery = !GetSwitch(DcuEva1Batt) && GetSwitch(DcuEva1BattSource);
        SetError(TssErrorFlag.Power, onPrimaryBattery && Eva1.primaryBatteryLevel.Value < 20f);
    }

    // The selected scrubber fills while the other vents, past 30% suit CO2 rises and past 60% it's an error.
    private void UpdateScrubbers()
    {
        var e = Eva1;
        bool co2 = GetSwitch(DcuEva1Co2);
        e.scrubberACo2Storage.algo = co2 ? TssFieldAlgo.Growth : TssFieldAlgo.Decay;
        e.scrubberBCo2Storage.algo = co2 ? TssFieldAlgo.Decay : TssFieldAlgo.Growth;
        var filling = co2 ? e.scrubberACo2Storage : e.scrubberBCo2Storage;
        e.suitPressureCo2.algo = filling.Value > 30f ? TssFieldAlgo.Growth : TssFieldAlgo.Decay;
        SetError(TssErrorFlag.Scrubber, filling.Value > 60f);
    }

    private void UpdateFanValues()
    {
        bool fan = GetSwitch(DcuEva1Fan);
        Eva1.primaryFanRpm.Value = fan && CurrentError != TssError.FanLow ? 30000f : 0f;
        Eva1.secondaryFanRpm.Value = fan ? 0f : 30000f;
    }

    // While plugged into the UIA: O2 vent / fill, suit pressurization and coolant fill / drain.
    private void UpdateUiaConnected()
    {
        var e = Eva1;
        TssField pri = e.primaryOxygenStorage, sec = e.secondaryOxygenStorage, suitOxy = e.suitPressureOxygen;
        bool oxy = GetSwitch(DcuEva1Oxy), vent = GetSwitch(UiaOxyVent), emuOxy = GetSwitch(UiaEva1Oxy);

        if (vent)
        {
            pri.Set(TssFieldAlgo.Decay, OxyVentRate);
            sec.Set(TssFieldAlgo.Decay, OxyVentRate);
        }
        else
        {
            var (active, idle) = oxy ? (pri, sec) : (sec, pri);
            if (emuOxy) { active.Set(TssFieldAlgo.Growth, OxyFillRate); idle.Set(TssFieldAlgo.Decay, OxyRate); }
            else { active.Set(TssFieldAlgo.Decay, OxyRate); idle.Set(TssFieldAlgo.Decay, 0f); }
        }

        float tankPsi = (oxy ? e.primaryOxygenPressure : e.secondaryOxygenPressure).Value;
        if (!vent && !emuOxy && oxy && tankPsi > 1000f)
        {
            if (suitOxy.Value < 4f) suitOxy.algo = TssFieldAlgo.Growth;
            else { suitOxy.Value = 4f; suitOxy.algo = TssFieldAlgo.Constant; }
        }

        e.suitPressureCo2.Value = 0f;
        e.suitPressureCo2.algo = TssFieldAlgo.Constant;

        bool pump = GetSwitch(DcuEva1Pump), supply = GetSwitch(UiaEva1WaterSupply), waste = GetSwitch(UiaEva1WaterWaste);
        if (pump && !supply && waste) e.coolantStorage.Set(TssFieldAlgo.Decay, CoolantDrainRate);
        else if (pump && supply && !waste) e.coolantStorage.Set(TssFieldAlgo.Growth, CoolantFillRate);
        else e.coolantStorage.Set(TssFieldAlgo.Decay, CoolantRate);
    }

    // Plain values step first, then the formulas that read them.
    private void StepValues()
    {
        _simTime++;
        if (CurrentError == TssError.None && _simTime == randomErrorAt)
            ThrowError((TssError)UnityEngine.Random.Range(1, 4));

        foreach (var f in Eva1.All()) if (f.Formula == null) f.Step();
        foreach (var f in Eva1.All()) if (f.Formula != null) f.Step();
    }
}
