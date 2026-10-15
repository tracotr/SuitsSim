using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static TssSwitch;

// Every EV1 telemetry value the TSS simulates; the defaults are the real TSS eva1.json values (⋮ → Reset restores them).
[CreateAssetMenu(fileName = "EvaData", menuName = "TSS/Data/EvaData")]
public class EvaData : ScriptableObject
{
    public TssField primaryBatteryLevel = new("Primary Battery", TssFieldAlgo.Decay, start: 100, rate: 0.05f);
    public TssField secondaryBatteryLevel = new("Secondary Battery", TssFieldAlgo.Decay, start: 100, rate: 0.05f);

    public TssField primaryOxygenStorage = new("Primary O2 Storage", TssFieldAlgo.Decay, start: 100, rate: 0.013f);
    public TssField secondaryOxygenStorage = new("Secondary O2 Storage", TssFieldAlgo.Decay, start: 100, rate: 0.013f);

    public TssField primaryOxygenPressure = new("Primary O2 Pressure", TssFieldAlgo.Dependent);
    public TssField secondaryOxygenPressure = new("Secondary O2 Pressure", TssFieldAlgo.Dependent);

    public TssField suitPressureOxygen = new("Suit O2 Pressure", TssFieldAlgo.Constant, min: 3, max: 4, rate: 0.26f);
    public TssField suitPressureCo2 = new("Suit CO2 Pressure", TssFieldAlgo.Growth, max: 0.1f, rate: 0.001f);
    public TssField suitPressureOther = new("Suit Other Pressure", TssFieldAlgo.Constant, max: 0.1f);
    public TssField suitPressureTotal = new("Suit Total Pressure", TssFieldAlgo.Dependent);
    public TssField helmetPressureCo2 = new("Helmet CO2 Pressure", TssFieldAlgo.Dependent);

    public TssField primaryFanRpm = new("Primary Fan", TssFieldAlgo.Constant, start: 30000, max: 30500, rate: 13574);
    public TssField secondaryFanRpm = new("Secondary Fan", TssFieldAlgo.Constant, start: 30000, max: 30500, rate: 10);

    public TssField scrubberACo2Storage = new("Scrubber A", TssFieldAlgo.Growth, rate: 0.13f);
    public TssField scrubberBCo2Storage = new("Scrubber B", TssFieldAlgo.Growth, rate: 0.13f);

    public TssField temperature = new("Temperature", TssFieldAlgo.Sine, start: 21.1f, min: -10, max: 40, baseV: 21.1f, amp: 5.1f, freq: 0.018f);
    public TssField coolantStorage = new("Coolant Storage", TssFieldAlgo.Decay, rate: 0.05f);
    public TssField coolantGasPressure = new("Coolant Gas Pressure", TssFieldAlgo.Dependent);
    public TssField coolantLiquidPressure = new("Coolant Liquid Pressure", TssFieldAlgo.Sine, min: 480, max: 520, baseV: 500, amp: 2, freq: 0.015f);

    public TssField heartRate = new("Heart Rate", TssFieldAlgo.Dependent);
    public TssField oxygenConsumption = new("O2 Consumption", TssFieldAlgo.Dependent);
    public TssField co2Production = new("CO2 Production", TssFieldAlgo.Dependent);

    public TssField elapsedTime = new("Elapsed Time", TssFieldAlgo.Growth, max: 10000, rate: 1);

    public TssField Get(TssValue v) => v switch
    {
        TssValue.PrimaryBatteryLevel => primaryBatteryLevel,
        TssValue.SecondaryBatteryLevel => secondaryBatteryLevel,
        TssValue.PrimaryOxygenStorage => primaryOxygenStorage,
        TssValue.SecondaryOxygenStorage => secondaryOxygenStorage,
        TssValue.PrimaryOxygenPressure => primaryOxygenPressure,
        TssValue.SecondaryOxygenPressure => secondaryOxygenPressure,
        TssValue.SuitPressureOxygen => suitPressureOxygen,
        TssValue.SuitPressureCo2 => suitPressureCo2,
        TssValue.SuitPressureOther => suitPressureOther,
        TssValue.SuitPressureTotal => suitPressureTotal,
        TssValue.HelmetPressureCo2 => helmetPressureCo2,
        TssValue.PrimaryFanRpm => primaryFanRpm,
        TssValue.SecondaryFanRpm => secondaryFanRpm,
        TssValue.ScrubberACo2Storage => scrubberACo2Storage,
        TssValue.ScrubberBCo2Storage => scrubberBCo2Storage,
        TssValue.Temperature => temperature,
        TssValue.CoolantStorage => coolantStorage,
        TssValue.CoolantGasPressure => coolantGasPressure,
        TssValue.CoolantLiquidPressure => coolantLiquidPressure,
        TssValue.HeartRate => heartRate,
        TssValue.OxygenConsumption => oxygenConsumption,
        TssValue.Co2Production => co2Production,
        TssValue.ElapsedTime => elapsedTime,
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    private const float OxyVentRate = 4.3f, OxyFillRate = 2.5f, OxyRate = 0.01f, OxyErrorRate = 0.1f,
        Co2Rate = 0.01f, CoolantFillRate = 5.76f, CoolantDrainRate = 6.78f, CoolantRate = 0.01f;

    // Hooks up the Dependent formulas and puts every value at its start.
    public void Init()
    {
        primaryOxygenPressure.Formula = () => primaryOxygenStorage.Value * 30f;
        secondaryOxygenPressure.Formula = () => secondaryOxygenStorage.Value * 30f;
        suitPressureTotal.Formula = () => suitPressureOxygen.Value + suitPressureCo2.Value + suitPressureOther.Value;
        helmetPressureCo2.Formula = () => suitPressureCo2.Value * 1.5f;
        coolantGasPressure.Formula = () => 0f;
        heartRate.Formula = () => 120f + temperature.Value * 1.55f;
        oxygenConsumption.Formula = () => 0.1f + 0.0001f * heartRate.Value;
        co2Production.Formula = () => 0.11f + 0.0002f * heartRate.Value;

        foreach (var field in All()) field.Init();
        ComputeFormulas();
    }

    public void ResetValues()
    {
        foreach (var field in All()) field.Reset();
        ComputeFormulas();
    }

    // Plain values step first, then the formulas that read them.
    public void Step()
    {
        foreach (var field in All()) if (field.Formula == null) field.Step();
        foreach (var field in All()) if (field.Formula != null) field.Step();
    }

    // O2 faults only start while the DCU is on the primary tank.
    public void ThrowError(TssSim sim)
    {
        bool onPrimaryOxygen = sim.GetSwitch(DcuEva1Oxy);
        switch (sim.CurrentError)
        {
            case TssError.O2Low:
                if (onPrimaryOxygen) suitPressureOxygen.Set(TssFieldAlgo.Decay, OxyErrorRate);
                break;
            case TssError.O2High:
                if (onPrimaryOxygen) suitPressureOxygen.Set(TssFieldAlgo.Growth, OxyErrorRate);
                break;
            case TssError.FanLow:
                primaryFanRpm.algo = TssFieldAlgo.Decay;
                helmetPressureCo2.Set(TssFieldAlgo.Growth, Co2Rate);
                break;
        }
    }

    // The DCU decides which battery, tank and fan are in use, unused ones hold still.
    public void UpdateActiveStates(TssSim sim)
    {
        bool onUmbilical = sim.GetSwitch(DcuEva1Batt), primaryBatterySelected = sim.GetSwitch(DcuEva1BattSource);
        bool onPrimaryOxygen = sim.GetSwitch(DcuEva1Oxy), onPrimaryFan = sim.GetSwitch(DcuEva1Fan);
        bool ventingTanks = sim.UiaConnected && sim.GetSwitch(UiaOxyVent);

        foreach (var field in All()) field.Active = true;

        primaryBatteryLevel.Active = !onUmbilical && primaryBatterySelected;
        secondaryBatteryLevel.Active = !onUmbilical && !primaryBatterySelected;
        primaryOxygenStorage.Active = onPrimaryOxygen || sim.CurrentError is TssError.O2Low or TssError.O2High || ventingTanks;
        secondaryOxygenStorage.Active = !onPrimaryOxygen || ventingTanks;
        primaryFanRpm.Active = onPrimaryFan || sim.CurrentError == TssError.FanLow;
        secondaryFanRpm.Active = !onPrimaryFan;
        coolantLiquidPressure.Active = sim.GetSwitch(DcuEva1Pump);
    }

    // On the secondary tank suit O2 recovers to 4 psi, on the primary it keeps failing.
    public void UpdateOxygen(TssSim sim)
    {
        bool onPrimaryOxygen = sim.GetSwitch(DcuEva1Oxy);
        var error = sim.CurrentError;
        if (!onPrimaryOxygen)
        {
            if (error == TssError.O2Low) { suitPressureOxygen.Set(TssFieldAlgo.Growth, OxyRate); suitPressureOxygen.max = 4f; }
            if (error == TssError.O2High) { suitPressureOxygen.Set(TssFieldAlgo.Decay, OxyRate); suitPressureOxygen.min = 4f; }
        }
        else if (error == TssError.O2Low) { suitPressureOxygen.Set(TssFieldAlgo.Decay, OxyErrorRate); suitPressureOxygen.min = 0f; }
        else if (error == TssError.O2High) { suitPressureOxygen.Set(TssFieldAlgo.Growth, OxyErrorRate); suitPressureOxygen.max = 4.2f; }
    }

    // Switching to the secondary fan clears the helmet CO2.
    public void UpdateFans(TssSim sim)
    {
        bool onPrimaryFan = sim.GetSwitch(DcuEva1Fan);
        bool primaryFanFailed = primaryFanRpm.algo == TssFieldAlgo.Decay;
        if (!onPrimaryFan && primaryFanFailed) helmetPressureCo2.Set(TssFieldAlgo.Decay, 1000f);
        primaryFanRpm.Value = onPrimaryFan && sim.CurrentError != TssError.FanLow ? 30000f : 0f;
        secondaryFanRpm.Value = onPrimaryFan ? 0f : 30000f;
    }

    // The selected scrubber fills while the other vents, past 30% suit CO2 rises.
    public void UpdateScrubbers(TssSim sim)
    {
        bool scrubberASelected = sim.GetSwitch(DcuEva1Co2);
        scrubberACo2Storage.algo = scrubberASelected ? TssFieldAlgo.Growth : TssFieldAlgo.Decay;
        scrubberBCo2Storage.algo = scrubberASelected ? TssFieldAlgo.Decay : TssFieldAlgo.Growth;
        var fillingScrubber = scrubberASelected ? scrubberACo2Storage : scrubberBCo2Storage;
        suitPressureCo2.algo = fillingScrubber.Value > 30f ? TssFieldAlgo.Growth : TssFieldAlgo.Decay;
    }

    public bool HasError(TssErrorFlag flag, TssSim sim) => flag switch
    {
        TssErrorFlag.Oxy => sim.GetSwitch(DcuEva1Oxy) && sim.CurrentError is TssError.O2Low or TssError.O2High,
        TssErrorFlag.Fan => sim.GetSwitch(DcuEva1Fan) && primaryFanRpm.algo == TssFieldAlgo.Decay,
        TssErrorFlag.Power => !sim.GetSwitch(DcuEva1Batt) && sim.GetSwitch(DcuEva1BattSource) && primaryBatteryLevel.Value < 20f,
        TssErrorFlag.Scrubber => (sim.GetSwitch(DcuEva1Co2) ? scrubberACo2Storage : scrubberBCo2Storage).Value > 60f,
        _ => false,
    };

    // While plugged into the UIA: O2 vent / fill, suit pressurization and coolant fill / drain.
    public void UpdateUiaConnected(TssSim sim)
    {
        if (!sim.UiaConnected) return;

        bool onPrimaryOxygen = sim.GetSwitch(DcuEva1Oxy), oxygenVentOpen = sim.GetSwitch(UiaOxyVent), emuOxygenOpen = sim.GetSwitch(UiaEva1Oxy);
        if (oxygenVentOpen)
        {
            primaryOxygenStorage.Set(TssFieldAlgo.Decay, OxyVentRate);
            secondaryOxygenStorage.Set(TssFieldAlgo.Decay, OxyVentRate);
        }
        else
        {
            var (activeTank, idleTank) = onPrimaryOxygen ? (primaryOxygenStorage, secondaryOxygenStorage) : (secondaryOxygenStorage, primaryOxygenStorage);
            if (emuOxygenOpen) { activeTank.Set(TssFieldAlgo.Growth, OxyFillRate); idleTank.Set(TssFieldAlgo.Decay, OxyRate); }
            else { activeTank.Set(TssFieldAlgo.Decay, OxyRate); idleTank.Set(TssFieldAlgo.Decay, 0f); }
        }

        float activeTankPressure = (onPrimaryOxygen ? primaryOxygenPressure : secondaryOxygenPressure).Value;
        if (!oxygenVentOpen && !emuOxygenOpen && onPrimaryOxygen && activeTankPressure > 1000f)
        {
            if (suitPressureOxygen.Value < 4f) suitPressureOxygen.algo = TssFieldAlgo.Growth;
            else { suitPressureOxygen.Value = 4f; suitPressureOxygen.algo = TssFieldAlgo.Constant; }
        }

        suitPressureCo2.Value = 0f;
        suitPressureCo2.algo = TssFieldAlgo.Constant;

        bool pumpOpen = sim.GetSwitch(DcuEva1Pump), waterSupplyOpen = sim.GetSwitch(UiaEva1WaterSupply), waterWasteOpen = sim.GetSwitch(UiaEva1WaterWaste);
        if (pumpOpen && !waterSupplyOpen && waterWasteOpen) coolantStorage.Set(TssFieldAlgo.Decay, CoolantDrainRate);
        else if (pumpOpen && waterSupplyOpen && !waterWasteOpen) coolantStorage.Set(TssFieldAlgo.Growth, CoolantFillRate);
        else coolantStorage.Set(TssFieldAlgo.Decay, CoolantRate);
    }

    // Dependent fields start at 0, so fill them in before the first tick.
    private void ComputeFormulas()
    {
        foreach (var field in All()) if (field.Formula != null) field.Value = field.Formula();
    }

    // Every TssField in the same order every time for ticking.
    public IEnumerable<TssField> All()
    {
        return from TssValue v in Enum.GetValues(typeof(TssValue)) select Get(v);
    }
}

public enum TssValue
{
    PrimaryBatteryLevel,
    SecondaryBatteryLevel,

    PrimaryOxygenStorage,
    SecondaryOxygenStorage,

    PrimaryOxygenPressure,
    SecondaryOxygenPressure,

    SuitPressureOxygen,
    SuitPressureCo2,
    SuitPressureOther,
    SuitPressureTotal,
    HelmetPressureCo2,

    PrimaryFanRpm,
    SecondaryFanRpm,

    ScrubberACo2Storage,
    ScrubberBCo2Storage,

    Temperature,

    CoolantStorage,
    CoolantGasPressure,
    CoolantLiquidPressure,

    HeartRate,
    OxygenConsumption,
    Co2Production,
    ElapsedTime,
}

public enum TssFieldAlgo
{
    Sine,
    Decay,
    Growth,
    Dependent,
    Constant
}

// One telemetry value and how it changes each second.
[Serializable]
public class TssField
{
    [Tooltip("Display name of the value.")]
    public string name;

    [Tooltip("How the value changes each second.")]
    public TssFieldAlgo algo;

    [Tooltip("Value at the start of the EVA (Sine starts at Base V, Dependent at 0).")]
    public float start;

    [Tooltip("Decay stops here.")]
    public float min;

    [Tooltip("Growth stops here.")]
    public float max = 100f;

    [Tooltip("Change per second for Decay and Growth.")]
    public float rate;

    [Tooltip("Center of the Sine wave.")]
    public float baseV;

    [Tooltip("Height of the Sine wave.")]
    public float amp;

    [Tooltip("Sine phase step per second.")]
    public float freq;

    public event Action<float> OnValueChanged;

    public TssField() { }

    public TssField(string name, TssFieldAlgo algo, float start = 0, float min = 0, float max = 100,
        float rate = 0, float baseV = 0, float amp = 0, float freq = 0)
    {
        this.name = name;
        this.algo = algo;
        this.start = start;
        this.min = min;
        this.max = max;
        this.rate = rate;
        this.baseV = baseV;
        this.amp = amp;
        this.freq = freq;
    }

    // runtime values
    [NonSerialized] public TssFieldAlgo StartAlgo;
    [NonSerialized] public float StartRate, StartMin, StartMax, Phase;
    [NonSerialized] public bool Active = true;
    [NonSerialized] public Func<float> Formula;
    [NonSerialized] private float _value;

    public float Value
    {
        get => _value;
        set
        {
            if (Mathf.Approximately(_value, value)) return;
            _value = value;
            OnValueChanged?.Invoke(_value);
        }
    }
    
    public void Init()
    {
        StartAlgo = algo;
        StartRate = rate;
        StartMin = min;
        StartMax = max;
        Reset();
    }

    public void Reset()
    {
        algo = StartAlgo;
        rate = StartRate;
        min = StartMin;
        max = StartMax;
        Phase = 0f;
        Active = true;
        Value = algo switch
        {
            TssFieldAlgo.Sine => baseV,
            TssFieldAlgo.Dependent => 0f,
            _ => start
        };
    }

    public void Set(TssFieldAlgo newAlgo, float newRate)
    {
        algo = newAlgo;
        rate = newRate;
    }

    // Advances the value by one second.
    public void Step()
    {
        if (!Active) return;
        switch (algo)
        {
            case TssFieldAlgo.Sine:
                Value = baseV + amp * Mathf.Sin(Phase);
                Phase += freq;

                if (Phase > 2f * Math.PI)
                    Phase = 0f;
                break;

            case TssFieldAlgo.Decay:
                Value = Mathf.Max(Value - rate, min); break;

            case TssFieldAlgo.Growth:
                Value = Mathf.Min(Value + rate, max); break;

            case TssFieldAlgo.Dependent:
                Value = Formula?.Invoke() ?? Value; break;

            case TssFieldAlgo.Constant:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
