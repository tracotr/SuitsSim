using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// A full procedure, e.g. egress, ingress or a POI task.
[CreateAssetMenu(fileName = "New Procedure", menuName = "TSS/Procedure")]
public class Procedure : ScriptableObject
{
    [Tooltip("Name shown on the HUD and task list")]
    public string title = "Default Procedure Title";

    [Tooltip("What this procedure is for")]
    public string description = "Default Procedure Description";

    [Tooltip("Urgent procedures (errors) pause whatever is running and take over.")]
    public ProcedurePriority priority = ProcedurePriority.Normal;

    [Tooltip("List of procedure steps in order")]
    public List<ProcedureStep> steps = new();
}

public enum ProcedurePriority
{
    Normal, Urgent
}

// One step, it passes once its requirements are met or CAPCOM confirms it.
[Serializable]
public class ProcedureStep
{
    [Tooltip("Section this step belongs to, e.g. Prep O2 Tanks")]
    public string header = "Procedure Step Header";

    [Tooltip("What the step tells the astronaut to do")]
    public string text = "Procedure Step Text";

    [Tooltip("The requirements for which switches should be flipped for this step to pass.")]
    public List<SwitchReq> switchRequirements = new();

    [Tooltip("The requirements for what values should be for this step to pass.")]
    public List<ValueReq> valueRequirements = new();

    [Tooltip("Require for a manual confirmation/proceed from capcom")]
    public bool requireConfirm;

    public bool HasRequirements => switchRequirements.Count > 0 || valueRequirements.Count > 0;

    // Manual steps wait for Confirm.
    public bool IsManual => requireConfirm || !HasRequirements;

    public bool CompletesOnItsOwn => !IsManual && RequirementsMet();

    // A confirm step can only be confirmed once its requirements are met.
    public bool CanConfirm => !HasRequirements || (requireConfirm && RequirementsMet());

    public void StartStep()
    {
        foreach (var r in switchRequirements) r.SaveStartState();
    }

    public bool RequirementsMet()
    {
        if (!HasRequirements) return false;

        if (switchRequirements.Any(r => !r.RequirementMet()))
        {
            return false;
        }

        if (valueRequirements.Any(r => !r.RequirementMet()))
        {
            return false;
        }

        return true;
    }

    // Anything this returns false for is an out-of-order flip.
    public bool ExpectsSwitch(TssSwitch s)
    {
        return switchRequirements.Any(r => r.requiredSwitch == s && r.RequirementMet());
    }
}

public enum SwitchState
{
    On, Off, Flip
}

[Serializable]
public class SwitchReq
{
    [Tooltip("The TSS switch this requirement checks")]
    public TssSwitch requiredSwitch;

    [Tooltip("On/Off: switch must be in that position. Flip: must differ from where it was when the step started.")]
    public SwitchState state = SwitchState.On;

    // runtime values
    [NonSerialized] private bool _startValue;

    // Flip needs to know where the switch started.
    public void SaveStartState()
    {
        _startValue = TssSim.Instance.GetSwitch(requiredSwitch);
    }

    public bool RequirementMet()
    {
        bool on = TssSim.Instance.GetSwitch(requiredSwitch);
        return state switch
        {
            SwitchState.On => on,
            SwitchState.Off => !on,
            _ => on != _startValue,
        };
    }
}

public enum Compare
{
    Below, Above
}

[Serializable]
public class ValueReq
{
    [Tooltip("The TSS telemetry value this requirement checks")]
    public TssValue value;

    [Tooltip("If the value should be below or above the threshold")]
    public Compare compare = Compare.Above;

    [Tooltip("The value to compare against")]
    public float threshold;

    public bool RequirementMet()
    {
        float v = TssSim.Instance.Eva1.Get(value).Value;
        return compare == Compare.Above ? v > threshold : v < threshold;
    }
}
