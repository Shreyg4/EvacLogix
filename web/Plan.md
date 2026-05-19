# EvacLogix Website Implementation Plan

Project: EvacLogix  
Scope: React + Vite + TypeScript website for presentation-day use, with embedded Unity WebGL as a contained enhancement  
Primary reference: [website-architecture-decisions.md](./website-architecture-decisions.md)

Status note:
All implementation phases should be marked with `[PLANNED]` until completed. Replace the phase prefix with `[DONE]` when a phase is fully complete and verified.

## Purpose

This plan converts the architecture decisions into a concrete, multi-phase implementation strategy optimized for an LLM coding agent.

The main goals are:
- preserve separation of concerns between website presentation and Unity simulation
- produce a polished, presentation-ready multi-page website
- keep the site meaningful even if Unity is unavailable
- make implementation deterministic, auditable, and testable

## Non-Negotiable Constraints

- Unity remains the source of truth for simulation controls, map switching, restarting, and editor behavior.
- The website must never become responsible for Unity game state.
- The website must still function as a complete presentation artifact without Unity.
- The embedded game must be isolated behind a single integration boundary.
- Major website copy must live in a centralized content layer.
- The site must use a shared shell and real routing from the start.

## Final Product Shape

The intended first complete website should provide:
- `Home` page
- `Demo` page
- `How It Works` page
- shared header and footer
- centralized content definitions
- reusable section components
- isolated `UnityEmbed` component
- graceful Unity fallback state
- backup Unity Play link

## Canonical Feature Checklist

This section is the source-of-truth feature inventory for implementation and review.

### Site-wide features

- project name displayed consistently as `EvacLogix`
- shared page shell across all routes
- minimal top-level navigation
- text-first brand treatment with room for a future mark
- meaningful but restrained footer
- clear modern presentation tone
- one shared visual theme with page-specific emphasis
- desktop-first layout that does not obviously break on smaller screens
- centralized mission statement reused across pages
- subtle status or scope framing only where useful
- brief scope note clarifying that the project is a simulation, not a real emergency guidance system

### Home page features

- human problem first narrative
- polished pitch-deck style presentation
- strong central claim
- primary CTA to `Demo`
- secondary CTA to `How It Works`
- UW and community impact content prominently visible
- qualitative highlights strip
- strong visual centerpiece
- blank or styled placeholder visual support until real assets exist
- full team section with names, roles, and responsibility split

### Demo page features

- dominant black embed frame
- play or launch interaction
- all controls owned by Unity, not the website
- concise framing text explaining what to do and where controls live
- graceful `Simulation unavailable` fallback state
- secondary Unity Play backup link
- page remains useful and readable without Unity

### How It Works page features

- user-facing, high-level explanation
- a few explanatory panels rather than a detailed systems spec
- calmer, more informational tone than `Home`
- navigation label shown as `How It Works`

### Architecture features

- real router from the start
- content separated from layout
- Unity integration isolated behind `UnityEmbed`
- reusable section system
- manual Unity WebGL build copy into `public/unity-build/`
- no unfinished or roadmap-style features presented on the final site unless actually completed

## Recommended Folder Architecture

The website should evolve toward this structure:

```text
web/
  e2e/
    app.spec.ts
  public/
    unity-build/
      .gitkeep
      # later: Unity WebGL export output
  src/
    app/
      App.tsx
      main.tsx
      router.tsx
    components/
      layout/
        AppShell.tsx
        Header.tsx
        Footer.tsx
        PageHeader.tsx
      sections/
        HeroSection.tsx
        StatementSection.tsx
        HighlightsSection.tsx
        DemoPreviewSection.tsx
        TeamSection.tsx
        InfoPanel.tsx
      unity/
        UnityEmbed.tsx
        UnityFallback.tsx
        UnityLaunchOverlay.tsx
        unity.types.ts
      ui/
        Button.tsx
        Section.tsx
        Card.tsx
        Tag.tsx
        Eyebrow.tsx
    content/
      siteContent.ts
      homeContent.ts
      demoContent.ts
      howItWorksContent.ts
      teamContent.ts
    pages/
      HomePage.tsx
      DemoPage.tsx
      HowItWorksPage.tsx
      NotFoundPage.tsx
    styles/
      globals.css
      tokens.css
      utilities.css
    types/
      content.ts
      navigation.ts
      unity.ts
    utils/
      routes.ts
      classNames.ts
      assertions.ts
    tests/
      app-shell.test.tsx
      routing.test.tsx
      home-page.test.tsx
      demo-page.test.tsx
      how-it-works-page.test.tsx
      unity-embed.test.tsx
      content-shape.test.ts
  README.md
  website-architecture-decisions.md
  Plan.md
  playwright.config.ts
```

## Separation of Concerns Model

This is the most important implementation rule.

### 1. Content Layer

Responsibility:
- mission statement
- page copy
- section data
- team data
- navigation labels
- CTA labels
- fallback messages

Must not contain:
- JSX-heavy layout logic
- Unity launch logic
- DOM state

Implementation guidance:
- prefer typed plain objects
- one exported content object per page domain
- centralize reusable phrases such as the mission statement
- treat this layer as the only approved location for major copy and structured page data

### 2. Layout Layer

Responsibility:
- consistent page shell
- header, footer, spacing, container widths
- section wrappers
- visual hierarchy

Must not contain:
- page-specific copy literals beyond placeholder defaults
- Unity state handling
- route decision logic beyond composition

Implementation guidance:
- shared shell owns global structure
- pages assemble sections but do not micromanage styles inline
- visual system should be driven by CSS tokens

### 3. Page Layer

Responsibility:
- compose content + reusable sections
- define the narrative order for each route
- connect route-level content to route-level layout

Must not contain:
- low-level UI primitives
- embedded Unity implementation details
- hardcoded long-form content that belongs in `content/`

Implementation guidance:
- page files should read like composition maps
- page files may choose section order and prop wiring, but not define deep content structures

### 4. Unity Integration Layer

Responsibility:
- render the black demo frame
- own launch/play interaction
- manage loading, unavailable, and future success states
- optionally expose a minimal typed interface for future WebGL hook-up

Must not contain:
- mission copy
- page layout concerns
- simulation control definitions

Implementation guidance:
- keep all Unity-specific state inside `src/components/unity/`
- the page should pass content and configuration, not manipulate internal Unity behavior
- the initial implementation may intentionally stop at launch overlay + unavailable fallback
- when real WebGL integration exists, only this layer may know Unity artifact details

### 5. UI Primitive Layer

Responsibility:
- buttons, cards, labels, generic wrappers

Must not contain:
- page meaning
- route-specific copy
- Unity state semantics

## Explicit Type Contracts

These are the minimum recommended content contracts. Agents should implement these or equivalent typed interfaces before building pages.

```ts
type NavItem = {
  label: "Home" | "Demo" | "How It Works";
  to: string;
};

type Cta = {
  label: string;
  to: string;
  variant: "primary" | "secondary";
};

type MissionStatement = {
  short: string;
  extended?: string;
};

type HighlightItem = {
  id: string;
  label: string;
  value?: string;
};

type TeamMember = {
  name: string;
  role: string;
  responsibility: string;
};

type HeroContent = {
  eyebrow?: string;
  title: string;
  body: string;
  primaryCta: Cta;
  secondaryCta: Cta;
};

type DemoInstruction = {
  id: string;
  text: string;
};

type ExplanatoryPanel = {
  id: string;
  title: string;
  body: string;
};

type FooterContent = {
  attribution: string;
  context: string;
};

type StatusCopy = {
  scopeNote: string;
  demoAvailabilityNote?: string;
};
```

## Unity WebGL Artifact Contract

This is the expected contract for Phase 8. The plan should assume this exact shape unless the Unity export workflow later proves otherwise.

### Expected file location

- all Unity build files live under `web/public/unity-build/`
- the website must not read Unity files from anywhere else

### Required runtime inputs

The real `UnityEmbed` implementation should receive or derive:
- loader script path
- data file path
- framework script path
- wasm file path
- product title

### Recommended configuration shape

```ts
type UnityBuildConfig = {
  loaderUrl: string;
  dataUrl: string;
  frameworkUrl: string;
  codeUrl: string;
  companyName?: string;
  productName: string;
  productVersion?: string;
};
```

### Artifact detection rule

- if required URLs are absent from config, `UnityEmbed` must enter `unavailable`
- if loader injection fails, `UnityEmbed` must enter `unavailable`
- if Unity instance creation rejects or times out, `UnityEmbed` must enter `unavailable`
- the route itself must never crash because Unity assets are missing

### State completion rule

`ready` means:
- loader loaded successfully
- Unity instance initialized successfully
- the frame is actively hosting the Unity canvas

## Multi-Phase Implementation Plan

## [DONE] Phase 0: Scaffold and Tooling

Objective:
- establish the website app without touching Unity project internals

Deliverables:
- initialize Vite React TypeScript app in `web/`
- add router dependency
- add test runner and React Testing Library
- add linting and formatting if desired
- preserve `public/unity-build/`

Recommended packages:
- `react`
- `react-dom`
- `react-router-dom`
- `vitest`
- `@testing-library/react`
- `@testing-library/jest-dom`
- `@testing-library/user-event`
- `jsdom`

Implementation notes:
- keep package setup standard and unsurprising
- avoid adding state libraries
- avoid adding CSS frameworks
- avoid introducing server dependencies

Definition of done:
- app boots
- router renders
- tests can run

Tests:
- smoke test that app renders shell
- smoke test that each route resolves

## [DONE] Phase 1: Core App Shell

Objective:
- create the stable structural frame of the site

Deliverables:
- `AppShell`
- `Header`
- `Footer`
- route configuration
- base global styles and design tokens

Implementation solution:
- `AppShell` wraps all routes
- header contains `EvacLogix`, nav links, and future-safe brand slot
- footer contains subtle attribution and project context
- visual tokens define colors, spacing, type scale, and surface treatments

Recommended design direction:
- safety/public-service tone
- bold but controlled presentation style
- one shared theme with route-level emphasis

Definition of done:
- all pages render inside one consistent shell
- active navigation works
- shell remains readable without any Unity integration

Tests:
- nav renders expected links
- footer renders attribution text
- route changes preserve shell

## [DONE] Phase 2: Central Content System

Objective:
- prevent copy drift and keep content editable without rewriting components

Deliverables:
- typed content schema
- `siteContent.ts`
- page-specific content files
- route-to-content mapping rules

Implementation solution:
- define interfaces for:
  - mission statement
  - nav items
  - CTA pair
  - team members
  - high-level panels
  - demo guidance
  - fallback text
  - footer attribution
  - scope note
  - status note
  - page headers
- content files should export plain objects only

Recommended minimum content files:

- `siteContent.ts`
  - brand name
  - mission statement
  - navigation
  - footer
  - scope note
- `homeContent.ts`
  - hero
  - problem statement
  - solution statement
  - highlights
  - demo teaser
- `demoContent.ts`
  - page intro
  - instructions
  - fallback copy
  - backup link label
- `howItWorksContent.ts`
  - page header
  - explanatory panels
- `teamContent.ts`
  - team list

Recommended content shape:

```ts
type Cta = {
  label: string;
  to: string;
  variant: "primary" | "secondary";
};

type TeamMember = {
  name: string;
  role: string;
  responsibility: string;
};
```

Definition of done:
- pages can be rendered from centralized content
- no large body copy remains trapped inside page components

Tests:
- content objects satisfy type constraints
- required page fields are present
- navigation labels remain synchronized with routes
- mission statement reuse is possible without copy duplication
- scope and status text fields are present where required

## [DONE] Phase 3: Reusable Section System

Objective:
- encode the visual/narrative system into reusable building blocks

Deliverables:
- `HeroSection`
- `StatementSection`
- `HighlightsSection`
- `DemoPreviewSection`
- `TeamSection`
- generic `InfoPanel`

Implementation solution:
- each section accepts structured props from content
- sections remain presentation-focused
- avoid making sections too abstract too early

Section behavior guidance:
- `HeroSection` supports primary and secondary CTA
- `HighlightsSection` supports qualitative highlights first
- `DemoPreviewSection` supports placeholder imagery
- `TeamSection` supports names, roles, and responsibility split

Definition of done:
- `HomePage` can be composed almost entirely from sections
- each section is reusable without page-specific hacks

Tests:
- each section renders required props
- CTA buttons render correct destinations
- highlight items render in stable order

## [DONE] Phase 4: Home Page

Objective:
- build the persuasive, presentation-first landing experience

Required narrative order:
- problem
- solution
- demo invitation
- team

Deliverables:
- strong mission-centered hero
- UW/community impact treatment
- highlight strip
- visual centerpiece placeholder
- team section
- explicit scope note placement

Implementation solution:
- open with human safety problem
- introduce EvacLogix as the solution
- drive the user toward `Demo`
- keep the tone confident and modern
- include only minimal scope framing
- include the short shared mission statement in a visibly important place
- include a restrained scope note near supporting content, not as the hero centerpiece

Important caution:
- avoid dashboard-like overload
- avoid speculative feature promises
- avoid deep technical jargon here

Definition of done:
- homepage tells the story without requiring the demo
- homepage can still persuade if Unity is absent

Tests:
- primary CTA points to `Demo`
- secondary CTA points to `How It Works` or equivalent deeper context
- team section renders all required member fields
- UW or community impact content is present
- highlight strip uses non-empty qualitative entries
- homepage includes scope note in a non-dominant position

## [DONE] Phase 5: Demo Page and Unity Boundary

Objective:
- create the route that hosts the embedded experience without letting Unity concerns leak into the rest of the app

Deliverables:
- `DemoPage`
- `UnityEmbed`
- `UnityLaunchOverlay`
- `UnityFallback`

Required behavior for initial implementation:
- show a dominant black frame
- show a play or launch control
- on launch attempt, transition into a controlled state
- if Unity is not integrated, surface `Simulation unavailable` with calm explanation
- show a secondary Unity Play backup link
- provide concise instructions about controls and what to do

Implementation solution:
- model embed state as a local finite state:
  - `idle`
  - `launching`
  - `ready`
  - `unavailable`
- initial implementation can intentionally transition from `idle` to `launching` to `unavailable`
- the future real loader should replace only the internal state handling inside `UnityEmbed`

Suggested interface:

```ts
type UnityEmbedProps = {
  title: string;
  instructions: string[];
  fallbackMessage: string;
  unavailableExplanation?: string;
  backupHref?: string;
  launchLabel?: string;
};
```

Important caution:
- do not put Unity launch state in page-level global state
- do not let the website expose simulation controls
- do not let the fallback state crash the route

Definition of done:
- demo page works as a polished standalone presentation surface
- Unity integration remains isolated
- fallback behavior is intentional and readable

Tests:
- launch button is visible on first render
- clicking launch transitions away from idle state
- unavailable state renders expected message
- backup link is present and secondary
- guidance text remains visible regardless of Unity state
- no website-side simulation control widgets are rendered outside the Unity frame
- launch control is keyboard-activatable

## [DONE] Phase 6: How It Works Page

Objective:
- explain the project in high-level, judge-friendly language

Deliverables:
- dedicated route labeled `How It Works`
- a few high-level explanatory panels
- stable route path and stable nav label mapping

Implementation solution:
- organize content into broad conceptual sections such as:
  - building modeling
  - simulated evacuation behavior
  - hazard and congestion context
- use user-facing language first
- support with light technical terms only where helpful

Important caution:
- do not turn this into a systems spec
- do not overpromise features not finished by presentation day

Definition of done:
- page stands on its own
- page complements, rather than duplicates, the home page

Tests:
- route title and navigation label stay aligned
- expected explanatory panels render
- technical copy remains high level and user-facing

## [DONE] Phase 7: Visual Polish and Presentation Pass

Objective:
- elevate the site from structurally correct to presentation-ready

Deliverables:
- consistent visual hierarchy
- motion where useful
- refined typography
- polished placeholders
- page emphasis differences without theme drift

Implementation solution:
- use CSS tokens rather than hardcoded values
- keep animations sparse and meaningful
- let the `Demo` page visually recede around the frame
- let the `Home` page carry the strongest persuasive composition
- preserve coherent typography and spacing across all pages

Important caution:
- do not add flashy effects that compete with clarity
- do not make the demo page visually louder than the embedded frame

Definition of done:
- site feels deliberate, not scaffolded
- all pages look like one product

Tests:
- no formal visual tests required initially
- manual review checklist should verify spacing, contrast, and route cohesion
- manual review should verify keyboard focus visibility and readable content hierarchy

## [DONE] Phase 8: Real Unity Integration Swap-In

Objective:
- replace placeholder-unavailable behavior inside `UnityEmbed` with actual WebGL launch logic when available

Deliverables:
- loader logic bound to actual Unity export
- successful render state
- fallback retained

Implementation solution:
- keep external `UnityEmbed` props stable
- swap internal launch logic to load Unity artifacts from `public/unity-build/`
- preserve fallback path if assets are missing or load fails
- read asset URLs from a typed config object rather than hardcoding scattered strings
- fail fast into `unavailable` if config is incomplete
- keep backup link visible in failure state

Important caution:
- this phase must not force redesign of pages, content, or shell
- if major non-Unity components need changes here, the boundary was not clean enough

Definition of done:
- real embed can launch from the same component boundary
- graceful fallback still works

Tests:
- config-shape validation if feasible
- asset existence check if feasible
- ready state renders frame content without route errors
- unavailable state still works when assets are missing
- timeout or rejected loader path still lands in controlled fallback

## [DONE] Phase 9: Playwright End-to-End Coverage

Objective:
- add committed browser-level verification for the presentation site without expanding scope into noisy visual regression testing

Deliverables:
- Playwright dependency
- `playwright.config.ts`
- `e2e/app.spec.ts`
- package scripts for browser test execution
- README instructions for Playwright usage

Implementation solution:
- run against the production preview build rather than the Vite dev server
- auto-start the preview server from Playwright config
- use Chromium only for the first committed suite
- save screenshots, traces, and video on failure only
- cover high-value route behavior and the real current demo fallback path
- include one smaller viewport sanity check in addition to the primary desktop path
- include lightweight accessibility smoke coverage through landmarks and visible headings

Important caution:
- do not turn this phase into a full visual regression system
- do not make Playwright replace the faster unit and route-level Vitest suite
- do not couple browser tests to real Unity assets being present

Definition of done:
- Playwright can run with one command
- the suite verifies homepage flow, `How It Works` routing, and demo fallback behavior
- the suite runs against the production preview build successfully
- browser test setup is documented in `README.md`

Tests:
- homepage loads with banner, navigation, main content, footer, and CTA flow
- `How It Works` route is reachable and renders the expected high-level panels
- `Demo` route can launch into the controlled `Simulation unavailable` fallback when Unity assets are absent
- homepage remains usable at a smaller viewport without obvious breakage

## LLM Agent Execution Strategy

An LLM agent should implement in this order:

1. app shell and routing
2. content schema and centralized content
3. reusable sections
4. homepage
5. demo page with placeholder Unity boundary
6. how-it-works page
7. tests
8. polish
9. real Unity integration later

## LLM Agent Guardrails

- Never place Unity-specific logic outside `src/components/unity/` unless the interface contract itself changes.
- Never place major copy directly into section or page files if it belongs in `content/`.
- Never let the website expose simulation controls that duplicate Unity controls.
- Never couple route availability to Unity readiness.
- Never change `How It Works` back to a low-level engineering doc unless the decisions file changes.
- Never introduce broad infrastructure before there is evidence it is needed.
- Never introduce numeric homepage claims unless the team explicitly confirms they are stable and trustworthy.
- Never present incomplete future features on the final site as if they are available now.
- Never let the `Demo` page become the owner of map switching, restarting, or editor-mode actions.


## Detailed Testing Plan

## Unit Tests

Test these in isolation:
- `Header`
- `Footer`
- `HeroSection`
- `HighlightsSection`
- `TeamSection`
- `UnityEmbed`
- `UnityFallback`
- `UnityLaunchOverlay`
- `DemoPreviewSection`

Assertions:
- correct text renders
- links and buttons exist
- state transitions behave predictably
- semantic headings and button roles are present where expected

## Route-Level Tests

Test:
- `HomePage`
- `DemoPage`
- `HowItWorksPage`

Assertions:
- page renders inside shell
- intended sections appear in order
- CTAs point to expected routes
- demo guidance appears on demo page
- site remains meaningful when Unity is unavailable
- no route depends on Unity success to mount

## Content Integrity Tests

Test:
- shared mission statement exists
- nav labels match configured routes
- team members have required fields
- highlight entries are not empty
- fallback messages are present
- brand name is consistently `EvacLogix`
- `How It Works` label maps to the technical route
- scope note exists
- status copy fields exist where configured

## Integration Tests

When the app exists, verify:
- router mounts all main pages
- demo launch button transitions state
- site remains navigable after demo failure state
- user can navigate to all routes without Unity assets present
- fallback state does not remove demo instructions

## Accessibility Tests

Verify:
- launch button is keyboard reachable and activatable
- navigation is keyboard reachable
- pages expose a meaningful heading hierarchy
- links have discernible text
- fallback messaging is readable and not color-dependent alone

## Architecture Enforcement Tests

Verify:
- Unity-specific imports are confined to the `unity/` component area
- content files export data, not JSX
- page files do not contain large multi-paragraph copy literals
- shared shell wraps all main routes

## Manual QA Checklist

- homepage explains the problem before the solution
- homepage has a strong visual centerpiece
- primary CTA to demo is obvious
- secondary CTA to deeper context exists
- `How It Works` stays high level
- `Demo` page frame visually dominates
- Unity Play backup is visible but secondary
- footer is present and restrained
- site still reads cleanly if Unity never loads
- top-level navigation remains minimal
- project name appears consistently as `EvacLogix`
- the site feels complete rather than roadmap-like

## Risks and Mitigations

### Risk: Unity concerns leak into website architecture

Mitigation:
- isolate everything in `UnityEmbed`
- use typed props
- keep controls inside the game

### Risk: Content drift across pages

Mitigation:
- central content layer
- shared mission statement
- content integrity tests

### Risk: The site becomes too technical or too roadmap-like

Mitigation:
- keep `How It Works` high level
- avoid unfinished feature claims
- keep caveats minimal

### Risk: Demo page feels broken if Unity is missing

Mitigation:
- design unavailable state intentionally
- keep instructions and framing text present
- keep backup link available

### Risk: Overengineering early

Mitigation:
- use only the structure needed for current decisions
- defer real Unity loading complexity until the placeholder route is stable

## Suggested Milestone Breakdown

### Milestone 1

- scaffold app
- router
- shared shell
- design tokens

### Milestone 2

- content layer
- reusable sections
- homepage

### Milestone 3

- demo page
- `UnityEmbed` placeholder flow
- fallback state

### Milestone 4

- `How It Works`
- test suite
- polish pass

### Milestone 5

- real Unity asset hook-up
- final presentation copy
- final screenshots or visuals if available

## Decision Coverage Map

This map exists to prevent drift between `website-architecture-decisions.md` and implementation.

- decisions `1, 6, 29, 45`: covered by multi-page routing and shared shell
- decisions `3, 10, 15, 30, 52, 53`: covered by `UnityEmbed`, fallback flow, and backup-link behavior
- decisions `4, 31, 58`: covered by separation-of-concerns constraints and Unity boundary rules
- decisions `5, 13, 25, 35, 47, 56`: covered by `How It Works` page structure and labeling
- decisions `8, 9, 17, 42, 43, 46`: covered by presentation direction and polish pass
- decisions `11, 20, 27, 32, 33`: covered by scope/status framing rules
- decisions `12, 14, 21, 26, 34, 40, 50, 51, 54, 55`: covered by homepage content requirements
- decisions `18, 37, 57`: covered by team section and footer requirements
- decisions `16, 41, 44`: covered by centralized content system and planning continuity
- decisions `19, 22, 36, 39, 48, 49`: covered by navigation, branding, theme, and reusable section rules
- decision `28`: covered by manual copy workflow and Unity artifact contract

## Success Criteria

The website plan is successful if:
- the code structure mirrors the architecture decisions
- the website is useful without Unity
- the Unity integration can evolve without reshaping the app
- an LLM agent can implement each phase with minimal ambiguity
- the final experience reads as a polished presentation artifact for judges
