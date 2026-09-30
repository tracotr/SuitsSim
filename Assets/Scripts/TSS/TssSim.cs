using System;
using System.Collections.Generic;
using UnityEngine;
using static TssSwitch;

// UIA and DCU switches. 2001-2016 are the real TSS command numbers; EVA 2's DCU has none,
// so it uses 2111+ and nothing in the sim reads it yet. Inspector names show what On means.
public enum TssSwitch
{
    // UIA, EVA 1
    [InspectorName("UIA/EVA 1/EMU Power (On)")] UiaEva1Power = 2001,
    [InspectorName("UIA/EVA 1/O2 (Open)")] UiaEva1Oxy = 2002,
    [InspectorName("UIA/EVA 1/Water Supply (Open)")] UiaEva1WaterSupply = 2003,
    [InspectorName("UIA/EVA 1/Water Waste (Open)")] UiaEva1WaterWaste = 2004,

    // UIA, EVA 2
    [InspectorName("UIA/EVA 2/EMU Power (On)")] UiaEva2Power = 2005,
    [InspectorName("UIA/EVA 2/O2 (Open)")] UiaEva2Oxy = 2006,
    [InspectorName("UIA/EVA 2/Water Supply (Open)")] UiaEva2WaterSupply = 2007,
    [InspectorName("UIA/EVA 2/Water Waste (Open)")] UiaEva2WaterWaste = 2008,

    // UIA, shared
    [InspectorName("UIA/O2 Vent (Open)")] UiaOxyVent = 2009,
    [InspectorName("UIA/Depress Pump (On)")] UiaDepress = 2010,

    // DCU, EVA 1
    [InspectorName("DCU/EVA 1/Batt (UMB)")] DcuEva1Batt = 2011,
    [InspectorName("DCU/EVA 1/OXY (PRI)")] DcuEva1Oxy = 2012,
    [InspectorName("DCU/EVA 1/Batt Source (PRI)")] DcuEva1BattSource = 2013,
    [InspectorName("DCU/EVA 1/Fan (PRI)")] DcuEva1Fan = 2014,
    [InspectorName("DCU/EVA 1/Pump (Open)")] DcuEva1Pump = 2015,
    [InspectorName("DCU/EVA 1/CO2 (PRI)")] DcuEva1Co2 = 2016,

    // DCU, EVA 2
    [InspectorName("DCU/EVA 2/Batt (UMB)")] DcuEva2Batt = 2111,
    [InspectorName("DCU/EVA 2/OXY (PRI)")] DcuEva2Oxy = 2112,
    [InspectorName("DCU/EVA 2/Batt Source (PRI)")] DcuEva2BattSource = 2113,
    [InspectorName("DCU/EVA 2/Fan (PRI)")] DcuEva2Fan = 2114,
    [InspectorName("DCU/EVA 2/Pump (Open)")] DcuEva2Pump = 2115,
    [InspectorName("DCU/EVA 2/CO2 (PRI)")] DcuEva2Co2 = 2116,
}

public enum TssError { None, O2Low, O2High, FanLow }

[DefaultExecutionOrder(-100)]
public class TssSim : MonoBehaviour
{
    public static TssSim Instance { get; private set;  }

    [SerializeField] private EvaData defaultEvaData;
    [Tooltip("Start the EVA as soon as play mode begins.")]
    public bool startOnPlay = true;
    [Tooltip("Seconds into the EVA when a random suit fault is thrown. -1 = never.")]
    public int randomErrorAt = -1;

    // Raised after every once-per-second update.
    public event Action Ticked;
    // Raised only when a switch actually changes.
    public event Action<TssSwitch, bool> SwitchChanged;

    public EvaData Eva1 { get; private set; }
    public bool Running { get; private set; }
    public TssError CurrentError { get; private set; }
    public bool FanError { get; private set; }
    public bool OxyError { get; private set; }
    public bool PowerError { get; private set; }
    public bool ScrubberError { get; private set; }
    public float PosX { get; private set; }
    public float PosY { get; private set; }
    public float Heading { get; private set; }

    // Per-second change rates (tank % or psi) used while filling, venting and during faults.
    private const float OxyVentRate = 4.3f, OxyFillRate = 2.5f, OxyRate = 0.01f, OxyErrorRate = 0.1f,
        Co2Rate = 0.01f, CoolantFillRate = 5.76f, CoolantDrainRate = 6.78f, CoolantRate = 0.01f;

    private readonly HashSet<TssSwitch> _on = new HashSet<TssSwitch>();
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

    private void Update()
    {
        _tickTimer += Time.deltaTime;
        if (_tickTimer < 1f) return;
        _tickTimer -= 1f;
        Advance(1);
    }

    // Runs `seconds` ticks right now, raising Ticked after each. Used by Update and tests.
    public void Advance(int seconds)
    {
        for (int i = 0; i < seconds; i++)
        {
            Tick();
            Ticked?.Invoke();
        }
    }

    public bool GetSwitch(TssSwitch s) => _on.Contains(s);

    // The sim reacts on its next tick.
    public void SetSwitch(TssSwitch s, bool value)
    {
        if (value ? _on.Add(s) : _on.Remove(s)) SwitchChanged?.Invoke(s, value);
    }

    public void SetImu(float x, float y, float heading)
    {
        PosX = x;
        PosY = y;
        Heading = heading;
    }

    [ContextMenu("Start EVA")]
    public void StartEva() => Running = true;

    // Stops the EVA and puts telemetry back to its starting values. Switches are kept.
    [ContextMenu("Reset EVA")]
    public void ResetEva()
    {
        Running = false;
        _simTime = 0;
        CurrentError = TssError.None;
        foreach (var f in Eva1.All()) f.Reset();
        ComputeFormulas();
    }

    // Dependent fields start at 0; fill them in now so they're right before the first tick.
    private void ComputeFormulas()
    {
        foreach (var f in Eva1.All()) if (f.Formula != null) f.Value = f.Formula();
    }

    // CAPCOM's pre-EVA step: every UIA switch down, every DCU switch back.
    [ContextMenu("Reset Switches")]
    public void ResetSwitches()
    {
        foreach (var s in new List<TssSwitch>(_on)) SetSwitch(s, false);
    }

    // O2 faults only start while the DCU is on the primary tank. Fix them with DCU OXY -> SEC,
    // and a fan fault with DCU FAN -> SEC.
    public void ThrowError(TssError type)
    {
        CurrentError = type;
        var e = Eva1;
        bool oxyPri = GetSwitch(DcuEva1Oxy);
        switch (type)
        {
            // Suit O2 pressure drops fast.
            case TssError.O2Low:
                if (oxyPri) e.suitPressureOxygen.Set(TssFieldAlgo.Decay, OxyErrorRate);
                break;
            // Suit O2 pressure climbs fast.
            case TssError.O2High:
                if (oxyPri) e.suitPressureOxygen.Set(TssFieldAlgo.Growth, OxyErrorRate);
                break;
            // Primary fan dies, so CO2 builds up in the helmet.
            case TssError.FanLow:
                e.primaryFanRpm.algo = TssFieldAlgo.Decay;
                e.helmetPressureCo2.Set(TssFieldAlgo.Growth, Co2Rate);
                break;
        }
    }

    [ContextMenu("Throw O2 Low")] private void ThrowO2Low() => ThrowError(TssError.O2Low);
    [ContextMenu("Throw O2 High")] private void ThrowO2High() => ThrowError(TssError.O2High);
    [ContextMenu("Throw Fan Low")] private void ThrowFanLow() => ThrowError(TssError.FanLow);
    
    
    private void Tick()
    {
        var e = Eva1;
        
        bool lu = GetSwitch(DcuEva1Batt), ps = GetSwitch(DcuEva1BattSource), oxy = GetSwitch(DcuEva1Oxy),
            fan = GetSwitch(DcuEva1Fan), pump = GetSwitch(DcuEva1Pump), co2 = GetSwitch(DcuEva1Co2);
        
        bool vent = GetSwitch(UiaOxyVent), emuOxy = GetSwitch(UiaEva1Oxy),
            supply = GetSwitch(UiaEva1WaterSupply), waste = GetSwitch(UiaEva1WaterWaste);
        
        bool uiaConnected = GetSwitch(UiaEva1Power) && lu;
        
        bool o2Err = CurrentError is TssError.O2Low or TssError.O2High;

        TssField pri = e.primaryOxygenStorage, sec = e.secondaryOxygenStorage, suitOxy = e.suitPressureOxygen,
            suitCo2 = e.suitPressureCo2, fanPri = e.primaryFanRpm;

        // DCU selection decides which battery, tank and fan are in use. Unused ones hold still.
        foreach (var f in e.All()) f.Active = true;
        
        e.primaryBatteryLevel.Active = !lu && ps;
        e.secondaryBatteryLevel.Active = !lu && !ps;
        pri.Active = oxy || o2Err;
        sec.Active = !oxy;
        fanPri.Active = fan || CurrentError == TssError.FanLow;
        e.secondaryFanRpm.Active = !fan;
        e.coolantLiquidPressure.Active = pump;
        if (uiaConnected && vent) pri.Active = sec.Active = true;

        // O2 fault: on the secondary tank, suit O2 recovers to 4 psi; on primary it keeps failing.
        if (!oxy)
        {
            if (CurrentError == TssError.O2Low) { suitOxy.Set(TssFieldAlgo.Growth, OxyRate); suitOxy.max = 4f; }
            if (CurrentError == TssError.O2High) { suitOxy.Set(TssFieldAlgo.Decay, OxyRate); suitOxy.min = 4f; }
        }
        else if (CurrentError == TssError.O2Low) { suitOxy.Set(TssFieldAlgo.Decay, OxyErrorRate); suitOxy.min = 0f; }
        else if (CurrentError == TssError.O2High) { suitOxy.Set(TssFieldAlgo.Growth, OxyErrorRate); suitOxy.max = 4.2f; }
        OxyError = oxy && o2Err;

        // Fan fault: switching to the secondary fan clears the helmet CO2.
        bool fanFailed = fanPri.algo == TssFieldAlgo.Decay;
        if (!fan && fanFailed) e.helmetPressureCo2.Set(TssFieldAlgo.Decay, 1000f);
        FanError = fan && fanFailed;

        // Power fault: primary battery under 20% while running on it.
        PowerError = !lu && ps && e.primaryBatteryLevel.Value < 20f;

        // CO2 scrubbers: the selected one fills while the other vents. Once the filling one
        // passes 30%, suit CO2 starts rising; past 60% it raises a scrubber error.
        e.scrubberACo2Storage.algo = co2 ? TssFieldAlgo.Growth : TssFieldAlgo.Decay;
        e.scrubberBCo2Storage.algo = co2 ? TssFieldAlgo.Decay : TssFieldAlgo.Growth;
        var filling = co2 ? e.scrubberACo2Storage : e.scrubberBCo2Storage;
        suitCo2.algo = filling.Value > 30f ? TssFieldAlgo.Growth : TssFieldAlgo.Decay;
        ScrubberError = filling.Value > 60f;

        // Selected fan spins at 30,000 rpm, the other is off. A failed primary fan reads 0.
        fanPri.Value = fan && CurrentError != TssError.FanLow ? 30000f : 0f;
        e.secondaryFanRpm.Value = fan ? 0f : 30000f;

        // While plugged into the UIA: egress behavior (O2 vent/fill, suit pressurization, coolant).
        if (uiaConnected)
        {
            // O2 VENT open empties both tanks. Otherwise EMU O2 open fills the DCU-selected tank.
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

            // With valves closed and a full primary tank, the suit pressurizes up to 4 psi.
            float tankPsi = (oxy ? e.primaryOxygenPressure : e.secondaryOxygenPressure).Value;
            if (!vent && !emuOxy && oxy && tankPsi > 1000f)
            {
                if (suitOxy.Value < 4f) suitOxy.algo = TssFieldAlgo.Growth;
                else { suitOxy.Value = 4f; suitOxy.algo = TssFieldAlgo.Constant; }
            }

            suitCo2.Value = 0f; // no CO2 buildup while on the umbilical
            suitCo2.algo = TssFieldAlgo.Constant;

            // Coolant: pump open + waste open drains it, pump open + supply open fills it.
            if (pump && !supply && waste) e.coolantStorage.Set(TssFieldAlgo.Decay, CoolantDrainRate);
            else if (pump && supply && !waste) e.coolantStorage.Set(TssFieldAlgo.Growth, CoolantFillRate);
            else e.coolantStorage.Set(TssFieldAlgo.Decay, CoolantRate);
        }

        if (!Running) return;
        _simTime++;
        if (CurrentError == TssError.None && _simTime == randomErrorAt)
            ThrowError((TssError)UnityEngine.Random.Range(1, 4));
        
        // Plain values first, then the ones computed from them.
        foreach (var f in e.All()) if (f.Formula == null) f.Step();
        foreach (var f in e.All()) if (f.Formula != null) f.Step();
    }

    // Play mode > right-click the component > Self Test. Runs egress O2 prep and a fan fault,
    // checks the results, then resets everything.
    [ContextMenu("Self Test")]
    private void SelfTest()
    {
        if (Eva1 == null) { Debug.LogWarning("TssSim Self Test only runs in Play mode."); return; }
        ResetSwitches();
        ResetEva();
        try
        {
            StartEva();
            SetSwitch(UiaEva1Power, true); SetSwitch(DcuEva1Batt, true); SetSwitch(DcuEva1Oxy, true); SetSwitch(DcuEva1Fan, true);
            SetSwitch(UiaOxyVent, true); Run(30);
            Check(Eva1.primaryOxygenPressure.Value < 10 && Eva1.secondaryOxygenPressure.Value < 10, "vent drains both tanks < 10 psi");
            SetSwitch(UiaOxyVent, false); SetSwitch(UiaEva1Oxy, true); Run(45);
            Check(Eva1.primaryOxygenPressure.Value >= 2999, "primary tank fills to 3000 psi");
            SetSwitch(UiaEva1Oxy, false); Run(20);
            Check(Mathf.Approximately(Eva1.suitPressureOxygen.Value, 4f), "suit O2 pressurizes to 4 psi");

            ThrowError(TssError.FanLow); Run(1);
            Check(FanError && Eva1.primaryFanRpm.Value == 0f, "fan fault raises FanError");
            SetSwitch(DcuEva1Fan, false); Run(1);
            Check(!FanError && Mathf.Approximately(Eva1.secondaryFanRpm.Value, 30000f), "secondary fan clears FanError");
            Debug.Log("TssSim self test passed");
        }
        finally
        {
            ResetSwitches();
            ResetEva();
            Running = startOnPlay;
        }
    }

    private void Run(int seconds) { for (int i = 0; i < seconds; i++) Tick(); }
    private static void Check(bool ok, string what) { if (!ok) throw new Exception("TssSim self test failed: " + what); }
}
