import type { UnityBridgeCommand } from "./unityBridge";

export type UnityAppProfile = {
  id: string;
  displayName: string;
  buildConfigPath: string;
  launchLabel: string;
  embedTitle: string;
  backupHref?: string;
  backupLabel?: string;
  fallbackMessage: string;
  fallbackExplanation: string;
  allowedBridgeCommands: UnityBridgeCommand[];
  hidden: boolean;
};
