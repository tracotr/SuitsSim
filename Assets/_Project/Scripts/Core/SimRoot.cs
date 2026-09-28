using SuitsSim.Logging;
using SuitsSim.Tss;
using SuitsSim.Tss.Embedded;
using UnityEngine;

namespace SuitsSim.Core
{
    /// <summary>
    /// One per scene. Owns the config, the TSS link and the session log so every
    /// other component reaches them through SimRoot.Instance instead of wiring
    /// references by hand.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(HitlLogger))]
    public class SimRoot : MonoBehaviour
    {
        public static SimRoot Instance { get; private set; }

        [SerializeField] SimConfig config;

        public SimConfig Config => config;
        /// <summary>The TSS this scene talks to: the embedded one or a real server, per SimConfig.tssMode.</summary>
        public ITssLink Tss { get; private set; }
        public EmbeddedTss Embedded => Tss as EmbeddedTss;
        public HitlLogger Log { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("Two SimRoots in the scene; destroying the second.", this);
                Destroy(gameObject);
                return;
            }
            if (config == null)
            {
                Debug.LogError("SimRoot has no SimConfig assigned. Create one via Assets > Create > SUITS Sim > Sim Config.", this);
                enabled = false;
                return;
            }

            Instance = this;
            Log = GetComponent<HitlLogger>();
            Log.Init(config);

            if (config.tssMode == TssMode.Embedded)
            {
                var embedded = FindAnyObjectByType<EmbeddedTss>();
                if (embedded == null)
                {
                    Debug.LogError("SimConfig is set to Embedded TSS but the scene has no EmbeddedTss. Add one (Add Component > SUITS Sim > Embedded TSS).", this);
                    enabled = false;
                    return;
                }
                Tss = embedded;
            }
            else
            {
                var client = GetComponent<TssClient>();
                if (client == null) client = gameObject.AddComponent<TssClient>();
                client.Init(config);
                Tss = client;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
