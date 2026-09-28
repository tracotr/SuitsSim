using UnityEngine;

// Sends the headset's position (Unity metres, X/Z ground plane) and heading to the TSS each tick.
public class ImuFeed : MonoBehaviour
{
    public Transform head; // XR Origin > Camera Offset > Main Camera

    void Start() => SimTss.Instance.Ticked += Send;
    void OnDestroy() { if (SimTss.Instance) SimTss.Instance.Ticked -= Send; }

    void Send()
    {
        Vector3 p = head.position;
        SimTss.Instance.Post(2017, p.x);
        SimTss.Instance.Post(2018, p.z);
        SimTss.Instance.Post(2019, head.eulerAngles.y);
    }
}
