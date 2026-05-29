import type { UnityBridgeCommand } from "../../types/unityBridge";
import type { UnityBuildConfig } from "../../types/unity";

export type UnityEmbedState = "idle" | "launching" | "ready" | "unavailable";

export type UnityEmbedProps = {
  title: string;
  instructions: string[];
  fallbackMessage: string;
  unavailableExplanation?: string;
  backupHref?: string;
  backupLabel?: string;
  launchLabel?: string;
  buildConfigPath?: string;
  buildConfig?: UnityBuildConfig | null;
  allowedBridgeCommands?: UnityBridgeCommand[];
  launchTimeoutMs?: number;
};
