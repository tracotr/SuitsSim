// C# port of the TSS2026 backend (src/data.c) by the NASA SUITS tech team: the rules that
// connect UIA/DCU switches, suit faults and LTV errors to the simulation, the in-memory
// versions of EVA.json / ROVER.json / LTV.json / LTV_ERRORS.json, and the UDP command table.
// Networking, the web UI and the DUST (Unreal) link are left out.
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SuitsSim.Tss.Embedded
{
    public sealed class TssBackend
    {
        public const string Eva = "EVA", Rover = "ROVER", Ltv = "LTV", LtvErrors = "LTV_ERRORS";

        /// <summary>udp_command_mappings from data.h: command -> "file.section.field" route and type.</summary>
        public static readonly IReadOnlyDictionary<uint, (string route, bool isBool)> CommandMap = new Dictionary<uint, (string, bool)>
        {
            [1103] = ("rover.pr_telemetry.cabin_heating", true),
            [1104] = ("rover.pr_telemetry.cabin_cooling", true),
            [1105] = ("rover.pr_telemetry.co2_scrubber", true),
            [1106] = ("rover.pr_telemetry.lights_on", true),
            [1107] = ("rover.pr_telemetry.brakes", true),
            [1109] = ("rover.pr_telemetry.throttle", false),
            [1110] = ("rover.pr_telemetry.steering", false),
            [1111] = ("rover.pr_telemetry.rover_pos_x", false),
            [1112] = ("rover.pr_telemetry.rover_pos_y", false),
            [1113] = ("rover.pr_telemetry.rover_pos_z", false),
            [1114] = ("rover.pr_telemetry.heading", false),
            [1115] = ("rover.pr_telemetry.pitch", false),
            [1116] = ("rover.pr_telemetry.roll", false),
            [1117] = ("rover.pr_telemetry.distance_traveled", false),
            [1118] = ("rover.pr_telemetry.speed", false),
            [1119] = ("rover.pr_telemetry.surface_incline", false),
            [1131] = ("rover.pr_telemetry.sunlight", false),
            [1132] = ("ltv.signal.strength", false),
            [2001] = ("eva.uia.eva1_power", true),
            [2002] = ("eva.uia.eva1_oxy", true),
            [2003] = ("eva.uia.eva1_water_supply", true),
            [2004] = ("eva.uia.eva1_water_waste", true),
            [2005] = ("eva.uia.eva2_power", true),
            [2006] = ("eva.uia.eva2_oxy", true),
            [2007] = ("eva.uia.eva2_water_supply", true),
            [2008] = ("eva.uia.eva2_water_waste", true),
            [2009] = ("eva.uia.oxy_vent", true),
            [2010] = ("eva.uia.depress", true),
            [2011] = ("eva.dcu.eva1.batt.lu", true),
            [2012] = ("eva.dcu.eva1.oxy", true),
            [2013] = ("eva.dcu.eva1.batt.ps", true),
            [2014] = ("eva.dcu.eva1.fan", true),
            [2015] = ("eva.dcu.eva1.pump", true),
            [2016] = ("eva.dcu.eva1.co2", true),
            [2017] = ("eva.imu.eva1.posx", false),
            [2018] = ("eva.imu.eva1.posy", false),
            [2019] = ("eva.imu.eva1.heading", false),
            [2020] = ("eva.imu.eva2.posx", false),
            [2021] = ("eva.imu.eva2.posy", false),
            [2022] = ("eva.imu.eva2.heading", false),
            [2023] = ("ltv_errors.error_procedures.0.needs_resolved", true),
            [2024] = ("ltv_errors.error_procedures.1.needs_resolved", true),
            [2025] = ("ltv_errors.error_procedures.2.needs_resolved", true),
            [2026] = ("ltv_errors.error_procedures.3.needs_resolved", true),
            [2027] = ("ltv_errors.error_procedures.4.needs_resolved", true),
            [2028] = ("ltv_errors.error_procedures.5.needs_resolved", true),
            [2029] = ("ltv_errors.error_procedures.6.needs_resolved", true),
            [2030] = ("ltv_errors.error_procedures.7.needs_resolved", true),
            [2050] = ("ltv.signal.ping_requested", true),
            [2051] = ("ltv.signal.ping_unlimited_requested", true),
        };

        public SimEngine Engine { get; }
        public Action<string> Log = _ => { };
        public int Ticks { get; private set; }

        readonly Dictionary<string, JObject> docs = new Dictionary<string, JObject>();

        // data.c file-level flags and update_ltv_error_dependencies statics
        bool fanErrorFlag;
        bool o2ErrorFlag;
        bool poorCommsResolvedPrevious;
        bool rebootResolvedPrevious = true;

        /// <param name="engineConfigs">eva1.json, eva2.json, rover.json from src/lib/simulation/config, in that order.</param>
        /// <param name="dataFiles">Initial contents of data/EVA.json, ROVER.json, LTV.json, LTV_ERRORS.json keyed by name.</param>
        public TssBackend(IEnumerable<string> engineConfigs, IDictionary<string, string> dataFiles, int? seed = null)
        {
            foreach (var kv in dataFiles) docs[kv.Key] = JObject.Parse(kv.Value);
            foreach (var name in new[] { Eva, Rover, Ltv, LtvErrors })
                if (!docs.ContainsKey(name)) throw new ArgumentException($"missing data file {name}.json");

            InitializeSwitchStates();

            Engine = new SimEngine(seed) { Log = m => Log(m) };
            foreach (var cfg in engineConfigs) Engine.LoadComponent(cfg);
            Engine.ExternalLookup = (file, path) => GetField(file.Replace(".json", ""), path);
            Engine.ResetHook = ResetLtvErrors;
            Engine.Initialize();

            SyncSimulationToJson(); // the server loop syncs before its first tick
        }

        // ------------------------------------------------------------------ public API

        /// <summary>A copy of one data file, as a GET would return it (LTV_ERRORS honors recovery mode).</summary>
        public JObject Snapshot(uint getCommand)
        {
            switch (getCommand)
            {
                case 0: return (JObject)docs[Rover].DeepClone();
                case 1: return (JObject)docs[Eva].DeepClone();
                case 2: return (JObject)docs[Ltv].DeepClone();
                case 3: return IsRecoveryModeResolved() ? (JObject)docs[LtvErrors].DeepClone() : RecoveryModeView();
                default: return null;
            }
        }

        /// <summary>UDP GET: the JSON text TSS would send back.</summary>
        public string HandleGet(uint command) => Snapshot(command)?.ToString(Formatting.Indented) ?? "";

        /// <summary>UDP POST (commands 1000-2999). Returns what TSS acknowledges.</summary>
        public bool HandlePost(uint command, float value)
        {
            if (!CommandMap.TryGetValue(command, out var m))
            {
                Log($"Invalid UDP POST command: {command}");
                return false;
            }
            string text = m.isBool
                ? (value != 0f ? "true" : "false")
                : ((double)value).ToString("F6", CultureInfo.InvariantCulture);
            bool result = ApplyRoute(m.route, text);
            // TSS quirk: without DUST connected, time_since_last_ping never advances, so 2050 always reports false
            if (command == 2050) result = false;
            return result;
        }

        /// <summary>
        /// The web UI's form update (html_form_json_update), e.g. ("eva.status.started", "true")
        /// to start the EVA. Everything the TSS web page can toggle goes through here.
        /// </summary>
        public bool ApplyRoute(string route, string value)
        {
            var parts = route.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) { Log($"Error: Route must have at least 2 parts (file.section): {route}"); return false; }

            string file;
            switch (parts[0])
            {
                case "eva": file = Eva; break;
                case "rover": file = Rover; break;
                case "ltv": file = Ltv; break;
                case "ltv_errors": file = LtvErrors; break;
                default: Log($"Error: Unsupported file type '{parts[0]}'"); return false;
            }

            if (parts.Length == 3)
            {
                string section = parts[1], field = parts[2];
                UpdateJson(file, section, field, value);

                if (file == Rover && section == "pr_telemetry" && field == "sim_running")
                {
                    if (value == "true") Engine.StartComponent("rover");
                    else Engine.ResetComponent("rover");
                }
                if (file == Eva && section == "status" && field == "started")
                {
                    if (value == "true")
                    {
                        Engine.StartComponent("eva1");
                        Engine.StartComponent("eva2");
                    }
                    else
                    {
                        Engine.ResetComponent("eva1");
                        Engine.TimeToCompleteTaskBoard = SimEngine.TaskBoardNotDone;
                        Engine.ResetComponent("eva2");
                        ResetEvaStationTiming();
                    }
                }
                SyncSimulationToJson(); // the server loop syncs within 100 ms of any change
                return true;
            }
            if (parts.Length > 3)
            {
                UpdateJson(file, parts[1], string.Join(".", parts, 2, parts.Length - 2), value);
                SyncSimulationToJson();
                return true;
            }
            Log($"Error: Invalid route format: {route}");
            return false;
        }

        public void StartEva() => ApplyRoute("eva.status.started", "true");
        public void StopAndResetEva() => ApplyRoute("eva.status.started", "false");

        /// <summary>One simulation second: increment_simulation followed by sync_simulation_to_json.</summary>
        public void Tick()
        {
            UpdateDcuSettings();
            UpdateUiaSettings();
            UpdateActiveStates();
            UpdateErrorStates();
            UpdateFanValues();
            UpdateUiaConnected();
            CabinTemperatureControl();
            Engine.Update(1f);
            UpdateLtvErrorDependencies();
            UpdateNumRemainingErrors();
            UpdateEvaStationTiming();
            SyncSimulationToJson();
            Ticks++;
        }

        // ------------------------------------------------------------------ JSON helpers

        /// <summary>update_json_file: replaces an existing value under file.section.path. Never adds keys.</summary>
        public void UpdateJson(string file, string section, string fieldPath, string newValue)
        {
            if (!docs.TryGetValue(file, out var doc)) return;
            JToken current = doc[section];
            if (current == null) { Log($"Error: Section {section} not found in JSON."); return; }

            var parts = fieldPath.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                {
                    if (!(current is JArray arr) || idx < 0 || idx >= arr.Count) { Log($"Error: bad array index {parts[i]}"); return; }
                    current = arr[idx];
                }
                else
                {
                    current = (current as JObject)?[parts[i]];
                    if (current == null) { Log($"Error: Field path not found at {parts[i]}."); return; }
                }
            }

            var final = parts[parts.Length - 1];
            var value = ParseValue(newValue);
            if (int.TryParse(final, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fi))
            {
                if (current is JArray arr && fi >= 0 && fi < arr.Count) arr[fi] = value;
            }
            else if (current is JObject obj && obj.ContainsKey(final))
            {
                obj[final] = value;
            }
        }

        static JToken ParseValue(string s)
        {
            if (s == "true") return new JValue(true);
            if (s == "false") return new JValue(false);
            if (s.Length > 0 && s[0] == '[')
            {
                var arr = new JArray();
                foreach (var item in s.Substring(1).Split(new[] { ',', ']' }, StringSplitOptions.RemoveEmptyEntries))
                    arr.Add(SimEngine.Atof(item.Trim()));
                return arr;
            }
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? new JValue(n) : new JValue(s);
        }

        /// <summary>get_field_from_json: numbers as-is, bools as 1/0, numeric strings parsed; otherwise null.</summary>
        public float? GetField(string file, string path)
        {
            if (!docs.TryGetValue(file, out var doc)) return null;
            JToken t = doc;
            foreach (var part in path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                t = (t as JObject)?[part];
                if (t == null) return null;
            }
            switch (t.Type)
            {
                case JTokenType.Float:
                case JTokenType.Integer: return (float)t.Value<double>();
                case JTokenType.Boolean: return t.Value<bool>() ? 1f : 0f;
                case JTokenType.String:
                    return double.TryParse((string)t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (float)d : (float?)null;
                default: return null;
            }
        }

        bool Flag(string file, string path) => (GetField(file, path) ?? 0f) == 1f;

        void SetError(string key, bool value) => UpdateJson(Eva, "error", key, value ? "true" : "false");

        // ------------------------------------------------------------------ init

        void InitializeSwitchStates()
        {
            if (docs[Rover]["pr_telemetry"] is JObject pr)
                foreach (var k in new[] { "cabin_heating", "cabin_cooling", "lights_on" })
                    if (pr.ContainsKey(k)) pr[k] = false;

            if (docs[Eva]["uia"] is JObject uia)
                foreach (var p in uia.Properties()) p.Value = false;

            if (docs[Eva].SelectToken("dcu.eva1") is JObject dcu)
            {
                foreach (var k in new[] { "oxy", "fan", "pump", "co2" }) if (dcu.ContainsKey(k)) dcu[k] = false;
                if (dcu["batt"] is JObject batt) { batt["lu"] = false; batt["ps"] = false; }
            }

            if (docs[LtvErrors]["error_procedures"] is JArray errors)
                for (int i = 0; i < errors.Count; i++)
                    if (errors[i] is JObject e) e["needs_resolved"] = !(i == 4 || i == 5);
        }

        // ------------------------------------------------------------------ switch state readers

        void UpdateDcuSettings()
        {
            var d = Engine.Dcu;
            d.BatteryLu = Flag(Eva, "dcu.eva1.batt.lu");
            d.BatteryPs = Flag(Eva, "dcu.eva1.batt.ps");
            d.Fan = Flag(Eva, "dcu.eva1.fan");
            d.O2 = Flag(Eva, "dcu.eva1.oxy");
            d.Pump = Flag(Eva, "dcu.eva1.pump");
            d.Co2 = Flag(Eva, "dcu.eva1.co2");
        }

        void UpdateUiaSettings()
        {
            var u = Engine.Uia;
            u.Eva1Power = Flag(Eva, "uia.eva1_power");
            u.Eva2Power = Flag(Eva, "uia.eva2_power");
            u.Eva1Oxy = Flag(Eva, "uia.eva1_oxy");
            u.Eva2Oxy = Flag(Eva, "uia.eva2_oxy");
            u.Eva1WaterSupply = Flag(Eva, "uia.eva1_water_supply");
            u.Eva2WaterSupply = Flag(Eva, "uia.eva2_water_supply");
            u.Eva1WaterWaste = Flag(Eva, "uia.eva1_water_waste");
            u.Eva2WaterWaste = Flag(Eva, "uia.eva2_water_waste");
            u.OxyVent = Flag(Eva, "uia.oxy_vent");
            u.Depress = Flag(Eva, "uia.depress");
        }

        /// <summary>UIA is connected when EV1 power is on at the UIA and the DCU is on umbilical (batt.lu = true).</summary>
        public bool IsUiaConnected => Engine.Uia.Eva1Power && Engine.Dcu.BatteryLu;

        // ------------------------------------------------------------------ rules

        void UpdateActiveStates()
        {
            var d = Engine.Dcu;
            foreach (var f in Engine.UpdateOrder)
            {
                f.Active = true;
                if (!f.Component.StartsWith("eva", StringComparison.Ordinal)) continue;

                var n = f.Name;
                if (n.StartsWith("primary_battery_level", StringComparison.Ordinal) && !(!d.BatteryLu && d.BatteryPs)) f.Active = false;
                else if (n.StartsWith("secondary_battery_level", StringComparison.Ordinal) && !(!d.BatteryLu && !d.BatteryPs)) f.Active = false;
                else if (n.StartsWith("oxy_pri_storage", StringComparison.Ordinal) && !d.O2) f.Active = false;
                else if (n.StartsWith("oxy_sec_storage", StringComparison.Ordinal) && d.O2) f.Active = false;
                else if (n.StartsWith("fan_pri_rpm", StringComparison.Ordinal) && !d.Fan) f.Active = false;
                else if (n.StartsWith("fan_sec_rpm", StringComparison.Ordinal) && d.Fan) f.Active = false;
                else if (n.StartsWith("coolant_liquid_pressure", StringComparison.Ordinal) && !d.Pump) f.Active = false;
                else f.Active = true;

                if ((Engine.ErrorType == SuitFault.O2PressureLow || Engine.ErrorType == SuitFault.O2PressureHigh) && n == "oxy_pri_storage") f.Active = true;
                else if (Engine.ErrorType == SuitFault.FanRpmLow && n == "fan_pri_rpm") f.Active = true;
            }

            if (IsUiaConnected && Engine.Uia.OxyVent)
            {
                var eva1 = Engine.Component("eva1");
                var pri = eva1?.Field("oxy_pri_storage");
                var sec = eva1?.Field("oxy_sec_storage");
                if (pri != null) pri.Active = true;
                if (sec != null) sec.Active = true;
            }
        }

        void UpdateErrorStates()
        {
            UpdateO2ErrorState();
            UpdateFanErrorState();
            UpdatePowerErrorState();
            UpdateScrubberState();
        }

        void UpdateO2ErrorState()
        {
            var f = Engine.Component("eva1")?.Field("suit_pressure_oxy");
            if (f == null) return;
            bool low = Engine.ErrorType == SuitFault.O2PressureLow;
            bool high = Engine.ErrorType == SuitFault.O2PressureHigh;

            if (!Engine.Dcu.O2) // on secondary oxygen: fault is bypassed, pressure recovers toward 4 psi
            {
                o2ErrorFlag = true;
                SetError("oxy_error", false);
                if (low) { f.Algorithm = SimAlgorithm.LinearGrowth; f.Rate = SimEngine.OxyRate; f.Max = 4f; }
                if (high) { f.Algorithm = SimAlgorithm.LinearDecay; f.Rate = SimEngine.OxyRate; f.Min = 4f; }
            }
            else if (low || high)
            {
                SetError("oxy_error", true);
                if (low)
                {
                    f.Algorithm = SimAlgorithm.LinearDecay; f.Rate = SimEngine.OxyErrorRate; f.Min = 0f;
                    if (o2ErrorFlag) { Engine.ThrowO2Low(); o2ErrorFlag = false; }
                }
                if (high)
                {
                    f.Algorithm = SimAlgorithm.LinearGrowth; f.Rate = SimEngine.OxyErrorRate; f.Max = 4.2f;
                    if (o2ErrorFlag) { Engine.ThrowO2High(); o2ErrorFlag = false; }
                }
            }
            else SetError("oxy_error", false);
        }

        void UpdateFanErrorState()
        {
            var eva1 = Engine.Component("eva1");
            var fan = eva1?.Field("fan_pri_rpm");
            var helmet = eva1?.Field("helmet_pressure_co2");
            if (fan == null || helmet == null) return;
            bool thrown = fan.Algorithm == SimAlgorithm.LinearDecay;

            if (!Engine.Dcu.Fan) // on secondary fan
            {
                fanErrorFlag = false;
                SetError("fan_error", false);
                if (thrown) { helmet.Algorithm = SimAlgorithm.LinearDecay; helmet.Rate = 1000f; }
            }
            else if (thrown)
            {
                SetError("fan_error", true);
                if (fanErrorFlag) { Engine.ThrowFanLow(); fanErrorFlag = false; } // TSS quirk: flag is never set true
            }
            else SetError("fan_error", false);
        }

        void UpdatePowerErrorState()
        {
            var f = Engine.Component("eva1")?.Field("primary_battery_level");
            if (f == null) return;
            var d = Engine.Dcu;
            bool thrown = f.Current < 20f;
            if (d.BatteryLu || !d.BatteryPs) SetError("power_error", false);
            else SetError("power_error", thrown);
        }

        void UpdateScrubberState()
        {
            var eva1 = Engine.Component("eva1");
            var a = eva1?.Field("scrubber_a_co2_storage");
            var b = eva1?.Field("scrubber_b_co2_storage");
            var co2 = eva1?.Field("suit_pressure_co2");
            if (a == null || b == null || co2 == null) return;

            bool onA = Engine.Dcu.Co2;
            a.Algorithm = onA ? SimAlgorithm.LinearGrowth : SimAlgorithm.LinearDecay;
            b.Algorithm = onA ? SimAlgorithm.LinearDecay : SimAlgorithm.LinearGrowth;

            bool loaded = (a.Algorithm == SimAlgorithm.LinearGrowth && a.Current > 30f) || (b.Algorithm == SimAlgorithm.LinearGrowth && b.Current > 30f);
            co2.Algorithm = loaded ? SimAlgorithm.LinearGrowth : SimAlgorithm.LinearDecay;

            SetError("scrubber_error", (a.Current > 60f && onA) || (b.Current > 60f && !onA));
        }

        void UpdateFanValues()
        {
            var eva1 = Engine.Component("eva1");
            var pri = eva1?.Field("fan_pri_rpm");
            var sec = eva1?.Field("fan_sec_rpm");
            if (pri == null || sec == null) return;

            if (Engine.ErrorType == SuitFault.FanRpmLow)
            {
                pri.Current = 0f;
                sec.Current = Engine.Dcu.Fan ? 0f : 30000f;
            }
            else
            {
                pri.Current = Engine.Dcu.Fan ? 30000f : 0f;
                sec.Current = Engine.Dcu.Fan ? 0f : 30000f;
            }
        }

        void UpdateUiaConnected()
        {
            var eva1 = Engine.Component("eva1");
            if (eva1 == null || !IsUiaConnected) return;

            var u = Engine.Uia;
            var d = Engine.Dcu;
            var pri = eva1.Field("oxy_pri_storage");
            var sec = eva1.Field("oxy_sec_storage");

            if (u.OxyVent)
            {
                foreach (var f in new[] { pri, sec })
                {
                    if (f == null) continue;
                    f.Active = true;
                    f.Algorithm = SimAlgorithm.LinearDecay;
                    f.Rate = SimEngine.OxyVentRate;
                }
            }
            else if (u.Eva1Oxy)
            {
                var filling = d.O2 ? pri : sec;
                var other = d.O2 ? sec : pri;
                if (filling != null) { filling.Algorithm = SimAlgorithm.LinearGrowth; filling.Rate = SimEngine.OxyFillRate; }
                if (other != null) { other.Algorithm = SimAlgorithm.LinearDecay; other.Rate = SimEngine.OxyRate; }
            }
            else if (pri != null && sec != null)
            {
                var inUse = d.O2 ? pri : sec;
                var idle = d.O2 ? sec : pri;
                inUse.Algorithm = SimAlgorithm.LinearDecay; inUse.Rate = SimEngine.OxyRate;
                idle.Algorithm = SimAlgorithm.LinearDecay; idle.Rate = 0f;
            }

            float tankPressure = eva1.Field(d.O2 ? "oxy_pri_pressure" : "oxy_sec_pressure")?.Current ?? 0f;

            // Pressurize the suit once the primary tank is charged (TSS checks > 1000 psi)
            if (!u.OxyVent && !u.Eva1Oxy && d.O2 && tankPressure > 1000f)
            {
                var suitO2 = eva1.Field("suit_pressure_oxy");
                if (suitO2 != null)
                {
                    if (suitO2.Current < 4f) suitO2.Algorithm = SimAlgorithm.LinearGrowth;
                    else { suitO2.Current = 4f; suitO2.Algorithm = SimAlgorithm.ConstantValue; }
                }
            }

            var co2 = eva1.Field("suit_pressure_co2");
            if (co2 != null) { co2.Current = 0f; co2.Algorithm = SimAlgorithm.ConstantValue; }

            var coolant = eva1.Field("coolant_storage");
            if (coolant != null)
            {
                if (d.Pump && !u.Eva1WaterSupply && u.Eva1WaterWaste) { coolant.Algorithm = SimAlgorithm.LinearDecay; coolant.Rate = SimEngine.CoolantDrainRate; }
                else if (d.Pump && u.Eva1WaterSupply && !u.Eva1WaterWaste) { coolant.Algorithm = SimAlgorithm.LinearGrowth; coolant.Rate = SimEngine.CoolantFillRate; }
                else { coolant.Algorithm = SimAlgorithm.LinearDecay; coolant.Rate = SimEngine.CoolantRate; }
            }
        }

        void CabinTemperatureControl()
        {
            var rover = Engine.Component("rover");
            var temp = rover?.Field("cabin_temperature");
            var target = rover?.Field("cabin_temperature_target");
            if (temp == null || target == null || rover.Field("cabin_heating") == null || rover.Field("cabin_cooling") == null) return;

            if (temp.Current < target.Current) { temp.Algorithm = SimAlgorithm.LinearGrowth; temp.Max = target.Current; }
            else if (temp.Current > target.Current) { temp.Algorithm = SimAlgorithm.LinearDecay; temp.Min = target.Current; }
            else { temp.Algorithm = SimAlgorithm.ConstantValue; temp.Current = target.Current; }
        }

        // ------------------------------------------------------------------ LTV errors

        JArray ErrorArray => docs[LtvErrors]["error_procedures"] as JArray;

        void UpdateLtvErrorDependencies()
        {
            var arr = ErrorArray;
            if (arr == null) return;
            JObject poor = null, reboot = null, bus = null;
            foreach (var item in arr)
            {
                switch (item.Value<string>("code"))
                {
                    case "3452": poor = (JObject)item; break;
                    case "2441": reboot = (JObject)item; break;
                    case "4968": bus = (JObject)item; break;
                }
            }
            if (poor == null || reboot == null || bus == null) return;
            if (poor["needs_resolved"]?.Type != JTokenType.Boolean || reboot["needs_resolved"]?.Type != JTokenType.Boolean) return;

            bool poorResolved = !poor.Value<bool>("needs_resolved");
            bool rebootResolved = !reboot.Value<bool>("needs_resolved");

            // Fixing comms (3452) or rebooting comms (2441) trips the Subsystem Power Bus error (4968)
            if ((poorResolved && poorCommsResolvedPrevious != poorResolved) || (rebootResolved && rebootResolvedPrevious != rebootResolved))
                bus["needs_resolved"] = true;
            if ((!poorResolved && poorCommsResolvedPrevious != poorResolved) || (!rebootResolved && rebootResolvedPrevious != rebootResolved))
                bus["needs_resolved"] = false;

            poorCommsResolvedPrevious = poorResolved;
            rebootResolvedPrevious = rebootResolved;
        }

        void UpdateNumRemainingErrors()
        {
            var arr = ErrorArray;
            if (arr == null) return;
            int remaining = 0;
            foreach (var item in arr)
                if (item["needs_resolved"]?.Type == JTokenType.Boolean && item.Value<bool>("needs_resolved")) remaining++;

            var eva1 = Engine.Component("eva1");
            if (eva1 == null) return;
            if (remaining == 0 && Engine.TimeToCompleteTaskBoard == SimEngine.TaskBoardNotDone && eva1.Running)
                Engine.TimeToCompleteTaskBoard = (int)eva1.SimulationTime;
            Engine.NumTaskBoardErrors = remaining;
        }

        /// <summary>backend_reset_errors. TSS quirk: slots 6 and 7 are left as they were.</summary>
        void ResetLtvErrors()
        {
            for (int i = 0; i < 6; i++)
                UpdateJson(LtvErrors, "error_procedures", $"{i}.needs_resolved", i == 4 || i == 5 ? "false" : "true");
        }

        public bool IsRecoveryModeResolved()
        {
            var arr = ErrorArray;
            if (arr == null) return false;
            foreach (var item in arr)
            {
                if (item.Value<string>("description") != "Exit Recovery Mode (ERM)") continue;
                var nr = item["needs_resolved"];
                return nr != null && nr.Type == JTokenType.Boolean && !nr.Value<bool>();
            }
            return false;
        }

        JObject RecoveryModeView()
        {
            var arr = ErrorArray;
            var view = new JArray();
            if (arr != null && arr.Count > 0) view.Add(arr[0].DeepClone());
            return new JObject { ["error_procedures"] = view };
        }

        // ------------------------------------------------------------------ station timing + sync

        static readonly string[] Stations = { "uia", "dcu", "spec" };

        void UpdateEvaStationTiming()
        {
            if (!(docs[Eva]["status"] is JObject status)) return;
            foreach (var name in Stations)
            {
                if (!(status[name] is JObject st) || st["started"] == null || st["time"] == null) continue;
                bool started = st["started"].Type == JTokenType.Boolean && st.Value<bool>("started");
                double time = st.Value<double>("time");
                if (started) st["time"] = time + 1.0;
                if (!started && st["completed"]?.Type == JTokenType.Boolean && !st.Value<bool>("completed") && time > 0) st["completed"] = true;
            }
        }

        void ResetEvaStationTiming()
        {
            if (!(docs[Eva]["status"] is JObject status)) return;
            foreach (var name in Stations)
            {
                if (!(status[name] is JObject st)) continue;
                if (st["time"] != null) st["time"] = 0.0;
                if (st["completed"] != null) st["completed"] = false;
            }
        }

        void SyncSimulationToJson()
        {
            var eva = docs[Eva];
            var status = eva["status"] as JObject;
            if (status == null) eva["status"] = status = new JObject();
            status["started"] = Engine.IsRunning("eva1") || Engine.IsRunning("eva2");

            var telemetry = eva["telemetry"] as JObject;
            if (telemetry == null) eva["telemetry"] = telemetry = new JObject();
            var eva1 = telemetry["eva1"] as JObject ?? (JObject)(telemetry["eva1"] = new JObject());
            var eva2 = telemetry["eva2"] as JObject ?? (JObject)(telemetry["eva2"] = new JObject());

            foreach (var f in Engine.UpdateOrder)
            {
                if (f.Component == "eva1") eva1[f.Name] = (double)f.Current;
                else if (f.Component == "eva2") eva2[f.Name] = (double)f.Current;
            }

            var rover = docs[Rover];
            var pr = rover["pr_telemetry"] as JObject;
            if (pr == null) rover["pr_telemetry"] = pr = new JObject();
            pr["sim_running"] = Engine.IsRunning("rover");
            foreach (var f in Engine.UpdateOrder)
                if (f.Component == "rover" && f.Algorithm != SimAlgorithm.ExternalValue)
                    pr[f.Name] = (double)f.Current;
        }
    }
}
