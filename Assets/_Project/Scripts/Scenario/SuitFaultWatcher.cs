using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SuitsSim.Core;
using SuitsSim.Tss;
using UnityEngine;

namespace SuitsSim.Scenario
{
    /// <summary>
    /// TSS throws a random suit fault (O2 pressure, fan RPM, scrubber, power) some seconds
    /// after the task board is finished. The tester has to notice it on the HUD and fix it
    /// on the DCU. This logs when each fault and each out-of-range value starts and clears,
    /// so the CSV holds "time to recover from a suit fault", the headline HITL number.
    /// </summary>
    [AddComponentMenu("SUITS Sim/Suit Fault Watcher")]
    public class SuitFaultWatcher : MonoBehaviour
    {
        [Serializable]
        public class Range
        {
            public string path;
            public float min, max;
            public Range(string path, float min, float max) { this.path = path; this.min = min; this.max = max; }
        }

        [SerializeField] string[] faultFlags = { "error.fan_error", "error.oxy_error", "error.power_error", "error.scrubber_error" };

        [Tooltip("From TSS2026 documents/telemetry_ranges/eva-telemetry-ranges.pdf. Only watched after egress.")]
        [SerializeField] List<Range> ranges = new List<Range>
        {
            new Range("telemetry.eva1.suit_pressure_oxy", 3.5f, 4.1f),
            new Range("telemetry.eva1.suit_pressure_co2", 0f, 0.1f),
            new Range("telemetry.eva1.helmet_pressure_co2", 0f, 0.15f),
            new Range("telemetry.eva1.fan_pri_rpm", 29000f, 31000f),
            new Range("telemetry.eva1.scrubber_a_co2_storage", 0f, 60f),
            new Range("telemetry.eva1.scrubber_b_co2_storage", 0f, 60f),
            new Range("telemetry.eva1.heart_rate", 50f, 160f),
            new Range("telemetry.eva1.temperature", 10f, 32f),
            new Range("telemetry.eva1.oxy_pri_storage", 20f, 100f),
            new Range("telemetry.eva1.primary_battery_level", 20f, 100f),
        };

        readonly Dictionary<string, float> activeSince = new Dictionary<string, float>();

        void OnEnable()
        {
            if (SimRoot.Instance != null) SimRoot.Instance.Tss.SnapshotReceived += OnSnapshot;
        }

        void OnDisable()
        {
            if (SimRoot.Instance != null) SimRoot.Instance.Tss.SnapshotReceived -= OnSnapshot;
        }

        void OnSnapshot(uint command, JObject eva)
        {
            if (command != TssCommand.GetEva) return;
            var runner = ScenarioRunner.Instance;
            if (runner == null || runner.Phase == EvaPhase.Idle || runner.Phase == EvaPhase.Complete) return;

            foreach (var flag in faultFlags)
                if (TssJson.TryBool(eva, flag, out var on)) Track("fault", flag, on, on ? "TRUE" : "false");

            // Suit values are meaningless while it is being filled at the UIA.
            if (runner.Phase == EvaPhase.Egress) return;
            foreach (var r in ranges)
                if (TssJson.TryFloat(eva, r.path, out var v))
                    Track("out_of_range", r.path, v < r.min || v > r.max, $"{v:G4} (ok {r.min}-{r.max})");
        }

        void Track(string category, string key, bool isActive, string value)
        {
            var log = SimRoot.Instance.Log;
            bool wasActive = activeSince.TryGetValue(key, out var since);
            if (isActive && !wasActive)
            {
                activeSince[key] = Time.time;
                log.Event(category + "_start", key, value);
            }
            else if (!isActive && wasActive)
            {
                activeSince.Remove(key);
                log.Event(category + "_clear", key, $"{value}; lasted {Time.time - since:F0}s");
            }
        }
    }
}
