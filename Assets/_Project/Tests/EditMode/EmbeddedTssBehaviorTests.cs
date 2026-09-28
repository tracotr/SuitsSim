using NUnit.Framework;
using SuitsSim.Tss;
using SuitsSim.Tss.Embedded;

namespace SuitsSim.Tests
{
    /// <summary>Scenario-level checks on the port: the things the HUD depends on.</summary>
    public class EmbeddedTssBehaviorTests
    {
        static float Eva1(TssBackend b, string field) => b.GetField(TssBackend.Eva, "telemetry.eva1." + field) ?? float.NaN;
        static void Ticks(TssBackend b, int n) { for (int i = 0; i < n; i++) b.Tick(); }

        [Test]
        public void TelemetryFrozenUntilEvaStarted()
        {
            var b = EmbeddedTssParityTests.LoadBackend();
            Ticks(b, 5);
            Assert.AreEqual(0f, Eva1(b, "eva_elapsed_time"), 0f);
            b.StartEva();
            Ticks(b, 5);
            Assert.AreEqual(5f, Eva1(b, "eva_elapsed_time"), 0f);
        }

        [Test]
        public void EgressFillsPrimaryTankThroughUia()
        {
            var b = EmbeddedTssParityTests.LoadBackend();
            b.StartEva();
            b.HandlePost(TssCommand.DcuEva1BattLu, 1f);   // umbilical
            b.HandlePost(TssCommand.UiaEva1Power, 1f);
            b.HandlePost(TssCommand.UiaOxyVent, 1f);
            Ticks(b, 30);
            Assert.IsTrue(Eva1(b, "oxy_pri_pressure") < 10f);  // vented

            b.HandlePost(TssCommand.UiaOxyVent, 0f);
            b.HandlePost(TssCommand.DcuEva1Oxy, 1f);          // primary
            b.HandlePost(TssCommand.UiaEva1Oxy, 1f);
            Ticks(b, 45);
            Assert.IsTrue(Eva1(b, "oxy_pri_pressure") > 2950f);
        }

        [Test]
        public void RecoveryModeHidesOtherErrorsUntilResolved()
        {
            var b = EmbeddedTssParityTests.LoadBackend();
            Assert.AreEqual(1, ((Newtonsoft.Json.Linq.JArray)b.Snapshot(3)["error_procedures"]).Count);
            b.HandlePost(TssCommand.LtvErrorNeedsResolved(0), 0f);
            Assert.Greater(((Newtonsoft.Json.Linq.JArray)b.Snapshot(3)["error_procedures"]).Count, 1);
        }

        [Test]
        public void FaultThrownAfterTaskBoardAndClearedBySecondaryOxygen()
        {
            var b = EmbeddedTssParityTests.LoadBackend();
            b.Engine.ForcedFault = SuitFault.O2PressureLow;
            b.StartEva();
            b.HandlePost(TssCommand.DcuEva1Oxy, 1f);
            for (int i = 0; i < 8; i++) b.HandlePost(TssCommand.LtvErrorNeedsResolved(i), 0f);
            Ticks(b, 2);
            b.HandlePost(TssCommand.LtvErrorNeedsResolved(4), 0f); // 4968 re-trips when comms are fixed
            Ticks(b, 15);
            Assert.AreEqual(SuitFault.O2PressureLow, b.Engine.ErrorType);
            Assert.AreEqual(1f, b.GetField(TssBackend.Eva, "error.oxy_error") ?? -1f, 0f);

            b.HandlePost(TssCommand.DcuEva1Oxy, 0f);              // switch to secondary
            Ticks(b, 2);
            Assert.AreEqual(0f, b.GetField(TssBackend.Eva, "error.oxy_error") ?? -1f, 0f);
        }

        [Test]
        public void UnknownCommandIsRejected()
        {
            var b = EmbeddedTssParityTests.LoadBackend();
            Assert.IsFalse(b.HandlePost(2999, 1f));
            Assert.IsTrue(b.HandlePost(TssCommand.DcuEva1Fan, 1f));
        }
    }
}
