using System;
using System.Collections.Generic;
using SuitsSim.Core;
using UnityEngine;

namespace SuitsSim.World
{
    public enum SiteId { Hab, TaskBoardAlpha, TaskBoardBravo }

    /// <summary>
    /// Named worksites in TSS coordinates. Defaults are from TSS2026
    /// documents/procedures/ev-team-coordinates.pdf (FY26, accurate to ~10 m).
    /// Replace them when the FY27 coordinates document is published.
    /// Each site can drive a marker transform (HAB model, task board prefab, beacon).
    /// </summary>
    [AddComponentMenu("SUITS Sim/Site Registry")]
    public class SiteRegistry : MonoBehaviour
    {
        [Serializable]
        public class Site
        {
            public SiteId id;
            public Vector2 tss;
            public Transform marker;
        }

        [SerializeField] List<Site> sites = new List<Site>
        {
            new Site { id = SiteId.Hab, tss = new Vector2(-5670f, -10060f) },
            new Site { id = SiteId.TaskBoardAlpha, tss = new Vector2(-5635f, -9970f) },
            new Site { id = SiteId.TaskBoardBravo, tss = new Vector2(-5615f, -9995f) },
        };

        [Tooltip("Raycast down onto terrain colliders when placing markers.")]
        [SerializeField] LayerMask groundMask = ~0;

        public static SiteRegistry Instance { get; private set; }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start() => SnapMarkers();

        public Vector2 Tss(SiteId id)
        {
            foreach (var s in sites) if (s.id == id) return s.tss;
            throw new KeyNotFoundException($"Site {id} is not in the registry");
        }

        public SiteId TaskBoardSite(TaskBoardSite which) =>
            which == Core.TaskBoardSite.Bravo ? SiteId.TaskBoardBravo : SiteId.TaskBoardAlpha;

        /// <summary>Moves every marker onto its TSS coordinate, dropped onto the ground.</summary>
        [ContextMenu("Snap markers to TSS coordinates")]
        public void SnapMarkers()
        {
            var config = SimRoot.Instance != null ? SimRoot.Instance.Config : FindConfigInEditor();
            if (config == null) { Debug.LogWarning("No SimConfig found; cannot place markers.", this); return; }

            var frame = config.Frame;
            foreach (var s in sites)
            {
                if (s.marker == null) continue;
                var p = frame.ToUnity(s.tss, 200f);
                if (Physics.Raycast(p, Vector3.down, out var hit, 1000f, groundMask)) p = hit.point;
                else p.y = 0f;
                s.marker.position = p;
            }
        }

        SimConfig FindConfigInEditor()
        {
            var root = FindAnyObjectByType<SimRoot>();
            return root != null ? root.Config : null;
        }
    }
}
