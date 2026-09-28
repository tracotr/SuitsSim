using Newtonsoft.Json.Linq;

namespace SuitsSim.Tss
{
    /// <summary>
    /// Reads TSS JSON by dotted path, the same convention TSS itself uses
    /// ("telemetry.eva1.oxy_pri_pressure"). Working with paths instead of typed
    /// DTOs means a renamed field in TSS2027 is a string change, not a recompile
    /// of model classes.
    /// </summary>
    public static class TssJson
    {
        public static bool TryFloat(JObject root, string path, out float value)
        {
            value = 0f;
            var token = root?.SelectToken(path);
            if (token == null) return false;
            switch (token.Type)
            {
                case JTokenType.Float:
                case JTokenType.Integer:
                    value = token.Value<float>();
                    return true;
                case JTokenType.Boolean:
                    value = token.Value<bool>() ? 1f : 0f;
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryBool(JObject root, string path, out bool value)
        {
            value = false;
            var token = root?.SelectToken(path);
            if (token == null || token.Type != JTokenType.Boolean) return false;
            value = token.Value<bool>();
            return true;
        }

        public static string Text(JObject root, string path, string fallback = "--") =>
            root?.SelectToken(path)?.ToString() ?? fallback;

        /// <summary>
        /// Responses carry no header, so identify them by shape. Each GET uses its own
        /// socket, so this is only a sanity check against a stale or misrouted reply.
        /// </summary>
        public static bool LooksLike(uint getCommand, JObject root)
        {
            if (root == null) return false;
            switch (getCommand)
            {
                case TssCommand.GetRover: return root["pr_telemetry"] != null;
                case TssCommand.GetEva: return root["telemetry"] != null;
                case TssCommand.GetLtv: return root["signal"] != null;
                case TssCommand.GetLtvErrors: return root["error_procedures"] != null;
                default: return true;
            }
        }
    }
}
