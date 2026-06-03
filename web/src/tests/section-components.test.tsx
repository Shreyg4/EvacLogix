import { screen } from "@testing-library/react"
import { HeroSection } from "../components/sections/HeroSection"
import { HighlightsSection } from "../components/sections/HighlightsSection"
import { TeamSection } from "../components/sections/TeamSection"
import { homeContent, homeHighlights, homeHighlightsSection } from "../content/homeContent"
import { teamContent, teamSectionContent } from "../content/teamContent"
import { renderWithElement } from "./testUtils"

describe("section components", () => {
  it("renders hero CTA destinations from content", () => {
    renderWithElement(<HeroSection content={homeContent} />)

    expect(screen.getByRole("link", { name: "Launch Demo" })).toHaveAttribute("href", "/demo")
    expect(screen.getByRole("link", { name: "Learn How It Works" })).toHaveAttribute(
      "href",
      "/how-it-works"
    )
  })

  it("renders highlights in the configured order", () => {
    renderWithElement(
      <HighlightsSection
        eyebrow={homeHighlightsSection.eyebrow}
        title={homeHighlightsSection.title}
        items={homeHighlights}
      />
    )

    const labels = screen.getAllByText(
      /Agent-Based Simulation|Building-Focused Scenarios|Hazard and Congestion Context/
    )

    expect(labels.map((node) => node.textContent)).toEqual(homeHighlights.map((item) => item.label))
  })

  it("renders team member role and responsibility data", () => {
    renderWithElement(
      <TeamSection
        eyebrow={teamSectionContent.eyebrow}
        title={teamSectionContent.title}
        body={teamSectionContent.body}
        members={teamContent}
      />
    )

    expect(screen.getByText(teamContent[0].role)).toBeInTheDocument()
    expect(screen.getByText(teamContent[0].responsibility)).toBeInTheDocument()
  })
})
