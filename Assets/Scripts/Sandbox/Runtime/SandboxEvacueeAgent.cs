using UnityEngine;
using UnityEngine.AI;

namespace EvacLogix.Sandbox.Runtime
{
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(CircleCollider2D))]
    [RequireComponent(typeof(Rigidbody2D))]
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
        private CircleCollider2D circleCollider;
        private Rigidbody2D cachedRigidbody2D;
        private GameObject navAgentObject;
        private NavMeshAgent navMeshAgent;
        private float floorElevation;
        private float speedMultiplier = 1f;
        // Per-agent scale on the repath interval so a whole crowd doesn't repath on the same frame
        // (synchronized repaths spike the routing cost and stutter the framerate).
        private float repathIntervalScale = 1f;

        public string AgentId => agentId;
        public string FloorId => floorId;
        public string TargetExitId => targetExitId;
        public float Health => health;
        public bool HasExited => hasExited;
        public Vector2 CurrentDestination => currentDestination;
        public float Radius => GetProfileRadius();
        public Vector2 CurrentWorldPosition => navAgentObject != null
            ? new Vector2(navAgentObject.transform.position.x, navAgentObject.transform.position.z)
            : (Vector2)transform.position;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            circleCollider = GetComponent<CircleCollider2D>();
            cachedRigidbody2D = GetComponent<Rigidbody2D>();
            if (spriteRenderer.sprite == null)
            {
                spriteRenderer.sprite = GenerateFallbackSprite();
            }

            if (circleCollider != null)
            {
                circleCollider.radius = 0.5f;
                circleCollider.offset = Vector2.zero;
                circleCollider.isTrigger = false;
            }

            if (cachedRigidbody2D != null)
            {
                cachedRigidbody2D.bodyType = RigidbodyType2D.Kinematic;
                cachedRigidbody2D.gravityScale = 0f;
                cachedRigidbody2D.freezeRotation = true;
                cachedRigidbody2D.interpolation = RigidbodyInterpolation2D.Interpolate;
                cachedRigidbody2D.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            }

            UpdateVisualFootprint();
            spriteRenderer.color = healthyColor;
        }

        private void OnDestroy()
        {
            DestroyNavAgentObject();
        }

        private void LateUpdate()
        {
            SyncVisualPosition();
        }

        public void Configure(SandboxAgentProfile agentProfile, string newAgentId, string newFloorId, Vector2 startPosition, float newFloorElevation = 0f)
        {
            profile = agentProfile;
            agentId = newAgentId;
            floorId = newFloorId;
            floorElevation = newFloorElevation;
            health = 1f;
            hasExited = false;
            // Start each agent at a random phase within its repath interval and give it a slightly
            // off-nominal interval so the crowd's repaths stay spread across frames instead of pulsing.
            var baseInterval = agentProfile != null ? agentProfile.RepathIntervalSeconds : 0.75f;
            repathIntervalScale = Random.Range(0.8f, 1.2f);
            repathTimer = Random.Range(0f, baseInterval);
            currentDestination = startPosition;
            EnsureNavAgentObject();
            UpdateVisualFootprint();
            SyncNavAgentPosition(startPosition);
            SyncVisualPosition();
            RefreshTint();
        }

        // Scales movement speed (used to slow agents crossing soft obstacles). 1 = full speed.
        public void SetSpeedMultiplier(float multiplier)
        {
            speedMultiplier = Mathf.Clamp(multiplier, 0.05f, 1f);
            if (navMeshAgent != null)
            {
                navMeshAgent.speed = GetProfileSpeed() * speedMultiplier;
            }
        }

        // Immediate health loss (used for escape-window fall injuries). Marks the agent dead at zero.
        public void ApplyInjury(float amount)
        {
            health = Mathf.Clamp01(health - Mathf.Max(0f, amount));
            if (health <= 0f)
            {
                hasExited = true;
            }

            RefreshTint();
        }

        // Warps the agent to a new floor/position (used when stepping through a portal). Keeps its
        // health and identity; the caller re-routes afterwards.
        public void Relocate(string newFloorId, Vector2 worldPosition)
        {
            floorId = newFloorId;
            repathTimer = 0f;
            SyncNavAgentPosition(worldPosition);
            SyncVisualPosition();
        }

        public void SetDestination(string exitId, Vector2 destination)
        {
            targetExitId = exitId;
            currentDestination = destination;
            repathTimer = 0f;

            if (CanControlNavMeshAgent())
            {
                navMeshAgent.isStopped = false;
                if (TryGetNavPosition(destination, out var navDestination))
                {
                    navMeshAgent.SetDestination(navDestination);
                }
            }
        }

        public void MarkExited()
        {
            hasExited = true;
            health = Mathf.Clamp01(health);
            if (CanControlNavMeshAgent())
            {
                navMeshAgent.isStopped = true;
                if (CanResetNavMeshPath())
                {
                    navMeshAgent.ResetPath();
                }
                // Pull the finished agent out of the crowd simulation so still-evacuating agents stop
                // avoiding it and can path straight through, instead of cramming behind it at the exit.
                // The agent no longer moves, so disabling the component is safe; the sprite stays put.
                navMeshAgent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                navMeshAgent.enabled = false;
            }

            if (spriteRenderer != null)
            {
                spriteRenderer.color = new Color(spriteRenderer.color.r, spriteRenderer.color.g, spriteRenderer.color.b, 0.5f);
            }
        }

        public void DespawnNow()
        {
            hasExited = true;
            if (CanControlNavMeshAgent())
            {
                navMeshAgent.isStopped = true;
                if (CanResetNavMeshPath())
                {
                    navMeshAgent.ResetPath();
                }
                navMeshAgent.enabled = false;
            }

            DestroyNavAgentObject();
            gameObject.SetActive(false);
        }

        public void Tick(float deltaTime, Vector2 movement, float fireExposure)
        {
            Tick(deltaTime, fireExposure);
        }

        public void Tick(float deltaTime, float fireExposure)
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
            SyncVisualPosition();
            RefreshTint();

            if (health <= 0f)
            {
                hasExited = true;
            }
        }

        public bool NeedsRepath()
        {
            return profile != null && repathTimer >= profile.RepathIntervalSeconds * repathIntervalScale;
        }

        public void ResetRepathTimer()
        {
            repathTimer = 0f;
        }

        public bool IsAtDestination()
        {
            var activeProfile = profile;
            return activeProfile != null && Vector2.Distance(CurrentWorldPosition, currentDestination) <= activeProfile.ExitReachDistance;
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

        private void EnsureNavAgentObject()
        {
            if (navAgentObject != null && navMeshAgent != null)
            {
                return;
            }

            navAgentObject = new GameObject($"{name}_NavMeshAgent");
            navAgentObject.transform.SetParent(transform.parent, false);
            navAgentObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            navMeshAgent = navAgentObject.AddComponent<NavMeshAgent>();
            navMeshAgent.updateRotation = false;
            navMeshAgent.updateUpAxis = false;
            navMeshAgent.autoBraking = true;
            navMeshAgent.radius = GetProfileRadius();
            navMeshAgent.speed = GetProfileSpeed();
            navMeshAgent.acceleration = Mathf.Max(1f, GetProfileSpeed() * 4f);
            navMeshAgent.stoppingDistance = GetProfileExitReachDistance();
            navMeshAgent.obstacleAvoidanceType = profile != null
                ? profile.ObstacleAvoidanceQuality
                : ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        }

        // Sets the agent's RVO avoidance priority (0-99, lower = higher priority). Assigned a seeded
        // random value per agent at spawn so contested space is resolved by one agent yielding rather
        // than both mirroring each other into a jitter.
        public void SetAvoidancePriority(int priority)
        {
            if (navMeshAgent != null)
            {
                navMeshAgent.avoidancePriority = Mathf.Clamp(priority, 0, 99);
            }
        }

        // Overrides the profile's RVO quality (the sim service steps this down for large crowds, where
        // high-quality avoidance on every agent is the dominant per-frame cost).
        public void SetObstacleAvoidanceQuality(ObstacleAvoidanceType quality)
        {
            if (navMeshAgent != null)
            {
                navMeshAgent.obstacleAvoidanceType = quality;
            }
        }

        private void SyncNavAgentPosition(Vector2 worldPosition)
        {
            if (navAgentObject == null)
            {
                return;
            }

            var navPosition = TryGetNavPosition(worldPosition, out var sampledPosition)
                ? sampledPosition
                : ToNavPosition(worldPosition);
            navAgentObject.transform.position = navPosition;

            if (navMeshAgent == null)
            {
                return;
            }

            navMeshAgent.speed = GetProfileSpeed();
            navMeshAgent.radius = GetProfileRadius();
            navMeshAgent.stoppingDistance = GetProfileExitReachDistance();

            if (!navMeshAgent.Warp(navPosition))
            {
                navMeshAgent.transform.position = navPosition;
            }
        }

        private void SyncVisualPosition()
        {
            if (navAgentObject == null)
            {
                return;
            }

            var navPosition = navAgentObject.transform.position;
            transform.position = new Vector3(navPosition.x, navPosition.z, transform.position.z);
        }

        private void UpdateVisualFootprint()
        {
            var diameter = Mathf.Max(0.1f, GetProfileRadius() * 2f);
            transform.localScale = new Vector3(diameter, diameter, 1f);
            if (circleCollider != null)
            {
                circleCollider.radius = 0.5f;
                circleCollider.offset = Vector2.zero;
            }
        }

        private Vector3 ToNavPosition(Vector2 worldPosition)
        {
            return new Vector3(worldPosition.x, floorElevation, worldPosition.y);
        }

        private bool TryGetNavPosition(Vector2 worldPosition, out Vector3 navPosition)
        {
            navPosition = ToNavPosition(worldPosition);
            if (NavMesh.SamplePosition(navPosition, out var hit, Mathf.Max(GetProfileRadius() * 3f, 0.5f), NavMesh.AllAreas))
            {
                navPosition = hit.position;
                return true;
            }

            return false;
        }

        private float GetProfileSpeed()
        {
            return profile != null ? profile.Speed : 1.5f;
        }

        private float GetProfileRadius()
        {
            return profile != null ? profile.Radius : 0.25f;
        }

        private float GetProfileExitReachDistance()
        {
            return profile != null ? profile.ExitReachDistance : 0.35f;
        }

        private void DestroyNavAgentObject()
        {
            if (navAgentObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(navAgentObject);
            }
            else
            {
                DestroyImmediate(navAgentObject);
            }

            navAgentObject = null;
            navMeshAgent = null;
        }

        private bool CanResetNavMeshPath()
        {
            return CanControlNavMeshAgent();
        }

        private bool CanControlNavMeshAgent()
        {
            return navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh;
        }

        private static Sprite GenerateFallbackSprite()
        {
            var texture = Texture2D.whiteTexture;
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
