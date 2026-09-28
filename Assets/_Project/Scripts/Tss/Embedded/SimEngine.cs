// C# port of the TSS2026 simulation library (src/lib/simulation: sim_engine.c,
// sim_algorithms.c, throw_errors.c) by the NASA SUITS tech team.
// Behavior is kept identical to the C version, including its quirks, so the embedded
// server matches the real one at test week. Quirks are marked "TSS quirk".
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace SuitsSim.Tss.Embedded
{
    public enum SimAlgorithm { SineWave, LinearDecay, LinearGrowth, DependentValue, ExternalValue, ConstantValue }

    /// <summary>Suit fault TSS injects after the task board is finished. None = no fault (NUM_ERRORS in C).</summary>
    public enum SuitFault { O2PressureLow = 0, O2PressureHigh = 1, FanRpmLow = 2, None = 3 }

    public sealed class SimField
    {
        public string Name;
        public string Component;
        public SimAlgorithm Algorithm;
        public SimAlgorithm StartingAlgorithm;
        public float Current, Min, Max, Start, Base, Amplitude, Frequency, Phase, Rate, StartingRate;
        public bool Active = true;
        public string[] DependsOn = Array.Empty<string>();
        public JObject Params;

        public override string ToString() => $"{Component}.{Name}={Current} ({Algorithm})";
    }

    public sealed class SimComponent
    {
        public string Name;
        public readonly List<SimField> Fields = new List<SimField>();
        public bool Running;
        public float SimulationTime;

        public SimField Field(string name)
        {
            foreach (var f in Fields) if (f.Name == name) return f;
            return null;
        }
    }

    public sealed class DcuSettings
    {
        public bool BatteryLu, BatteryPs, Fan, O2, Pump, Co2;
    }

    public sealed class UiaSettings
    {
        public bool Eva1Power, Eva1Oxy, Eva1WaterSupply, Eva1WaterWaste;
        public bool Eva2Power, Eva2Oxy, Eva2WaterSupply, Eva2WaterWaste;
        public bool OxyVent, Depress;
    }

    public sealed class SimEngine
    {
        // sim_algorithms.h constants
        public const float OxyVentRate = 4.3f;
        public const float OxyFillRate = 2.5f;
        public const float OxyRate = 0.01f;
        public const float OxyErrorRate = 0.1f;
        public const float Co2Rate = 0.01f;
        public const float CoolantFillRate = 5.76f;
        public const float CoolantDrainRate = 6.78f;
        public const float CoolantRate = 0.01f;
        public const int InitialTaskBoardErrors = 4;
        public const int TaskBoardNotDone = -10;

        public readonly List<SimComponent> Components = new List<SimComponent>();
        public readonly List<SimField> UpdateOrder = new List<SimField>();
        public readonly DcuSettings Dcu = new DcuSettings();
        public readonly UiaSettings Uia = new UiaSettings();

        public int NumTaskBoardErrors;
        public int TimeToCompleteTaskBoard;
        public int ErrorTime;
        public SuitFault ErrorType = SuitFault.None;
        public bool Initialized { get; private set; }

        /// <summary>Resolves external_value fields: (file e.g. "ROVER.json", dotted path) -> value or null.</summary>
        public Func<string, string, float?> ExternalLookup;
        /// <summary>Called after each component reset (TSS: ltv_reset + backend_reset_errors).</summary>
        public Action ResetHook;
        public Action<string> Log = _ => { };

        /// <summary>Forces the next thrown fault instead of rolling one. For tests and scripted demos.</summary>
        public SuitFault? ForcedFault;

        readonly Random rng;

        public SimEngine(int? seed = null)
        {
            rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        // ------------------------------------------------------------------ loading

        public void LoadComponent(string configJson)
        {
            var root = JObject.Parse(configJson);
            var name = root.Value<string>("component_name") ?? throw new FormatException("config has no component_name");
            if (!(root["fields"] is JObject fields)) throw new FormatException($"{name}: config has no fields object");

            var component = new SimComponent { Name = name };
            foreach (var prop in fields.Properties())
            {
                var p = (JObject)prop.Value;
                var algo = p.Value<string>("algorithm");
                if (algo == null) { Log($"Error: Missing algorithm for field {prop.Name}"); continue; }

                var f = new SimField
                {
                    Name = prop.Name,
                    Component = name,
                    Algorithm = ParseAlgorithm(algo),
                    Min = Num(p, "min_value", 0f),
                    Max = Num(p, "max_value", 100f),
                    Base = Num(p, "base_value", 0f),
                    Start = Num(p, "start_value", 0f),
                    Amplitude = Num(p, "amplitude", 0f),
                    Frequency = Num(p, "frequency", 0f),
                    Phase = Num(p, "phase", 0f),
                    Rate = Num(p, "rate", 0f),
                    Params = p,
                };
                f.StartingAlgorithm = f.Algorithm;
                f.StartingRate = f.Rate;
                f.Current = f.Start;
                if (p["depends_on"] is JArray deps)
                {
                    var list = new List<string>();
                    foreach (var d in deps) if (d.Type == JTokenType.String) list.Add((string)d);
                    f.DependsOn = list.ToArray();
                }
                component.Fields.Add(f);
            }
            Components.Add(component);
        }

        static float Num(JObject p, string key, float fallback)
        {
            var t = p[key];
            return t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) ? (float)t.Value<double>() : fallback;
        }

        public static SimAlgorithm ParseAlgorithm(string s)
        {
            switch (s)
            {
                case "linear_decay": return SimAlgorithm.LinearDecay;
                case "linear_growth": return SimAlgorithm.LinearGrowth;
                case "dependent_value": return SimAlgorithm.DependentValue;
                case "external_value": return SimAlgorithm.ExternalValue;
                case "constant_value": return SimAlgorithm.ConstantValue;
                default: return SimAlgorithm.SineWave; // TSS quirk: unknown names fall back to sine
            }
        }

        // ------------------------------------------------------------------ init

        public void Initialize()
        {
            if (Initialized) return;
            SortByDependencies();

            ErrorTime = rng.Next(10) + 1;           // time_to_throw_error: 1..10 s after the board is done
            NumTaskBoardErrors = InitialTaskBoardErrors;
            TimeToCompleteTaskBoard = TaskBoardNotDone;
            ErrorType = SuitFault.None;

            foreach (var f in UpdateOrder)
            {
                switch (f.Algorithm)
                {
                    case SimAlgorithm.SineWave:
                        if (IsNumber(f.Params["base_value"])) f.Current = (float)f.Params.Value<double>("base_value");
                        break;
                    case SimAlgorithm.LinearDecay:
                        if (IsNumber(f.Params["start_value"])) f.Current = (float)f.Params.Value<double>("start_value");
                        break;
                    case SimAlgorithm.LinearGrowth:
                    case SimAlgorithm.ConstantValue:
                        f.Current = IsNumber(f.Params["start_value"]) ? (float)f.Params.Value<double>("start_value") : 0f;
                        break;
                    default:
                        f.Current = 0f; // dependent / external: computed on first update
                        break;
                }
            }
            Initialized = true;
        }

        static bool IsNumber(JToken t) => t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer);

        /// <summary>Same pass structure as sort_fields_by_dependencies, so update order matches TSS.</summary>
        void SortByDependencies()
        {
            var all = new List<SimField>();
            foreach (var c in Components) all.AddRange(c.Fields);
            UpdateOrder.Clear();
            var resolved = new HashSet<SimField>();
            var resolvedNames = new HashSet<string>();

            while (UpdateOrder.Count < all.Count)
            {
                bool progress = false;
                foreach (var f in all)
                {
                    if (resolved.Contains(f)) continue;
                    bool ready = true;
                    foreach (var d in f.DependsOn) if (!resolvedNames.Contains(d)) { ready = false; break; }
                    if (!ready) continue;
                    UpdateOrder.Add(f);
                    resolved.Add(f);
                    resolvedNames.Add(f.Name);
                    progress = true;
                }
                if (!progress) throw new InvalidOperationException("Circular or missing dependency in simulation fields");
            }
        }

        // ------------------------------------------------------------------ lookup

        public SimComponent Component(string name)
        {
            foreach (var c in Components) if (c.Name == name) return c;
            return null;
        }

        /// <summary>TSS quirk: lookup by name across all components, first match wins (eva1 before eva2).</summary>
        public SimField FindField(string name)
        {
            foreach (var c in Components)
                foreach (var f in c.Fields)
                    if (f.Name == name) return f;
            return null;
        }

        public float GetValue(string name) => FindField(name)?.Current ?? 0f;

        public bool IsRunning(string component) => Component(component)?.Running ?? false;

        // ------------------------------------------------------------------ control

        public void StartComponent(string name)
        {
            var c = Component(name);
            if (c == null) { Log($"Warning: Component '{name}' not found"); return; }
            c.Running = true;
        }

        public void StopComponent(string name)
        {
            var c = Component(name);
            if (c != null) c.Running = false;
        }

        public void ResetComponent(string name)
        {
            ErrorType = SuitFault.None;
            var c = Component(name);
            if (c == null) { Log($"Warning: Component '{name}' not found"); return; }
            c.Running = false;
            c.SimulationTime = 0f;
            foreach (var f in c.Fields)
            {
                // TSS quirk: min/max changed by faults are not restored
                f.Algorithm = f.StartingAlgorithm;
                f.Rate = f.StartingRate;
                f.Current = f.Start;
                f.Phase = 0f;
            }
            ResetHook?.Invoke();
        }

        // ------------------------------------------------------------------ update

        public void Update(float dt)
        {
            if (!Initialized) return;
            foreach (var c in Components) if (c.Running) c.SimulationTime += dt;

            var eva1 = Component("eva1");
            if (eva1 != null && eva1.Running)
            {
                // TSS quirk: exact float comparison; works because time advances in whole seconds
                if (NumTaskBoardErrors == 0 && eva1.SimulationTime == TimeToCompleteTaskBoard + ErrorTime)
                {
                    ThrowRandomFault();
                    Log($"Error thrown at simulation time: {eva1.SimulationTime:F2} seconds");
                }
                else if (eva1.SimulationTime == TimeToCompleteTaskBoard + ErrorTime)
                {
                    TimeToCompleteTaskBoard++;
                }
            }

            foreach (var f in UpdateOrder)
            {
                var c = Component(f.Component);
                if (c == null || !c.Running) continue;
                switch (f.Algorithm)
                {
                    case SimAlgorithm.SineWave: f.Current = SineWave(f); break;
                    case SimAlgorithm.LinearDecay: f.Current = LinearDecay(f); break;
                    case SimAlgorithm.LinearGrowth: f.Current = LinearGrowth(f); break;
                    case SimAlgorithm.DependentValue: f.Current = Dependent(f); break;
                    case SimAlgorithm.ExternalValue: f.Current = External(f); break;
                    case SimAlgorithm.ConstantValue: break;
                }
            }
        }

        static float SineWave(SimField f)
        {
            if (!f.Active) return f.Current;
            if (f.Phase < 0f) f.Phase = 0f;
            float value = f.Base + f.Amplitude * MathF.Sin(f.Phase);
            f.Phase += f.Frequency;
            if (f.Phase > 2.0f * 3.1415f) f.Phase = 0f; // TSS quirk: resets to 0 rather than wrapping
            return value;
        }

        static float LinearDecay(SimField f)
        {
            if (!f.Active) return f.Current;
            float v = f.Current - f.Rate;
            return v < f.Min ? f.Min : v;
        }

        static float LinearGrowth(SimField f)
        {
            if (!f.Active) return f.Current;
            float v = f.Current + f.Rate;
            return v > f.Max ? f.Max : v;
        }

        float Dependent(SimField f)
        {
            if (!f.Active) return f.Current;
            var formula = f.Params.Value<string>("formula");
            if (formula == null) { Log($"Warning: No formula specified for dependent field {f.Name}"); return 0f; }
            return EvaluateFormula(formula);
        }

        float External(SimField f)
        {
            var file = f.Params.Value<string>("file_path");
            var path = f.Params.Value<string>("field_path");
            if (file == null || path == null || ExternalLookup == null) return 0f;
            return ExternalLookup(file, path) ?? 0f;
        }

        // ------------------------------------------------------------------ formulas

        /// <summary>
        /// Two-stack evaluator from sim_algo_evaluate_formula. Tokens must be space separated
        /// ("oxy_pri_storage * 30.0"); names resolve to field values, unknown names to 0.
        /// </summary>
        public float EvaluateFormula(string formula)
        {
            var values = new Stack<float>();
            var ops = new Stack<char>();

            void Apply()
            {
                char op = ops.Pop();
                float b = values.Pop();
                float a = values.Pop();
                values.Push(ApplyOperator(op, a, b));
            }

            foreach (var token in formula.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token == ",") continue;
                if (token == "(") { ops.Push('('); continue; }
                if (token == ")")
                {
                    while (ops.Count > 0 && ops.Peek() != '(')
                    {
                        if (values.Count < 2) { ops.Pop(); break; }
                        Apply();
                    }
                    if (ops.Count > 0 && ops.Peek() == '(') ops.Pop();
                    continue;
                }
                if (token.Length == 1 && "+-*/".IndexOf(token[0]) >= 0)
                {
                    char op = token[0];
                    while (ops.Count > 0 && ops.Peek() != '(' && Precedence(ops.Peek()) >= Precedence(op))
                    {
                        if (values.Count < 2) { ops.Pop(); break; }
                        Apply();
                    }
                    ops.Push(op);
                    continue;
                }
                values.Push(TokenValue(token));
            }

            while (ops.Count > 0)
            {
                if (ops.Peek() == '(') { ops.Pop(); continue; }
                if (values.Count < 2) break;
                Apply();
            }
            return values.Count > 0 ? values.Peek() : 0f;
        }

        static int Precedence(char op) => op == '*' || op == '/' ? 3 : op == '+' || op == '-' ? 2 : 0;

        static float ApplyOperator(char op, float a, float b)
        {
            switch (op)
            {
                case '+': return a + b;
                case '-': return a - b;
                case '*': return a * b;
                case '/': return b != 0f ? a / b : 0f;
                default: return 0f;
            }
        }

        float TokenValue(string token)
        {
            bool numeric = char.IsDigit(token[0]) || (token[0] == '-' && token.Length > 1 && char.IsDigit(token[1]));
            return numeric ? (float)Atof(token) : GetValue(token);
        }

        /// <summary>C atof: parses the longest numeric prefix, 0 if none.</summary>
        public static double Atof(string s)
        {
            int end = 0;
            if (end < s.Length && (s[end] == '-' || s[end] == '+')) end++;
            while (end < s.Length && char.IsDigit(s[end])) end++;
            if (end < s.Length && s[end] == '.') { end++; while (end < s.Length && char.IsDigit(s[end])) end++; }
            if (end < s.Length && (s[end] == 'e' || s[end] == 'E'))
            {
                int e = end + 1;
                if (e < s.Length && (s[e] == '-' || s[e] == '+')) e++;
                if (e < s.Length && char.IsDigit(s[e])) { while (e < s.Length && char.IsDigit(s[e])) e++; end = e; }
            }
            return double.TryParse(s.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0;
        }

        // ------------------------------------------------------------------ faults (throw_errors.c)

        public bool ThrowRandomFault()
        {
            ErrorType = ForcedFault ?? (SuitFault)rng.Next(3);
            Log($"Error type determined to throw: {(int)ErrorType}");
            switch (ErrorType)
            {
                case SuitFault.O2PressureLow: return ThrowO2Low();
                case SuitFault.O2PressureHigh: return ThrowO2High();
                case SuitFault.FanRpmLow: return ThrowFanLow();
                default: return false;
            }
        }

        public bool ThrowO2Low()
        {
            var f = Component("eva1")?.Field("suit_pressure_oxy");
            if (f == null) return false;
            if (Dcu.O2) { f.Algorithm = SimAlgorithm.LinearDecay; f.Rate = OxyErrorRate; }
            return true;
        }

        public bool ThrowO2High()
        {
            var f = Component("eva1")?.Field("suit_pressure_oxy");
            if (f == null) return false;
            if (Dcu.O2) { f.Algorithm = SimAlgorithm.LinearGrowth; f.Rate = OxyErrorRate; }
            return true;
        }

        public bool ThrowFanLow()
        {
            var eva1 = Component("eva1");
            var fan = eva1?.Field("fan_pri_rpm");
            var helmet = eva1?.Field("helmet_pressure_co2");
            if (fan == null || helmet == null) return false;
            fan.Algorithm = SimAlgorithm.LinearDecay;
            helmet.Algorithm = SimAlgorithm.LinearGrowth;
            helmet.Rate = Co2Rate;
            return true;
        }
    }
}
