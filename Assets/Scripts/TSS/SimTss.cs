using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Simulated Telemetry Stream Server (TSS) for the NASA SUITS EVA.
/// Uses the same command numbers and JSON layout as the real server, so HUD code
/// written against this works unchanged against the real one.
///
///   string json = SimTss.Instance.Get(1);          // read EVA telemetry as JSON
///   bool ok     = SimTss.Instance.Post(2012, 1f);  // flip DCU oxygen to primary
///
/// Telemetry updates once per second. The behavior (rates, formulas, error handling)
/// matches the real TSS, including its odd spots, so values look like the real stream.
/// </summary>
public class SimTss : MonoBehaviour
{
    /// <summary>Suit faults that can be injected.</summary>
    public enum TssError { None, O2Low, O2High, FanLow }

    public static SimTss Instance { get; private set; }

    /// <summary>Raised after every once-per-second telemetry update.</summary>
    public event Action Ticked;

    [Tooltip("Start the EVA as soon as play mode begins.")]
    public bool startOnPlay = true;
    [Tooltip("Seconds into the EVA when a random suit fault is thrown. -1 = never.")]
    public int randomErrorAt = -1;

    // Per-second change rates (tank % or psi) used while filling, venting and during faults.
    const float OxyVentRate = 4.3f, OxyFillRate = 2.5f, OxyRate = 0.01f, OxyErrorRate = 0.1f,
        Co2Rate = 0.01f, CoolantFillRate = 5.76f, CoolantDrainRate = 6.78f, CoolantRate = 0.01f;

    // Command number -> JSON field it writes. Switches are stored as bools, everything else as floats.
    static readonly Dictionary<int, string> PostPaths = new Dictionary<int, string>
    {
        // UIA switches
        {2001, "eva.uia.eva1_power"}, {2002, "eva.uia.eva1_oxy"},
        {2003, "eva.uia.eva1_water_supply"}, {2004, "eva.uia.eva1_water_waste"},
        {2005, "eva.uia.eva2_power"}, {2006, "eva.uia.eva2_oxy"},
        {2007, "eva.uia.eva2_water_supply"}, {2008, "eva.uia.eva2_water_waste"},
        {2009, "eva.uia.oxy_vent"}, {2010, "eva.uia.depress"},

        // DCU switches (EV1)
        {2011, "eva.dcu.eva1.batt.lu"}, {2012, "eva.dcu.eva1.oxy"}, {2013, "eva.dcu.eva1.batt.ps"},
        {2014, "eva.dcu.eva1.fan"}, {2015, "eva.dcu.eva1.pump"}, {2016, "eva.dcu.eva1.co2"},

        // Crew positions and headings
        {2017, "eva.imu.eva1.posx"}, {2018, "eva.imu.eva1.posy"}, {2019, "eva.imu.eva1.heading"},
        {2020, "eva.imu.eva2.posx"}, {2021, "eva.imu.eva2.posy"}, {2022, "eva.imu.eva2.heading"},
    };

    // Starting EVA JSON. The telemetry section is filled in from the simulated values.
    const string EvaTemplate = @"{
  ""telemetry"": { ""eva1"": {}, ""eva2"": {} },
  ""status"": { ""started"": false },
  ""dcu"": {
    ""eva1"": { ""oxy"": false, ""fan"": false, ""pump"": false, ""co2"": false, ""batt"": { ""lu"": false, ""ps"": false } },
    ""eva2"": { ""batt"": false, ""oxy"": false, ""comm"": false, ""fan"": false, ""pump"": false, ""co2"": false }
  },
  ""error"": { ""fan_error"": false, ""oxy_error"": false, ""power_error"": false, ""scrubber_error"": false },
  ""imu"": {
    ""eva1"": { ""posx"": 0.0, ""posy"": 0.0, ""heading"": 0.0 },
    ""eva2"": { ""posx"": 0.0, ""posy"": 0.0, ""heading"": 0.0 }
  },
  ""uia"": {
    ""eva1_power"": false, ""eva1_oxy"": false, ""eva1_water_supply"": false, ""eva1_water_waste"": false,
    ""eva2_power"": false, ""eva2_oxy"": false, ""eva2_water_supply"": false, ""eva2_water_waste"": false,
    ""oxy_vent"": false, ""depress"": false
  }
}";

    // How a value changes each second.
    enum Algo
    {
        Sine,       // oscillates around a base value
        Decay,      // drops by `rate`, stopping at `min`
        Growth,     // rises by `rate`, stopping at `max`
        Dependent,  // computed from other values
        Constant,   // holds its value
    }

    // One telemetry value, e.g. "oxy_pri_storage".
    class Field
    {
        public string name;
        public Algo algo, startAlgo;
        public float value, start, min, max, rate, startRate, baseV, amp, freq, phase;
        public bool active = true;      // inactive values are frozen (e.g. the tank not selected on the DCU)
        public Func<float> formula;     // only for Dependent values

        // Advance this value by one second.
        public void Step()
        {
            if (!active) return;
            switch (algo)
            {
                case Algo.Sine:
                    value = baseV + amp * Mathf.Sin(phase);
                    phase += freq;
                    if (phase > 2f * 3.1415f) phase = 0f;
                    break;
                case Algo.Decay: value = Mathf.Max(value - rate, min); break;
                case Algo.Growth: value = Mathf.Min(value + rate, max); break;
                case Algo.Dependent: value = formula(); break;
            }
        }
    }

    readonly List<Field> eva1 = new List<Field>(), eva2 = new List<Field>(), all = new List<Field>();
    // Lookup by name. EV1 is registered first, so when EV2 shares a name (e.g. "heart_rate"),
    // this returns EV1's value. That means EV2's formulas read EV1's numbers, same as the real TSS.
    readonly Dictionary<string, Field> byName = new Dictionary<string, Field>();

    JObject eva;
    TssError error;
    bool running, fanError, oxyError, powerError, scrubberError;
    int simTime;
    float tickTimer;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(this); return; }
        Instance = this;
        eva = JObject.Parse(EvaTemplate);
        BuildFields();
        running = startOnPlay;
        WriteJson();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        tickTimer += Time.deltaTime; // raise Time.timeScale to fast-forward the EVA
        if (tickTimer < 1f) return;
        tickTimer -= 1f;
        Tick();
        Ticked?.Invoke();
    }

    // ---------------------------------------------------------------- Public API

    /// <summary>Returns the EVA telemetry JSON for command 1, or null for any other command.</summary>
    public string Get(int command) => command == 1 ? eva.ToString() : null;

    /// <summary>
    /// Sets a switch or value by command number (2001-2022). Switches take 0 or 1.
    /// The sim reacts on its next update. Returns false for unknown commands.
    /// </summary>
    public bool Post(int command, float value)
    {
        if (!PostPaths.TryGetValue(command, out var path)) return false;
        // Walk the path (skipping the leading "eva") down to the parent object, then overwrite the field.
        var parts = path.Split('.');
        JToken token = eva;
        for (int i = 1; i < parts.Length - 1 && token != null; i++)
            token = token[parts[i]];
        if (!(token is JObject parent) || !parent.TryGetValue(parts[parts.Length - 1], out var old)) return false;
        parent[parts[parts.Length - 1]] = old.Type == JTokenType.Boolean ? new JValue(value != 0f) : new JValue(value);
        return true;
    }

    public bool Running => running;
    public TssError CurrentError => error;

    /// <summary>Starts the EVA clock and telemetry changes.</summary>
    [ContextMenu("Start EVA")]
    public void StartEva() => running = true;

    /// <summary>Stops the EVA and puts all telemetry back to its starting values. Switch positions are kept.</summary>
    [ContextMenu("Reset EVA")]
    public void ResetEva()
    {
        running = false;
        simTime = 0;
        error = TssError.None;
        foreach (var f in all)
        {
            f.algo = f.startAlgo;
            f.rate = f.startRate;
            f.value = f.start;
            f.phase = 0f;
        }
        WriteJson();
    }

    /// <summary>
    /// Injects a suit fault on EV1. O2 faults only start while the DCU is on the primary tank.
    /// Fix them by switching the DCU: OXY to SEC for O2 faults, FAN to SEC for a fan fault.
    /// </summary>
    public void ThrowError(TssError type)
    {
        error = type;
        bool oxyPri = B(eva["dcu"]["eva1"]["oxy"]);
        switch (type)
        {
            // Suit O2 pressure drops fast.
            case TssError.O2Low:
                if (oxyPri) Set(E("suit_pressure_oxy"), Algo.Decay, OxyErrorRate);
                break;
            // Suit O2 pressure climbs fast.
            case TssError.O2High:
                if (oxyPri) Set(E("suit_pressure_oxy"), Algo.Growth, OxyErrorRate);
                break;
            // Primary fan dies, so CO2 builds up in the helmet.
            case TssError.FanLow:
                E("fan_pri_rpm").algo = Algo.Decay;
                Set(E("helmet_pressure_co2"), Algo.Growth, Co2Rate);
                break;
        }
    }

    // Right-click menu shortcuts on the component, for testing.
    [ContextMenu("Throw O2 Low")] void ThrowO2Low() => ThrowError(TssError.O2Low);
    [ContextMenu("Throw O2 High")] void ThrowO2High() => ThrowError(TssError.O2High);
    [ContextMenu("Throw Fan Low")] void ThrowFanLow() => ThrowError(TssError.FanLow);

    // ---------------------------------------------------------------- Simulation

    // Advances the simulation by one second. Order matters: switches and faults decide
    // how each value will change, then every value steps once, then the JSON is refreshed.
    void Tick()
    {
        // Read current switch positions.
        var dcu = eva["dcu"]["eva1"];
        var uia = eva["uia"];
        bool lu = B(dcu["batt"]["lu"]), ps = B(dcu["batt"]["ps"]), oxy = B(dcu["oxy"]),
            fan = B(dcu["fan"]), pump = B(dcu["pump"]), co2 = B(dcu["co2"]);
        bool vent = B(uia["oxy_vent"]), emuOxy = B(uia["eva1_oxy"]),
            supply = B(uia["eva1_water_supply"]), waste = B(uia["eva1_water_waste"]);
        bool uiaConnected = B(uia["eva1_power"]) && lu; // umbilical power on and DCU battery on UMB

        Field pri = E("oxy_pri_storage"), sec = E("oxy_sec_storage"), suitOxy = E("suit_pressure_oxy"),
            suitCo2 = E("suit_pressure_co2"), helmet = E("helmet_pressure_co2"), fanPri = E("fan_pri_rpm"),
            scrubA = E("scrubber_a_co2_storage"), scrubB = E("scrubber_b_co2_storage");

        // DCU selection decides which battery, tank and fan are in use. Unused ones hold still.
        // (Matched by name, so it applies to EV2's fields too.)
        bool o2Err = error == TssError.O2Low || error == TssError.O2High;
        foreach (var f in all)
        {
            switch (f.name)
            {
                case "primary_battery_level": f.active = !lu && ps; break;
                case "secondary_battery_level": f.active = !lu && !ps; break;
                case "oxy_pri_storage": f.active = oxy || o2Err; break;
                case "oxy_sec_storage": f.active = !oxy; break;
                case "fan_pri_rpm": f.active = fan || error == TssError.FanLow; break;
                case "fan_sec_rpm": f.active = !fan; break;
                case "coolant_liquid_pressure": f.active = pump; break;
                default: f.active = true; break;
            }
        }
        if (uiaConnected && vent) pri.active = sec.active = true; // venting empties both tanks

        // O2 fault: on the secondary tank, suit O2 recovers to 4 psi; on primary it keeps failing.
        if (!oxy)
        {
            if (error == TssError.O2Low) { Set(suitOxy, Algo.Growth, OxyRate); suitOxy.max = 4f; }
            if (error == TssError.O2High) { Set(suitOxy, Algo.Decay, OxyRate); suitOxy.min = 4f; }
        }
        else if (error == TssError.O2Low) { Set(suitOxy, Algo.Decay, OxyErrorRate); suitOxy.min = 0f; }
        else if (error == TssError.O2High) { Set(suitOxy, Algo.Growth, OxyErrorRate); suitOxy.max = 4.2f; }
        oxyError = oxy && o2Err;

        // Fan fault: switching to the secondary fan clears the helmet CO2.
        bool fanFailed = fanPri.algo == Algo.Decay;
        if (!fan && fanFailed) Set(helmet, Algo.Decay, 1000f);
        fanError = fan && fanFailed;

        // Power fault: primary battery under 20% while running on it.
        powerError = !lu && ps && E("primary_battery_level").value < 20f;

        // CO2 scrubbers: the selected one fills while the other vents. Once the filling one
        // passes 30%, suit CO2 starts rising; past 60% it raises a scrubber error.
        scrubA.algo = co2 ? Algo.Growth : Algo.Decay;
        scrubB.algo = co2 ? Algo.Decay : Algo.Growth;
        var filling = co2 ? scrubA : scrubB;
        suitCo2.algo = filling.value > 30f ? Algo.Growth : Algo.Decay;
        scrubberError = filling.value > 60f;

        // Selected fan spins at 30,000 rpm, the other is off. A failed primary fan reads 0.
        fanPri.value = fan && error != TssError.FanLow ? 30000f : 0f;
        E("fan_sec_rpm").value = fan ? 0f : 30000f;

        // While plugged into the UIA: egress procedure behavior (O2 vent/fill, suit pressurization, coolant).
        if (uiaConnected)
        {
            // O2 VENT open empties both tanks. Otherwise EMU O2 open fills the DCU-selected tank.
            if (vent)
            {
                Set(pri, Algo.Decay, OxyVentRate);
                Set(sec, Algo.Decay, OxyVentRate);
            }
            else
            {
                var (active, idle) = oxy ? (pri, sec) : (sec, pri);
                if (emuOxy) { Set(active, Algo.Growth, OxyFillRate); Set(idle, Algo.Decay, OxyRate); }
                else { Set(active, Algo.Decay, OxyRate); Set(idle, Algo.Decay, 0f); }
            }

            // With valves closed and a full primary tank, the suit pressurizes up to 4 psi.
            float tankPsi = E(oxy ? "oxy_pri_pressure" : "oxy_sec_pressure").value;
            if (!vent && !emuOxy && oxy && tankPsi > 1000f)
            {
                if (suitOxy.value < 4f) suitOxy.algo = Algo.Growth;
                else { suitOxy.value = 4f; suitOxy.algo = Algo.Constant; }
            }

            suitCo2.value = 0f; // no CO2 buildup while on the umbilical
            suitCo2.algo = Algo.Constant;

            // Coolant: pump open + waste open drains it, pump open + supply open fills it.
            var coolant = E("coolant_storage");
            if (pump && !supply && waste) Set(coolant, Algo.Decay, CoolantDrainRate);
            else if (pump && supply && !waste) Set(coolant, Algo.Growth, CoolantFillRate);
            else Set(coolant, Algo.Decay, CoolantRate);
        }

        // Step every value once, only while the EVA is running.
        if (running)
        {
            simTime++;
            if (error == TssError.None && simTime == randomErrorAt)
                ThrowError((TssError)UnityEngine.Random.Range(1, 4));
            // Plain values first, then the ones computed from them.
            foreach (var f in all) if (f.formula == null) f.Step();
            foreach (var f in all) if (f.formula != null) f.Step();
        }

        WriteJson();
    }

    // Copies the current values, errors and running state into the EVA JSON.
    void WriteJson()
    {
        eva["status"]["started"] = running;
        var err = eva["error"];
        err["fan_error"] = fanError;
        err["oxy_error"] = oxyError;
        err["power_error"] = powerError;
        err["scrubber_error"] = scrubberError;
        var telemetry = eva["telemetry"];
        foreach (var f in eva1) telemetry["eva1"][f.name] = f.value;
        foreach (var f in eva2) telemetry["eva2"][f.name] = f.value;
    }

    // Defines every telemetry value for both suits: how it changes and its starting numbers.
    // List order is the order the keys appear in the JSON.
    void BuildFields()
    {
        // EV1: the player's suit, fully simulated.
        eva1.AddRange(new[]
        {
            F("primary_battery_level", Algo.Decay, start: 100, rate: 0.05f),
            F("secondary_battery_level", Algo.Decay, start: 100, rate: 0.05f),
            F("oxy_pri_storage", Algo.Decay, start: 100, rate: 0.013f),
            F("oxy_sec_storage", Algo.Decay, start: 100, rate: 0.013f),
            F("oxy_pri_pressure", Algo.Dependent, f: () => V("oxy_pri_storage") * 30f),
            F("oxy_sec_pressure", Algo.Dependent, f: () => V("oxy_sec_storage") * 30f),
            F("suit_pressure_oxy", Algo.Constant, min: 3, max: 4, rate: 0.26f),
            F("suit_pressure_co2", Algo.Growth, max: 0.1f, rate: 0.001f),
            F("suit_pressure_other", Algo.Constant, max: 0.1f),
            F("suit_pressure_total", Algo.Dependent, f: () => V("suit_pressure_oxy") + V("suit_pressure_co2") + V("suit_pressure_other")),
            F("helmet_pressure_co2", Algo.Dependent, f: () => V("suit_pressure_co2") * 1.5f),
            F("fan_pri_rpm", Algo.Constant, start: 30000, max: 30500, rate: 13574),
            F("fan_sec_rpm", Algo.Constant, start: 30000, max: 30500, rate: 10),
            F("scrubber_a_co2_storage", Algo.Growth, rate: 0.13f),
            F("scrubber_b_co2_storage", Algo.Growth, rate: 0.13f),
            F("temperature", Algo.Sine, start: 21.1f, min: -10, max: 40, baseV: 21.1f, amp: 5.1f, freq: 0.018f),
            F("coolant_storage", Algo.Decay, rate: 0.05f),
            F("coolant_gas_pressure", Algo.Dependent, f: () => 0f),
            F("coolant_liquid_pressure", Algo.Sine, min: 480, max: 520, baseV: 500, amp: 2, freq: 0.015f),
            F("heart_rate", Algo.Dependent, f: () => 120f + V("temperature") * 1.55f),
            F("oxy_consumption", Algo.Dependent, f: () => 0.1f + 0.0001f * V("heart_rate")),
            F("co2_production", Algo.Dependent, f: () => 0.11f + 0.0002f * V("heart_rate")),
            F("eva_elapsed_time", Algo.Growth, max: 10000, rate: 1),
        });

        // EV2: the second crew member. Mostly background values; its battery, tanks and
        // elapsed time stay constant and both scrubbers move together, as on the real TSS.
        eva2.AddRange(new[]
        {
            F("battery_level", Algo.Decay, start: 100),
            F("oxy_pri_storage", Algo.Decay, start: 100),
            F("oxy_sec_storage", Algo.Decay, start: 100),
            F("oxy_pri_pressure", Algo.Dependent, f: () => V("oxy_pri_storage") * 30f),
            F("oxy_sec_pressure", Algo.Dependent, f: () => V("oxy_sec_storage") * 30f),
            F("suit_pressure_oxy", Algo.Sine, baseV: 4, amp: 0.08f, freq: 0.012f),
            F("suit_pressure_co2", Algo.Sine, baseV: 0.05f, amp: 0.04f, freq: 0.01f),
            F("suit_pressure_other", Algo.Decay),
            F("suit_pressure_total", Algo.Dependent, f: () => V("suit_pressure_oxy") + V("suit_pressure_co2") + V("suit_pressure_other")),
            F("helmet_pressure_co2", Algo.Dependent, f: () => V("suit_pressure_co2") * 1.5f),
            F("fan_pri_rpm", Algo.Dependent, f: () => 30000f - V("suit_pressure_co2") * 10000f),
            F("fan_sec_rpm", Algo.Dependent, f: () => 30000f - V("suit_pressure_co2") * 10000f),
            F("scrubber_a_co2_storage", Algo.Sine, baseV: 30, amp: 30, freq: 0.003f),
            F("scrubber_b_co2_storage", Algo.Sine, baseV: 30, amp: 30, freq: 0.003f),
            F("temperature", Algo.Sine, baseV: 21.1f, amp: 5.1f, freq: 0.018f),
            F("coolant_storage", Algo.Dependent, f: () => 100f),
            F("coolant_gas_pressure", Algo.Dependent, f: () => 0f),
            F("coolant_liquid_pressure", Algo.Sine, baseV: 500, amp: 2, freq: 0.015f),
            F("heart_rate", Algo.Dependent, f: () => 90f + (V("temperature") - 21.1f) * 0.36f),
            F("oxy_consumption", Algo.Dependent, f: () => 0.08f + (V("heart_rate") - 70f) * 0.0008f),
            F("co2_production", Algo.Dependent, f: () => 0.08f + (V("heart_rate") - 70f) * 0.0008f),
            F("eva_elapsed_time", Algo.Growth),
        });

        all.AddRange(eva1);
        all.AddRange(eva2);
        foreach (var f in all)
            if (!byName.ContainsKey(f.name)) byName[f.name] = f;
    }

    // Shorthand for creating a Field.
    static Field F(string name, Algo algo, float start = 0, float min = 0, float max = 100, float rate = 0,
        float baseV = 0, float amp = 0, float freq = 0, Func<float> f = null) => new Field
    {
        name = name, algo = algo, startAlgo = algo, start = start, min = min, max = max,
        rate = rate, startRate = rate, baseV = baseV, amp = amp, freq = freq, formula = f,
        // Oscillating values start at their base, computed ones at 0, the rest at `start`.
        value = algo == Algo.Sine ? baseV : algo == Algo.Dependent ? 0f : start,
    };

    Field E(string name) => byName[name];                     // field by name (EV1 first)
    float V(string name) => byName[name].value;               // value by name, used in formulas
    static void Set(Field f, Algo algo, float rate) { f.algo = algo; f.rate = rate; }
    static bool B(JToken t) => t != null && t.Type != JTokenType.Null && (bool)t; // JSON switch -> bool

    // ---------------------------------------------------------------- Self test

    // Right-click the component > Self Test. Runs the egress O2 prep procedure and a fan
    // fault through Get/Post, checks the results, then resets everything.
    [ContextMenu("Self Test")]
    void SelfTest()
    {
        if (all.Count == 0) Awake();
        eva = JObject.Parse(EvaTemplate);
        ResetEva();
        try
        {
            StartEva();
            Post(2001, 1); Post(2011, 1); Post(2012, 1); Post(2014, 1); // EMU1 power, BATT UMB, OXY PRI, FAN PRI
            Post(2009, 1); Run(30);                                     // O2 vent open
            Check(T("oxy_pri_pressure") < 10 && T("oxy_sec_pressure") < 10, "vent drains both tanks < 10 psi");
            Post(2009, 0); Post(2002, 1); Run(45);                      // vent closed, EMU-1 O2 open
            Check(T("oxy_pri_pressure") >= 2999, "primary tank fills to 3000 psi");
            Post(2002, 0); Run(20);                                     // EMU-1 O2 closed
            Check(Mathf.Approximately(T("suit_pressure_oxy"), 4f), "suit O2 pressurizes to 4 psi");

            ThrowError(TssError.FanLow); Run(1);
            Check(Err("fan_error") && T("fan_pri_rpm") == 0f, "fan fault raises fan_error");
            Post(2014, 0); Run(1);                                      // DCU FAN -> SEC
            Check(!Err("fan_error") && T("fan_sec_rpm") == 30000f, "secondary fan clears fan_error");

            Check(!Post(9999, 1) && Get(9999) == null, "unknown commands rejected");
            Debug.Log("SimTss self test passed");
        }
        finally
        {
            eva = JObject.Parse(EvaTemplate);
            ResetEva();
            running = startOnPlay;
        }
    }

    // Test helpers: advance N seconds, read an EV1 value, read an error flag, fail loudly.
    void Run(int seconds) { for (int i = 0; i < seconds; i++) Tick(); }
    float T(string field) => (float)JObject.Parse(Get(1))["telemetry"]["eva1"][field];
    bool Err(string flag) => (bool)JObject.Parse(Get(1))["error"][flag];
    static void Check(bool ok, string what) { if (!ok) throw new Exception("SimTss self test failed: " + what); }
}
