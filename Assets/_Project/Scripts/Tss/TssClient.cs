using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SuitsSim.Core;
using UnityEngine;

namespace SuitsSim.Tss
{
    /// <summary>
    /// The sim's only link to TSS. It plays the part of the physical world:
    /// it writes what the real UIA, DCU, IMU and task board would write, and it
    /// polls the JSON files it needs to render panel lights and the PDD display.
    ///
    /// Networking runs on background tasks; results are handed to the main thread
    /// in Update, so every event here fires on Unity's main thread.
    /// </summary>
    [AddComponentMenu("SUITS Sim/TSS Client")]
    public class TssClient : MonoBehaviour, ITssLink
    {
        [Tooltip("GET commands to poll. The EV scenario needs EVA (1) and LTV_ERRORS (3).")]
        [SerializeField] List<uint> pollCommands = new List<uint> { TssCommand.GetEva, TssCommand.GetLtvErrors };
        [SerializeField] bool registerAsTaskBoard = false;

        /// <summary>(command, parsed json). Fires once per successful poll.</summary>
        public event Action<uint, JObject> SnapshotReceived;
        /// <summary>(command, value, reason). Fires when TSS rejects or never acknowledges a write.</summary>
        public event Action<uint, float, string> PostFailed;

        public JObject Eva => Latest(TssCommand.GetEva);
        public JObject LtvErrors => Latest(TssCommand.GetLtvErrors);
        public bool IsConnected => SecondsSinceAnySnapshot < Mathf.Max(3f, config != null ? config.pollIntervalSeconds * 3f : 3f);
        public float SecondsSinceAnySnapshot => lastAnySnapshotUtc == DateTime.MinValue
            ? float.PositiveInfinity
            : (float)(DateTime.UtcNow - lastAnySnapshotUtc).TotalSeconds;
        public int PostsSent => postsSent;
        public string Description => config != null ? $"TSS {config.tssHost}:{config.tssPort}" : "TSS (no config)";
        public int PostsFailed { get; private set; }

        SimConfig config;
        CancellationTokenSource cts;
        readonly Dictionary<uint, JObject> latest = new Dictionary<uint, JObject>();
        readonly ConcurrentQueue<(uint cmd, JObject json)> inbox = new ConcurrentQueue<(uint, JObject)>();
        readonly ConcurrentQueue<(uint cmd, float value)> outbox = new ConcurrentQueue<(uint, float)>();
        readonly ConcurrentQueue<(uint cmd, float value, string reason)> failures = new ConcurrentQueue<(uint, float, string)>();
        readonly SemaphoreSlim outboxSignal = new SemaphoreSlim(0);
        DateTime lastAnySnapshotUtc = DateTime.MinValue;
        int postsSent;

        public JObject Latest(uint getCommand) => latest.TryGetValue(getCommand, out var j) ? j : null;

        public void Init(SimConfig simConfig)
        {
            config = simConfig;
            Restart();
        }

        /// <summary>Call after changing host/port at runtime (the conductor console does).</summary>
        public void Restart()
        {
            Stop();
            if (config == null) return;
            cts = new CancellationTokenSource();
            var ct = cts.Token;
            foreach (var cmd in pollCommands)
                Task.Run(() => PollLoop(cmd, ct), ct);
            Task.Run(() => PostLoop(ct), ct);
            if (registerAsTaskBoard) Send(TssCommand.RegisterTaskBoard, 1f);
        }

        public void Send(uint command, float value)
        {
            outbox.Enqueue((command, value));
            outboxSignal.Release();
        }

        public void Send(uint command, bool value) => Send(command, value ? 1f : 0f);

        void Update()
        {
            while (inbox.TryDequeue(out var msg))
            {
                latest[msg.cmd] = msg.json;
                lastAnySnapshotUtc = DateTime.UtcNow;
                SnapshotReceived?.Invoke(msg.cmd, msg.json);
            }
            while (failures.TryDequeue(out var f))
            {
                PostsFailed++;
                PostFailed?.Invoke(f.cmd, f.value, f.reason);
            }
        }

        void OnDisable() => Stop();

        void Stop()
        {
            if (cts == null) return;
            cts.Cancel();
            cts.Dispose();
            cts = null;
        }

        // ---------------------------------------------------------------- background

        async Task PollLoop(uint command, CancellationToken ct)
        {
            var channel = new Channel(config.tssHost, config.tssPort);
            var interval = TimeSpan.FromSeconds(config.pollIntervalSeconds);
            var timeoutMs = (int)(config.requestTimeoutSeconds * 1000);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var started = DateTime.UtcNow;
                    try
                    {
                        var req = TssPacket.Get(command, TssPacket.Now());
                        await channel.Client.SendAsync(req, req.Length);
                        var bytes = await channel.ReceiveAsync(timeoutMs);
                        if (bytes != null)
                        {
                            var json = JObject.Parse(Encoding.UTF8.GetString(bytes).TrimEnd('\0'));
                            if (TssJson.LooksLike(command, json)) inbox.Enqueue((command, json));
                        }
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        // TSS down or restarting: keep polling, the conductor console shows the stale age.
                        Debug.LogWarning($"[TSS] poll {command} failed: {e.Message}");
                        channel.Reset();
                    }

                    var wait = interval - (DateTime.UtcNow - started);
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                }
            }
            catch (OperationCanceledException) { }
            finally { channel.Dispose(); }
        }

        async Task PostLoop(CancellationToken ct)
        {
            var channel = new Channel(config.tssHost, config.tssPort);
            var timeoutMs = (int)(config.requestTimeoutSeconds * 1000);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await outboxSignal.WaitAsync(ct);
                    if (!outbox.TryDequeue(out var msg)) continue;
                    try
                    {
                        var req = TssPacket.Post(msg.cmd, msg.value, TssPacket.Now());
                        await channel.Client.SendAsync(req, req.Length);
                        Interlocked.Increment(ref postsSent);
                        if (msg.cmd == TssCommand.RegisterTaskBoard) continue; // TSS sends no ack for 4000
                        var ack = await channel.ReceiveAsync(timeoutMs);
                        if (ack == null) failures.Enqueue((msg.cmd, msg.value, "no ack"));
                        else if (!TssPacket.IsAckTrue(ack)) failures.Enqueue((msg.cmd, msg.value, "TSS returned false"));
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        failures.Enqueue((msg.cmd, msg.value, e.Message));
                        channel.Reset();
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally { channel.Dispose(); }
        }

        /// <summary>
        /// A connected UdpClient that can time out a receive. UdpClient.ReceiveAsync
        /// has no cancellation on .NET Standard 2.1, so a timeout disposes the socket
        /// and opens a fresh one; this also drops any late reply to the old request.
        /// </summary>
        sealed class Channel : IDisposable
        {
            readonly string host;
            readonly int port;
            public UdpClient Client { get; private set; }

            public Channel(string host, int port)
            {
                this.host = host;
                this.port = port;
                Open();
            }

            void Open()
            {
                Client = new UdpClient();
                Client.Connect(host, port);
            }

            public async Task<byte[]> ReceiveAsync(int timeoutMs)
            {
                var receive = Client.ReceiveAsync();
                var done = await Task.WhenAny(receive, Task.Delay(timeoutMs));
                if (done == receive) return receive.Result.Buffer;
                _ = receive.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                Reset();
                return null;
            }

            public void Reset()
            {
                Client?.Dispose();
                Open();
            }

            public void Dispose() => Client?.Dispose();
        }
    }
}
