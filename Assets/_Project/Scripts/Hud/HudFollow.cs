using UnityEngine;

namespace SuitsSim.Hud
{
    /// <summary>
    /// Lazy-follow for a world-space HUD canvas: stays put while the head moves a little
    /// and eases back into view past a threshold, which is closer to how a HoloLens
    /// tag-along panel feels than a rigid head lock (and far less nauseating in VR).
    /// </summary>
    [AddComponentMenu("SUITS Sim/HUD Follow")]
    public class HudFollow : MonoBehaviour
    {
        [SerializeField] Transform head;
        [SerializeField] float distance = 1.2f;
        [SerializeField] float verticalOffset = -0.15f;
        [SerializeField] float horizontalOffset = 0f;
        [SerializeField] float angleThreshold = 20f;
        [SerializeField] float followSpeed = 4f;

        bool catchingUp;

        void Start()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (head != null) Snap();
        }

        void LateUpdate()
        {
            if (head == null) return;
            var target = TargetPosition();
            var toPanel = transform.position - head.position;
            var angle = Vector3.Angle(Flat(head.forward), Flat(toPanel));
            if (angle > angleThreshold || Vector3.Distance(transform.position, target) > distance * 0.5f) catchingUp = true;

            if (catchingUp)
            {
                transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
                if (Vector3.Distance(transform.position, target) < 0.02f) catchingUp = false;
            }
            transform.rotation = Quaternion.LookRotation(transform.position - head.position, Vector3.up);
        }

        [ContextMenu("Snap in front of head")]
        public void Snap()
        {
            transform.position = TargetPosition();
            transform.rotation = Quaternion.LookRotation(transform.position - head.position, Vector3.up);
        }

        Vector3 TargetPosition()
        {
            var fwd = Flat(head.forward).normalized;
            var right = Vector3.Cross(Vector3.up, fwd);
            return head.position + fwd * distance + Vector3.up * verticalOffset + right * horizontalOffset;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
