import type {
  ArchitectureLayer,
  FlowStep,
  HeroContent,
  StatementContent,
  TechStackItem
} from "../types/content"

export const technicalPageContent: HeroContent = {
  eyebrow: "Technical Documentation",
  title: "How EvacLogix is built.",
  body: "EvacLogix pairs a Unity (C#) simulation engine with a React presentation site. The engine compiles to WebGL so the full building editor and evacuation simulation run entirely in the browser, while this site frames the experience and serves the build. This page gives a high-level view of the architecture and the technology behind it."
}

export const techStackSection: StatementContent = {
  eyebrow: "Tech Stack",
  title: "Technology Stack",
  body: "The project is split into a Unity simulation engine and a web presentation layer, connected by a thin browser bridge."
}

export const techStackItems: TechStackItem[] = [
  {
    id: "unity",
    name: "Unity 6 (6000.4) + C#",
    role: "Simulation engine, building editor, rendering, and gameplay logic, built around a dependency-injected service architecture."
  },
  {
    id: "webgl",
    name: "Unity WebGL / IL2CPP",
    role: "Compiles the C# engine to WebAssembly so the full editor and simulation run in the browser with no install."
  },
  {
    id: "navmesh",
    name: "Unity AI Navigation (NavMesh)",
    role: "Builds walkable surfaces from the authored building and drives agent pathfinding toward exits."
  },
  {
    id: "burst",
    name: "Burst + Collections",
    role: "High-performance jobs and native containers used to keep agent and fire simulation responsive."
  },
  {
    id: "react",
    name: "React 19 + React Router",
    role: "Presentation site that hosts the embedded build, navigation, and supporting documentation."
  },
  {
    id: "vite",
    name: "Vite 7 + TypeScript",
    role: "Build tooling and type-safe, content-driven UI for the website."
  },
  {
    id: "bridge",
    name: "Browser File Bridge",
    role: "JavaScript ↔ Unity interop for importing blueprint images and importing/exporting project JSON."
  },
  {
    id: "hosting",
    name: "Netlify",
    role: "Static hosting and continuous deploys for the site and the bundled Unity WebGL build."
  }
]

export const architectureSection: StatementContent = {
  eyebrow: "Architecture",
  title: "System Architecture",
  body: "From the browser down to the simulation core, EvacLogix is organized into clear layers. The web shell loads the Unity WebGL runtime, which hosts a modular Sandbox application split into authoring, data, simulation, and rendering responsibilities."
}

export const architectureLayers: ArchitectureLayer[] = [
  {
    id: "web",
    name: "Web Presentation Layer",
    summary: "React + Vite site served from Netlify.",
    items: ["Pages & navigation", "Unity embed + launch overlay", "Per-target build profiles"]
  },
  {
    id: "runtime",
    name: "Unity WebGL Runtime",
    summary: "C# engine compiled to WebAssembly via IL2CPP.",
    items: ["loader.js + .wasm + .data", "Browser file bridge (JS interop)", "URP rendering"]
  },
  {
    id: "app",
    name: "Sandbox Application Modules",
    summary: "Dependency-injected services that make up the editor.",
    items: [
      "Authoring (walls, objects, tools, undo/redo)",
      "Data (project model, serialization, migrations, validation)",
      "Infrastructure (files, blueprint import, fire, floors, scale)",
      "UI & Rendering (HUD, panels, overlays, gizmos)"
    ]
  },
  {
    id: "sim",
    name: "Simulation Core",
    summary: "Turns an authored building into a running evacuation.",
    items: [
      "NavMesh build from layout",
      "Route graph + agent pathfinding",
      "Evacuee agents & crowd movement",
      "Fire cell spread model"
    ]
  }
]

export const dataFlowSection: StatementContent = {
  eyebrow: "Data Flow",
  title: "From Blueprint to Simulation",
  body: "A typical session moves through a clear pipeline: bring in a layout, calibrate it to real-world scale, author the building, validate it, then run the evacuation and review results."
}

export const dataFlowSteps: FlowStep[] = [
  {
    id: "import",
    title: "Import / New",
    detail: "Start from scratch or import a blueprint image or saved project JSON."
  },
  {
    id: "calibrate",
    title: "Calibrate Scale",
    detail: "Set a known distance so the model matches real-world dimensions."
  },
  {
    id: "author",
    title: "Author Building",
    detail: "Place walls, doors, stairs, exits, and teleporters across floors."
  },
  {
    id: "validate",
    title: "Validate",
    detail: "Structural checks flag gaps, unreachable areas, and missing exits."
  },
  {
    id: "simulate",
    title: "Simulate",
    detail: "Spawn agents and optional fire, then run the evacuation in real time."
  },
  {
    id: "review",
    title: "Review & Export",
    detail: "Watch congestion form, then export the project or a preview image."
  }
]

export const technicalScopeNote: StatementContent = {
  eyebrow: "Note",
  title: "Scope",
  body: "EvacLogix is a modeling and simulation tool for exploration and planning, not a validated life-safety or real-time emergency guidance system."
}
