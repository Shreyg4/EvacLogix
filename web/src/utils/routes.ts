import type { NavItem } from "../types/navigation"

export const appRoutes = {
  home: "/",
  demo: "/demo",
  howItWorks: "/how-it-works",
  architecture: "/architecture",
  userGuide: "/user-guide"
} as const

export const primaryNavItems: NavItem[] = [
  { label: "Home", to: appRoutes.home, end: true },
  { label: "Demo", to: appRoutes.demo },
  { label: "How It Works", to: appRoutes.howItWorks },
  { label: "Architecture", to: appRoutes.architecture },
  { label: "User Guide", to: appRoutes.userGuide }
]
