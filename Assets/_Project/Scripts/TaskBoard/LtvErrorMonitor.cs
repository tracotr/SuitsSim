using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SuitsSim.Core;
using SuitsSim.Tss;
using TMPro;
using UnityEngine;

namespace SuitsSim.TaskBoard
{
    /// <summary>
    /// Mirrors LTV_ERRORS.json and drives the task board's PDD display.
    ///
    /// TSS hides every error except Recovery Mode (4800) until 4800 is resolved, and new
    /// errors can appear after others clear, so this keeps polling for the whole repair.
    /// Array index = TSS resolve slot (command 2023 + index). While in recovery mode the
    /// reply has only one entry; 4800 is also index 0 in the full file, so the slot holds.
    /// </summary>
    [AddComponentMenu("SUITS Sim/LTV Error Monitor")]
    public class LtvErrorMonitor : MonoBehaviour
    {
        public class LtvError
        {
            public int index;
            public string code;
            public string description;
            public bool needsResolved;
            public string procedureText;
        }

        [Tooltip("The PDD screen on the task board. Shows the first unresolved code, like the real display.")]
        [SerializeField] TMP_Text pddDisplay;

        public static LtvErrorMonitor Instance { get; private set; }

        public IReadOnlyList<LtvError> Errors => errors;
        public bool HasData { get; private set; }
        public bool AllResolved => HasData && Active == null;
        public LtvError Active
        {
            get { foreach (var e in errors) if (e.needsResolved) return e; return null; }
        }

        readonly List<LtvError> errors = new List<LtvError>();
        readonly HashSet<string> seenActive = new HashSet<string>();

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            if (SimRoot.Instance != null) SimRoot.Instance.Tss.SnapshotReceived += OnSnapshot;
        }

        void OnDisable()
        {
            if (SimRoot.Instance != null) SimRoot.Instance.Tss.SnapshotReceived -= OnSnapshot;
        }

        void OnSnapshot(uint command, JObject json)
        {
            if (command != TssCommand.GetLtvErrors) return;
            if (!(json["error_procedures"] is JArray arr)) return;

            errors.Clear();
            for (int i = 0; i < arr.Count; i++)
            {
                var e = arr[i];
                var procedures = e["procedures"] as JArray;
                errors.Add(new LtvError
                {
                    index = i,
                    code = e.Value<string>("code"),
                    description = e.Value<string>("description"),
                    needsResolved = e.Value<bool?>("needs_resolved") ?? false,
                    procedureText = procedures != null && procedures.Count > 0 ? procedures[0].ToString() : "",
                });
            }
            HasData = true;

            var log = SimRoot.Instance.Log;
            foreach (var err in errors)
            {
                if (err.needsResolved && seenActive.Add(err.code)) log.Event("ltv_error", err.code, "ACTIVE");
                if (!err.needsResolved && seenActive.Remove(err.code)) log.Event("ltv_error", err.code, "RESOLVED");
            }

            if (pddDisplay != null)
            {
                var active = Active;
                pddDisplay.text = active == null ? "PDD  OK" : $"PDD  {active.code}\n{active.description}";
            }
        }

        /// <summary>Tells TSS the error is fixed. Called by the repair tracker or the conductor.</summary>
        public void Resolve(int index)
        {
            var root = SimRoot.Instance;
            if (root == null) return;
            root.Tss.Send(TssCommand.LtvErrorNeedsResolved(index), false);
            root.Log.Event("ltv_error", index < errors.Count ? errors[index].code : $"slot{index}", "RESOLVE_SENT");
        }

        // No reset here on purpose: TSS decides which errors start active and which appear
        // later. Between participants, reset TSS itself (scripts/reset_data.bat or restart).
        public void ClearLocalState() => seenActive.Clear();
    }
}
