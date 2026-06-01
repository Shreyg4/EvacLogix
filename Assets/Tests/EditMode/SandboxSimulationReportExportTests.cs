using System;
using System.IO;
using System.Reflection;
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
    }
}
