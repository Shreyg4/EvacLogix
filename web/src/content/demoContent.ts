import type { HeroContent, StatementContent } from "../types/content"
import { getDefaultUnityAppProfile } from "./unityAppProfiles"

export const demoPageContent: HeroContent = {
  eyebrow: "Demo",
  title: "Run the EvacLogix building and evacuation simulation.",
  body: "Launch the embedded tool below to model a building and simulate an evacuation directly in your browser. The site frames the experience, but all editing and simulation controls live inside the embedded app."
}

export const demoGuidanceContent: StatementContent = {
  eyebrow: "Notes",
  title: "Demo Route Guidance",
  body: "The website provides context, launch controls, and fallback behavior if the build cannot load. The full editor and simulation run inside the embedded experience. For a step-by-step walkthrough, see the User Guide."
}

export const demoInstructions: string[] = [
  "Press play to attempt launching the embedded simulation.",
  "Click inside the frame so it receives your mouse and keyboard.",
  "Build a layout (or import a blueprint), then switch to simulation mode to run the evacuation.",
  "New here? See the User Guide for a step-by-step walkthrough."
]

export const defaultDemoProfile = getDefaultUnityAppProfile()
