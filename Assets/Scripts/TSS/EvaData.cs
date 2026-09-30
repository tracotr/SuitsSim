using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "EvaData", menuName = "TSS/Data/EvaData")]
public class EvaData : ScriptableObject
{
    public TssField primaryBatteryLevel;
    public TssField secondaryBatteryLevel;

    public TssField primaryOxygenStorage;
    public TssField secondaryOxygenStorage;

    public TssField primaryOxygenPressure;
    public TssField secondaryOxygenPressure;

    public TssField suitPressureOxygen;
    public TssField suitPressureCo2;
    public TssField suitPressureOther;
    public TssField suitPressureTotal;
    public TssField helmetPressureCo2;

    public TssField primaryFanRpm;
    public TssField secondaryFanRpm;

    public TssField scrubberACo2Storage;
    public TssField scrubberBCo2Storage;

    public TssField temperature;
    public TssField coolantStorage;
    public TssField coolantGasPressure;
    public TssField coolantLiquidPressure;

    public TssField heartRate;
    public TssField oxygenConsumption;
    public TssField co2Production;

    public TssField elapsedTime;

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

    // step order = TssValue order
    public IEnumerable<TssField> All()
    {
        foreach (TssValue v in Enum.GetValues(typeof(TssValue))) yield return Get(v);
    }

    // TS starting values
    private void Reset()
    {
        primaryBatteryLevel = F("Primary Battery", TssFieldAlgo.Decay, start: 100, rate: 0.05f);
        secondaryBatteryLevel = F("Secondary Battery", TssFieldAlgo.Decay, start: 100, rate: 0.05f);
        primaryOxygenStorage = F("Primary O2 Storage", TssFieldAlgo.Decay, start: 100, rate: 0.013f);
        secondaryOxygenStorage = F("Secondary O2 Storage", TssFieldAlgo.Decay, start: 100, rate: 0.013f);
        primaryOxygenPressure = F("Primary O2 Pressure", TssFieldAlgo.Dependent);
        secondaryOxygenPressure = F("Secondary O2 Pressure", TssFieldAlgo.Dependent);
        suitPressureOxygen = F("Suit O2 Pressure", TssFieldAlgo.Constant, min: 3, max: 4, rate: 0.26f);
        suitPressureCo2 = F("Suit CO2 Pressure", TssFieldAlgo.Growth, max: 0.1f, rate: 0.001f);
        suitPressureOther = F("Suit Other Pressure", TssFieldAlgo.Constant, max: 0.1f);
        suitPressureTotal = F("Suit Total Pressure", TssFieldAlgo.Dependent);
        helmetPressureCo2 = F("Helmet CO2 Pressure", TssFieldAlgo.Dependent);
        primaryFanRpm = F("Primary Fan", TssFieldAlgo.Constant, start: 30000, max: 30500, rate: 13574);
        secondaryFanRpm = F("Secondary Fan", TssFieldAlgo.Constant, start: 30000, max: 30500, rate: 10);
        scrubberACo2Storage = F("Scrubber A", TssFieldAlgo.Growth, rate: 0.13f);
        scrubberBCo2Storage = F("Scrubber B", TssFieldAlgo.Growth, rate: 0.13f);
        temperature = F("Temperature", TssFieldAlgo.Sine, start: 21.1f, min: -10, max: 40, baseV: 21.1f, amp: 5.1f, freq: 0.018f);
        coolantStorage = F("Coolant Storage", TssFieldAlgo.Decay, rate: 0.05f);
        coolantGasPressure = F("Coolant Gas Pressure", TssFieldAlgo.Dependent);
        coolantLiquidPressure = F("Coolant Liquid Pressure", TssFieldAlgo.Sine, min: 480, max: 520, baseV: 500, amp: 2, freq: 0.015f);
        heartRate = F("Heart Rate", TssFieldAlgo.Dependent);
        oxygenConsumption = F("O2 Consumption", TssFieldAlgo.Dependent);
        co2Production = F("CO2 Production", TssFieldAlgo.Dependent);
        elapsedTime = F("Elapsed Time", TssFieldAlgo.Growth, max: 10000, rate: 1);
    }

    private static TssField F(string name, TssFieldAlgo algo, float start = 0, float min = 0, float max = 100,
        float rate = 0, float baseV = 0, float amp = 0, float freq = 0) => new TssField
    {
        name = name, algo = algo, start = start, min = min, max = max,
        rate = rate, baseV = baseV, amp = amp, freq = freq,
    };
}

// One entry per EvaData field, in step order (plain values step before the formulas that read them).
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

[Serializable]
public class TssField
{
    public string name; // display label
    public TssFieldAlgo algo;
    public float start, min, max = 100f, rate, baseV, amp, freq;

    // Runtime state
    [NonSerialized] public TssFieldAlgo StartAlgo;

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

    [NonSerialized] public Action<float> OnValueChanged;

    [NonSerialized] public float StartRate, StartMin, StartMax, Phase;
    [NonSerialized] public bool Active = true;
    [NonSerialized] public Func<float> Formula;

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

    public void Step()
    {
        if (!Active) return;
        switch (algo)
        {
            case TssFieldAlgo.Sine:
                Value = baseV + amp * Mathf.Sin(Phase);
                Phase += freq;
                if (Phase > 2f * Math.PI) Phase = 0f;
                break;
            case TssFieldAlgo.Decay: Value = Mathf.Max(Value - rate, min); break;
            case TssFieldAlgo.Growth: Value = Mathf.Min(Value + rate, max); break;
            case TssFieldAlgo.Dependent: Value = Formula?.Invoke() ?? Value; break;
            case TssFieldAlgo.Constant:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
