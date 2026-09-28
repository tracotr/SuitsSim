using NUnit.Framework;
using SuitsSim.Tss;

namespace SuitsSim.Tests
{
    public class TssPacketTests
    {
        static string Hex(byte[] b) => System.BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();

        [Test]
        public void Get_MatchesReadmeExample()
        {
            // TSS2026 README: timestamp 1763414183, command 0 -> 691b90a700000000
            Assert.AreEqual("691b90a700000000", Hex(TssPacket.Get(TssCommand.GetRover, 1763414183)));
        }

        [Test]
        public void PostThrottle_MatchesReadmeExample()
        {
            // TSS2026 README: timestamp 1763412577, command 1109, value 50.0 -> 691b8a610000045542480000
            Assert.AreEqual("691b8a610000045542480000", Hex(TssPacket.Post(1109, 50f, 1763412577)));
        }

        [Test]
        public void PostBool_SendsFloatOneOrZero()
        {
            Assert.AreEqual("3f800000", Hex(TssPacket.Post(TssCommand.UiaDepress, true, 0)).Substring(16));
            Assert.AreEqual("00000000", Hex(TssPacket.Post(TssCommand.UiaDepress, false, 0)).Substring(16));
        }

        [Test]
        public void Ack_AnyNonZeroByteIsTrue()
        {
            Assert.IsTrue(TssPacket.IsAckTrue(new byte[] { 1, 0, 0, 0 }));   // x86 server
            Assert.IsTrue(TssPacket.IsAckTrue(new byte[] { 0, 0, 0, 1 }));   // big-endian server
            Assert.IsFalse(TssPacket.IsAckTrue(new byte[] { 0, 0, 0, 0 }));
            Assert.IsFalse(TssPacket.IsAckTrue(new byte[] { 1 }));
        }

        [Test]
        public void LtvErrorSlots_MapToTssCommands()
        {
            Assert.AreEqual(2023u, TssCommand.LtvErrorNeedsResolved(0));
            Assert.AreEqual(2030u, TssCommand.LtvErrorNeedsResolved(7));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => TssCommand.LtvErrorNeedsResolved(8));
        }
    }
}
