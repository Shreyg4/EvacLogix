using UnityEngine;

namespace EvacLogix.Sandbox.Rendering
{
    // Derives a stable, well-distributed display color for a teleport pair from the pair's identity (the
    // unordered {portalId, targetPortalId}) rather than a fixed palette index. Both endpoints compute the
    // same color, every pair is effectively unique, and there is no small palette to exhaust and loop. This
    // is purely cosmetic — pairing itself is established by targetTeleportPortalId, never by color.
    public static class SandboxTeleportPairColor
    {
        // Shown for a portal that has not been linked to a partner yet.
        public static readonly Color Unpaired = new(0.55f, 0.55f, 0.6f, 1f);

        public static Color Resolve(string portalId, string targetPortalId)
        {
            if (string.IsNullOrWhiteSpace(portalId) || string.IsNullOrWhiteSpace(targetPortalId))
            {
                return Unpaired;
            }

            // Order-independent key so both ends of the pair hash to the same hue.
            var key = string.CompareOrdinal(portalId, targetPortalId) <= 0
                ? portalId + "|" + targetPortalId
                : targetPortalId + "|" + portalId;

            // FNV-1a, not string.GetHashCode (which is randomized per-process on some runtimes) so the
            // color is stable across sessions.
            var hue = Fnv1aHash(key) / (float)uint.MaxValue;
            return Color.HSVToRGB(Mathf.Repeat(hue, 1f), 0.65f, 0.95f);
        }

        private static uint Fnv1aHash(string value)
        {
            unchecked
            {
                var hash = 2166136261u;
                for (var i = 0; i < value.Length; i += 1)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }

                return hash;
            }
        }
    }
}
