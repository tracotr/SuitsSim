using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

// The wrist-mounted display: every panel fanned over the back of the off hand's wrist, shown while it faces the head like checking a watch.
// Grabbing a wrist panel pulls out a world copy (see SpatialPanel); the wrist keeps its own.
public class WristDisplay : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Main Camera under the XR Origin.")]
    [SerializeField] private Transform head;

    [Tooltip("Left hand visual's palm bone.")]
    [SerializeField] private Transform leftPalm;

    [Tooltip("Right hand visual's palm bone.")]
    [SerializeField] private Transform rightPalm;

    [Tooltip("Panel prefabs shown on the wrist, front to back.")]
    [SerializeField] private SpatialPanel[] panels;

    [Header("Placement")]
    [Tooltip("How directly the back of the wrist must face the head to show the display, 1 = straight at it.")]
    [SerializeField] private float wristFacing = 0.6f;

    [Tooltip("Where the display sits from the palm bone, in metres (y = off the back of the hand, z = toward the fingers).")]
    [SerializeField] private Vector3 wristOffset = new(0f, 0.03f, -0.07f);

    [Tooltip("Size of the wrist panels relative to the world ones.")]
    [SerializeField] private float dockedScale = 0.35f;

    [Tooltip("Gap between fanned panels, in metres.")]
    [SerializeField] private float fanSpacing = 0.015f;

    [Tooltip("Turn between fanned panels, in degrees.")]
    [SerializeField] private float fanAngle = 6f;

    // runtime values
    private readonly List<SpatialPanel> _docked = new();
    private Transform _palm;
    private bool _shown = true;

    // Palm bones point +Z to the fingers and +Y out the back of the hand; the thumb is -X on the right hand, +X on the left.
    private void Start()
    {
        bool left = UserHandedness.Instance.OffHand == Handedness.Left;
        _palm = left ? leftPalm : rightPalm;
        transform.SetParent(_palm, false);
        transform.SetLocalPositionAndRotation(wristOffset, Quaternion.LookRotation(Vector3.down, left ? Vector3.right : Vector3.left));

        for (int i = 0; i < panels.Length; i++)
        {
            var panel = Instantiate(panels[i], transform);
            panel.transform.SetLocalPositionAndRotation(new Vector3(0f, i * fanSpacing, i * 0.002f), Quaternion.Euler(0f, 0f, -i * fanAngle));
            panel.transform.localScale = panels[i].transform.localScale * dockedScale;
            panel.Dock(panels[i]);
            _docked.Add(panel);
        }
    }

    private void LateUpdate()
    {
        bool shown = Vector3.Dot(_palm.up, (head.position - _palm.position).normalized) > wristFacing;
        if (shown == _shown) return;
        _shown = shown;
        foreach (var panel in _docked) panel.gameObject.SetActive(shown);
    }
}
