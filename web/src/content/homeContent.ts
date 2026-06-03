import type { Cta, HeroContent, HighlightItem, StatementContent } from "../types/content"
import { appRoutes } from "../utils/routes"

export const homePrimaryCta: Cta = {
  label: "Launch Demo",
  to: appRoutes.demo,
  variant: "primary"
}

export const homeSecondaryCta: Cta = {
  label: "Learn How It Works",
  to: appRoutes.howItWorks,
  variant: "secondary"
}

export const homeContent: HeroContent = {
  eyebrow: "Evacuation Simulation",
  title: "EvacLogix helps viewers explore how people evacuate buildings under pressure.",
  body: "EvacLogix is a presentation-focused simulation project that frames evacuation safety as a human problem first, then demonstrates how interactive simulation can help people understand movement, congestion, and response behavior inside buildings.",
  primaryCta: homePrimaryCta,
  secondaryCta: homeSecondaryCta
}

export const homeHighlights: HighlightItem[] = [
  {
    id: "agents",
    label: "Agent-Based Simulation",
    value: "Designed to explore how groups move through buildings during evacuation scenarios."
  },
  {
    id: "building-context",
    label: "Building-Focused Scenarios",
    value: "Structured around indoor spaces, exits, movement constraints, and route choice."
  },
  {
    id: "hazard-context",
    label: "Hazard and Congestion Context",
    value: "Intended to visualize how fire spread and crowding can influence evacuation behavior."
  }
]

export const homeProblemStatement: StatementContent = {
  eyebrow: "Problem",
  title: "Evacuation is difficult to reason about before an emergency happens.",
  body: "People rarely get to see how congestion, limited exits, and evolving hazards affect evacuation behavior until a crisis is already underway. That makes safety planning harder to communicate, evaluate, and improve."
}

export const homeSolutionStatement: StatementContent = {
  eyebrow: "Solution",
  title: "EvacLogix turns evacuation behavior into something visible and discussable.",
  body: "By presenting evacuation scenarios as an interactive simulation, the project gives viewers a clearer way to examine building response, movement patterns, and the tradeoffs that appear when many people must leave at once."
}

export const homeImpactStatement: StatementContent = {
  eyebrow: "Community Impact",
  title: "Built with a University of Washington and public-safety context in mind.",
  body: "The project is grounded in a campus and community safety framing: helping viewers think more concretely about evacuation routes, building layouts, and the design decisions that shape safe exits."
}

export const homeDemoPreview = {
  eyebrow: "Demo",
  title: "The simulation is the centerpiece of the presentation.",
  body: "The website introduces the mission, but the demo route is where viewers engage the embedded simulation directly. In the finished experience, controls remain inside Unity while the site frames what the audience should look for.",
  placeholderLabel: "Homepage simulation placeholder"
}

export const homeHighlightsSection: StatementContent = {
  eyebrow: "Highlights",
  title: "What EvacLogix Emphasizes",
  body: "A concise overview of the concepts the project is designed to surface during presentation."
}

export const homeMissionStatementSection: StatementContent = {
  eyebrow: "Mission",
  title: "Mission Statement",
  body: "Explore evacuation behavior through an interactive building-safety simulation."
}

export const homeScopeStatement: StatementContent = {
  eyebrow: "Scope",
  title: "Scope Note",
  body: "EvacLogix is a simulation experience, not a real-time emergency guidance system."
}
