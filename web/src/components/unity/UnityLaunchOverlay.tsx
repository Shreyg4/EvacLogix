import { Button } from "../ui/Button"

type UnityLaunchOverlayProps = {
  title: string
  launchLabel: string
  onLaunch: () => void
}

export function UnityLaunchOverlay({ title, launchLabel, onLaunch }: UnityLaunchOverlayProps) {
  return (
    <div className="unity-overlay unity-overlay-idle">
      <p className="eyebrow">Embedded Simulation</p>
      <h3>{title}</h3>
      <Button variant="primary" className="unity-launch-button" onClick={onLaunch}>
        {launchLabel}
      </Button>
    </div>
  )
}
