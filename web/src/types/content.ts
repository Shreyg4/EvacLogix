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

export type TechStackItem = {
  id: string
  name: string
  role: string
}

export type ArchitectureLayer = {
  id: string
  name: string
  summary: string
  items: string[]
}

export type FlowStep = {
  id: string
  title: string
  detail: string
}

export type GuideStep = {
  id: string
  step: string
  title: string
  detail: string
}

export type AudienceItem = {
  id: string
  audience: string
  useCase: string
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
