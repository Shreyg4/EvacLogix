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
    body: "The simulation treats a building as a navigable environment with rooms, exits, barriers, and movement constraints so viewers can reason about how space affects evacuation."
  },
  {
    id: "evacuation-behavior",
    title: "Evacuation Behavior",
    body: "Instead of focusing on one person at a time, EvacLogix is meant to show how groups move, slow down, and respond differently when many people are trying to leave at once."
  },
  {
    id: "hazard-context",
    title: "Hazard and Congestion Context",
    body: "The project frames evacuation in the presence of changing conditions such as crowding and fire spread, helping viewers see how safe routes can become more complex under pressure."
  }
]

export const howItWorksPanelsSection: StatementContent = {
  title: "High-Level Panels",
  body: "These panels explain the project in user-facing terms first, keeping the focus on what the simulation communicates rather than on engine-level implementation details."
}
