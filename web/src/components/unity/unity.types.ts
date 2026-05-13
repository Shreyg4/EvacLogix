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
  buildConfig?: UnityBuildConfig | null;
  launchTimeoutMs?: number;
};
