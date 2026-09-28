using SuitsSim.Core;
using SuitsSim.Logging;
using SuitsSim.Panels;
using SuitsSim.Scenario;
using SuitsSim.TaskBoard;
using SuitsSim.Tss.Embedded;
using SuitsSim.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SuitsSim.Conductor
{
    /// <summary>
    /// Desktop-window panel for the person running the test (CAPCOM / test conductor),
    /// drawn on the PC mirror while the tester is in the headset. IMGUI on purpose:
    /// it is tooling, never seen in VR, and needs no canvas setup.
    ///
    /// Keys: F1 start run, F2 advance phase, F3 log "verbal confirmation", F9 abort,
    /// F10 hide/show.
    /// </summary>
    [AddComponentMenu("SUITS Sim/Conductor Console")]
    public class ConductorConsole : MonoBehaviour
    {
        [SerializeField] RepairProcedureTracker repairTracker;
        [SerializeField] bool visible = true;

        string note = "";
        string host;
        string lastFailure = "";
        Rect window = new Rect(12, 12, 400, 600);
        Vector2 scroll;

        void Start()
        {
            var root = SimRoot.Instance;
            if (root == null) { enabled = false; return; }
            host = root.Config.tssHost;
            root.Tss.PostFailed += (cmd, v, reason) => lastFailure = $"cmd {cmd} = {v}: {reason}";
            if (repairTracker == null) repairTracker = FindAnyObjectByType<RepairProcedureTracker>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            var runner = ScenarioRunner.Instance;
            if (kb == null || runner == null) return;
            if (kb.f1Key.wasPressedThisFrame) runner.StartRun();
            if (kb.f2Key.wasPressedThisFrame) runner.Advance();
            if (kb.f3Key.wasPressedThisFrame) SimRoot.Instance.Log.Event("conductor", "verbal", $"confirmed during {runner.Phase}");
            if (kb.f9Key.wasPressedThisFrame) runner.Abort();
            if (kb.f10Key.wasPressedThisFrame) visible = !visible;
        }

        void OnGUI()
        {
            if (!visible || SimRoot.Instance == null) return;
            window = GUILayout.Window(4242, window, Draw, "SUITS Sim Conductor");
        }

        void Draw(int id)
        {
            var root = SimRoot.Instance;
            var cfg = root.Config;
            var tss = root.Tss;
            var runner = ScenarioRunner.Instance;
            scroll = GUILayout.BeginScrollView(scroll);

            // --- TSS link
            GUILayout.Label($"{tss.Description}  {(tss.IsConnected ? "<color=lime>LIVE</color>" : "<color=red>NO DATA</color>")}  " +
                            $"last {Age(tss.SecondsSinceAnySnapshot)}  posts {tss.PostsSent}/{tss.PostsFailed} failed", Rich());
            var embedded = root.Embedded;
            if (embedded != null)
            {
                if (!embedded.Ready) GUILayout.Label($"<color=red>{embedded.LoadError}</color>", Rich());
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Start EVA")) embedded.StartEva();
                if (GUILayout.Button("Reset EVA")) embedded.ResetEva();
                if (GUILayout.Button("Reboot TSS")) embedded.Boot();
                embedded.paused = GUILayout.Toggle(embedded.paused, "Pause");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Speed x{embedded.timeScale:0.#}", GUILayout.Width(80));
                embedded.timeScale = Mathf.Round(GUILayout.HorizontalSlider(embedded.timeScale, 0f, 20f) * 2f) / 2f;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Fault now:", GUILayout.Width(70));
                if (GUILayout.Button("O2 low")) embedded.ThrowFaultNow(SuitFault.O2PressureLow);
                if (GUILayout.Button("O2 high")) embedded.ThrowFaultNow(SuitFault.O2PressureHigh);
                if (GUILayout.Button("Fan")) embedded.ThrowFaultNow(SuitFault.FanRpmLow);
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                host = GUILayout.TextField(host, GUILayout.Width(200));
                if (GUILayout.Button("Reconnect"))
                {
                    cfg.tssHost = host.Trim();
                    tss.Restart();
                }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Push all switch positions to TSS")) PanelRegistry.PushAll();
            if (!string.IsNullOrEmpty(lastFailure)) GUILayout.Label($"<color=orange>Last write failure: {lastFailure}</color>", Rich());

            // --- Session setup
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Participant", GUILayout.Width(80));
            cfg.participantId = GUILayout.TextField(cfg.participantId, GUILayout.Width(80));
            if (GUILayout.Toggle(cfg.taskBoard == TaskBoardSite.Alpha, "Alpha")) cfg.taskBoard = TaskBoardSite.Alpha;
            if (GUILayout.Toggle(cfg.taskBoard == TaskBoardSite.Bravo, "Bravo")) cfg.taskBoard = TaskBoardSite.Bravo;
            GUILayout.EndHorizontal();
            cfg.autoAdvancePhases = GUILayout.Toggle(cfg.autoAdvancePhases, "Auto-advance phases");

            // --- Run control
            GUILayout.Space(6);
            if (runner != null)
            {
                var over = runner.PhaseElapsed > runner.PhaseBudgetSeconds && runner.PhaseBudgetSeconds > 0;
                GUILayout.Label($"<b>{runner.Phase}</b>  {Clock(runner.PhaseElapsed)} / {Clock(runner.PhaseBudgetSeconds)}" +
                                $"{(over ? "  <color=red>OVER</color>" : "")}   total {Clock(runner.TotalElapsed)}", Rich());
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("F1 Start")) runner.StartRun();
                if (GUILayout.Button("F2 Advance")) runner.Advance();
                if (GUILayout.Button("F9 Abort")) runner.Abort();
                GUILayout.EndHorizontal();
            }

            var me = AstronautReporter.Instance;
            if (me != null)
                GUILayout.Label($"EV{cfg.evaNumber} ({me.TssPosition.x:F1}, {me.TssPosition.y:F1})  hdg {me.TssHeading:F0}  walked {me.MetersWalked:F0} m");

            // --- LTV repair
            GUILayout.Space(6);
            var monitor = LtvErrorMonitor.Instance;
            var active = monitor != null ? monitor.Active : null;
            if (monitor == null || !monitor.HasData) GUILayout.Label("LTV errors: waiting for TSS");
            else if (active == null) GUILayout.Label("LTV errors: all resolved");
            else
            {
                GUILayout.Label($"LTV error {active.code}: {active.description}");
                if (repairTracker != null && repairTracker.ActiveCode == active.code && repairTracker.GroupCount > 0)
                {
                    GUILayout.Label($"Procedure step group {repairTracker.GroupIndex + 1}/{repairTracker.GroupCount}, deviations {repairTracker.Deviations}");
                    GUILayout.Label($"Next: {string.Join(", ", repairTracker.Pending)}");
                }
                if (GUILayout.Button($"Resolve {active.code} manually")) monitor.Resolve(active.index);
            }

            // --- Notes
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            note = GUILayout.TextField(note);
            if (GUILayout.Button("Log note", GUILayout.Width(80)) && note.Length > 0)
            {
                root.Log.Event("conductor", "note", note);
                note = "";
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("F3 = log verbal confirmation.  F10 = hide.");

            var log = root.GetComponent<HitlLogger>();
            if (log.InSession) GUILayout.Label($"Logging: {log.CurrentPath}");
            if (embedded == null) GUILayout.Label("Between participants: reset TSS (scripts/reset_data.bat) and press START EVA in its web UI.");

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        static GUIStyle rich;
        static GUIStyle Rich() => rich ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
        static string Clock(float s) => $"{(int)s / 60}:{(int)s % 60:00}";
        static string Age(float s) => float.IsInfinity(s) ? "never" : $"{s:F1}s ago";
    }
}
