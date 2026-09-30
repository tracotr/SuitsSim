using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Procedure", menuName = "TSS/Procedure")]
public class Procedure : ScriptableObject
{
    public string title = "Default Procedure Title";
    public string description = "Default Procedure Description";
    [Tooltip("Urgent procedures (errors) pause whatever is running and take over.")]
    public ProcedurePriority priority = ProcedurePriority.Normal;
    public List<ProcedureStep> steps = new();
}

public enum ProcedurePriority { Normal, Urgent }

[Serializable]
public class ProcedureStep
{
    public string header = "Procedure Step Header";
    public string text = "Procedure Step Text";
    public List<SwitchReq> switchRequirements = new();
    public List<ValueReq> valueRequirements = new();

    // No requirements = manual step, done on Confirm.
    public bool IsManual => switchRequirements.Count == 0 && valueRequirements.Count == 0;

    // Call when the step becomes current, so Flip requirements know the starting position.
    public void Begin(TssSim sim)
    {
        foreach (var r in switchRequirements) r.Begin(sim);
    }

    public bool IsMet(TssSim sim)
    {
        if (IsManual) return false;
        foreach (var r in switchRequirements) if (!r.IsMet(sim)) return false;
        foreach (var r in valueRequirements) if (!r.IsMet(sim.Eva1)) return false;
        return true;
    }

    // True if this step expects `s` to be where it is now. Anything else is an out-of-order flip.
    public bool Expects(TssSwitch s, TssSim sim)
    {
        foreach (var r in switchRequirements) if (r.requiredSwitch == s && r.IsMet(sim)) return true;
        return false;
    }
}

public enum SwitchState { On, Off, Flip }

[Serializable]
public class SwitchReq
{
    public TssSwitch requiredSwitch;
    [Tooltip("On/Off: switch must be in that position. Flip: must differ from where it was when the step started.")]
    public SwitchState state = SwitchState.On;

    [NonSerialized] private bool _startValue;

    public void Begin(TssSim sim) => _startValue = sim.GetSwitch(requiredSwitch);

    public bool IsMet(TssSim sim)
    {
        bool on = sim.GetSwitch(requiredSwitch);
        return state switch
        {
            SwitchState.On => on,
            SwitchState.Off => !on,
            _ => on != _startValue,
        };
    }
}

public enum Compare { Below, Above }

[Serializable]
public class ValueReq
{
    public TssValue value;
    public Compare compare = Compare.Above;
    public float threshold;

    public bool IsMet(EvaData eva)
    {
        float v = eva.Get(value).Value;
        return compare == Compare.Above ? v > threshold : v < threshold;
    }
}
