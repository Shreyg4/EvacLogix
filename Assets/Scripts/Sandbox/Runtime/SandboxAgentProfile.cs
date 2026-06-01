using UnityEngine;
using UnityEngine.AI;

namespace EvacLogix.Sandbox.Runtime
{
    [CreateAssetMenu(menuName = "EvacLogix/Sandbox Agent Profile", fileName = "SandboxAgentProfile")]
    public sealed class SandboxAgentProfile : ScriptableObject
    {
        [SerializeField] private float speed = 1.5f;
        [SerializeField] private float radius = 0.125f;
        [SerializeField] private float repathIntervalSeconds = 0.75f;
        [SerializeField] private float safeHealthDecayPerSecond = 0.01f;
        [SerializeField] private float fireHealthDecayPerSecond = 0.45f;
        [SerializeField] private float fireDangerRadius = 2.5f;
        [SerializeField] private float fireAvoidanceWeight = 2f;
        [SerializeField] private float exitReachDistance = 0.35f;

        [Header("Crowd avoidance")]
        // Each agent is given a random priority in [min,max] at spawn so that, when two agents contest
        // the same space, one deterministically yields instead of both mirroring each other (jitter).
        // Lower number = higher priority in Unity's RVO.
        [SerializeField] private int avoidancePriorityMin = 30;
        [SerializeField] private int avoidancePriorityMax = 70;
        [SerializeField] private ObstacleAvoidanceType obstacleAvoidanceQuality = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

        public float Speed => Mathf.Max(0.1f, speed);
        public float Radius => Mathf.Max(0.05f, radius);
        public float RepathIntervalSeconds => Mathf.Max(0.1f, repathIntervalSeconds);
        public float SafeHealthDecayPerSecond => Mathf.Max(0f, safeHealthDecayPerSecond);
        public float FireHealthDecayPerSecond => Mathf.Max(0f, fireHealthDecayPerSecond);
        public float FireDangerRadius => Mathf.Max(0.1f, fireDangerRadius);
        public float FireAvoidanceWeight => Mathf.Max(0f, fireAvoidanceWeight);
        public float ExitReachDistance => Mathf.Max(0.1f, exitReachDistance);
        public int AvoidancePriorityMin => Mathf.Clamp(Mathf.Min(avoidancePriorityMin, avoidancePriorityMax), 0, 99);
        public int AvoidancePriorityMax => Mathf.Clamp(Mathf.Max(avoidancePriorityMin, avoidancePriorityMax), 0, 99);
        public ObstacleAvoidanceType ObstacleAvoidanceQuality => obstacleAvoidanceQuality;
    }
}