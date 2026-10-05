using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(HingeJoint), typeof(XRSimpleInteractable))]
public class TssToggle : MonoBehaviour
{
    public TssSwitch sw = TssSwitch.UiaEva1Power;
    public Vector3 leverDirection = Vector3.left; // which way the lever points, in Pivot's local space
    public float throwAngle = 30f;                // must match the hinge limits
    public float deadband = 3f;                   // degrees past center before it counts as flipped
    public float grace = 0.15f;                   // seconds to keep contact if hover flickers
    [Range(0.1f, 1f)] public float give = 0.5f;   // how far the lever follows the finger before the detent (1 = no resistance)
    public AudioSource click;
    public Color hoverColor = new(0.3f, 0.8f, 1f);  // lever tint while a finger / ray is on it
    public Color warningColor = new(1f, 0.15f, 0.1f); // blinks when flipped out of procedure order
    public float warningTime = 0.9f;                // seconds of blinking (3 blinks)

    [SerializeField] private HingeJoint hinge;
    [SerializeField] private XRSimpleInteractable interactable;
    private Quaternion _restRot;
    private Vector3 _restPos;
    private Transform _finger;
    private float _prevF, _target, _lostAt = -1f;
    private bool _on, _pushing;
    private Renderer[] _renderers;
    private MaterialPropertyBlock _hoverBlock, _warningBlock, _shown;
    private float _warnStart = -1f;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        _restRot = transform.localRotation;
        _restPos = transform.localPosition;
        _target = -throwAngle;
        _renderers = GetComponentsInChildren<Renderer>(); // the lever
        _hoverBlock = new MaterialPropertyBlock();
        _hoverBlock.SetColor(BaseColor, hoverColor);
        _warningBlock = new MaterialPropertyBlock();
        _warningBlock.SetColor(BaseColor, warningColor);

        interactable.hoverEntered.AddListener(_ => RefreshTint());
        interactable.hoverExited.AddListener(_ => RefreshTint()); // stays lit while anything else hovers
        interactable.hoverEntered.AddListener(a =>
        {
            if (a.interactorObject is not XRPokeInteractor) return;
            _finger = a.interactorObject.GetAttachTransform(interactable);
            _prevF = FingerAngle();
            _lostAt = -1f;
        });
        interactable.hoverExited.AddListener(a =>
        {
            if (a.interactorObject is XRPokeInteractor) _lostAt = Time.time;
        });
        // Controllers (ray / near-far, incl. the XR Interaction Simulator): select flips it.
        // Not with tracked hands: their pinch is a select too and would flip switches from across the room.
        interactable.selectEntered.AddListener(a =>
        {
            if (a.interactorObject is XRPokeInteractor ||
                XRInputModalityManager.currentInputMode.Value != XRInputModalityManager.InputMode.MotionController) return;
            Flip(!_on);
        });
    }

    private void Start()
    {
        _on = TssSim.Instance.GetSwitch(sw);
        TssSim.Instance.SwitchChanged += ReadBack;
        if (ProcedureRunner.Instance) ProcedureRunner.Instance.UnexpectedSwitch += OnUnexpected;
    }
    
    private void OnDestroy()
    {
        if (TssSim.Instance) TssSim.Instance.SwitchChanged -= ReadBack;
        if (ProcedureRunner.Instance) ProcedureRunner.Instance.UnexpectedSwitch -= OnUnexpected;
    }

    // Fingertip angle around the hinge, measured the same way as hinge.angle.
    private float FingerAngle()
    {
        var p = transform.parent.InverseTransformPoint(_finger.position) - _restPos;
        var axis = _restRot * hinge.axis;
        var rest = _restRot * leverDirection;
        return Vector3.SignedAngle(rest, Vector3.ProjectOnPlane(p, axis), axis);
    }

    private void Update()
    {
        if (_finger && _lostAt >= 0f && Time.time - _lostAt > grace) _finger = null;

        float rest = _on ? throwAngle : -throwAngle;
        if (!_finger) _pushing = false;
        else
        {
            // Push only: contact starts when the finger crosses the lever, never pulled back.
            float f = FingerAngle();
            if (!_pushing && ((_prevF >= _target && f < _target) || (_prevF <= _target && f > _target))) _pushing = true;
            _prevF = f;

            if (_pushing)
            {
                float push = Mathf.Clamp(f, -throwAngle, throwAngle);
                if (_on ? push >= rest : push <= rest) _pushing = false;   // finger backed off
                else if (_on ? push < -deadband : push > deadband)
                {
                    // Detent: past center it lets go and snaps all the way over.
                    Flip(!_on);
                    _pushing = false;
                    rest = _on ? throwAngle : -throwAngle;
                }
                else
                {
                    _target = Mathf.Lerp(rest, push, give); // stiff: lever only follows part of the push
                }
            }
        }
        if (!_pushing) _target = rest;

        var s = hinge.spring;
        s.targetPosition = _target;
        hinge.spring = s;

        if (_warnStart >= 0f) RefreshTint();
    }

    private void Flip(bool value)
    {
        _on = value;
        if (click) click.PlayOneShot(click.clip);
        TssSim.Instance.SetSwitch(sw, _on);
    }

    private void OnUnexpected(TssSwitch s)
    {
        if (s != sw) return;
        _warnStart = Time.time;
        RefreshTint();
    }

    // Warning blink beats hover tint; only touches the renderers when the tint actually changes.
    private void RefreshTint()
    {
        float t = Time.time - _warnStart;
        if (_warnStart >= 0f && t >= warningTime) _warnStart = -1f;
        bool blinkOn = _warnStart >= 0f && (int)(t / (warningTime / 6f)) % 2 == 0;
        var block = blinkOn ? _warningBlock : interactable.isHovered ? _hoverBlock : null;
        if (block == _shown) return;
        _shown = block;
        foreach (var r in _renderers) r.SetPropertyBlock(block);
    }

    // Board shows TSS truth
    private void ReadBack(TssSwitch s, bool value)
    {
        if (s == sw && !_finger) _on = value;
    }
}