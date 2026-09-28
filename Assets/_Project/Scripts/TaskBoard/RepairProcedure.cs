using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SuitsSim.TaskBoard
{
    /// <summary>
    /// A machine-checkable version of one LTV repair procedure, loaded from JSON in
    /// Assets/_Project/Procedures. Only physical actions are encoded; "verify" and
    /// "announce" steps are left to the tester and the conductor.
    ///
    /// Groups run in order. Actions inside a group can happen in any order
    /// (e.g. "move MPS, GNC and CDH to OFF").
    /// </summary>
    [Serializable]
    public class RepairProcedure
    {
        public string code;
        public string title;
        public string source;
        public List<StepGroup> groups = new List<StepGroup>();

        [Serializable]
        public class StepGroup
        {
            [JsonProperty("ref")] public string stepRef;   // step number(s) in the NASA procedure text
            public List<StepAction> actions = new List<StepAction>();
        }

        [Serializable]
        public class StepAction
        {
            public ActionKind action;
            public string control;
            public bool state;           // for Set: true = the switch's "on" label

            public bool Matches(StepAction other) =>
                action == other.action && control == other.control && (action != ActionKind.Set || state == other.state);

            public override string ToString() => action == ActionKind.Set ? $"{control}={(state ? "ON" : "OFF")}" : $"{action} {control}";
        }

        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter), true)]
        public enum ActionKind { Set, Press, Release }

        public static RepairProcedure FromJson(string json) => JsonConvert.DeserializeObject<RepairProcedure>(json);
    }
}
