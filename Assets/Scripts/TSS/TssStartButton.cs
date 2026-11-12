using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Button on the UIA that starts the EVA telemetry. It shows whether the sim is running.
public class TssStartButton : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Pinch or grab it to press.")]
    [SerializeField] private XRSimpleInteractable button;

    [Tooltip("Renderer tinted by the sim state.")]
    [SerializeField] private Renderer buttonRenderer;

    [Tooltip("Label on the button.")]
    [SerializeField] private TMP_Text label;

    [Header("Colors")]
    [SerializeField] private Color stoppedColor = new(0.15f, 0.45f, 0.15f);
    [SerializeField] private Color runningColor = new(0.3f, 1f, 0.3f);

    // runtime values
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock _block;

    private void Awake()
    {
        _block = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        button.selectEntered.AddListener(Press);
        TssSim.Instance.RunningChanged += Show;
        Show(TssSim.Instance.Running);
    }

    // TssSim may already be gone when play mode stops.
    private void OnDisable()
    {
        button.selectEntered.RemoveListener(Press);
        if (TssSim.Instance) TssSim.Instance.RunningChanged -= Show;
    }

    private void Press(SelectEnterEventArgs args) => TssSim.Instance.StartEva();

    private void Show(bool running)
    {
        _block.SetColor(BaseColor, running ? runningColor : stoppedColor);
        buttonRenderer.SetPropertyBlock(_block);
        label.text = running ? "EVA RUNNING" : "START EVA";
    }
}
