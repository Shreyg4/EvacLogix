import { screen } from "@testing-library/react"
import { demoPageContent } from "../content/demoContent"
import { howItWorksPageContent } from "../content/howItWorksContent"
import { homeContent } from "../content/homeContent"
import { siteContent } from "../content/siteContent"
import { technicalPageContent } from "../content/technicalContent"
import { userGuidePageContent } from "../content/userGuideContent"
import { renderAppAt } from "./testUtils"

describe("routing", () => {
  it("resolves the home route", async () => {
    renderAppAt("/")
    expect(await screen.findByRole("heading", { name: homeContent.title })).toBeInTheDocument()
  })

  it("resolves the demo route", async () => {
    renderAppAt("/demo")
    expect(await screen.findByRole("heading", { name: demoPageContent.title })).toBeInTheDocument()
  })

  it("resolves the how it works route", async () => {
    renderAppAt("/how-it-works")
    expect(
      await screen.findByRole("heading", { name: howItWorksPageContent.title })
    ).toBeInTheDocument()
  })

  it("resolves the architecture route", async () => {
    renderAppAt("/architecture")
    expect(
      await screen.findByRole("heading", { name: technicalPageContent.title })
    ).toBeInTheDocument()
  })

  it("resolves the user guide route", async () => {
    renderAppAt("/user-guide")
    expect(
      await screen.findByRole("heading", { name: userGuidePageContent.title })
    ).toBeInTheDocument()
  })

  it("preserves the shared shell across routes", async () => {
    renderAppAt("/demo")

    expect(await screen.findByRole("navigation", { name: "Primary" })).toBeInTheDocument()
    expect(screen.getByText("EvacLogix")).toBeInTheDocument()
    expect(screen.getByText(siteContent.footer.attribution)).toBeInTheDocument()
  })
})
