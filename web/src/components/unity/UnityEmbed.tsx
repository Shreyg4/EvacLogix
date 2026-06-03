import { useEffect, useRef, useState, type CSSProperties } from "react"
import {
  createUnityInstanceBridge,
  fetchUnityBuildConfig,
  fetchUnityBuildConfigFromPath,
  loadUnityLoader,
  validateUnityBuildConfig
} from "../../utils/unity"
import { UnityFallback } from "./UnityFallback"
import { UnityLaunchOverlay } from "./UnityLaunchOverlay"
import type { UnityEmbedProps, UnityEmbedState } from "./unity.types"
import type { UnityBrowserBridgeApi } from "../../types/unityBridge"
import type { UnityInstance } from "../../utils/unity"

type UnityAspectRatioOption = {
  label: string
  value: string
}

type KeyboardWithLock = {
  lock?: (keyCodes?: string[]) => Promise<void>
  unlock?: () => void
}

const unityAspectRatioStorageKey = "evaclogix:unity-aspect-ratio"
const unityUiScaleStorageKey = "evaclogix:unity-ui-scale"
const unityUiScaleStep = 0.25
const unityUiScaleMin = 1
const unityUiScaleMax = 2

const unityAspectRatioOptions: UnityAspectRatioOption[] = [
  { label: "16:9", value: "16 / 9" },
  { label: "4:3", value: "4 / 3" },
  { label: "3:2", value: "3 / 2" },
  { label: "1:1", value: "1 / 1" },
  { label: "Tall", value: "9 / 16" }
]

function getInitialUnityAspectRatio(): UnityAspectRatioOption {
  const storedValue = window.localStorage.getItem(unityAspectRatioStorageKey)
  return (
    unityAspectRatioOptions.find((option) => option.value === storedValue) ??
    unityAspectRatioOptions[0]
  )
}

function getInitialUnityUiScale(): number {
  const storedValue = Number(window.localStorage.getItem(unityUiScaleStorageKey))

  if (!Number.isFinite(storedValue)) {
    return unityUiScaleMin
  }

  return Math.min(unityUiScaleMax, Math.max(unityUiScaleMin, storedValue))
}

export function UnityEmbed({
  title,
  instructions,
  fallbackMessage,
  unavailableExplanation,
  backupHref,
  backupLabel = "Open Unity Play backup",
  launchLabel = "Play Simulation",
  buildConfigPath,
  buildConfig,
  allowedBridgeCommands = [],
  launchTimeoutMs = 60000
}: UnityEmbedProps) {
  const [state, setState] = useState<UnityEmbedState>("idle")
  const [showReadyOverlay, setShowReadyOverlay] = useState(false)
  const [isFullscreen, setIsFullscreen] = useState(false)
  const [aspectRatio, setAspectRatio] = useState<UnityAspectRatioOption>(getInitialUnityAspectRatio)
  const [uiScale, setUiScale] = useState(getInitialUnityUiScale)
  const unityFrameRef = useRef<HTMLDivElement | null>(null)
  const canvasHostRef = useRef<HTMLDivElement | null>(null)
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const unityInstanceRef = useRef<UnityInstance | null>(null)
  const uiScaleRef = useRef(uiScale)

  const selectDefaultUnityTool = () => {
    unityInstanceRef.current?.SendMessage?.("UI", "SelectDefaultTool")
  }

  useEffect(() => {
    const bridge = window.EvacLogixSandboxBridge as UnityBrowserBridgeApi | undefined
    bridge?.setAllowedCommands(allowedBridgeCommands)

    return () => {
      bridge?.setAllowedCommands([])
    }
  }, [allowedBridgeCommands])

  useEffect(() => {
    if (state !== "ready") {
      setShowReadyOverlay(false)
      return
    }

    setShowReadyOverlay(true)
    const timeoutId = window.setTimeout(() => {
      setShowReadyOverlay(false)
    }, 1200)

    return () => {
      window.clearTimeout(timeoutId)
    }
  }, [state])

  useEffect(() => {
    const handleFullscreenChange = () => {
      setIsFullscreen(document.fullscreenElement === unityFrameRef.current)
    }

    document.addEventListener("fullscreenchange", handleFullscreenChange)

    return () => {
      document.removeEventListener("fullscreenchange", handleFullscreenChange)
    }
  }, [])

  useEffect(() => {
    const keyboard = (navigator as Navigator & { keyboard?: KeyboardWithLock }).keyboard

    if (!isFullscreen || state !== "ready") {
      keyboard?.unlock?.()
      return
    }

    keyboard?.lock?.(["Escape"]).catch(() => {
      // Browsers without Keyboard Lock will keep their native Escape fullscreen behavior.
    })

    return () => {
      keyboard?.unlock?.()
    }
  }, [isFullscreen, state])

  useEffect(() => {
    const handleEscape = (event: KeyboardEvent) => {
      if (
        event.key !== "Escape" ||
        state !== "ready" ||
        document.fullscreenElement !== unityFrameRef.current
      ) {
        return
      }

      event.preventDefault()
      event.stopPropagation()
      selectDefaultUnityTool()
    }

    document.addEventListener("keydown", handleEscape, { capture: true })

    return () => {
      document.removeEventListener("keydown", handleEscape, { capture: true })
    }
  }, [state])

  useEffect(() => {
    uiScaleRef.current = uiScale
    if (state !== "ready") {
      return
    }

    unityInstanceRef.current?.SendMessage?.("UI", "SetUiScale", uiScale.toFixed(2))
  }, [state, uiScale])

  useEffect(() => {
    if (state !== "launching") {
      return
    }

    let cancelled = false

    const launchUnity = async () => {
      if (!canvasHostRef.current) {
        if (!cancelled) {
          setState("unavailable")
        }
        return
      }

      if (!canvasRef.current) {
        const canvas = document.createElement("canvas")
        canvas.id = "evaclogix-unity-canvas"
        canvas.className = "unity-canvas"
        canvasHostRef.current.innerHTML = ""
        canvasHostRef.current.appendChild(canvas)
        canvasRef.current = canvas
      }

      const resolvedBuildConfig =
        buildConfig ??
        (buildConfigPath
          ? await fetchUnityBuildConfigFromPath(buildConfigPath)
          : await fetchUnityBuildConfig())

      if (!validateUnityBuildConfig(resolvedBuildConfig)) {
        if (!cancelled) {
          setState("unavailable")
        }
        return
      }

      try {
        await loadUnityLoader(resolvedBuildConfig.loaderUrl)

        const readyPromise = createUnityInstanceBridge(canvasRef.current, resolvedBuildConfig)
        const timeoutPromise = new Promise<never>((_, reject) => {
          window.setTimeout(() => reject(new Error("Unity launch timed out.")), launchTimeoutMs)
        })

        const unityInstance = await Promise.race([readyPromise, timeoutPromise])

        if (!cancelled) {
          unityInstanceRef.current = unityInstance
          unityInstance.SendMessage?.("UI", "SetUiScale", uiScaleRef.current.toFixed(2))
          setState("ready")
        }
      } catch {
        if (!cancelled) {
          setState("unavailable")
        }
      }
    }

    launchUnity()

    return () => {
      cancelled = true
    }
  }, [buildConfig, buildConfigPath, launchTimeoutMs, state])

  const handleEnterFullscreen = async () => {
    if (!unityFrameRef.current || !document.fullscreenEnabled) {
      return
    }

    await unityFrameRef.current.requestFullscreen()
  }

  const handleExitFullscreen = async () => {
    if (!document.fullscreenElement) {
      return
    }

    await document.exitFullscreen()
  }

  const handleAspectRatioChange = (nextAspectRatio: UnityAspectRatioOption) => {
    setAspectRatio(nextAspectRatio)
    window.localStorage.setItem(unityAspectRatioStorageKey, nextAspectRatio.value)
  }

  const handleUiScaleChange = (nextScale: number) => {
    const clampedScale = Math.min(unityUiScaleMax, Math.max(unityUiScaleMin, nextScale))
    setUiScale(clampedScale)
    window.localStorage.setItem(unityUiScaleStorageKey, String(clampedScale))
  }

  return (
    <section className="page-card unity-section" aria-labelledby="unity-embed-title">
      <div className="unity-copy">
        <p className="eyebrow">Demo</p>
        <h2 id="unity-embed-title">{title}</h2>
        <p>
          The simulation is launched from inside this frame. The website explains what to look for,
          but all controls remain inside Unity.
        </p>
        <ul className="content-list" aria-label="Demo instructions">
          {instructions.map((instruction) => (
            <li key={instruction}>{instruction}</li>
          ))}
        </ul>
      </div>

      <div className="unity-view-controls" aria-label="Simulation view settings">
        <span className="unity-view-label">View</span>
        <div className="unity-ratio-options" role="group" aria-label="Aspect ratio">
          {unityAspectRatioOptions.map((option) => (
            <button
              key={option.value}
              className={
                option.value === aspectRatio.value
                  ? "unity-ratio-button active"
                  : "unity-ratio-button"
              }
              type="button"
              aria-pressed={option.value === aspectRatio.value}
              onClick={() => handleAspectRatioChange(option)}
            >
              {option.label}
            </button>
          ))}
        </div>
        <div className="unity-ui-scale-controls" role="group" aria-label="Simulator UI size">
          <button
            className="unity-ui-scale-button"
            type="button"
            onClick={() => handleUiScaleChange(uiScale - unityUiScaleStep)}
            disabled={uiScale <= unityUiScaleMin}
            aria-label="Decrease simulator UI size"
          >
            -
          </button>
          <span className="unity-ui-scale-value">{Math.round(uiScale * 100)}%</span>
          <button
            className="unity-ui-scale-button"
            type="button"
            onClick={() => handleUiScaleChange(uiScale + unityUiScaleStep)}
            disabled={uiScale >= unityUiScaleMax}
            aria-label="Increase simulator UI size"
          >
            +
          </button>
        </div>
      </div>

      <div
        ref={unityFrameRef}
        className="unity-frame"
        style={
          {
            "--unity-aspect-ratio": aspectRatio.value
          } as CSSProperties
        }
        aria-label="Unity simulation frame"
      >
        <div ref={canvasHostRef} className="unity-canvas-host" aria-hidden={state !== "ready"} />

        {state === "ready" ? (
          <div className="unity-fullscreen-controls">
            {isFullscreen ? (
              <button
                className="unity-fullscreen-button"
                type="button"
                onClick={handleExitFullscreen}
              >
                Exit Fullscreen
              </button>
            ) : (
              <button
                className="unity-fullscreen-button"
                type="button"
                onClick={handleEnterFullscreen}
                disabled={!document.fullscreenEnabled}
              >
                Fullscreen
              </button>
            )}
          </div>
        ) : null}

        {state === "idle" ? (
          <UnityLaunchOverlay
            title={title}
            launchLabel={launchLabel}
            onLaunch={() => setState("launching")}
          />
        ) : null}

        {state === "launching" ? (
          <div className="unity-overlay unity-overlay-launching" role="status" aria-live="polite">
            <p className="eyebrow">Launching</p>
            <h3>Starting simulation...</h3>
            <p>Loading the Unity WebGL build. This can take a moment on the first visit.</p>
          </div>
        ) : null}

        {state === "unavailable" ? (
          <UnityFallback
            message={fallbackMessage}
            explanation={unavailableExplanation}
            backupHref={backupHref}
            backupLabel={backupLabel}
          />
        ) : null}

        {state === "ready" && showReadyOverlay ? (
          <div className="unity-overlay unity-overlay-ready" role="status">
            <h3>Simulation Ready</h3>
          </div>
        ) : null}
      </div>
    </section>
  )
}
