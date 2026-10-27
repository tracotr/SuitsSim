using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Panel with the procedure runner's current step: a small context line, the step itself with its action first, and a preview of the next step.
public class ProcedureDisplay : WristDisplayPanel
{
    [Header("References")]
    [Tooltip("Small top line: procedure title and the step's section.")]
    [SerializeField] private TMP_Text title;

    [Tooltip("Small top-right step count, e.g. STEP 7/31.")]
    [SerializeField] private TMP_Text count;

    [Tooltip("What the current step tells the astronaut to do.")]
    [SerializeField] private TMP_Text step;

    [Tooltip("Dim bottom line previewing the next step.")]
    [SerializeField] private TMP_Text nextStep;

    [Tooltip("Outline around the panel, tinted by priority.")]
    [SerializeField] private Image border;

    [Header("Colors")]
    [Tooltip("Border color for normal procedures.")]
    [SerializeField] private Color normalColor = new(0.35f, 0.8f, 1f);

    [Tooltip("Border color for urgent (error) procedures.")]
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
        var current = runner.CurrentStep;
        var next = procedure && runner.Index + 1 < procedure.steps.Count ? procedure.steps[runner.Index + 1] : null;

        title.text = !procedure ? "NO PROCEDURE"
            : current != null ? $"{procedure.title.ToUpper()} · {current.header.ToUpper()}"
            : procedure.title.ToUpper();
        count.text = procedure ? $"STEP {runner.Index + 1}/{procedure.steps.Count}" : "";
        step.text = current != null ? LeadWithAction(current.text) : "";
        nextStep.text = next != null ? $"NEXT  {next.text}" : "";
        border.color = runner.IsUrgent ? urgentColor : normalColor;
    }

    // Bold caps on the first word, the action, so it reads first.
    private static string LeadWithAction(string text)
    {
        int space = text.IndexOf(' ');
        return space < 0 ? $"<b>{text.ToUpper()}</b>" : $"<b>{text[..space].ToUpper()}</b>{text[space..]}";
    }
}
