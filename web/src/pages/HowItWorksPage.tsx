import { HeroSection } from "../components/sections/HeroSection"
import { InfoPanel } from "../components/sections/InfoPanel"
import {
  howItWorksPageContent,
  howItWorksPanels,
  howItWorksPanelsSection
} from "../content/howItWorksContent"

export function HowItWorksPage() {
  return (
    <div className="page-stack page-how-it-works">
      <HeroSection content={howItWorksPageContent} />
      <section className="page-card" aria-labelledby="how-panels-title">
        <h2 id="how-panels-title">{howItWorksPanelsSection.title}</h2>
        <p>{howItWorksPanelsSection.body}</p>
        <div className="content-stack">
          {howItWorksPanels.map((panel) => (
            <InfoPanel key={panel.id} title={panel.title} body={panel.body} />
          ))}
        </div>
      </section>
    </div>
  )
}
