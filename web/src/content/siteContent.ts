import type { SiteContent } from "../types/content";
import { appRoutes } from "../utils/routes";

export const siteContent: SiteContent = {
  brandName: "EvacLogix",
  brandTagline: "Evacuation simulation presentation site",
  mission: {
    short: "Explore evacuation behavior through an interactive building-safety simulation.",
    extended:
      "EvacLogix is a presentation-focused simulation project that helps viewers understand evacuation dynamics, safety considerations, and building-response context."
  },
  navigation: [
    { label: "Home", to: appRoutes.home, end: true },
    { label: "Demo", to: appRoutes.demo },
    { label: "How It Works", to: appRoutes.howItWorks }
  ],
  footer: {
    attribution: "EvacLogix presentation website.",
    context: "Built as a React, Vite, and TypeScript companion to the Unity evacuation simulation."
  },
  status: {
    scopeNote: "EvacLogix is a simulation experience, not a real-time emergency guidance system."
  }
};
