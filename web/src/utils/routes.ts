import type { NavItem } from "../types/navigation"

export const appRoutes = {
  home: "/",
  demo: "/demo",
  howItWorks: "/how-it-works"
} as const

export const primaryNavItems: NavItem[] = [
  { label: "Home", to: appRoutes.home, end: true },
  { label: "Demo", to: appRoutes.demo },
  { label: "How It Works", to: appRoutes.howItWorks }
]
