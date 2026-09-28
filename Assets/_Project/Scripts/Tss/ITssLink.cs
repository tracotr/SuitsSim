using System;
using Newtonsoft.Json.Linq;

namespace SuitsSim.Tss
{
    /// <summary>
    /// What the rest of the sim talks to. Two implementations: EmbeddedTss (the port running
    /// in the scene) and TssClient (UDP to a real TSS server). Components never care which.
    /// Every event fires on Unity's main thread.
    /// </summary>
    public interface ITssLink
    {
        /// <summary>(get command, json) once per TSS second for EVA (1) and LTV_ERRORS (3), plus ROVER/LTV when embedded.</summary>
        event Action<uint, JObject> SnapshotReceived;
        /// <summary>(command, value, reason) when TSS rejects a write or never acknowledges it.</summary>
        event Action<uint, float, string> PostFailed;

        JObject Eva { get; }
        JObject LtvErrors { get; }
        JObject Latest(uint getCommand);

        bool IsConnected { get; }
        float SecondsSinceAnySnapshot { get; }
        int PostsSent { get; }
        int PostsFailed { get; }
        string Description { get; }

        /// <summary>Same as a UDP POST from a peripheral. Bools go as 0/1.</summary>
        void Send(uint command, float value);
        void Send(uint command, bool value);

        /// <summary>Reconnect (network) or reboot to a fresh server state (embedded).</summary>
        void Restart();
    }
}
