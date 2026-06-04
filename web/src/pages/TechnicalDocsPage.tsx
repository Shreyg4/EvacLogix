import { ArchitectureDiagram } from "../components/sections/ArchitectureDiagram"
import { FlowDiagram } from "../components/sections/FlowDiagram"
import { HeroSection } from "../components/sections/HeroSection"
import { StatementSection } from "../components/sections/StatementSection"
import { TechStackGrid } from "../components/sections/TechStackGrid"
import {
  architectureLayers,
  architectureSection,
  dataFlowSection,
  dataFlowSteps,
  technicalPageContent,
  technicalScopeNote,
  techStackItems,
  techStackSection
} from "../content/technicalContent"

export function TechnicalDocsPage() {
  return (
    <div className="page-stack page-technical">
      <HeroSection content={technicalPageContent} />

      <section className="page-card" aria-labelledby="tech-stack-title">
        <p className="eyebrow">{techStackSection.eyebrow}</p>
        <h2 id="tech-stack-title">{techStackSection.title}</h2>
        <p>{techStackSection.body}</p>
        <TechStackGrid items={techStackItems} />
      </section>

      <section className="page-card" aria-labelledby="architecture-title">
        <p className="eyebrow">{architectureSection.eyebrow}</p>
        <h2 id="architecture-title">{architectureSection.title}</h2>
        <p>{architectureSection.body}</p>
        <ArchitectureDiagram layers={architectureLayers} />
      </section>

      <section className="page-card" aria-labelledby="data-flow-title">
        <p className="eyebrow">{dataFlowSection.eyebrow}</p>
        <h2 id="data-flow-title">{dataFlowSection.title}</h2>
        <p>{dataFlowSection.body}</p>
        <FlowDiagram steps={dataFlowSteps} />
      </section>

      <StatementSection
        eyebrow={technicalScopeNote.eyebrow}
        title={technicalScopeNote.title}
        body={technicalScopeNote.body}
      />
    </div>
  )
}
