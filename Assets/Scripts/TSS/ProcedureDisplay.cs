using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Panel with the procedure runner's current procedure and step.
public class ProcedureDisplay : WristDisplayPanel
{
    [Header("References")]
    [Tooltip("Procedure title and step count.")]
    [SerializeField] private TMP_Text title;

    [Tooltip("Section header of the current step.")]
    [SerializeField] private TMP_Text header;

    [Tooltip("What the current step tells the astronaut to do.")]
    [SerializeField] private TMP_Text step;

    [Tooltip("Bar on the card's edge, tinted by priority.")]
    [SerializeField] private Image accent;

    [Header("Colors")]
    [Tooltip("Title and accent color for normal procedures.")]
    [SerializeField] private Color normalColor = new(0.35f, 0.8f, 1f);

    [Tooltip("Title and accent color for urgent (error) procedures.")]
    [SerializeField] private Color urgentColor = new(1f, 0.6f, 0.15f);

    // Steps change between sim ticks, so also redraw when the runner moves on.
    protected override void OnEnable()
    {
        base.OnEnable();
        ProcedureRunner.Instance.Changed += Refresh;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (ProcedureRunner.Instance) ProcedureRunner.Instance.Changed -= Refresh;
    }

    protected override void Refresh()
    {
        var runner = ProcedureRunner.Instance;
        var procedure = runner.Current;
        var currentStep = runner.CurrentStep;

        title.text = procedure ? $"{procedure.title.ToUpper()}  {runner.Index + 1}/{procedure.steps.Count}" : "NO PROCEDURE";
        title.color = accent.color = runner.IsUrgent ? urgentColor : normalColor;
        header.text = currentStep?.header ?? "";
        step.text = currentStep?.text ?? "";
    }
}
