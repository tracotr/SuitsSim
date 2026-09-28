using SuitsSim.Core;
using UnityEngine;

namespace SuitsSim.World
{
    /// <summary>
    /// First-pass world: a flat quad textured with the rock yard map, sized and placed
    /// so its corners sit on the given TSS bounds. Gives a walkable, correctly scaled
    /// yard before the 3D scan is aligned. Put it on a Quad (rotated flat by this script).
    ///
    /// Defaults are the rock yard range from the TSS2026 README. The raw TIFF's aspect
    /// (3507x2220) is slightly off 220x135 m, so it likely has a margin: tune the bounds
    /// until the HAB and task board markers land on their map symbols.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("SUITS Sim/Map Ground Plane")]
    public class MapGroundPlane : MonoBehaviour
    {
        [SerializeField] SimConfig config;
        [SerializeField] Vector2 tssMin = new Vector2(-5765f, -10075f);
        [SerializeField] Vector2 tssMax = new Vector2(-5545f, -9940f);

        void OnValidate() => Apply();
        void OnEnable() => Apply();

        [ContextMenu("Apply bounds")]
        void Apply()
        {
            if (config == null) return;
            var frame = config.Frame;
            var center = (tssMin + tssMax) * 0.5f;
            var size = tssMax - tssMin;
            transform.position = frame.ToUnity(center);
            // Unity's Quad faces -Z; lay it flat facing up, then apply the map yaw.
            transform.rotation = Quaternion.Euler(0f, config.worldYawOffsetDegrees, 0f) * Quaternion.Euler(90f, 0f, 0f);
            transform.localScale = new Vector3(size.x, size.y, 1f);
        }
    }
}
