using SuitsSim.Core;
using SuitsSim.Tss;
using UnityEngine;

namespace SuitsSim.World
{
    /// <summary>
    /// Stands in for the suit IMU: turns the VR user's head pose into TSS
    /// eva{n}.posx / posy / heading, so TSS (and its web UI) tracks the VR tester the
    /// way it tracks a real suit at test week. The HUD reads position from here directly.
    /// Put it on the XR Origin and point <see cref="head"/> at the Main Camera.
    /// </summary>
    [AddComponentMenu("SUITS Sim/Astronaut Reporter")]
    public class AstronautReporter : MonoBehaviour
    {
        [SerializeField] Transform head;
        [Tooltip("Log position to the HITL CSV at this rate (for path replay and distance walked).")]
        [SerializeField] float logHz = 1f;

        public static AstronautReporter Instance { get; private set; }

        public Vector2 TssPosition { get; private set; }
        public float TssHeading { get; private set; }
        /// <summary>Clockwise bearing from map up, without the TSS heading offset/sign. For local nav math.</summary>
        public float MapBearing { get; private set; }
        public float MetersWalked { get; private set; }

        Vector2 lastSentPos = new Vector2(float.NaN, float.NaN);
        float lastSentHeading = float.NaN;
        float nextSendTime, nextLogTime;
        Vector2 lastWalkSample;
        bool hasWalkSample;

        void Awake()
        {
            Instance = this;
            if (head == null && Camera.main != null) head = Camera.main.transform;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            var root = SimRoot.Instance;
            if (root == null || head == null) return;

            var frame = root.Config.Frame;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(head.up, Vector3.up); // looking straight down

            TssPosition = frame.ToTss(head.position);
            TssHeading = frame.HeadingFromForward(forward);
            MapBearing = new WorldFrame(frame.TssOrigin, frame.YawOffsetDegrees).HeadingFromForward(forward);
            TrackDistance();

            if (Time.unscaledTime >= nextSendTime)
            {
                nextSendTime = Time.unscaledTime + 1f / root.Config.positionSendHz;
                SendIfChanged(root);
            }

            if (logHz > 0f && Time.unscaledTime >= nextLogTime)
            {
                nextLogTime = Time.unscaledTime + 1f / logHz;
                root.Log.Event("pose", "heading", TssHeading.ToString("F1"));
            }
        }

        void SendIfChanged(SimRoot root)
        {
            var cfg = root.Config;
            bool moved = float.IsNaN(lastSentPos.x) || Vector2.Distance(TssPosition, lastSentPos) >= cfg.positionDeadbandMeters;
            bool turned = float.IsNaN(lastSentHeading) || Mathf.Abs(WorldFrame.DeltaDegrees(TssHeading, lastSentHeading)) >= cfg.headingDeadbandDegrees;
            if (!moved && !turned) return;

            int eva = cfg.evaNumber;
            if (moved)
            {
                root.Tss.Send(TssCommand.ImuPosX(eva), TssPosition.x);
                root.Tss.Send(TssCommand.ImuPosY(eva), TssPosition.y);
                lastSentPos = TssPosition;
            }
            if (turned)
            {
                root.Tss.Send(TssCommand.ImuHeading(eva), TssHeading);
                lastSentHeading = TssHeading;
            }
        }

        void TrackDistance()
        {
            if (!hasWalkSample) { lastWalkSample = TssPosition; hasWalkSample = true; return; }
            var step = Vector2.Distance(TssPosition, lastWalkSample);
            if (step < 0.25f) return; // ignore head bob and standing sway
            MetersWalked += step;
            lastWalkSample = TssPosition;
        }

        public void ResetWalked()
        {
            MetersWalked = 0f;
            hasWalkSample = false;
        }

        public float DistanceTo(Vector2 tss) => Vector2.Distance(TssPosition, tss);
    }
}
