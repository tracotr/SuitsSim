using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One row on the EVA panel. It's a bar with the label and value on top, coloured by the value's nominal range from the TSS telemetry ranges.
// Fill gauges are for resources and fill left to right. Band gauges are for values that should stay in the middle and show a marker over the nominal zone.
// The color blends from good to bad as the value gets worse.
public class TelemetryGauge : MonoBehaviour
{
    public enum Kind { Fill, Band }

    [Header("References")]
    [Tooltip("Small tag after the label for which source is in use, e.g. PRI.")]
    [SerializeField] private TMP_Text sourceText;

    [Tooltip("Current value and unit.")]
    [SerializeField] private TMP_Text valueText;

    [Tooltip("Bar that fills left to right (Fill gauges).")]
    [SerializeField] private Image fill;

    [Tooltip("Lighter zone showing the nominal range (Band gauges).")]
    [SerializeField] private Image band;

    [Tooltip("Marker at the current value (Band gauges).")]
    [SerializeField] private Image marker;

    [Header("Range")]
    [SerializeField] private Kind kind = Kind.Fill;

    [Tooltip("Unit shown after the value.")]
    [SerializeField] private string unit = "%";

    [Tooltip("Number format for the value, e.g. 0 or 0.00.")]
    [SerializeField] private string format = "0";

    [Tooltip("Value at the left and right ends of the bar.")]
    [SerializeField] private Vector2 scale = new(0f, 100f);

    [Tooltip("Nominal range from the TSS telemetry ranges. Outside it the bar is fully the bad color.")]
    [SerializeField] private Vector2 nominal = new(20f, 100f);

    [Tooltip("Fill gauges: on for resources that get worse as they empty (O2, battery), off for ones that get worse as they fill (CO2, scrubber).")]
    [SerializeField] private bool lowIsBad = true;

    [Header("Colors")]
    [Tooltip("Color when the value is healthy.")]
    [SerializeField] private Color goodColor = new(0.35f, 0.8f, 1f);

    [Tooltip("Color it shifts toward as the value gets worse, and when it's out of range.")]
    [SerializeField] private Color badColor = new(1f, 0.55f, 0.1f);

    [Tooltip("Opacity of the fill so the label and value on top of it stay readable.")]
    [Range(0f, 1f)]
    [SerializeField] private float fillAlpha = 0.45f;

    private void Awake()
    {
        bool isBand = kind == Kind.Band;
        fill.gameObject.SetActive(!isBand);
        band.gameObject.SetActive(isBand);
        marker.gameObject.SetActive(isBand);
        if (isBand) SetSpan(band.rectTransform, Along(nominal.x), Along(nominal.y));
    }

    // Moves the bar or marker and colours the row. Source names the tank, battery or scrubber in use.
    public void Show(float value, string source = "")
    {
        float t = Along(value);
        if (kind == Kind.Fill) SetSpan(fill.rectTransform, 0f, t);
        else SetSpan(marker.rectTransform, t, t);

        var color = ColorFor(value);
        marker.color = color;
        fill.color = new Color(color.r, color.g, color.b, fillAlpha);
        valueText.text = $"{value.ToString(format)} {unit}";
        sourceText.text = source;
    }

    // Blends from good to bad by how full the bar is, or for bands how far it is from the middle of nominal. Out of range is fully bad.
    private Color ColorFor(float value)
    {
        if (value < nominal.x || value > nominal.y) return badColor;
        float bad = kind == Kind.Band
            ? Mathf.Abs(value - (nominal.x + nominal.y) * 0.5f) / ((nominal.y - nominal.x) * 0.5f)
            : lowIsBad ? 1f - Along(value) : Along(value);
        return Color.Lerp(goodColor, badColor, bad);
    }

    private float Along(float value) => Mathf.InverseLerp(scale.x, scale.y, value);

    private static void SetSpan(RectTransform rect, float from, float to)
    {
        rect.anchorMin = new Vector2(from, rect.anchorMin.y);
        rect.anchorMax = new Vector2(to, rect.anchorMax.y);
    }
}
