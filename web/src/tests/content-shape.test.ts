import { demoInstructions } from "../content/demoContent"
import { howItWorksPanels } from "../content/howItWorksContent"
import {
  homeContent,
  homeHighlightsSection,
  homeMissionStatementSection,
  homeScopeStatement
} from "../content/homeContent"
import { siteContent } from "../content/siteContent"
import {
  defaultUnityAppProfileId,
  getDefaultUnityAppProfile,
  getVisibleUnityAppProfiles,
  unityAppProfiles
} from "../content/unityAppProfiles"
import { appRoutes } from "../utils/routes"

describe("content shape", () => {
  it("defines the shared mission statement and brand consistently", () => {
    expect(siteContent.brandName).toBe("EvacLogix")
    expect(siteContent.mission.short.length).toBeGreaterThan(0)
    expect(siteContent.navigation).toHaveLength(3)
  })

  it("provides route-level content objects", () => {
    expect(homeContent.title.length).toBeGreaterThan(0)
    expect(demoInstructions.length).toBeGreaterThan(0)
    expect(howItWorksPanels.length).toBeGreaterThan(0)
  })

  it("provides scope framing text", () => {
    expect(siteContent.status.scopeNote).toContain("simulation")
  })

  it("keeps navigation labels synchronized with configured routes", () => {
    expect(siteContent.navigation).toEqual([
      { label: "Home", to: appRoutes.home, end: true },
      { label: "Demo", to: appRoutes.demo },
      { label: "How It Works", to: appRoutes.howItWorks }
    ])
  })

  it("defines homepage support sections in centralized content", () => {
    expect(homeHighlightsSection.title.length).toBeGreaterThan(0)
    expect(homeMissionStatementSection.body.length).toBeGreaterThan(0)
    expect(homeScopeStatement.body).toBe(siteContent.status.scopeNote)
  })

  it("defines named Unity app profiles with one visible default target", () => {
    expect(unityAppProfiles.length).toBeGreaterThanOrEqual(2)
    expect(getDefaultUnityAppProfile().id).toBe(defaultUnityAppProfileId)
    expect(getVisibleUnityAppProfiles()).toHaveLength(1)
    expect(getVisibleUnityAppProfiles()[0]?.id).toBe("evac-sim")
    expect(unityAppProfiles.find((profile) => profile.id === "sandbox-editor")?.hidden).toBe(true)
  })
})
