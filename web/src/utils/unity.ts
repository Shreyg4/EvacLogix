import type { UnityBuildConfig } from "../types/unity";

type CreateUnityInstance = (
  canvasHost: HTMLElement,
  config: UnityBuildConfig
) => Promise<unknown>;

type UnityWindow = Window & {
  createUnityInstance?: CreateUnityInstance;
};

export function validateUnityBuildConfig(
  config: UnityBuildConfig | null | undefined
): config is UnityBuildConfig {
  return Boolean(
    config &&
      config.loaderUrl &&
      config.dataUrl &&
      config.frameworkUrl &&
      config.codeUrl &&
      config.productName
  );
}

export function loadUnityLoader(loaderUrl: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const existing = document.querySelector(`script[data-unity-loader="${loaderUrl}"]`);
    if (existing) {
      resolve();
      return;
    }

    const script = document.createElement("script");
    script.src = loaderUrl;
    script.async = true;
    script.dataset.unityLoader = loaderUrl;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error("Unity loader failed to load."));
    document.body.appendChild(script);
  });
}

export async function fetchUnityBuildConfig(): Promise<UnityBuildConfig | null> {
  try {
    const response = await fetch("/unity-build/unity-build-config.json");

    if (!response.ok) {
      return null;
    }

    const json = (await response.json()) as UnityBuildConfig;
    return json;
  } catch {
    return null;
  }
}

export async function createUnityInstanceBridge(
  canvasHost: HTMLElement,
  config: UnityBuildConfig
): Promise<unknown> {
  const unityWindow = window as UnityWindow;

  if (!unityWindow.createUnityInstance) {
    throw new Error("Unity runtime bridge was not found after loader execution.");
  }

  return unityWindow.createUnityInstance(canvasHost, config);
}
