import type { ArchitectureLayer } from "../../types/content"

type ArchitectureDiagramProps = {
  layers: ArchitectureLayer[]
}

export function ArchitectureDiagram({ layers }: ArchitectureDiagramProps) {
  return (
    <div className="arch-diagram" role="list" aria-label="System architecture layers">
      {layers.map((layer, index) => (
        <div key={layer.id} className="arch-layer-wrap">
          <article className="sub-card arch-layer" role="listitem">
            <div className="arch-layer-head">
              <h3>{layer.name}</h3>
              <p className="arch-layer-summary">{layer.summary}</p>
            </div>
            <ul className="arch-layer-items">
              {layer.items.map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
          </article>
          {index < layers.length - 1 ? (
            <span className="arch-connector" aria-hidden="true">
              ↓
            </span>
          ) : null}
        </div>
      ))}
    </div>
  )
}
