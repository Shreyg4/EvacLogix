using UnityEngine;

namespace EvacLogix.Sandbox.Runtime
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SandboxEvacueeAgent : MonoBehaviour
    {
        [SerializeField] private SandboxAgentProfile profile;
        [SerializeField] private string agentId = string.Empty;
        [SerializeField] private string floorId = string.Empty;
        [SerializeField] private string targetExitId = string.Empty;
        [SerializeField] private float health = 1f;
        [SerializeField] private bool hasExited;
        [SerializeField] private float repathTimer;
        [SerializeField] private Vector2 currentDestination;
        [SerializeField] private Color healthyColor = new(0.25f, 0.85f, 0.45f, 1f);
        [SerializeField] private Color warningColor = new(0.95f, 0.75f, 0.25f, 1f);
        [SerializeField] private Color dangerColor = new(0.95f, 0.25f, 0.2f, 1f);

        private SpriteRenderer spriteRenderer;

        public string AgentId => agentId;
        public string FloorId => floorId;
        public string TargetExitId => targetExitId;
        public float Health => health;
        public bool HasExited => hasExited;
        public Vector2 CurrentDestination => currentDestination;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer.sprite == null)
            {
                spriteRenderer.sprite = GenerateFallbackSprite();
            }

            spriteRenderer.color = healthyColor;
        }

        public void Configure(SandboxAgentProfile agentProfile, string newAgentId, string newFloorId, Vector2 startPosition)
        {
            profile = agentProfile;
            agentId = newAgentId;
            floorId = newFloorId;
            health = 1f;
            hasExited = false;
            repathTimer = 0f;
            currentDestination = startPosition;
            transform.position = new Vector3(startPosition.x, startPosition.y, transform.position.z);
            RefreshTint();
        }

        public void SetDestination(string exitId, Vector2 destination)
        {
            targetExitId = exitId;
            currentDestination = destination;
            repathTimer = 0f;
        }

        public void MarkExited()
        {
            hasExited = true;
            health = Mathf.Clamp01(health);
            spriteRenderer.color = new Color(spriteRenderer.color.r, spriteRenderer.color.g, spriteRenderer.color.b, 0.5f);
        }

        public void Tick(float deltaTime, Vector2 movement, float fireExposure)
        {
            if (hasExited)
            {
                return;
            }

            var activeProfile = profile;
            if (activeProfile == null)
            {
                return;
            }

            repathTimer += deltaTime;
            health = Mathf.Clamp01(health - (activeProfile.SafeHealthDecayPerSecond + Mathf.Max(0f, fireExposure) * activeProfile.FireHealthDecayPerSecond) * deltaTime);

            var nextPosition = (Vector2)transform.position + movement * (activeProfile.Speed * deltaTime);
            transform.position = new Vector3(nextPosition.x, nextPosition.y, transform.position.z);
            RefreshTint();

            if (health <= 0f)
            {
                hasExited = true;
            }
        }

        public bool NeedsRepath()
        {
            return profile != null && repathTimer >= profile.RepathIntervalSeconds;
        }

        public void ResetRepathTimer()
        {
            repathTimer = 0f;
        }

        public bool IsAtDestination()
        {
            var activeProfile = profile;
            return activeProfile != null && Vector2.Distance((Vector2)transform.position, currentDestination) <= activeProfile.ExitReachDistance;
        }

        private void RefreshTint()
        {
            if (spriteRenderer == null)
            {
                return;
            }

            if (health <= 0.35f)
            {
                spriteRenderer.color = dangerColor;
            }
            else if (health <= 0.7f)
            {
                spriteRenderer.color = warningColor;
            }
            else
            {
                spriteRenderer.color = healthyColor;
            }
        }

        private static Sprite GenerateFallbackSprite()
        {
            var texture = Texture2D.whiteTexture;
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}