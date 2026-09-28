using System;
using System.Globalization;
using System.IO;
using System.Text;
using SuitsSim.Core;
using SuitsSim.Scenario;
using SuitsSim.World;
using UnityEngine;

namespace SuitsSim.Logging
{
    /// <summary>
    /// One CSV per run under Application.persistentDataPath/hitl. Every row carries the
    /// phase and the tester's TSS position, so the file alone answers "how long did
    /// egress take, where did they walk, how fast did they react to the fan fault".
    /// Columns: utc, t_s, participant, phase, category, key, value, tss_x, tss_y
    /// </summary>
    [AddComponentMenu("SUITS Sim/HITL Logger")]
    public class HitlLogger : MonoBehaviour
    {
        public string CurrentPath { get; private set; }
        public bool InSession => writer != null;

        SimConfig config;
        StreamWriter writer;
        float sessionStart;

        public void Init(SimConfig simConfig) => config = simConfig;

        public void BeginSession()
        {
            EndSession();
            var dir = Path.Combine(Application.persistentDataPath, "hitl");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            CurrentPath = Path.Combine(dir, $"{Sanitize(config.participantId)}_{stamp}.csv");
            writer = new StreamWriter(CurrentPath, false, new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine("utc,t_s,participant,phase,category,key,value,tss_x,tss_y");
            sessionStart = Time.time;
            Event("session", "start", $"tss={config.tssHost}:{config.tssPort}; board={config.taskBoard}; note={config.sessionNote}");
            Debug.Log($"[HITL] logging to {CurrentPath}");
        }

        public void EndSession()
        {
            if (writer == null) return;
            Event("session", "end", "");
            writer.Dispose();
            writer = null;
        }

        /// <summary>Safe to call anytime; outside a session it only goes to the console.</summary>
        public void Event(string category, string key, string value)
        {
            if (writer == null)
            {
                if (category != "pose") Debug.Log($"[HITL] {category} {key} {value}");
                return;
            }

            var phase = ScenarioRunner.Instance != null ? ScenarioRunner.Instance.Phase.ToString() : "";
            var pos = AstronautReporter.Instance != null ? AstronautReporter.Instance.TssPosition : Vector2.zero;
            var inv = CultureInfo.InvariantCulture;
            writer.WriteLine(string.Join(",",
                DateTime.UtcNow.ToString("o", inv),
                (Time.time - sessionStart).ToString("F2", inv),
                Csv(config.participantId), phase, Csv(category), Csv(key), Csv(value),
                pos.x.ToString("F2", inv), pos.y.ToString("F2", inv)));
        }

        void OnDestroy() => EndSession();
        void OnApplicationQuit() => EndSession();

        static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return string.IsNullOrWhiteSpace(s) ? "P00" : s;
        }
    }
}
