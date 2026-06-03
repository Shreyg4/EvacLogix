import type { UnityAppProfile } from "../types/unityAppProfile"

export const unityAppProfiles: UnityAppProfile[] = [
  {
    id: "evac-sim",
    displayName: "Evac Simulation",
    buildConfigPath: "/unity-build/evac-sim/unity-build-config.json",
    launchLabel: "Play Simulation",
    embedTitle: "Embedded Simulation Frame",
    backupHref: "https://play.unity.com/",
    backupLabel: "Open Unity Play",
    fallbackMessage: "Simulation unavailable",
    fallbackExplanation:
      "The current build could not be launched from this embedded frame. The page remains usable, and a secondary backup path can still be provided.",
    allowedBridgeCommands: ["ImportBlueprintImage", "ImportProjectJson", "ExportProjectJson"],
    hidden: false
  },
  {
    id: "sandbox-editor",
    displayName: "Sandbox Editor",
    buildConfigPath: "/unity-build/sandbox-editor/unity-build-config.json",
    launchLabel: "Launch Sandbox Editor",
    embedTitle: "Embedded Sandbox Editor",
    backupHref: "https://play.unity.com/",
    backupLabel: "Open Unity Play",
    fallbackMessage: "Sandbox editor unavailable",
    fallbackExplanation:
      "The sandbox editor target is registered with the site but should remain hidden from the main experience until its browser-native bridge is fully ready.",
    allowedBridgeCommands: ["ImportBlueprintImage", "ImportProjectJson", "ExportProjectJson"],
    hidden: true
  }
]

export const defaultUnityAppProfileId = "evac-sim"

export function getUnityAppProfile(
  profileId: string | null | undefined
): UnityAppProfile | undefined {
  return unityAppProfiles.find((profile) => profile.id === profileId)
}

export function getDefaultUnityAppProfile(): UnityAppProfile {
  return getUnityAppProfile(defaultUnityAppProfileId) ?? unityAppProfiles[0]
}

export function getVisibleUnityAppProfiles(): UnityAppProfile[] {
  return unityAppProfiles.filter((profile) => !profile.hidden)
}
