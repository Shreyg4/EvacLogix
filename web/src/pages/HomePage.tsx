import { HeroSection } from "../components/sections/HeroSection"
import { HighlightsSection } from "../components/sections/HighlightsSection"
import { StatementSection } from "../components/sections/StatementSection"
import {
  homeContent,
  homeHighlights,
  homeHighlightsSection,
  homeImpactStatement,
  homeMissionStatementSection,
  homeProblemStatement,
  homeScopeStatement,
  homeSolutionStatement
} from "../content/homeContent"

export function HomePage() {
  return (
    <div className="page-stack page-home">
      <HeroSection content={homeContent} />
      <StatementSection
        eyebrow={homeProblemStatement.eyebrow}
        title={homeProblemStatement.title}
        body={homeProblemStatement.body}
      />
      <StatementSection
        eyebrow={homeSolutionStatement.eyebrow}
        title={homeSolutionStatement.title}
        body={homeSolutionStatement.body}
      />
      <StatementSection
        eyebrow={homeImpactStatement.eyebrow}
        title={homeImpactStatement.title}
        body={homeImpactStatement.body}
      />
      <HighlightsSection
        eyebrow={homeHighlightsSection.eyebrow}
        title={homeHighlightsSection.title}
        items={homeHighlights}
      />
      <StatementSection
        eyebrow={homeMissionStatementSection.eyebrow}
        title={homeMissionStatementSection.title}
        body={homeMissionStatementSection.body}
      />
      <StatementSection
        eyebrow={homeScopeStatement.eyebrow}
        title={homeScopeStatement.title}
        body={homeScopeStatement.body}
      />
    </div>
  )
}
