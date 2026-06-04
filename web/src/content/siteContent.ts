import type { SiteContent } from "../types/content"
import { appRoutes } from "../utils/routes"

export const siteContent: SiteContent = {
  brandName: "EvacLogix",
  brandTagline: "Interactive building & evacuation simulation",
  mission: {
    short: "Explore evacuation behavior through an interactive building-safety simulation.",
    extended:
      "EvacLogix is a browser-based tool for modeling buildings and simulating how people evacuate them. Users can sketch a floor plan or import a blueprint, lay out walls, doors, stairs, and exits across multiple floors, then run agent-based evacuation and fire-spread simulations to see where congestion and bottlenecks form."
  },
  navigation: [
    { label: "Home", to: appRoutes.home, end: true },
    { label: "Demo", to: appRoutes.demo },
    { label: "How It Works", to: appRoutes.howItWorks },
    { label: "Architecture", to: appRoutes.architecture },
    { label: "User Guide", to: appRoutes.userGuide }
  ],
  footer: {
    attribution: "EvacLogix presentation website.",
    context:
      "A React, Vite, and TypeScript site hosting the Unity WebGL building and evacuation simulation."
  },
  status: {
    scopeNote: "EvacLogix is a simulation experience, not a real-time emergency guidance system."
  }
}
