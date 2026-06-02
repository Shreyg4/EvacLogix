using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EvacLogix.Tests.EditMode
{
    public sealed class SandboxSimulationReportExportTests
    {
        [Test]
        public void AgentSimulationService_ExportsSummaryAndHeatmapReportsIndividually()
        {
            var host = new GameObject("SimulationReportExportHost");
            var simulationService = host.AddComponent<SandboxAgentSimulationService>();
            var outputDirectory = Path.Combine(Path.GetTempPath(), $"EvacLogixReportExport-{Guid.NewGuid():N}");
            var summaryPath = Path.Combine(outputDirectory, "summary.json");
            var heatmapPath = Path.Combine(outputDirectory, "heatmap.json");

            try
            {
                SetLastSimulationRunReport(simulationService);

                Assert.That(simulationService.ExportSimulationSummaryReport(summaryPath), Is.True);
                Assert.That(simulationService.ExportSimulationTravelDensityHeatmapReport(heatmapPath), Is.True);
                Assert.That(File.Exists(summaryPath), Is.True);
                Assert.That(File.Exists(heatmapPath), Is.True);
                Assert.That(File.ReadAllText(summaryPath), Does.Contain("deadAgents"));
                Assert.That(File.ReadAllText(heatmapPath), Does.Contain("sampleCount"));
            }
            finally
            {
                if (Directory.Exists(outputDirectory))
                {
                    Directory.Delete(outputDirectory, true);
                }

                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AgentSimulationService_DespawnsTerminalAgentAndUpdatesReportFromFinalHealth()
        {
            var host = new GameObject("SimulationTerminalHost");
            var workspaceService = host.AddComponent<SandboxProjectWorkspaceService>();
            var simulationService = host.AddComponent<SandboxAgentSimulationService>();
            var agentObject = new GameObject("Agent-fixture");
            var agent = agentObject.AddComponent<SandboxEvacueeAgent>();

            try
            {
                workspaceService.SendMessage("Awake");
                simulationService.SendMessage("Awake");

                var project = new BuildingProjectData();
                project.floors.Add(new FloorData
                {
                    floorId = "floor-1",
                    name = "Floor 1",
                    order = 0,
                    exits =
                    {
                        new ExitZoneData
                        {
                            exitZoneId = "exit-1",
                            center = Vector2.zero,
                            size = Vector2.one
                        }
                    }
                });
                workspaceService.SetActiveProject(project);

                var profile = ScriptableObject.CreateInstance<SandboxAgentProfile>();
                agent.Configure(profile, "agent-1", "floor-1", Vector2.zero, 0f);
                agent.SetDestination("exit-1", Vector2.zero);

                SetPrivateField(simulationService, "simulationActive", true);
                SetPrivateField(simulationService, "totalSpawnedAgents", 1);
                ((List<SandboxEvacueeAgent>)typeof(SandboxAgentSimulationService)
                    .GetField("activeAgents", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(simulationService)).Add(agent);
                ((Dictionary<string, string>)typeof(SandboxAgentSimulationService)
                    .GetField("agentSpawnFloorIds", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(simulationService))["agent-1"] = "floor-1";

                typeof(SandboxAgentSimulationService)
                    .GetMethod("TickAgents", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(simulationService, new object[] { 0f });

                Assert.That(simulationService.ActiveAgents.Count, Is.EqualTo(0));
                Assert.That(agent == null, Is.True);
                Assert.That(GameObject.Find("Agent-fixture_NavMeshAgent"), Is.Null);
                Assert.That(simulationService.LastSimulationRunReport.summary.completedSuccessfully, Is.True);
                Assert.That(simulationService.LastSimulationRunReport.summary.evacuatedAgents, Is.EqualTo(1));
                Assert.That(simulationService.LastSimulationRunReport.summary.deadAgents, Is.EqualTo(0));
                Assert.That(simulationService.LastSimulationRunReport.summary.floorOutcomes[0].evacuatedAgents, Is.EqualTo(1));

                Object.DestroyImmediate(profile);
            }
            finally
            {
                if (agentObject != null)
                {
                    Object.DestroyImmediate(agentObject);
                }

                Object.DestroyImmediate(host);
            }
        }

        private static void SetLastSimulationRunReport(SandboxAgentSimulationService simulationService)
        {
            var report = new SandboxSimulationRunReportData
            {
                summary = new SandboxSimulationSummaryReportData
                {
                    didRun = true,
                    completedSuccessfully = true,
                    completedUtc = "2026-06-01T00:00:00.0000000Z",
                    totalAgents = 2,
                    evacuatedAgents = 1,
                    injuredAgents = 1,
                    deadAgents = 1,
                    averageHealth = 0.5f,
                    floorOutcomes =
                    {
                        new SandboxSimulationFloorOutcomeData
                        {
                            floorId = "floor-1",
                            floorName = "Floor 1",
                            spawnedAgents = 2,
                            evacuatedAgents = 1,
                            injuredAgents = 1,
                            deadAgents = 1,
                            averageHealth = 0.5f
                        }
                    }
                },
                travelDensity = new SandboxSimulationTravelDensityReportData
                {
                    didRun = true,
                    completedSuccessfully = true,
                    completedUtc = "2026-06-01T00:00:00.0000000Z",
                    cellSize = 1f,
                    cells =
                    {
                        new SandboxSimulationTravelDensityCellData
                        {
                            floorId = "floor-1",
                            floorName = "Floor 1",
                            center = new Vector2(0.5f, 0.5f),
                            sampleCount = 3,
                            cumulativeSeconds = 1.5f,
                            intensity = 3f
                        }
                    }
                }
            };

            var field = typeof(SandboxAgentSimulationService).GetField("lastSimulationRunReport", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(simulationService, report);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(target, value);
        }
    }
}
