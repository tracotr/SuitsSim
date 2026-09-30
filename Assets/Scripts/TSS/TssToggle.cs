using UnityEngine;
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
    public AudioSource click;

    [SerializeField] private HingeJoint hinge;
    [SerializeField] private XRSimpleInteractable interactable;
    private Quaternion _restRot;
    private Vector3 _restPos;
    private Transform _finger;
    private float _prevF, _target, _lostAt = -1f;
    private bool _on;

    private void Awake()
    {
        _restRot = transform.localRotation;
        _restPos = transform.localPosition;
        _target = -throwAngle;

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
    }

    private void Start()
    {
        _on = TssSim.Instance.GetSwitch(sw);
        TssSim.Instance.SwitchChanged += ReadBack;
    }
    
    private void OnDestroy() { if (TssSim.Instance) TssSim.Instance.SwitchChanged -= ReadBack; }

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

        if (_finger)
        {
            // Push only: the lever moves when the finger crosses it, never pulled back.
            float f = FingerAngle();
            if ((_prevF >= _target && f < _target) || (_prevF <= _target && f > _target))
                _target = Mathf.Clamp(f, -throwAngle, throwAngle);
            _prevF = f;

            if (!_on && _target > deadband) Flip(true);
            else if (_on && _target < -deadband) Flip(false);
        }
        else
        {
            _target = _on ? throwAngle : -throwAngle; 
        }

        var s = hinge.spring;
        s.targetPosition = _target;
        hinge.spring = s;
    }

    private void Flip(bool value)
    {
        _on = value;
        if (click) click.PlayOneShot(click.clip);
        TssSim.Instance.SetSwitch(sw, _on);
    }

    // Board shows TSS truth
    private void ReadBack(TssSwitch s, bool value)
    {
        if (s == sw && !_finger) _on = value;
    }
}