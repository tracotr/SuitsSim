using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One telemetry readout on the EVA panel, coloured by the value's nominal range from the TSS telemetry ranges.
// Rings are for 0 to 100 percent values and fill clockwise from the top with the value inside. Bars are for everything else and run left to right with the value beside them.
// Fill gauges fill up to the value. Band gauges are for values that should stay in the middle and show a marker over the nominal zone.
// Ticks mark where the nominal range starts and ends. The color blends from good to bad as the value gets worse, and the value turns orange once it's out of range.
public class TelemetryGauge : MonoBehaviour
{
    public enum Kind { Fill, Band }
    public enum Shape { Ring, Bar }

    [Header("References")]
    [Tooltip("Optional small tag for which source is in use, e.g. UMB.")]
    [SerializeField] private TMP_Text sourceText;

    [Tooltip("Current value and unit. Rings put the unit on a line under the number.")]
    [SerializeField] private TMP_Text valueText;

    [Tooltip("Filled image, radial for rings and horizontal for bars (Fill gauges).")]
    [SerializeField] private Image fill;

    [Tooltip("Lighter zone showing the nominal range (Band gauges). On rings it is a filled radial arc.")]
    [SerializeField] private Image band;

    [Tooltip("Marker at the current value (Band gauges). On rings its pivot sits on the ring centre so turning it moves it round the ring.")]
    [SerializeField] private Image marker;

    [Tooltip("Ticks at the low and high ends of the nominal range. On rings they pivot on the ring centre. A tick at either end is hidden.")]
    [SerializeField] private RectTransform lowTick;
    [SerializeField] private RectTransform highTick;

    [Header("Range")]
    [SerializeField] private Shape shape = Shape.Ring;
    [SerializeField] private Kind kind = Kind.Fill;

    [Tooltip("Unit shown under the value, e.g. PSI.")]
    [SerializeField] private string unit = "%";

    [Tooltip("Number format for the value, e.g. 0 or 0.00.")]
    [SerializeField] private string format = "0";

    [Tooltip("Values at the start and end of the ring or bar.")]
    [SerializeField] private Vector2 scale = new(0f, 100f);

    [Tooltip("Nominal range from the TSS telemetry ranges. Outside it the gauge is fully the bad color.")]
    [SerializeField] private Vector2 nominal = new(20f, 100f);

    [Tooltip("Fill gauges: on for resources that get worse as they empty (O2, battery), off for ones that get worse as they fill (CO2, scrubber).")]
    [SerializeField] private bool lowIsBad = true;

    [Header("Colors")]
    [Tooltip("Color when the value is healthy.")]
    [SerializeField] private Color goodColor = new(0.35f, 0.8f, 1f);

    [Tooltip("Color it shifts toward as the value gets worse, and when it's out of range.")]
    [SerializeField] private Color badColor = new(1f, 0.55f, 0.1f);

    [Tooltip("Color for a gauge whose system is switched off, e.g. the fan not in use.")]
    [SerializeField] private Color idleColor = new(0.45f, 0.5f, 0.55f);

    [Tooltip("Opacity of the fill.")]
    [Range(0f, 1f)]
    [SerializeField] private float fillAlpha = 0.45f;

    private void Awake()
    {
        bool isBand = kind == Kind.Band;
        fill.gameObject.SetActive(!isBand);
        band.gameObject.SetActive(isBand);
        marker.gameObject.SetActive(isBand);
        if (isBand && shape == Shape.Ring)
        {
            band.fillAmount = Along(nominal.y) - Along(nominal.x);
            band.rectTransform.localEulerAngles = Around(Along(nominal.x));
        }
        else if (isBand) Span(band.rectTransform, Along(nominal.x), Along(nominal.y));
        PlaceTick(lowTick, Along(nominal.x));
        PlaceTick(highTick, Along(nominal.y));
    }

    // Fills the gauge or moves the marker and colours it. Source names the tank, battery or scrubber in use.
    // Idle gauges belong to a system that's switched off, so they go grey instead of warning.
    public void Show(float value, string source = "", bool idle = false)
    {
        float t = Along(value);
        if (kind == Kind.Fill) fill.fillAmount = t;
        else Place(marker.rectTransform, t);

        bool outOfRange = !idle && (value < nominal.x || value > nominal.y);
        var color = idle ? idleColor : ColorFor(value);
        marker.color = color;
        fill.color = new Color(color.r, color.g, color.b, fillAlpha);
        string number = value.ToString(format), units = $"<color=#9AA8B4>{unit}</color>";
        valueText.text = shape == Shape.Ring ? $"{number}<br><size=70%>{units}</size>" : $"{number} <size=80%>{units}</size>";
        valueText.color = idle ? idleColor : outOfRange ? badColor : Color.white;
        if (sourceText) sourceText.text = source;
    }

    // Blends from good to bad by how full the ring is, or for bands how far it is from the middle of nominal. Out of range is fully bad.
    private Color ColorFor(float value)
    {
        if (value < nominal.x || value > nominal.y) return badColor;
        float bad = kind == Kind.Band
            ? Mathf.Abs(value - (nominal.x + nominal.y) * 0.5f) / ((nominal.y - nominal.x) * 0.5f)
            : lowIsBad ? 1f - Along(value) : Along(value);
        return Color.Lerp(goodColor, badColor, bad);
    }

    private float Along(float value) => Mathf.InverseLerp(scale.x, scale.y, value);

    private void PlaceTick(RectTransform tick, float t)
    {
        tick.gameObject.SetActive(t > 0.01f && t < 0.99f);
        Place(tick, t);
    }

    // Puts a marker or tick t of the way along the bar, or round the ring.
    private void Place(RectTransform rect, float t)
    {
        if (shape == Shape.Ring) rect.localEulerAngles = Around(t);
        else Span(rect, t, t);
    }

    private static void Span(RectTransform rect, float from, float to)
    {
        rect.anchorMin = new Vector2(from, rect.anchorMin.y);
        rect.anchorMax = new Vector2(to, rect.anchorMax.y);
    }

    // Rotation that puts something at t of the way round the ring, clockwise from the top.
    private static Vector3 Around(float t) => new(0f, 0f, -t * 360f);
}
