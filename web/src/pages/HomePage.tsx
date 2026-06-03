import { DemoPreviewSection } from "../components/sections/DemoPreviewSection"
import { HeroSection } from "../components/sections/HeroSection"
import { HighlightsSection } from "../components/sections/HighlightsSection"
import { StatementSection } from "../components/sections/StatementSection"
import { TeamSection } from "../components/sections/TeamSection"
import {
  homeContent,
  homeDemoPreview,
  homeHighlights,
  homeHighlightsSection,
  homeImpactStatement,
  homeMissionStatementSection,
  homeProblemStatement,
  homeScopeStatement,
  homeSolutionStatement
} from "../content/homeContent"
import { teamContent, teamSectionContent } from "../content/teamContent"

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
      <DemoPreviewSection
        eyebrow={homeDemoPreview.eyebrow}
        title={homeDemoPreview.title}
        body={homeDemoPreview.body}
        placeholderLabel={homeDemoPreview.placeholderLabel}
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
      <TeamSection
        eyebrow={teamSectionContent.eyebrow}
        title={teamSectionContent.title}
        body={teamSectionContent.body}
        members={teamContent}
      />
    </div>
  )
}
