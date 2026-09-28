using SuitsSim.Core;
using UnityEngine;

namespace SuitsSim.World
{
    /// <summary>
    /// Solves the TSS&lt;-&gt;Unity frame from two landmarks. Place two empty transforms
    /// on recognizable features of the imported terrain (a rock, a corner of the yard),
    /// enter their TSS coordinates from the annotated map, then run
    /// "Calibrate from landmarks" from the component's context menu.
    ///
    /// It writes origin and yaw into the SimConfig and reports the scale ratio. A ratio
    /// far from 1.0 means the mesh was imported at the wrong scale: fix the model's
    /// import scale rather than compensating here, or distances in the HITL logs will be wrong.
    /// </summary>
    [AddComponentMenu("SUITS Sim/Coordinate Calibrator")]
    public class CoordinateCalibrator : MonoBehaviour
    {
        [SerializeField] SimConfig config;
        [SerializeField] Transform landmarkA;
        [SerializeField] Vector2 landmarkATss;
        [SerializeField] Transform landmarkB;
        [SerializeField] Vector2 landmarkBTss;

        [ContextMenu("Calibrate from landmarks")]
        public void Calibrate()
        {
            if (config == null || landmarkA == null || landmarkB == null)
            {
                Debug.LogError("Assign the SimConfig and both landmarks first.", this);
                return;
            }

            var ua = new Vector2(landmarkA.position.x, landmarkA.position.z);
            var ub = new Vector2(landmarkB.position.x, landmarkB.position.z);
            var unityDelta = ub - ua;
            var tssDelta = landmarkBTss - landmarkATss;
            if (unityDelta.magnitude < 1f || tssDelta.magnitude < 1f)
            {
                Debug.LogError("Landmarks are too close together; pick points at least 20 m apart.", this);
                return;
            }

            // Bearings clockwise from +Z (Unity) and +Y (TSS map up).
            float unityBearing = Mathf.Atan2(unityDelta.x, unityDelta.y) * Mathf.Rad2Deg;
            float tssBearing = Mathf.Atan2(tssDelta.x, tssDelta.y) * Mathf.Rad2Deg;
            float yaw = WorldFrame.Normalize360(unityBearing - tssBearing);
            float scale = unityDelta.magnitude / tssDelta.magnitude;

            // Origin: the TSS point that maps to Unity (0,0), derived from landmark A.
            var toMap = Quaternion.Euler(0f, -yaw, 0f) * new Vector3(ua.x, 0f, ua.y);
            var origin = landmarkATss - new Vector2(toMap.x, toMap.z);

#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(config, "Calibrate TSS frame");
#endif
            config.worldYawOffsetDegrees = yaw;
            config.tssOrigin = origin;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(config);
#endif

            var level = Mathf.Abs(scale - 1f) > 0.05f ? LogType.Warning : LogType.Log;
            Debug.unityLogger.Log(level, "Calibrator",
                $"yaw = {yaw:F2} deg, origin = ({origin.x:F2}, {origin.y:F2}), scale ratio = {scale:F3}" +
                (level == LogType.Warning ? "  <- mesh scale is off; fix the model import scale." : ""));
        }
    }
}
