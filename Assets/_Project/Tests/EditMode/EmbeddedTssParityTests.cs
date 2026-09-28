using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SuitsSim.Tss.Embedded;

namespace SuitsSim.Tests
{
    /// <summary>
    /// Replays a session recorded from the real TSS2026 server (Tools/tss_record.py) through the
    /// C# port and compares every EVA.json value and every LTV error flag, second by second.
    /// Needs the TSS data files imported into Assets/_Project/TssData (SUITS Sim > Import TSS Data).
    /// </summary>
    public class EmbeddedTssParityTests
    {
        public static string DataDir =>
            Environment.GetEnvironmentVariable("SUITS_TSS_DATA") ?? "Assets/_Project/TssData";
        const string TraceDir = "Assets/_Project/Tests/EditMode/Parity/";

        public static TssBackend LoadBackend(int? seed = 1)
        {
            if (!File.Exists(Path.Combine(DataDir, "eva1.json")))
                Assert.Ignore($"TSS data not imported into {DataDir}; run SUITS Sim > Import TSS Data first.");
            string Read(string f) => File.ReadAllText(Path.Combine(DataDir, f));
            return new TssBackend(
                new[] { Read("eva1.json"), Read("eva2.json"), Read("rover.json") },
                new Dictionary<string, string>
                {
                    ["EVA"] = Read("EVA.json"), ["ROVER"] = Read("ROVER.json"),
                    ["LTV"] = Read("LTV.json"), ["LTV_ERRORS"] = Read("LTV_ERRORS.json"),
                },
                seed);
        }

        [TestCase("tss2026_trace_o2_fault.json")]
        [TestCase("tss2026_trace_fan_fault.json")]
        public void MatchesRecordedTss2026Session(string traceFile)
        {
            var backend = LoadBackend();
            var trace = JObject.Parse(File.ReadAllText(TraceDir + traceFile));
            backend.Engine.ErrorTime = trace.Value<int>("error_time");
            backend.Engine.ForcedFault = (SuitFault)trace.Value<int>("error_type");

            var steps = (JObject)trace["script"]["steps"];
            var mismatches = new List<string>();
            backend.Tick(); // TSS has already ticked before the first sample

            foreach (JObject sample in (JArray)trace["samples"])
            {
                int k = sample.Value<int>("k");
                if (steps[k.ToString(CultureInfo.InvariantCulture)] is JArray actions)
                    foreach (JObject a in actions)
                    {
                        if (a["udp"] != null) backend.HandlePost(a.Value<uint>("udp"), a.Value<float>("value"));
                        else backend.ApplyRoute(a.Value<string>("route"), a.Value<string>("value"));
                    }

                Compare($"k={k} eva", sample["eva"], backend.Snapshot(1), mismatches);
                var errors = (JArray)backend.Snapshot(3)["error_procedures"];
                var expected = (JArray)sample["errors"];
                if (errors.Count != expected.Count) mismatches.Add($"k={k} errors: {errors.Count} entries, TSS had {expected.Count}");
                else for (int i = 0; i < errors.Count; i++)
                        if (errors[i].Value<bool>("needs_resolved") != expected[i].Value<bool>("needs_resolved"))
                            mismatches.Add($"k={k} error {expected[i]["code"]}: needs_resolved {errors[i]["needs_resolved"]}, TSS {expected[i]["needs_resolved"]}");

                backend.Tick();
                if (mismatches.Count > 40) break;
            }

            Assert.IsTrue(mismatches.Count == 0,
                $"{mismatches.Count} mismatches vs TSS2026:\n" + string.Join("\n", mismatches.Take(40)));
        }

        static void Compare(string path, JToken expected, JToken actual, List<string> diffs)
        {
            if (expected is JObject eo)
            {
                foreach (var p in eo.Properties())
                {
                    var a = (actual as JObject)?[p.Name];
                    if (a == null) { diffs.Add($"{path}.{p.Name}: missing"); continue; }
                    Compare($"{path}.{p.Name}", p.Value, a, diffs);
                }
                return;
            }
            if (expected.Type == JTokenType.Float || expected.Type == JTokenType.Integer)
            {
                if (actual.Type != JTokenType.Float && actual.Type != JTokenType.Integer) { diffs.Add($"{path}: {actual} vs TSS {expected}"); return; }
                double e = expected.Value<double>(), v = actual.Value<double>();
                if (Math.Abs(e - v) > 1e-3 + 1e-5 * Math.Abs(e)) diffs.Add($"{path}: {v} vs TSS {e}");
                return;
            }
            if (!JToken.DeepEquals(expected, actual)) diffs.Add($"{path}: {actual} vs TSS {expected}");
        }
    }
}
