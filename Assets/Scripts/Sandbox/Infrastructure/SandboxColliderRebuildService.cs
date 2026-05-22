using System;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxColliderRebuildService : MonoBehaviour
    {
        [SerializeField] private int rebuildRequestCount;

        public event Action<int> RebuildRequested;

        public int RebuildRequestCount => rebuildRequestCount;

        public void RequestRebuild()
        {
            rebuildRequestCount += 1;
            RebuildRequested?.Invoke(rebuildRequestCount);
        }

        public void ResetCounter()
        {
            rebuildRequestCount = 0;
        }
    }
}
