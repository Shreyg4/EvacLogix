import type { UnityBuildConfig } from "../types/unity"

export type UnityInstance = {
  SendMessage?: (gameObjectName: string, methodName: string, value?: string | number) => void
}

type CreateUnityInstance = (
  canvas: HTMLCanvasElement,
  config: UnityBuildConfig
) => Promise<UnityInstance>

type UnityWindow = Window & {
  createUnityInstance?: CreateUnityInstance
}

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
  )
}

export function loadUnityLoader(loaderUrl: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const existing = document.querySelector(`script[data-unity-loader="${loaderUrl}"]`)
    if (existing) {
      resolve()
      return
    }

    const script = document.createElement("script")
    script.src = loaderUrl
    script.async = true
    script.dataset.unityLoader = loaderUrl
    script.onload = () => resolve()
    script.onerror = () => reject(new Error("Unity loader failed to load."))
    document.body.appendChild(script)
  })
}

export async function fetchUnityBuildConfig(): Promise<UnityBuildConfig | null> {
  return fetchUnityBuildConfigFromPath("/unity-build/unity-build-config.json")
}

export async function fetchUnityBuildConfigFromPath(
  buildConfigPath: string
): Promise<UnityBuildConfig | null> {
  try {
    const response = await fetch(buildConfigPath)

    if (!response.ok) {
      return null
    }

    const json = (await response.json()) as UnityBuildConfig
    return json
  } catch {
    return null
  }
}

export async function createUnityInstanceBridge(
  canvas: HTMLCanvasElement,
  config: UnityBuildConfig
): Promise<UnityInstance> {
  const unityWindow = window as UnityWindow

  if (!unityWindow.createUnityInstance) {
    throw new Error("Unity runtime bridge was not found after loader execution.")
  }

  // The loader defaults the wasm to "no-store", so a repeat visitor re-downloads ~28 MB every load.
  // Routing every file through "must-revalidate" stores the wasm + data in Unity's IndexedDB cache and
  // serves them on a cheap 304, while still picking up rebakes (unlike "immutable", which would need a
  // productVersion bump to avoid serving stale binaries). A JSON-provided value still wins via the spread.
  const mergedConfig: UnityBuildConfig = {
    cacheControl: () => "must-revalidate",
    ...config,
  }

  return unityWindow.createUnityInstance(canvas, mergedConfig)
}
