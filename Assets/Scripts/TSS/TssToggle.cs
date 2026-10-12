using UnityEngine;
using UnityEngine.XR.Content.Interaction;
using UnityEngine.XR.Interaction.Toolkit;

// Connects an XRLever to its TSS switch, both ways, and gives it a click, hover tint and warning blink.
public class TssToggle : MonoBehaviour
{
    [Header("References")]
    [Tooltip("XRLever on the lever's Pivot.")]
    [SerializeField] private XRLever lever;

    [Tooltip("Renderer of the lever that gets tinted on hover and warnings.")]
    [SerializeField] private Renderer leverRenderer;

    [Tooltip("Plays when the switch flips.")]
    [SerializeField] private AudioSource click;

    [Header("Switch")]
    [Tooltip("The TSS switch this lever controls.")]
    [SerializeField] private TssSwitch sw = TssSwitch.UiaEva1Power;

    [Header("Feedback")]
    [Tooltip("Lever tint while a hand or controller is on it.")]
    [SerializeField] private Color hoverColor = new(0.3f, 0.8f, 1f);

    [Tooltip("Blinks when flipped out of procedure order.")]
    [SerializeField] private Color warningColor = new(1f, 0.15f, 0.1f);

    [Tooltip("Seconds of blinking (3 blinks).")]
    [SerializeField] private float warningTime = 0.9f;

    // runtime values
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private bool _settingLever;
    private float _warningStart = -1f;
    private MaterialPropertyBlock _hoverBlock, _warningBlock, _shownBlock;

    private void Awake()
    {
        _hoverBlock = new MaterialPropertyBlock();
        _hoverBlock.SetColor(BaseColor, hoverColor);
        _warningBlock = new MaterialPropertyBlock();
        _warningBlock.SetColor(BaseColor, warningColor);
    }

    private void OnEnable()
    {
        SetLever(TssSim.Instance.GetSwitch(sw));

        lever.onLeverActivate.AddListener(OnLeverOn);
        lever.onLeverDeactivate.AddListener(OnLeverOff);
        lever.hoverEntered.AddListener(OnHoverChanged);
        lever.hoverExited.AddListener(OnHoverChanged);
        TssSim.Instance.SwitchChanged += OnSwitchChanged;
        if (ProcedureRunner.Instance) ProcedureRunner.Instance.UnexpectedSwitch += OnUnexpectedSwitch;
    }

    private void OnDisable()
    {
        lever.onLeverActivate.RemoveListener(OnLeverOn);
        lever.onLeverDeactivate.RemoveListener(OnLeverOff);
        lever.hoverEntered.RemoveListener(OnHoverChanged);
        lever.hoverExited.RemoveListener(OnHoverChanged);
        if (TssSim.Instance) TssSim.Instance.SwitchChanged -= OnSwitchChanged;
        if (ProcedureRunner.Instance) ProcedureRunner.Instance.UnexpectedSwitch -= OnUnexpectedSwitch;
    }

    private void Update()
    {
        if (_warningStart >= 0f) RefreshTint();
    }

    private void OnLeverOn() => SendToTss(true);

    private void OnLeverOff() => SendToTss(false);

    private void SendToTss(bool on)
    {
        if (_settingLever) return;
        if (click) click.PlayOneShot(click.clip);
        TssSim.Instance.SetSwitch(sw, on);
    }

    // The lever fires its events when set from code too, so those are ignored here.
    private void SetLever(bool on)
    {
        _settingLever = true;
        lever.value = on;
        _settingLever = false;
    }

    private void OnSwitchChanged(TssSwitch s, bool value)
    {
        if (s == sw && value != lever.value) SetLever(value);
    }

    private void OnHoverChanged(BaseInteractionEventArgs args) => RefreshTint();

    private void OnUnexpectedSwitch(TssSwitch s)
    {
        if (s != sw) return;
        _warningStart = Time.time;
        RefreshTint();
    }

    // The warning blink wins over the hover tint.
    private void RefreshTint()
    {
        float t = Time.time - _warningStart;
        if (_warningStart >= 0f && t >= warningTime) _warningStart = -1f;
        bool blinkOn = _warningStart >= 0f && (int)(t / (warningTime / 6f)) % 2 == 0;
        var block = blinkOn ? _warningBlock : lever.isHovered ? _hoverBlock : null;
        if (block == _shownBlock) return;
        _shownBlock = block;
        leverRenderer.SetPropertyBlock(block);
    }
}
