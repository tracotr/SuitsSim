using UnityEngine;

// Sends the headset's position and heading to the TSS IMU every tick.
public class ImuFeed : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Main camera of the XR Origin")]
    [SerializeField] private Transform head;
    
    private void OnEnable()
    {
        TssSim.Instance.Ticked += Send;
    }

    private void OnDisable()
    {
        TssSim.Instance.Ticked -= Send;
    }

    private void Send()
    {
        var p = head.position;
        TssSim.Instance.SetImu(p.x, p.z, head.eulerAngles.y);
    }
}
