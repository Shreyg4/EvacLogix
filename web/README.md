# EvacLogix Web

This folder contains the React + Vite + TypeScript website that hosts the Unity WebGL experience for EvacLogix.

The web app is intentionally separate from the Unity project so the website can evolve without interfering with `Assets/`, `Packages/`, or other Unity-managed files.

## What This Site Hosts

The site currently supports multiple Unity app targets through named app profiles.

Current profiles:
- `evac-sim`
- `sandbox-editor`

Relevant source:
- [web/src/content/unityAppProfiles.ts](/home/vincenth/Documents/Projects/385/EvacLogix/web/src/content/unityAppProfiles.ts:1)

Current behavior:
- the normal `Demo` page defaults to `evac-sim`
- `sandbox-editor` is intentionally hidden from main navigation
- `sandbox-editor` can still be opened directly with:
  - `/demo?app=sandbox-editor`

## Folder Layout

- `src/` contains the React application
- `public/unity-build/` contains Unity WebGL build outputs and per-target config files
- `e2e/` contains Playwright browser tests
- `Plan.md` tracks the website-side implementation plan

Current Unity build locations:
- `web/public/unity-build/evac-sim/`
- `web/public/unity-build/sandbox-editor/`

Each Unity target is expected to have its own:
- build artifacts (`.loader.js`, `.data`, `.framework.js`, `.wasm`)
- `unity-build-config.json`

## Requirements

You need:
- Node.js
- npm
- a Unity WebGL build exported from the Unity project

The web app was recently built and tested with:
- Node `v25.9.0`
- npm `11.13.0`

## Install

### macOS or Linux

From the repo root:

```bash
cd web
npm install
```

### Windows PowerShell

From the repo root:

```powershell
cd web
npm install
```

### Windows Command Prompt

From the repo root:

```cmd
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

Vite will print a local URL, usually:

```text
http://localhost:5173/
```

Useful routes:
- default demo target:
  - `http://localhost:5173/demo`
- sandbox editor target:
  - `http://localhost:5173/demo?app=sandbox-editor`

## Useful Commands

Run unit/integration tests:

```bash
cd web
npm test
```

Run Playwright browser tests:

```bash
cd web
npm run test:e2e
```

Run Playwright browser tests in headed mode:

```bash
cd web
npm run test:e2e:headed
```

Build production assets:

```bash
cd web
npm run build
```

Preview the production build:

```bash
cd web
npm run preview
```

Install Playwright Chromium on a new machine:

```bash
cd web
npx playwright install chromium
```

## How Unity Connects To The Site

For `sandbox-editor`, the website does more than just embed a canvas.

The site also provides a browser bridge for:
- importing blueprint images
- importing project JSON
- exporting project JSON

Relevant files:
- [web/src/services/unityBrowserBridge.ts](/home/vincenth/Documents/Projects/385/EvacLogix/web/src/services/unityBrowserBridge.ts:1)
- [web/src/types/unityBridge.ts](/home/vincenth/Documents/Projects/385/EvacLogix/web/src/types/unityBridge.ts:1)
- [web/src/components/unity/UnityEmbed.tsx](/home/vincenth/Documents/Projects/385/EvacLogix/web/src/components/unity/UnityEmbed.tsx:1)

The bridge is enabled per Unity app profile. Right now:
- `evac-sim` has no browser file commands
- `sandbox-editor` allows:
  - `ImportBlueprintImage`
  - `ImportProjectJson`
  - `ExportProjectJson`

## Unity Export Settings For Sandbox Editor

When building `sandbox-editor` for the website, these settings are recommended:

- include `Scenes/SandboxEditor`
- exclude `Scenes/Bootstrap` for the browser-targeted sandbox build unless bootstrap explicitly loads the sandbox scene
- `Compression Format: Disabled`
- `Texture Compression: Default`

Why:
- compressed `.br` builds caused local dev serving friction
- the sandbox target works more reliably with a direct `SandboxEditor` startup scene

## Exporting A New Sandbox Build

These instructions assume you already built a new Unity WebGL output folder such as:
- `EvacLogixSandbox0.012`

### Step 1: Build From Unity

Inside Unity:
1. Switch the target platform to `WebGL`.
2. Make sure the correct scene list is selected for the sandbox build.
3. Use the recommended WebGL settings above.
4. Build to a versioned folder at the repo root, for example:
   - `EvacLogixSandbox0.012/`

### Step 2: Sanity-Check The Build Standalone

Before wiring it into the website, test the build by itself.

#### macOS or Linux

From the repo root:

```bash
cd EvacLogixSandbox0.012
python3 -m http.server 8000
```

Open:

```text
http://localhost:8000
```

#### Windows PowerShell

From the repo root:

```powershell
cd EvacLogixSandbox0.012
py -m http.server 8000
```

Open:

```text
http://localhost:8000
```

#### Windows Command Prompt

From the repo root:

```cmd
cd EvacLogixSandbox0.012
py -m http.server 8000
```

Open:

```text
http://localhost:8000
```

If the standalone build is blank or broken, fix the Unity build first before copying anything into the website.

### Step 3: Copy The Build Into The Website

Replace `EvacLogixSandbox0.012` below with the actual build folder name you exported.

#### macOS or Linux

From the repo root:

```bash
rm -f web/public/unity-build/sandbox-editor/*
cp EvacLogixSandbox0.012/Build/* web/public/unity-build/sandbox-editor/
cat > web/public/unity-build/sandbox-editor/unity-build-config.json <<'EOF'
{
  "loaderUrl": "/unity-build/sandbox-editor/EvacLogixSandbox0.012.loader.js",
  "dataUrl": "/unity-build/sandbox-editor/EvacLogixSandbox0.012.data",
  "frameworkUrl": "/unity-build/sandbox-editor/EvacLogixSandbox0.012.framework.js",
  "codeUrl": "/unity-build/sandbox-editor/EvacLogixSandbox0.012.wasm",
  "productName": "Sandbox Editor"
}
EOF
```

#### Windows PowerShell

From the repo root:

```powershell
Remove-Item web/public/unity-build/sandbox-editor/* -Force
Copy-Item EvacLogixSandbox0.012/Build/* web/public/unity-build/sandbox-editor/
@'
{
  "loaderUrl": "/unity-build/sandbox-editor/EvacLogixSandbox0.012.loader.js",
  "dataUrl": "/unity-build/sandbox-editor/EvacLogixSandbox0.012.data",
  "frameworkUrl": "/unity-build/sandbox-editor/EvacLogixSandbox0.012.framework.js",
  "codeUrl": "/unity-build/sandbox-editor/EvacLogixSandbox0.012.wasm",
  "productName": "Sandbox Editor"
}
'@ | Set-Content web/public/unity-build/sandbox-editor/unity-build-config.json
```

#### Windows Command Prompt

From the repo root:

```cmd
del /Q web\public\unity-build\sandbox-editor\*
copy EvacLogixSandbox0.012\Build\* web\public\unity-build\sandbox-editor\
```

Then create or edit:

```text
web/public/unity-build/sandbox-editor/unity-build-config.json
```

with:

```json
{
  "loaderUrl": "/unity-build/sandbox-editor/EvacLogixSandbox0.012.loader.js",
  "dataUrl": "/unity-build/sandbox-editor/EvacLogixSandbox0.012.data",
  "frameworkUrl": "/unity-build/sandbox-editor/EvacLogixSandbox0.012.framework.js",
  "codeUrl": "/unity-build/sandbox-editor/EvacLogixSandbox0.012.wasm",
  "productName": "Sandbox Editor"
}
```

### Step 4: Run The Site And Verify

#### macOS or Linux

```bash
cd web
npm run dev
```

#### Windows PowerShell

```powershell
cd web
npm run dev
```

#### Windows Command Prompt

```cmd
cd web
npm run dev
```

Then open:

```text
http://localhost:5173/demo?app=sandbox-editor
```

## Minimum Smoke Test For A New Sandbox Build

After copying a new build into the site, verify:
- sandbox launches in the website
- `Create Default Project` works
- `Import Blueprint Image` opens the file picker
- canceling the picker does not permanently disable controls
- importing a blueprint actually renders it
- opacity slider works
- background size slider works
- `Import JSON` opens the file picker
- `Export JSON` downloads a file

## Current Import Limits

Current browser-enforced limits:
- blueprint image import: `25 MB`
- project JSON import: `5 MB`

Those limits are defined in:
- [Assets/Scripts/Sandbox/Infrastructure/SandboxFileActionContracts.cs](/home/vincenth/Documents/Projects/385/EvacLogix/Assets/Scripts/Sandbox/Infrastructure/SandboxFileActionContracts.cs:80)

## Common Problems

### The sandbox route shows fallback instead of Unity

Check:
- the files actually exist in `web/public/unity-build/sandbox-editor/`
- `unity-build-config.json` matches the exact exported filenames
- the build was copied into the correct app target folder

### Unity loads standalone but not in the site

Check:
- `unity-build-config.json`
- that the sandbox route is:
  - `/demo?app=sandbox-editor`
- browser console output from the page

### Import works but cancel leaves buttons disabled

This was previously a browser bridge issue. The current website bridge should recover cleanly after file-picker cancel, but this is worth re-testing on new browsers.

### Export JSON says the bridge is unavailable

That was previously caused by a Unity WebGL backend export bug. Make sure the site is using the newest WebGL build after any Unity-side bridge/export fix.

### Firefox shader warnings appear

Those URP/WebGL warnings have been seen in Firefox. If the sandbox still renders and functions, they are not necessarily fatal, but Firefox remains a good browser to keep testing because it tends to expose WebGL issues early.

## Current Status

At the time of writing:
- the website runs
- tests pass
- the `sandbox-editor` WebGL target is integrated into the site
- browser-native import/export flows exist for the sandbox editor
- the hidden sandbox route is:
  - `/demo?app=sandbox-editor`
