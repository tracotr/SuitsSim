using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SuitsSim.Tss.Embedded
{
    /// <summary>
    /// The TSS2026 telemetry server, running inside the scene. Drop it on a GameObject, assign
    /// the TSS data files (SUITS Sim > Import TSS Data does this), and every component that
    /// used to need a separate server talks to this instead.
    ///
    /// Talk to it three ways:
    ///   - C#: Get(1) for EVA.json, Post(2014, 1) to flip the DCU fan, SetRoute("eva.status.started", "true")
    ///   - Events: SnapshotReceived fires once per simulated second, like a client polling TSS
    ///   - UDP (optional): turn on Serve Udp and external tools (tss_probe.py, a HoloLens app)
    ///     connect to this machine on port 14141 exactly as they would to the real server
    /// </summary>
    [DefaultExecutionOrder(-1100)]
    [AddComponentMenu("SUITS Sim/Embedded TSS")]
    public class EmbeddedTss : MonoBehaviour, ITssLink
    {
        [Header("TSS data (SUITS Sim > Import TSS Data)")]
        [SerializeField] TextAsset eva1Config;
        [SerializeField] TextAsset eva2Config;
        [SerializeField] TextAsset roverConfig;
        [SerializeField] TextAsset evaJson;
        [SerializeField] TextAsset roverJson;
        [SerializeField] TextAsset ltvJson;
        [SerializeField] TextAsset ltvErrorsJson;

        [Header("Simulation")]
        [Tooltip("Simulated seconds per real second. 1 = real time like test week; raise it to fast-forward tank fills while building the HUD.")]
        [Range(0f, 20f)] public float timeScale = 1f;
        public bool paused;
        [Tooltip("0 = random each boot, like the real server. Set a value for repeatable fault timing.")]
        [SerializeField] int seed;
        [Tooltip("Force which suit fault TSS throws after the task board. None = random, like the real server.")]
        [SerializeField] SuitFault forcedFault = SuitFault.None;
        [SerializeField] bool startEvaOnPlay;
        [SerializeField] bool logToConsole;

        [Header("UDP server (optional)")]
        [SerializeField] bool serveUdp;
        [SerializeField] int udpPort = 14141;

        public event Action<uint, JObject> SnapshotReceived;
        public event Action<uint, float, string> PostFailed;
        /// <summary>Fires after every simulated second, before SnapshotReceived.</summary>
        public event Action<EmbeddedTss> Ticked;

        public TssBackend Backend { get; private set; }
        public bool Ready => Backend != null;
        public string LoadError { get; private set; }

        public JObject Eva => Latest(TssCommand.GetEva);
        public JObject LtvErrors => Latest(TssCommand.GetLtvErrors);
        public bool IsConnected => Ready;
        public float SecondsSinceAnySnapshot => Ready ? Time.unscaledTime - lastPublish : float.PositiveInfinity;
        public int PostsSent { get; private set; }
        public int PostsFailed { get; private set; }
        public string Description => Ready ? $"embedded TSS, t={Backend.Ticks}s x{timeScale:0.#}" : "embedded TSS (not loaded)";

        static readonly uint[] Published = { TssCommand.GetRover, TssCommand.GetEva, TssCommand.GetLtv, TssCommand.GetLtvErrors };
        readonly Dictionary<uint, JObject> latest = new Dictionary<uint, JObject>();
        float accumulator, lastPublish;

        // --------------------------------------------------------------- lifecycle

        void Awake() => Boot();

        void Start()
        {
            if (startEvaOnPlay) StartEva();
            if (serveUdp) StartUdp();
        }

        void OnDisable() => StopUdp();

        /// <summary>Fresh server: same as restarting ./server.exe with pristine data files.</summary>
        public void Boot()
        {
            LoadError = null;
            try
            {
                var missing = new List<string>();
                void Need(TextAsset t, string name) { if (t == null) missing.Add(name); }
                Need(eva1Config, "eva1.json"); Need(eva2Config, "eva2.json"); Need(roverConfig, "rover.json");
                Need(evaJson, "EVA.json"); Need(roverJson, "ROVER.json"); Need(ltvJson, "LTV.json"); Need(ltvErrorsJson, "LTV_ERRORS.json");
                if (missing.Count > 0) throw new InvalidOperationException("missing " + string.Join(", ", missing) + ". Run SUITS Sim > Import TSS Data.");

                Backend = new TssBackend(
                    new[] { eva1Config.text, eva2Config.text, roverConfig.text },
                    new Dictionary<string, string>
                    {
                        [TssBackend.Eva] = evaJson.text,
                        [TssBackend.Rover] = roverJson.text,
                        [TssBackend.Ltv] = ltvJson.text,
                        [TssBackend.LtvErrors] = ltvErrorsJson.text,
                    },
                    seed != 0 ? seed : (int?)null);
                Backend.Log = m => { if (logToConsole) Debug.Log("[TSS] " + m); };
                if (forcedFault != SuitFault.None) Backend.Engine.ForcedFault = forcedFault;
                accumulator = 0f;
                Publish();
            }
            catch (Exception e)
            {
                Backend = null;
                LoadError = e.Message;
                Debug.LogError($"[EmbeddedTss] {e.Message}", this);
            }
        }

        void Update()
        {
            DrainUdp();
            if (!Ready || paused) return;
            accumulator += Time.unscaledDeltaTime * timeScale;
            int budget = 50; // cap catch-up after a hitch
            while (accumulator >= 1f && budget-- > 0)
            {
                accumulator -= 1f;
                Backend.Tick();
                Ticked?.Invoke(this);
            }
            if (budget < 50) Publish();
        }

        void Publish()
        {
            lastPublish = Time.unscaledTime;
            foreach (var cmd in Published) latest[cmd] = Backend.Snapshot(cmd);
            foreach (var cmd in Published) SnapshotReceived?.Invoke(cmd, latest[cmd]);
        }

        // --------------------------------------------------------------- API

        public JObject Latest(uint getCommand) => latest.TryGetValue(getCommand, out var j) ? j : null;

        /// <summary>Current state right now (not the last published second), as a UDP GET would return it.</summary>
        public JObject Get(uint getCommand) => Backend?.Snapshot(getCommand);

        /// <summary>UDP-style write. Returns TSS's acknowledgement.</summary>
        public bool Post(uint command, float value)
        {
            if (!Ready) return false;
            PostsSent++;
            bool ok = Backend.HandlePost(command, value);
            if (!ok)
            {
                PostsFailed++;
                PostFailed?.Invoke(command, value, "TSS returned false");
            }
            return ok;
        }

        public void Send(uint command, float value) => Post(command, value);
        public void Send(uint command, bool value) => Post(command, value ? 1f : 0f);

        /// <summary>A TSS web UI action, e.g. ("eva.status.started", "true") or ("eva.error.fan_error", "true").</summary>
        public bool SetRoute(string route, string value)
        {
            if (!Ready) return false;
            bool ok = Backend.ApplyRoute(route, value);
            RefreshNow();
            return ok;
        }

        [ContextMenu("Start EVA")]
        public void StartEva() => SetRoute("eva.status.started", "true");

        [ContextMenu("Stop and reset EVA")]
        public void ResetEva() => SetRoute("eva.status.started", "false");

        /// <summary>Reboots to pristine data and starts the EVA: a clean run, like CAPCOM resetting TSS.</summary>
        [ContextMenu("Reboot and start EVA")]
        public void RebootAndStartEva()
        {
            Boot();
            StartEva();
        }

        /// <summary>Throws a suit fault immediately instead of waiting for the task board. For HUD work.</summary>
        public void ThrowFaultNow(SuitFault fault)
        {
            if (!Ready || fault == SuitFault.None) return;
            var engine = Backend.Engine;
            var previous = engine.ForcedFault;
            engine.ForcedFault = fault;
            engine.ThrowRandomFault();
            engine.ForcedFault = previous;
            RefreshNow();
        }

        public void Restart() => Boot();

        /// <summary>Publish immediately instead of waiting for the next simulated second.</summary>
        public void RefreshNow()
        {
            if (Ready) Publish();
        }

        // --------------------------------------------------------------- UDP server

        UdpClient udp;
        Thread udpThread;
        readonly ConcurrentQueue<(IPEndPoint from, byte[] data)> udpInbox = new ConcurrentQueue<(IPEndPoint, byte[])>();

        void StartUdp()
        {
            try
            {
                udp = new UdpClient(udpPort);
                udpThread = new Thread(UdpLoop) { IsBackground = true, Name = "EmbeddedTss UDP" };
                udpThread.Start();
                Debug.Log($"[EmbeddedTss] serving TSS UDP on port {udpPort}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[EmbeddedTss] could not open UDP port {udpPort}: {e.Message}. Is a real TSS already running here?", this);
            }
        }

        void StopUdp()
        {
            udp?.Close();
            udp = null;
        }

        void UdpLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (udp != null)
            {
                try
                {
                    var from = any;
                    var data = udp.Receive(ref from);
                    udpInbox.Enqueue((from, data));
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { return; }
            }
        }

        /// <summary>Requests are answered on the main thread because the backend is not thread-safe.</summary>
        void DrainUdp()
        {
            while (udpInbox.TryDequeue(out var req))
            {
                if (udp == null || req.data.Length < 8 || !Ready) continue;
                uint command = TssPacket.ReadU32BE(req.data, 4);
                byte[] reply = null;

                if (TssCommand.IsGet(command))
                {
                    reply = Encoding.UTF8.GetBytes(Backend.HandleGet(command));
                }
                else if (command < 3000 && command != 1130 && req.data.Length >= 12)
                {
                    float value = BitConverter.Int32BitsToSingle(unchecked((int)TssPacket.ReadU32BE(req.data, 8)));
                    bool ok = Post(command, value);
                    reply = new byte[] { (byte)(ok ? 1 : 0), 0, 0, 0 }; // TSS answers in x86 byte order
                }
                // 1130 LIDAR, 3000 DUST heartbeat and 4000 task board registration get no reply, as in TSS

                if (reply != null)
                {
                    try { udp.Send(reply, reply.Length, req.from); }
                    catch (Exception e) { Debug.LogWarning($"[EmbeddedTss] UDP reply failed: {e.Message}"); }
                }
            }
        }
    }
}
