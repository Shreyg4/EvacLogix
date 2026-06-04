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
  eyebrow: "Project Overview",
  title: "EvacLogix helps viewers explore how people evacuate buildings under pressure.",
  body: "EvacLogix is a browser-based tool for modeling buildings and simulating how people leave them in an emergency. Draw a floor plan or import a blueprint, lay out walls, doors, stairs, and exits across multiple floors, then run agent-based evacuation and fire-spread simulations to see exactly where congestion and bottlenecks form — all without installing anything.",
  primaryCta: homePrimaryCta,
  secondaryCta: homeSecondaryCta
}

export const homeHighlights: HighlightItem[] = [
  {
    id: "agents",
    label: "Agent-Based Simulation",
    value:
      "NavMesh-driven evacuee agents route to the nearest exits, so you can watch crowds move and slow down in real time."
  },
  {
    id: "building-context",
    label: "Building-Focused Scenarios",
    value:
      "Author multi-floor buildings with walls, doors, stairs, exits, and teleporters — or import a blueprint and trace it to scale."
  },
  {
    id: "hazard-context",
    label: "Hazard and Congestion Context",
    value:
      "Add a spreading fire and watch how evolving hazards and crowding reshape which routes stay safe."
  }
]

export const homeProblemStatement: StatementContent = {
  eyebrow: "Problem",
  title: "Evacuation is difficult to reason about before an emergency happens.",
  body: "People rarely get to see how congestion, limited exits, and evolving hazards affect evacuation behavior until a crisis is already underway. That makes safety planning harder to communicate, evaluate, and improve."
}

export const homeSolutionStatement: StatementContent = {
  eyebrow: "Solution",
  title: "EvacLogix turns evacuation behavior into something you can build, run, and see.",
  body: "Instead of guessing, users model a real space and run an interactive simulation of it. Evacuee agents pathfind to exits while an optional fire spreads, making building response, movement patterns, and the tradeoffs of getting many people out at once visible and discussable."
}

export const homeImpactStatement: StatementContent = {
  eyebrow: "Community Impact",
  title: "Built for the University of Washington community.",
  body: "Because it runs in any browser, anyone at UW — students, instructors, event organizers, and facilities and safety staff — can model a classroom, venue, or building and reason concretely about evacuation routes, exit capacity, and the design decisions that shape safe exits."
}

export const homeDemoPreview = {
  eyebrow: "Demo",
  title: "The simulation is the centerpiece of the experience.",
  body: "The site introduces the project, but the Demo page is where you launch the embedded tool directly: build a layout, then run the evacuation. All editing and simulation controls live inside the Unity experience.",
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
