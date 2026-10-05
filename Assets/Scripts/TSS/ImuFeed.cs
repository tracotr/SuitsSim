using UnityEngine;

// Sends the headset's position (Unity metres, X/Z ground plane) and heading to the TSS each tick.
public class ImuFeed : MonoBehaviour
{
    public Transform head; // XR Origin > Camera Offset > Main Camera

    private void Start() => TssSim.Instance.Ticked += Send;
    private void OnDestroy() { if (TssSim.Instance) TssSim.Instance.Ticked -= Send; }

    private void Send()
    {
        var p = head.position;
        TssSim.Instance.SetImu(p.x, p.z, head.eulerAngles.y);
    }
}
