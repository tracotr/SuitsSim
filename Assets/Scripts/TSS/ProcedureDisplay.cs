using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD card with the procedure runner's current procedure and step.
public class ProcedureDisplay : MonoBehaviour
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

    // runtime values
    private ProcedureRunner _runner;

    private void OnEnable()
    {
        _runner = ProcedureRunner.Instance;
        _runner.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (_runner) _runner.Changed -= Refresh;
    }

    private void Refresh()
    {
        var procedure = _runner.Current;
        var currentStep = _runner.CurrentStep;

        title.text = procedure ? $"{procedure.title.ToUpper()}  {_runner.Index + 1}/{procedure.steps.Count}" : "NO PROCEDURE";
        title.color = accent.color = _runner.IsUrgent ? urgentColor : normalColor;
        header.text = currentStep?.header ?? "";
        step.text = currentStep?.text ?? "";
    }
}
