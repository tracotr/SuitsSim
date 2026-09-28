using Newtonsoft.Json.Linq;
using SuitsSim.Core;
using SuitsSim.Tss;
using TMPro;
using UnityEngine;

namespace SuitsSim.Hud
{
    /// <summary>
    /// Binds one TMP label to one TSS JSON path, the same data-path idea the TSS web UI
    /// uses. A quick way to get values on screen while you design the real HUD.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    [AddComponentMenu("SUITS Sim/HUD Binding")]
    public class HudBinding : MonoBehaviour
    {
        [SerializeField] uint source = TssCommand.GetEva;
        [SerializeField] string path = "telemetry.eva1.oxy_pri_pressure";
        [SerializeField] string label = "O2 PRI";
        [SerializeField] string format = "F0";
        [SerializeField] string units = "psi";

        [Header("Caution / warning")]
        [SerializeField] bool useRange = true;
        [SerializeField] float min = 600f;
        [SerializeField] float max = 3000f;
        [Tooltip("Fraction of the range treated as 'nearing a limit' (the ranges doc asks for early warnings).")]
        [Range(0f, 0.5f)] [SerializeField] float cautionBand = 0.1f;
        [SerializeField] Color nominal = Color.white;
        [SerializeField] Color caution = new Color(1f, 0.8f, 0.2f);
        [SerializeField] Color warning = new Color(1f, 0.3f, 0.25f);

        TMP_Text text;

        void Awake() => text = GetComponent<TMP_Text>();

        void OnEnable()
        {
            if (SimRoot.Instance != null) SimRoot.Instance.Tss.SnapshotReceived += OnSnapshot;
            text.text = $"{label}  --";
        }

        void OnDisable()
        {
            if (SimRoot.Instance != null) SimRoot.Instance.Tss.SnapshotReceived -= OnSnapshot;
        }

        void OnSnapshot(uint command, JObject json)
        {
            if (command != source) return;
            if (!TssJson.TryFloat(json, path, out var v))
            {
                text.text = $"{label}  {TssJson.Text(json, path)}";
                text.color = nominal;
                return;
            }

            text.text = $"{label}  {v.ToString(format)} {units}";
            if (!useRange) { text.color = nominal; return; }

            var band = (max - min) * cautionBand;
            if (v < min || v > max) text.color = warning;
            else if (v < min + band || v > max - band) text.color = caution;
            else text.color = nominal;
        }
    }
}
