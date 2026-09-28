using System;
using System.Collections.Generic;
using SuitsSim.Core;
using SuitsSim.Panels;
using SuitsSim.TaskBoard;
using SuitsSim.Tss;
using SuitsSim.World;
using UnityEngine;

namespace SuitsSim.Scenario
{
    public enum EvaPhase { Idle, Egress, NavToLtv, LtvRepair, NavToPr, Ingress, Complete }

    /// <summary>
    /// Runs the EV test timeline and times each phase against its budget. Budgets are
    /// from the FY26 EV procedure timeline (TSS2026 documents/procedures); update them
    /// when the FY27 timeline is published. Each phase has a completion check so a run
    /// can proceed hands-free, and the conductor can always advance manually for the
    /// verbal steps ("verbally confirm", "announce").
    /// </summary>
    [AddComponentMenu("SUITS Sim/Scenario Runner")]
    public class ScenarioRunner : MonoBehaviour
    {
        [Serializable]
        public class PhaseBudget
        {
            public EvaPhase phase;
            public float budgetSeconds;
        }

        [SerializeField] List<PhaseBudget> budgets = new List<PhaseBudget>
        {
            new PhaseBudget { phase = EvaPhase.Egress, budgetSeconds = 7 * 60 },
            new PhaseBudget { phase = EvaPhase.NavToLtv, budgetSeconds = 3 * 60 },
            new PhaseBudget { phase = EvaPhase.LtvRepair, budgetSeconds = 10 * 60 },
            new PhaseBudget { phase = EvaPhase.NavToPr, budgetSeconds = 3 * 60 },
            new PhaseBudget { phase = EvaPhase.Ingress, budgetSeconds = 2 * 60 },
        };

        [Tooltip("EVA telemetry values to record when egress ends, i.e. did the tester actually fill the suit.")]
        [SerializeField] string[] egressChecks =
        {
            "telemetry.eva1.oxy_pri_pressure", "telemetry.eva1.oxy_sec_pressure",
            "telemetry.eva1.coolant_storage", "telemetry.eva1.suit_pressure_oxy",
            "telemetry.eva1.primary_battery_level",
        };

        public static ScenarioRunner Instance { get; private set; }
        public event Action<EvaPhase, EvaPhase> PhaseChanged;

        public EvaPhase Phase { get; private set; } = EvaPhase.Idle;
        public float PhaseElapsed => Phase == EvaPhase.Idle ? 0f : Time.time - phaseStartedAt;
        public float TotalElapsed => Phase == EvaPhase.Idle ? 0f : Time.time - runStartedAt;
        public float PhaseBudgetSeconds => BudgetFor(Phase);

        float phaseStartedAt, runStartedAt, phaseStartMeters;
        bool ingressConnected;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void StartRun()
        {
            var root = SimRoot.Instance;
            var embedded = root.Embedded;
            if (embedded != null) embedded.Boot();       // fresh TSS, like restarting the server
            PanelRegistry.ResetAll();                    // CAPCOM: UIA down, DCU back, task board pre-repair
            if (embedded != null) embedded.StartEva();   // CAPCOM: START EVA telemetry
            if (AstronautReporter.Instance != null) AstronautReporter.Instance.ResetWalked();
            if (LtvErrorMonitor.Instance != null) LtvErrorMonitor.Instance.ClearLocalState();

            root.Log.BeginSession();
            if (!TssJson.TryBool(root.Tss.Eva, "status.started", out var started) || !started)
                root.Log.Event("warning", "tss", "EVA telemetry not started: press START in the TSS web UI");

            runStartedAt = Time.time;
            EnterPhase(EvaPhase.Egress);
        }

        public void Advance(string reason = "conductor")
        {
            if (Phase == EvaPhase.Idle || Phase == EvaPhase.Complete) return;
            ExitPhase(reason);
            EnterPhase(Phase + 1);
            if (Phase == EvaPhase.Complete)
            {
                SimRoot.Instance.Log.Event("run", "total", $"{TotalElapsed:F0}s");
                SimRoot.Instance.Log.EndSession();
            }
        }

        public void Abort()
        {
            if (Phase == EvaPhase.Idle) return;
            SimRoot.Instance.Log.Event("run", "abort", Phase.ToString());
            SimRoot.Instance.Log.EndSession();
            SetPhase(EvaPhase.Idle);
        }

        void Update()
        {
            if (Phase == EvaPhase.Idle || Phase == EvaPhase.Complete) return;
            if (Phase == EvaPhase.Ingress && PanelRegistry.Is("UMBILICAL", true)) ingressConnected = true;
            if (SimRoot.Instance.Config.autoAdvancePhases && IsPhaseDone()) Advance("auto");
        }

        bool IsPhaseDone()
        {
            var cfg = SimRoot.Instance.Config;
            var sites = SiteRegistry.Instance;
            var astronaut = AstronautReporter.Instance;
            switch (Phase)
            {
                case EvaPhase.Egress:
                    return PanelRegistry.Is("UMBILICAL", false)
                        && PanelRegistry.Is("UIA.DEPRESS", false)
                        && PanelRegistry.Is("DCU.BATT", false)   // false = suit battery (LOCAL)
                        && PhaseElapsed > 5f; // don't trip on the reset state
                case EvaPhase.NavToLtv:
                    return sites != null && astronaut != null
                        && astronaut.DistanceTo(sites.Tss(sites.TaskBoardSite(cfg.taskBoard))) <= cfg.arrivalRadiusMeters;
                case EvaPhase.LtvRepair:
                    return LtvErrorMonitor.Instance != null && LtvErrorMonitor.Instance.AllResolved;
                case EvaPhase.NavToPr:
                    return sites != null && astronaut != null
                        && astronaut.DistanceTo(sites.Tss(SiteId.Hab)) <= cfg.arrivalRadiusMeters;
                case EvaPhase.Ingress:
                    return ingressConnected && PanelRegistry.Is("UMBILICAL", false);
                default:
                    return false;
            }
        }

        void EnterPhase(EvaPhase next)
        {
            SetPhase(next);
            phaseStartedAt = Time.time;
            phaseStartMeters = AstronautReporter.Instance != null ? AstronautReporter.Instance.MetersWalked : 0f;
            ingressConnected = false;
            if (next != EvaPhase.Complete)
                SimRoot.Instance.Log.Event("phase_start", next.ToString(), $"budget {BudgetFor(next):F0}s");
        }

        void ExitPhase(string reason)
        {
            var root = SimRoot.Instance;
            var elapsed = PhaseElapsed;
            var budget = BudgetFor(Phase);
            var walked = AstronautReporter.Instance != null ? AstronautReporter.Instance.MetersWalked - phaseStartMeters : 0f;
            root.Log.Event("phase_end", Phase.ToString(),
                $"{elapsed:F0}s of {budget:F0}s{(elapsed > budget ? " OVER" : "")}; walked {walked:F0} m; by {reason}");

            if (Phase == EvaPhase.Egress)
            {
                foreach (var path in egressChecks)
                    root.Log.Event("egress_check", path, TssJson.Text(root.Tss.Eva, path));
            }
        }

        void SetPhase(EvaPhase next)
        {
            var prev = Phase;
            Phase = next;
            PhaseChanged?.Invoke(prev, next);
        }

        float BudgetFor(EvaPhase phase)
        {
            foreach (var b in budgets) if (b.phase == phase) return b.budgetSeconds;
            return 0f;
        }
    }
}
