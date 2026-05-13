# EvacLogix Web

This folder contains the React + Vite + TypeScript presentation website for EvacLogix.

The website is intentionally separate from the Unity project so the web app can evolve without interfering with `Assets/`, `Packages/`, or other Unity-managed project files.

## What This Is

The site is a presentation-focused frontend with:
- a `Home` page for mission, goals, and team context
- a `Demo` page for the embedded Unity WebGL experience
- a `How It Works` page for a high-level explanation of the simulation

Right now, the Unity integration path is real, but if no WebGL build exists in `public/unity-build/`, the demo falls back gracefully to `Simulation unavailable`.

## Folder Notes

- `src/` contains the React application
- `public/unity-build/` is where Unity WebGL export files should go
- `Plan.md` tracks implementation phases
- `website-architecture-decisions.md` records the architecture decisions behind the site

## Requirements

You need:
- Node.js
- npm

The project was built and tested with:
- Node `v25.9.0`
- npm `11.13.0`

## Install

From the repo root:

```bash
cd web
npm install
```

## Run The Site

### macOS or Linux

From the repo root:

```bash
cd web
npm run dev
```

Vite will print a local URL, usually:

```text
http://localhost:5173/
```

### Windows PowerShell

From the repo root:

```powershell
cd web
npm run dev
```

### Windows Command Prompt

From the repo root:

```cmd
cd web
npm run dev
```

## Other Commands

Run tests:

```bash
cd web
npm test
```

Run browser tests with Playwright:

```bash
cd web
npm run test:e2e
```

Run browser tests in headed mode:

```bash
cd web
npm run test:e2e:headed
```

Build for production:

```bash
cd web
npm run build
```

Preview the production build:

```bash
cd web
npm run preview
```

Playwright runs against the production preview build automatically. The first time you set it up on a machine, install the Chromium browser used by the suite:

```bash
cd web
npx playwright install chromium
```

## Unity WebGL Build Placement

When a real Unity WebGL export is available, its web build files should be placed in:

```text
web/public/unity-build/
```

The current integration also expects a config file at:

```text
web/public/unity-build/unity-build-config.json
```

If those assets are missing, the `Demo` page will stay usable and show the fallback state instead of crashing.

## Current Status

- the website app runs
- tests pass
- the presentation pages exist
- the Unity embed boundary exists
- real Unity assets are not yet present in `public/unity-build/`
