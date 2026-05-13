import { useEffect, useRef, useState } from "react";
import {
  createUnityInstanceBridge,
  fetchUnityBuildConfig,
  loadUnityLoader,
  validateUnityBuildConfig
} from "../../utils/unity";
import { UnityFallback } from "./UnityFallback";
import { UnityLaunchOverlay } from "./UnityLaunchOverlay";
import type { UnityEmbedProps, UnityEmbedState } from "./unity.types";

export function UnityEmbed({
  title,
  instructions,
  fallbackMessage,
  unavailableExplanation,
  backupHref,
  backupLabel = "Open Unity Play backup",
  launchLabel = "Play Simulation",
  buildConfig,
  launchTimeoutMs = 4000
}: UnityEmbedProps) {
  const [state, setState] = useState<UnityEmbedState>("idle");
  const canvasHostRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (state !== "launching") {
      return;
    }

    let cancelled = false;

    const launchUnity = async () => {
      if (!canvasHostRef.current) {
        if (!cancelled) {
          setState("unavailable");
        }
        return;
      }

      const resolvedBuildConfig = buildConfig ?? (await fetchUnityBuildConfig());

      if (!validateUnityBuildConfig(resolvedBuildConfig)) {
        if (!cancelled) {
          setState("unavailable");
        }
        return;
      }

      try {
        await loadUnityLoader(resolvedBuildConfig.loaderUrl);

        const readyPromise = createUnityInstanceBridge(canvasHostRef.current, resolvedBuildConfig);
        const timeoutPromise = new Promise<never>((_, reject) => {
          window.setTimeout(() => reject(new Error("Unity launch timed out.")), launchTimeoutMs);
        });

        await Promise.race([readyPromise, timeoutPromise]);

        if (!cancelled) {
          setState("ready");
        }
      } catch {
        if (!cancelled) {
          setState("unavailable");
        }
      }
    };

    launchUnity();

    return () => {
      cancelled = true;
    };
  }, [buildConfig, launchTimeoutMs, state]);

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

      <div className="unity-frame" aria-label="Unity simulation frame">
        <div ref={canvasHostRef} className="unity-canvas-host" aria-hidden={state !== "ready"} />

        {state === "idle" ? (
          <UnityLaunchOverlay title={title} launchLabel={launchLabel} onLaunch={() => setState("launching")} />
        ) : null}

        {state === "launching" ? (
          <div className="unity-overlay unity-overlay-launching" role="status" aria-live="polite">
            <p className="eyebrow">Launching</p>
            <h3>Starting simulation...</h3>
            <p>The current implementation intentionally falls back if Unity is not connected yet.</p>
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

        {state === "ready" ? (
          <div className="unity-overlay unity-overlay-ready" role="status">
            <h3>Simulation Ready</h3>
          </div>
        ) : null}
      </div>
    </section>
  );
}
