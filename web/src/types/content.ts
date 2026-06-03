import type { NavItem } from "./navigation"

export type Cta = {
  label: string
  to: string
  variant: "primary" | "secondary"
}

export type MissionStatement = {
  short: string
  extended?: string
}

export type HighlightItem = {
  id: string
  label: string
  value?: string
}

export type TeamMember = {
  name: string
  role: string
  responsibility: string
}

export type HeroContent = {
  eyebrow?: string
  title: string
  body: string
  primaryCta?: Cta
  secondaryCta?: Cta
}

export type DemoInstruction = {
  id: string
  text: string
}

export type ExplanatoryPanel = {
  id: string
  title: string
  body: string
}

export type StatementContent = {
  eyebrow?: string
  title: string
  body: string
}

export type FooterContent = {
  attribution: string
  context: string
}

export type StatusCopy = {
  scopeNote: string
  demoAvailabilityNote?: string
}

export type SiteContent = {
  brandName: string
  brandTagline: string
  mission: MissionStatement
  navigation: NavItem[]
  footer: FooterContent
  status: StatusCopy
}
