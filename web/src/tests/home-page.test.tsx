import { screen } from "@testing-library/react";
import {
  homeHighlights,
  homeImpactStatement,
  homeProblemStatement,
  homeSolutionStatement
} from "../content/homeContent";
import { siteContent } from "../content/siteContent";
import { teamSectionContent } from "../content/teamContent";
import { renderAppAt } from "./testUtils";

describe("home page", () => {
  it("renders the reusable sections and CTA buttons", async () => {
    renderAppAt("/");

    expect(await screen.findByRole("link", { name: "Launch Demo" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Learn How It Works" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "What EvacLogix Emphasizes" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: teamSectionContent.title })).toBeInTheDocument();
  });

  it("renders all configured highlights", async () => {
    renderAppAt("/");

    for (const item of homeHighlights) {
      expect(await screen.findByText(item.label)).toBeInTheDocument();
    }
  });

  it("presents the problem before the solution and includes scope framing", async () => {
    renderAppAt("/");

    const problemHeading = await screen.findByRole("heading", { name: homeProblemStatement.title });
    const solutionHeading = screen.getByRole("heading", { name: homeSolutionStatement.title });
    const impactHeading = screen.getByRole("heading", { name: homeImpactStatement.title });

    expect(problemHeading.compareDocumentPosition(solutionHeading)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect(solutionHeading.compareDocumentPosition(impactHeading)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect(screen.getByText(siteContent.status.scopeNote)).toBeInTheDocument();
  });
});
