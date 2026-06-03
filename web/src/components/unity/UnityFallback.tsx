type UnityFallbackProps = {
  message: string
  explanation?: string
  backupHref?: string
  backupLabel?: string
}

export function UnityFallback({
  message,
  explanation,
  backupHref,
  backupLabel
}: UnityFallbackProps) {
  return (
    <div className="unity-overlay unity-overlay-unavailable" role="status" aria-live="polite">
      <p className="eyebrow">Simulation Status</p>
      <h3>{message}</h3>
      {explanation ? <p>{explanation}</p> : null}
      {backupHref && backupLabel ? (
        <a className="backup-link" href={backupHref}>
          {backupLabel}
        </a>
      ) : null}
    </div>
  )
}
