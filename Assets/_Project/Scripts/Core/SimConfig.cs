using UnityEngine;

namespace SuitsSim.Core
{
    public enum TaskBoardSite { Alpha, Bravo }

    public enum TssMode
    {
        /// <summary>The EmbeddedTss component in the scene. No server, works in standalone builds.</summary>
        Embedded,
        /// <summary>A real TSS server over UDP (the one NASA runs at test week, or yours in WSL).</summary>
        Network,
    }

    /// <summary>
    /// Every tunable for a test session in one asset. Create one per setup
    /// (e.g. "Laptop TSS", "Lab TSS", "JSC mock") and swap it on SimRoot.
    /// </summary>
    [CreateAssetMenu(menuName = "SUITS Sim/Sim Config", fileName = "SimConfig")]
    public class SimConfig : ScriptableObject
    {
        [Header("TSS")]
        [Tooltip("Embedded = the EmbeddedTss object in the scene. Network = a real TSS server at the host below.")]
        public TssMode tssMode = TssMode.Embedded;
        [Tooltip("IP printed by ./server.exe, e.g. 172.20.182.43. Test week uses a different address, so never hard-code it.")]
        public string tssHost = "127.0.0.1";
        public int tssPort = 14141;
        [Tooltip("TSS recalculates telemetry once per second; polling faster gains nothing.")]
        [Min(0.2f)] public float pollIntervalSeconds = 1f;
        [Min(0.1f)] public float requestTimeoutSeconds = 1.5f;

        [Header("Astronaut")]
        [Tooltip("Which suit the VR user plays. The FY26 EV procedures use EV1.")]
        [Range(1, 2)] public int evaNumber = 1;
        [Min(0.2f)] public float positionSendHz = 2f;
        public float positionDeadbandMeters = 0.05f;
        public float headingDeadbandDegrees = 1f;

        [Header("TSS <-> Unity frame (run CoordinateCalibrator to fill these)")]
        [Tooltip("TSS (x, y) that sits at Unity (0, 0). Default is the FY26 HAB egress point.")]
        public Vector2 tssOrigin = new Vector2(-5670f, -10060f);
        [Tooltip("Rotation of TSS +Y (map up) relative to Unity +Z, degrees clockwise seen from above.")]
        public float worldYawOffsetDegrees = 0f;
        [Tooltip("Added to the computed heading so it matches what TSS/IMU reports. Verify with tss_probe.py watch-imu.")]
        public float headingOffsetDegrees = 0f;
        public bool headingClockwise = true;

        [Header("Scenario")]
        public TaskBoardSite taskBoard = TaskBoardSite.Alpha;
        [Tooltip("Coordinates in the FY26 doc are accurate to ~10 m; keep this generous until FY27 numbers are out.")]
        public float arrivalRadiusMeters = 5f;
        [Tooltip("Advance phases automatically when their completion condition is met. Off = conductor advances manually.")]
        public bool autoAdvancePhases = true;

        [Header("HITL logging")]
        public string participantId = "P00";
        public string sessionNote = "";

        public WorldFrame Frame => new WorldFrame(tssOrigin, worldYawOffsetDegrees, headingOffsetDegrees, headingClockwise);
    }
}
