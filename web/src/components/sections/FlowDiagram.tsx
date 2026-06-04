import type { FlowStep } from "../../types/content"

type FlowDiagramProps = {
  steps: FlowStep[]
}

export function FlowDiagram({ steps }: FlowDiagramProps) {
  return (
    <ol className="flow-diagram" aria-label="Data flow pipeline">
      {steps.map((step, index) => (
        <li key={step.id} className="flow-step">
          <article className="sub-card flow-step-card">
            <span className="flow-step-index" aria-hidden="true">
              {index + 1}
            </span>
            <h3>{step.title}</h3>
            <p>{step.detail}</p>
          </article>
          {index < steps.length - 1 ? (
            <span className="flow-connector" aria-hidden="true">
              →
            </span>
          ) : null}
        </li>
      ))}
    </ol>
  )
}
