using System;

namespace SuitsSim.Tss
{
    /// <summary>
    /// TSS wire format. Requests are big-endian:
    ///   GET  = [timestamp u32][command u32]               (8 bytes)
    ///   POST = [timestamp u32][command u32][value f32]    (12 bytes)
    /// A GET response is the raw JSON text. A POST response is a 4-byte status
    /// written in the server's native byte order (01 00 00 00 on x86 = true),
    /// so any non-zero byte counts as success.
    /// </summary>
    public static class TssPacket
    {
        public static uint Now() => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public static byte[] Get(uint command, uint timestamp)
        {
            var b = new byte[8];
            WriteU32BE(b, 0, timestamp);
            WriteU32BE(b, 4, command);
            return b;
        }

        public static byte[] Post(uint command, float value, uint timestamp)
        {
            var b = new byte[12];
            WriteU32BE(b, 0, timestamp);
            WriteU32BE(b, 4, command);
            WriteU32BE(b, 8, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
            return b;
        }

        public static byte[] Post(uint command, bool value, uint timestamp) =>
            Post(command, value ? 1f : 0f, timestamp);

        public static bool IsAckTrue(byte[] response)
        {
            if (response == null || response.Length < 4) return false;
            return (response[0] | response[1] | response[2] | response[3]) != 0;
        }

        public static void WriteU32BE(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        public static uint ReadU32BE(byte[] buffer, int offset) =>
            ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) |
            ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
    }
}
