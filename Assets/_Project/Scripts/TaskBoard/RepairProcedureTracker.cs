using System.Collections.Generic;
using System.Linq;
using SuitsSim.Core;
using SuitsSim.Panels;
using UnityEngine;

namespace SuitsSim.TaskBoard
{
    /// <summary>
    /// Watches the tester work the virtual task board against the procedure for the
    /// active LTV error. Correct actions advance the procedure; anything else is logged
    /// as a deviation (a HITL metric) without blocking. When the last group completes,
    /// it tells TSS the error is resolved, which is what the physical task board does.
    /// </summary>
    [AddComponentMenu("SUITS Sim/Repair Procedure Tracker")]
    public class RepairProcedureTracker : MonoBehaviour
    {
        [Tooltip("JSON procedure files from Assets/_Project/Procedures.")]
        [SerializeField] List<TextAsset> procedureFiles = new List<TextAsset>();
        [Tooltip("Only controls with these id prefixes count as task board actions.")]
        [SerializeField] string[] taskBoardPrefixes = { "PDD.", "POPS.", "SMC.", "NAV.", "COMMS.", "TB." };

        public string ActiveCode { get; private set; }
        public int GroupIndex { get; private set; }
        public int GroupCount => current?.groups.Count ?? 0;
        public int Deviations { get; private set; }
        public IEnumerable<string> Pending => pending.Select(a => a.ToString());

        readonly Dictionary<string, RepairProcedure> procedures = new Dictionary<string, RepairProcedure>();
        readonly List<RepairProcedure.StepAction> pending = new List<RepairProcedure.StepAction>();
        RepairProcedure current;
        int activeIndex = -1;
        float startedAt;

        void Awake()
        {
            foreach (var file in procedureFiles)
            {
                if (file == null) continue;
                try
                {
                    var p = RepairProcedure.FromJson(file.text);
                    procedures[p.code] = p;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Procedure {file.name} failed to parse: {e.Message}", file);
                }
            }
        }

        void OnEnable()
        {
            PanelSwitch.AnyChanged += OnSwitch;
            PanelButton.AnyPressed += OnPress;
            PanelButton.AnyReleased += OnRelease;
        }

        void OnDisable()
        {
            PanelSwitch.AnyChanged -= OnSwitch;
            PanelButton.AnyPressed -= OnPress;
            PanelButton.AnyReleased -= OnRelease;
        }

        void Update()
        {
            var monitor = LtvErrorMonitor.Instance;
            if (monitor == null || !monitor.HasData) return;

            var active = monitor.Active;
            var code = active?.code;
            if (code == ActiveCode) return;

            ActiveCode = code;
            activeIndex = active?.index ?? -1;
            Begin(code);
        }

        void Begin(string code)
        {
            current = null;
            pending.Clear();
            GroupIndex = 0;
            Deviations = 0;
            if (code == null) return;

            var log = SimRoot.Instance.Log;
            if (!procedures.TryGetValue(code, out current))
            {
                log.Event("repair", code, "NO_PROCEDURE_FILE (conductor resolves manually)");
                return;
            }
            startedAt = Time.time;
            log.Event("repair", code, "START");
            LoadGroup();
        }

        void LoadGroup()
        {
            pending.Clear();
            if (current != null && GroupIndex < current.groups.Count)
                pending.AddRange(current.groups[GroupIndex].actions);
        }

        void OnSwitch(PanelSwitch s, PanelSwitch.ChangeSource source)
        {
            if (source != PanelSwitch.ChangeSource.Tester) return;
            Handle(new RepairProcedure.StepAction { action = RepairProcedure.ActionKind.Set, control = s.ControlId, state = s.State });
        }

        void OnPress(PanelButton b) =>
            Handle(new RepairProcedure.StepAction { action = RepairProcedure.ActionKind.Press, control = b.ControlId });

        void OnRelease(PanelButton b) =>
            Handle(new RepairProcedure.StepAction { action = RepairProcedure.ActionKind.Release, control = b.ControlId });

        void Handle(RepairProcedure.StepAction done)
        {
            if (current == null || !IsTaskBoardControl(done.control)) return;
            var log = SimRoot.Instance.Log;

            int match = pending.FindIndex(p => p.Matches(done));
            if (match < 0)
            {
                Deviations++;
                log.Event("repair_deviation", ActiveCode,
                    $"did {done} at step {current.groups[GroupIndex].stepRef}; expected {string.Join(" | ", Pending)}");
                return;
            }

            pending.RemoveAt(match);
            if (pending.Count > 0) return;

            log.Event("repair_step", ActiveCode, $"step {current.groups[GroupIndex].stepRef} done");
            GroupIndex++;
            if (GroupIndex < current.groups.Count)
            {
                LoadGroup();
                return;
            }

            log.Event("repair", ActiveCode, $"COMPLETE {Time.time - startedAt:F0}s, {Deviations} deviations");
            LtvErrorMonitor.Instance.Resolve(activeIndex);
            current = null; // wait for TSS to confirm and surface the next error
        }

        bool IsTaskBoardControl(string id)
        {
            foreach (var p in taskBoardPrefixes) if (id.StartsWith(p)) return true;
            return false;
        }
    }
}
