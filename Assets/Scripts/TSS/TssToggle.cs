using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Poke toggle on a sprung hinge. While the fingertip pushes the lever, the hinge spring
// targets the finger; on release it targets whichever end the lever is closest to.
[RequireComponent(typeof(HingeJoint), typeof(XRSimpleInteractable))]
public class TssToggle : MonoBehaviour
{
    public int command = 2001;
    public string jsonPath = "uia.eva1_power";
    public Vector3 leverDirection = Vector3.left; // which way the lever points, in Pivot's local space
    public float throwAngle = 30f;                // must match the hinge limits
    public float deadband = 3f;                   // degrees past center before it counts as flipped
    public float grace = 0.15f;                   // seconds to keep contact if hover flickers
    public AudioSource click;

    HingeJoint hinge;
    XRSimpleInteractable interactable;
    Quaternion restRot;
    Vector3 restPos;
    Transform finger;
    float prevF, target, lostAt = -1f;
    bool on;

    void Awake()
    {
        hinge = GetComponent<HingeJoint>();
        interactable = GetComponent<XRSimpleInteractable>();
        restRot = transform.localRotation;
        restPos = transform.localPosition;
        target = -throwAngle;

        interactable.hoverEntered.AddListener(a =>
        {
            if (a.interactorObject is not XRPokeInteractor) return;
            finger = a.interactorObject.GetAttachTransform(interactable);
            prevF = FingerAngle();
            lostAt = -1f;
        });
        interactable.hoverExited.AddListener(a =>
        {
            if (a.interactorObject is XRPokeInteractor) lostAt = Time.time;
        });
    }

    void Start() => SimTss.Instance.Ticked += ReadBack;
    void OnDestroy() { if (SimTss.Instance) SimTss.Instance.Ticked -= ReadBack; }

    // Fingertip angle around the hinge, measured the same way as hinge.angle.
    float FingerAngle()
    {
        var p = transform.parent.InverseTransformPoint(finger.position) - restPos;
        var axis = restRot * hinge.axis;
        var rest = restRot * leverDirection;
        return Vector3.SignedAngle(rest, Vector3.ProjectOnPlane(p, axis), axis);
    }

    void Update()
    {
        if (finger && lostAt >= 0f && Time.time - lostAt > grace) finger = null;

        if (finger)
        {
            // Push only: the lever moves when the finger crosses it, never pulled back.
            float f = FingerAngle();
            if ((prevF >= target && f < target) || (prevF <= target && f > target))
                target = Mathf.Clamp(f, -throwAngle, throwAngle);
            prevF = f;

            if (!on && target > deadband) Flip(true);
            else if (on && target < -deadband) Flip(false);
        }
        else
        {
            target = on ? throwAngle : -throwAngle; // spring snaps it home
        }

        var s = hinge.spring;
        s.targetPosition = target;
        hinge.spring = s;
    }

    void Flip(bool value)
    {
        on = value;
        if (click) click.PlayOneShot(click.clip);
        SimTss.Instance.Post(command, on ? 1 : 0);
    }

    // Board shows TSS truth: if something else changed the switch, the spring swings the lever.
    void ReadBack()
    {
        if (finger) return;
        on = JObject.Parse(SimTss.Instance.Get(1)).SelectToken(jsonPath).Value<bool>();
    }
}