using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Panel with the procedure runner's current step. It shows the task name, the step with its action first, and a preview of the next step.
public class ProcedureDisplay : WristDisplayPanel
{
    [Header("References")]
    [Tooltip("Small top line: the current task's name, e.g. PREP O2 TANKS.")]
    [SerializeField] private TMP_Text title;

    [Tooltip("Small top-right step count, e.g. STEP 7/31.")]
    [SerializeField] private TMP_Text count;

    [Tooltip("What the current step tells the astronaut to do.")]
    [SerializeField] private TMP_Text step;

    [Tooltip("Dim bottom line previewing the next step.")]
    [SerializeField] private TMP_Text nextStep;

    [Tooltip("Outline around the panel, tinted by priority.")]
    [SerializeField] private Image border;

    [Tooltip("Bottom-right button that confirms the step. Only shown while the step is waiting for a confirm.")]
    [SerializeField] private XRSimpleInteractable confirmButton;

    [Tooltip("Room kept clear on the right of the next step line while Confirm is showing.")]
    [SerializeField] private float confirmMargin = 72f;

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
        confirmButton.selectEntered.AddListener(Confirm);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (ProcedureRunner.Instance) ProcedureRunner.Instance.Changed -= Refresh;
        confirmButton.selectEntered.RemoveListener(Confirm);
    }

    protected override void Refresh()
    {
        var runner = ProcedureRunner.Instance;
        var procedure = runner.Current;
        var current = runner.CurrentStep;
        var next = procedure && runner.Index + 1 < procedure.steps.Count ? procedure.steps[runner.Index + 1] : null;

        title.text = !procedure ? "NO PROCEDURE"
            : current != null ? current.header.ToUpper()
            : procedure.title.ToUpper();
        count.text = procedure ? $"STEP {runner.Index + 1}/{procedure.steps.Count}" : "";
        step.text = current != null ? current.text : "";
        nextStep.text = next != null ? $"NEXT  {next.text}" : "";
        border.color = runner.IsUrgent ? urgentColor : normalColor;

        bool canConfirm = current != null && current.CanConfirm;
        confirmButton.gameObject.SetActive(canConfirm);
        nextStep.margin = new Vector4(0f, 0f, canConfirm ? confirmMargin : 0f, 0f);
    }

    private void Confirm(SelectEnterEventArgs args) => ProcedureRunner.Instance.Confirm();
}
