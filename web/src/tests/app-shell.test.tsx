import { screen } from "@testing-library/react"
import { siteContent } from "../content/siteContent"
import { renderAppAt } from "./testUtils"

describe("app shell", () => {
  it("renders the site brand and footer", async () => {
    renderAppAt("/")

    expect(await screen.findByText(siteContent.brandName)).toBeInTheDocument()
    expect(screen.getByRole("navigation", { name: "Primary" })).toBeInTheDocument()
    expect(screen.getByText(siteContent.footer.attribution)).toBeInTheDocument()
  })
})
