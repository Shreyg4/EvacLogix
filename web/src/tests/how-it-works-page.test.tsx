import { screen } from "@testing-library/react"
import {
  howItWorksPageContent,
  howItWorksPanels,
  howItWorksPanelsSection
} from "../content/howItWorksContent"
import { renderAppAt } from "./testUtils"

describe("how it works page", () => {
  it("renders the route hero and section heading", async () => {
    renderAppAt("/how-it-works")

    expect(
      await screen.findByRole("heading", { name: howItWorksPageContent.title })
    ).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: howItWorksPanelsSection.title })).toBeInTheDocument()
  })

  it("renders all explanatory panels", async () => {
    renderAppAt("/how-it-works")

    for (const panel of howItWorksPanels) {
      expect(await screen.findByRole("heading", { name: panel.title })).toBeInTheDocument()
      expect(screen.getByText(panel.body)).toBeInTheDocument()
    }
  })

  it("keeps the page high level and user-facing", async () => {
    renderAppAt("/how-it-works")

    expect(await screen.findByText(/user-facing terms first/i)).toBeInTheDocument()
    expect(
      screen.getByText(/without turning into a low-level systems specification/i)
    ).toBeInTheDocument()
  })
})
