import type { ExplanatoryPanel, HeroContent, StatementContent } from "../types/content"

export const howItWorksPageContent: HeroContent = {
  eyebrow: "How It Works",
  title: "A high-level look at what the simulation is meant to show.",
  body: "EvacLogix is designed to help viewers understand evacuation behavior in a way that stays accessible during presentation. This page explains the model at a high level without turning into a low-level systems specification."
}

export const howItWorksPanels: ExplanatoryPanel[] = [
  {
    id: "building-modeling",
    title: "Building Modeling",
    body: "You build the space yourself: draw walls and place doors, stairs, and exits across multiple floors, or import a blueprint image and calibrate it so the model matches real-world dimensions. The tool detects rooms and treats the result as a navigable environment."
  },
  {
    id: "evacuation-behavior",
    title: "Evacuation Behavior",
    body: "When you run a simulation, a crowd of evacuee agents pathfinds toward the nearest exits over a navigation mesh built from your layout. Rather than one person at a time, you see how groups move, queue at doors, and slow down when many people leave at once."
  },
  {
    id: "hazard-context",
    title: "Hazard and Congestion Context",
    body: "An optional fire spreads cell by cell through the building during a run. Watching crowding and fire evolve together makes it clear how safe routes can become blocked or congested under pressure."
  },
  {
    id: "iterate",
    title: "Validate and Iterate",
    body: "Built-in validation flags gaps, unreachable areas, and missing exits before you simulate. You can save and reload projects, adjust the layout, and re-run to compare how design changes affect the evacuation."
  }
]

export const howItWorksPanelsSection: StatementContent = {
  title: "High-Level Panels",
  body: "These panels explain the project in user-facing terms first, keeping the focus on what the simulation communicates rather than on engine-level implementation details."
}
