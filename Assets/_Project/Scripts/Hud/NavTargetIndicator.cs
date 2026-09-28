using SuitsSim.Core;
using SuitsSim.Scenario;
using SuitsSim.World;
using TMPro;
using UnityEngine;

namespace SuitsSim.Hud
{
    /// <summary>
    /// Straight-line distance and relative bearing to the current phase's destination.
    /// Starting point for HUD navigation. Swap in your own path planning (e.g. A* over the
    /// rock yard map with the keep-out zones from TSS2026 documents/maps/annotated).
    /// </summary>
    [AddComponentMenu("SUITS Sim/Nav Target Indicator")]
    public class NavTargetIndicator : MonoBehaviour
    {
        [SerializeField] TMP_Text text;
        [Tooltip("Optional arrow on the HUD; rotated around its local Z to point at the target.")]
        [SerializeField] RectTransform arrow;

        void Update()
        {
            var runner = ScenarioRunner.Instance;
            var sites = SiteRegistry.Instance;
            var me = AstronautReporter.Instance;
            if (runner == null || sites == null || me == null || SimRoot.Instance == null) return;

            SiteId? target = runner.Phase switch
            {
                EvaPhase.NavToLtv => sites.TaskBoardSite(SimRoot.Instance.Config.taskBoard),
                EvaPhase.LtvRepair => sites.TaskBoardSite(SimRoot.Instance.Config.taskBoard),
                EvaPhase.NavToPr => SiteId.Hab,
                EvaPhase.Ingress => SiteId.Hab,
                EvaPhase.Egress => SiteId.Hab,
                _ => null,
            };

            if (target == null)
            {
                if (text != null) text.text = "";
                if (arrow != null) arrow.gameObject.SetActive(false);
                return;
            }

            var goal = sites.Tss(target.Value);
            var delta = goal - me.TssPosition;
            var bearing = Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg; // map bearing, 0 = map up
            var relative = WorldFrame.DeltaDegrees(bearing, me.MapBearing);

            if (text != null) text.text = $"{target.Value}  {delta.magnitude:F0} m  {relative:+0;-0}°";
            if (arrow != null)
            {
                arrow.gameObject.SetActive(true);
                arrow.localRotation = Quaternion.Euler(0f, 0f, -relative);
            }
        }
    }
}
