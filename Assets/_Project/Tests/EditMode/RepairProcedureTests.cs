using System.IO;
using NUnit.Framework;
using SuitsSim.TaskBoard;

namespace SuitsSim.Tests
{
    public class RepairProcedureTests
    {
        [Test]
        public void RecoveryModeProcedure_Parses()
        {
            var json = File.ReadAllText("Assets/_Project/Procedures/4800_exit_recovery_mode.json");
            var p = RepairProcedure.FromJson(json);
            Assert.AreEqual("4800", p.code);
            Assert.Greater(p.groups.Count, 10);
            Assert.AreEqual(3, p.groups[0].actions.Count);
            Assert.AreEqual(RepairProcedure.ActionKind.Press, p.groups[1].actions[0].action);
        }

        [Test]
        public void Matches_IgnoresStateForButtons()
        {
            var press = new RepairProcedure.StepAction { action = RepairProcedure.ActionKind.Press, control = "PDD.CSR", state = true };
            var done = new RepairProcedure.StepAction { action = RepairProcedure.ActionKind.Press, control = "PDD.CSR", state = false };
            Assert.IsTrue(press.Matches(done));
        }

        [Test]
        public void Matches_ChecksStateForSwitches()
        {
            var want = new RepairProcedure.StepAction { action = RepairProcedure.ActionKind.Set, control = "POPS.MPS", state = false };
            var wrong = new RepairProcedure.StepAction { action = RepairProcedure.ActionKind.Set, control = "POPS.MPS", state = true };
            Assert.IsFalse(want.Matches(wrong));
        }
    }
}
