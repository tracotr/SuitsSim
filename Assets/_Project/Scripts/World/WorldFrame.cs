using UnityEngine;

namespace SuitsSim.Core
{
    /// <summary>
    /// Conversion between TSS map coordinates (meters; +X right/east, +Y up/north on
    /// the rock yard map) and Unity world space (+X right, +Z forward, +Y up).
    /// Pure math, no scene access, so it is unit tested in EditMode.
    /// </summary>
    public readonly struct WorldFrame
    {
        public readonly Vector2 TssOrigin;
        public readonly float YawOffsetDegrees;
        public readonly float HeadingOffsetDegrees;
        public readonly bool HeadingClockwise;

        public WorldFrame(Vector2 tssOrigin, float yawOffsetDegrees, float headingOffsetDegrees = 0f, bool headingClockwise = true)
        {
            TssOrigin = tssOrigin;
            YawOffsetDegrees = yawOffsetDegrees;
            HeadingOffsetDegrees = headingOffsetDegrees;
            HeadingClockwise = headingClockwise;
        }

        public Vector3 ToUnity(Vector2 tss, float height = 0f)
        {
            var d = tss - TssOrigin;
            var v = Quaternion.Euler(0f, YawOffsetDegrees, 0f) * new Vector3(d.x, 0f, d.y);
            v.y = height;
            return v;
        }

        public Vector2 ToTss(Vector3 unity)
        {
            var v = Quaternion.Euler(0f, -YawOffsetDegrees, 0f) * new Vector3(unity.x, 0f, unity.z);
            return TssOrigin + new Vector2(v.x, v.z);
        }

        /// <summary>Map heading in degrees [0, 360) for a Unity-space forward vector. 0 = map up.</summary>
        public float HeadingFromForward(Vector3 forward)
        {
            var m = Quaternion.Euler(0f, -YawOffsetDegrees, 0f) * forward;
            var bearing = Mathf.Atan2(m.x, m.z) * Mathf.Rad2Deg;
            if (!HeadingClockwise) bearing = -bearing;
            return Normalize360(bearing + HeadingOffsetDegrees);
        }

        public static float Normalize360(float degrees)
        {
            var d = degrees % 360f;
            return d < 0f ? d + 360f : d;
        }

        /// <summary>Signed smallest difference a - b in degrees, in (-180, 180].</summary>
        public static float DeltaDegrees(float a, float b)
        {
            var d = Normalize360(a - b);
            return d > 180f ? d - 360f : d;
        }
    }
}
