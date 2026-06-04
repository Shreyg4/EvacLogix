import type { AudienceItem, GuideStep, HeroContent, StatementContent } from "../types/content"
import { appRoutes } from "../utils/routes"

export const userGuidePageContent: HeroContent = {
  eyebrow: "User Guide",
  title: "Using EvacLogix in the UW community.",
  body: "EvacLogix runs entirely in your web browser, so anyone in the University of Washington community can model a space and simulate an evacuation without installing anything. This guide walks through getting started, building a layout, and running a simulation.",
  primaryCta: {
    label: "Open the Demo",
    to: appRoutes.demo,
    variant: "primary"
  }
}

export const userGuideAudienceSection: StatementContent = {
  eyebrow: "Who It's For",
  title: "Who Can Use It",
  body: "The tool is built to be approachable for non-experts while still useful for people who think about building safety."
}

export const userGuideAudiences: AudienceItem[] = [
  {
    id: "students",
    audience: "Students & instructors",
    useCase:
      "Explore how layout, exits, and crowding affect evacuation as a hands-on safety and design learning tool."
  },
  {
    id: "rso",
    audience: "Event & RSO organizers",
    useCase: "Sketch a room or venue and sanity-check exit flow before hosting a large gathering."
  },
  {
    id: "facilities",
    audience: "Facilities & safety staff",
    useCase:
      "Import a floor plan, model real dimensions, and visualize bottlenecks to inform planning conversations."
  }
]

export const userGuideRequirementsSection: StatementContent = {
  eyebrow: "Before You Start",
  title: "What You Need",
  body: "A modern desktop browser (Chrome, Edge, Firefox, or Safari) with WebGL enabled, and a reasonably recent computer. The first load downloads the simulation build, so give it a moment on slower connections. A mouse is recommended for precise editing."
}

export const userGuideStepsSection: StatementContent = {
  eyebrow: "Walkthrough",
  title: "Getting Started",
  body: "Follow these steps from the Demo page to go from a blank canvas to a running evacuation."
}

export const userGuideSteps: GuideStep[] = [
  {
    id: "launch",
    step: "1",
    title: "Launch the tool",
    detail:
      "Open the Demo page and press the launch button. Wait for the build to load, then click into the frame so it receives your mouse and keyboard."
  },
  {
    id: "start-project",
    step: "2",
    title: "Start or import a project",
    detail:
      "Create a new project to draw from scratch, import a blueprint image to trace over, or load a previously exported project file."
  },
  {
    id: "calibrate",
    step: "3",
    title: "Set the scale",
    detail:
      "If you imported a blueprint, use the calibration tool to mark a known real-world distance so the building is sized correctly."
  },
  {
    id: "build",
    step: "4",
    title: "Build the layout",
    detail:
      "Use the tool palette to draw walls and place doors, stairs, and exits. Add more floors as needed and connect them with stairs or teleporters."
  },
  {
    id: "validate",
    step: "5",
    title: "Check for issues",
    detail:
      "Open the validation panel to catch gaps, unreachable rooms, or missing exits, and fix anything it highlights before simulating."
  },
  {
    id: "simulate",
    step: "6",
    title: "Run the simulation",
    detail:
      "Switch to simulation mode to spawn evacuee agents and optionally start a fire, then watch how people route to exits and where congestion builds."
  },
  {
    id: "save",
    step: "7",
    title: "Save your work",
    detail:
      "Export the project to a file to keep or share it, or export a preview image of your layout. Re-import the file anytime to continue."
  }
]

export const userGuideTipsSection: StatementContent = {
  eyebrow: "Tips",
  title: "Tips & Troubleshooting",
  body: "Keyboard shortcuts and undo/redo speed up editing — hover controls in the editor to discover them. If the simulation feels slow, reduce the number of agents or close other browser tabs. If the frame stops responding to input, click inside it again to refocus, and reload the page if a build fails to load."
}
