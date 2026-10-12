using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
