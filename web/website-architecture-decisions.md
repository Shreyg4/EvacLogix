# Website Architecture Decisions

Project: EvacLogix

This document records website architecture decisions as they are resolved.

## Decision Log

### 1. What kind of website are we building?

Question:
Should the website be primarily a container for the Unity simulation, or a fuller product experience with supporting pages around it?

Answer:
Fuller product experience.

Recommendation:
Make the website a fuller product experience with one main simulation page.

Reasoning:
The site should explain EvacLogix's mission, goals, and project impact in addition to hosting the demo.

### 2. Who is the primary audience?

Question:
Is this website mainly for judges/professors at presentation time, or for general public users after the project is done?

Answer:
Judges/professors.

Recommendation:
Design first for judges/professors, second for public viewers.

Reasoning:
This keeps the site focused on communicating project value, technical direction, and demo readiness clearly during presentations.

### 3. How should the simulation be delivered?

Question:
Should the Unity simulation be embedded directly inside the website, or should the site link out to Unity Play as the live demo?

Answer:
Embedded WebGL as primary, Unity Play as backup.

Recommendation:
Use an embedded Unity WebGL build as the main experience and keep a Unity Play link available as a fallback.

Reasoning:
Embedding the simulation keeps the demo self-contained for judges and avoids context switching during presentations.

### 4. Where should simulation controls live?

Question:
Should the website controls change the live Unity simulation directly, or should the site mostly explain the controls and let users interact inside the Unity UI itself?

Answer:
All controls live in Unity.

Recommendation:
Keep simulation controls inside Unity.

Reasoning:
This avoids confusing duplication, reduces integration complexity, and preserves separation of concerns between the website and the simulation.

### 5. How much technical detail should the website include?

Question:
Should the website include only polished final content, or should it also show technical process details like your simulation model, agent logic, and evacuation assumptions?

Answer:
Presentation-first with a dedicated technical section.

Recommendation:
Lead with polished presentation content, then include a dedicated technical section for methodology and implementation details.

Reasoning:
This helps judges understand the mission and value quickly, while still giving the project technical credibility and depth.

### 6. Should the website use one page or multiple pages?

Question:
Should the website be a single long scrolling presentation page with sections, or a small multi-page site with separate routes like `Home`, `Demo`, and `Technical Approach`?

Answer:
Small multi-page site.

Recommendation:
Build a small multi-page site.

Reasoning:
Separate routes make the project easier to present, keep the Unity demo page focused, and give the content a cleaner structure for judges.

### 7. What is the minimum page set for version one?

Question:
What are the minimum pages the first version absolutely needs?

Answer:
Three core pages plus a short team section.

Recommendation:
Use these as the minimum structure:
- `Home`
- `Demo`
- `Technical Approach`
- A short `Our Team` section on the `Home` page

Reasoning:
This is the smallest complete structure that communicates the mission, shows the simulation, explains the engineering, and acknowledges the team without over-expanding scope.

### 8. What presentation style should the website use?

Question:
Should the `Home` page feel more like a polished pitch deck in website form, or more like a product landing page with lighter academic tone?

Answer:
Polished pitch deck in website form.

Recommendation:
Design the site as a polished pitch deck in website form.

Reasoning:
This matches a judge/professor audience and supports a presentation-focused narrative about the problem, mission, solution, and technical execution.

### 9. What visual identity should the website emphasize?

Question:
Should the website’s visual identity lean more toward emergency/safety/public-service communication, or more toward sleek technical simulation software?

Answer:
Emergency/safety/public-service first, with technical polish underneath.

Recommendation:
Emphasize emergency, safety, and public-service communication while retaining a technically polished presentation.

Reasoning:
This better matches the project’s mission and community impact while still leaving room to showcase engineering quality.

### 10. How should the demo page handle loading the simulation?

Question:
Should the `Demo` page load the Unity WebGL build immediately on page visit, or should it show a pre-demo briefing with a button like `Launch Simulation`?

Answer:
Use a launch button, potentially styled as a play button, inside a large black demo frame.

Recommendation:
Show a large black rectangle representing the simulation viewport, with a clear play or launch control that attempts to start Unity.

Reasoning:
This gives the page a deliberate presentation flow, avoids forcing a heavy WebGL load on arrival, and supports a graceful fallback while integration is unfinished.

Current implementation target:
If Unity is not yet connected, pressing play should attempt to launch and then return a visible fallback state such as "Simulation unavailable" with a short explanation.

### 11. How should the project be framed in website copy?

Question:
Should the website copy explicitly acknowledge that the simulation is an evolving prototype, or should it present everything as if it is already fully production-ready?

Answer:
Finished presentation artifact with minimal, essential scope framing.

Recommendation:
Present EvacLogix as a polished, finished presentation artifact while keeping only essential scope framing, such as clarifying that it is a simulation rather than a real emergency guidance system.

Reasoning:
This keeps the site credible and honest without making it read like an in-progress roadmap.

### 12. What should the home page lead with?

Question:
Should the website’s `Home` page lead with the human problem first, or with the technical simulation first?

Answer:
Human problem first.

Recommendation:
Lead the `Home` page with the evacuation and safety problem, then introduce the simulation as the proposed solution.

Reasoning:
This makes the project’s purpose immediately clear and helps judges understand the stakes before encountering the technical system.

### 13. How detailed should the technical page be right now?

Question:
Should the `Technical Approach` page explain the exact implementation details already named in your proposal, like agent states, congestion weighting, exits, and fire spread, or should it stay higher level and avoid details until the implementation is further along?

Answer:
High level only.

Recommendation:
Keep the `Technical Approach` page high level for now.

Reasoning:
This avoids overcommitting the website to implementation details too early and keeps the presentation aligned with the current state of the project.

### 14. Should the home page push viewers into the demo?

Question:
Should the `Home` page have a clear call-to-action that pushes viewers into the demo, or should it be more informational and let navigation do the work?

Answer:
Use a strong call-to-action to the demo.

Recommendation:
Include one primary call-to-action on the `Home` page that directs viewers to the simulation demo.

Reasoning:
This makes the site easier to present, keeps the user journey clear, and reduces ambiguity for judges.

### 15. Should the website remain useful if Unity is unavailable?

Question:
Should the website be built so it can function meaningfully even when Unity is unavailable, or is the site acceptable only if the embedded simulation works?

Answer:
The site should function independently of Unity and fail gracefully when Unity is unavailable.

Recommendation:
Treat the website as a complete presentation layer on its own, with the Unity demo as an enhancement rather than a hard dependency.

Reasoning:
This protects presentation quality, preserves separation of concerns, and ensures the site remains useful even if WebGL integration is not ready or fails during a demo.

### 16. How should website content be managed?

Question:
Do you want the website content to be hardcoded in React components at first, or structured in a way that is easy for your team to update later, like a central data or content file?

Answer:
Use a central content structure.

Recommendation:
Store major website copy and structured content in a centralized content or configuration layer rather than scattering it across components.

Reasoning:
This makes the site easier for the team to maintain, update, and polish as project details evolve.

### 17. How much should the team invest in the visual presentation early?

Question:
Should the visual design aim for fast and simple implementation first, or should it intentionally invest in a distinctive presentation look early because the site is part of the judged experience?

Answer:
Invest in a distinctive presentation look early, while keeping implementation scope controlled.

Recommendation:
Prioritize a distinctive, presentation-ready visual identity from the start, but limit the number of moving parts so the site remains maintainable.

Reasoning:
The website is part of the judged experience, so a stronger visual presentation improves how the project is perceived without requiring unnecessary technical complexity.

### 18. What should the `Our Team` section include?

Question:
Should the `Our Team` section just list names and roles, or should it also briefly explain how responsibilities are split between Unity simulation work and website/presentation work?

Answer:
Include names, roles, and a short responsibility split.

Recommendation:
Show each team member’s name, role, and a concise description of what they are responsible for.

Reasoning:
This gives proper credit, communicates collaboration clearly, and helps judges understand how work is divided across the project.

### 19. What device experience should version one prioritize?

Question:
Should the first website version assume desktop presentation first, or should mobile responsiveness be treated as equally important from day one?

Answer:
Desktop-first, with enough responsiveness to avoid obvious breakage on smaller screens.

Recommendation:
Prioritize desktop and presentation-screen layouts first, while keeping the structure flexible enough that the site does not completely fall apart on smaller devices.

Reasoning:
The likely viewing environment is a laptop or projector, so desktop should drive design decisions. Minimal responsiveness still reduces risk if the site is opened elsewhere.

### 20. Should the website show project status language?

Question:
Should the website include any live project status language like `in progress`, `prototype`, or `current iteration`, or should it avoid status labels and just present the experience cleanly?

Answer:
Use light status language.

Recommendation:
Include only subtle status framing where it clarifies scope or availability, especially around the demo, without making the site feel unfinished.

Reasoning:
This helps the experience feel intentional and credible without weakening the presentation-day polish.

### 21. How prominent should UW and community impact be?

Question:
Should the website mention the University of Washington context and community impact prominently on the `Home` page, or keep that secondary behind the main project overview?

Answer:
Prominent on the `Home` page.

Recommendation:
Feature the University of Washington context and community impact clearly on the `Home` page.

Reasoning:
This grounds the project in a real context, reinforces its public-safety mission, and strengthens its relevance for judges and professors.

### 22. How complex should the site navigation be?

Question:
Should the site navigation stay very minimal, like just `Home`, `Demo`, and `Technical Approach`, or should it also include direct jump links or section anchors for presentation convenience?

Answer:
Keep top-level navigation minimal, with optional internal anchors later if needed.

Recommendation:
Use a minimal top-level navigation for the main pages and only add section anchors if they solve a clear presentation need later.

Reasoning:
This keeps the site clean, focused, and easy to present without introducing unnecessary clutter.

### 23. What should the home page show before live demo visuals are ready?

Question:
Should the website include screenshots or static mockups of the Unity simulation on the `Home` page before the real embed is ready, or should it avoid visuals until the live demo exists?

Answer:
Use blank screenshot placeholders.

Recommendation:
Reserve space for simulation visuals with intentional placeholder frames until real screenshots or demo imagery are available.

Reasoning:
This preserves layout structure and presentation intent without forcing incomplete or low-quality assets into the site too early.

### 24. How much guidance should appear on the demo page?

Question:
Should the `Demo` page include explanatory text beside the Unity frame, like what the viewer is seeing and what to pay attention to, or should the frame dominate with very little surrounding copy?

Answer:
Include concise framing text that explains the controls and roughly what the viewer should do.

Recommendation:
Pair the demo frame with short guidance that explains what the simulation represents, where the controls live, and how to interact with the experience.

Reasoning:
This helps judges engage with the demo quickly and reduces confusion, especially since all controls will live inside Unity.

### 25. How should the technical page describe the system?

Question:
Should the `Technical Approach` page describe the system in terms of user-facing concepts like agents, exits, congestion, and fire behavior, or in more engine-oriented terms like navmesh, pathfinding, weights, and state machines?

Answer:
User-facing concepts first, with light technical terminology underneath.

Recommendation:
Explain the simulation in accessible, user-facing terms first and then support that explanation with selective technical language where it adds clarity.

Reasoning:
This makes the page easier for judges to understand while still preserving technical credibility.

### 26. How bold should the homepage hero message be?

Question:
Should the homepage hero section make one big claim about what EvacLogix does, or should it stay more cautious and descriptive?

Answer:
Use one strong central claim, while keeping it honest.

Recommendation:
Lead the homepage with a clear, memorable statement about what EvacLogix helps users understand or explore.

Reasoning:
The homepage needs a strong center of gravity, and a concise claim gives judges an immediate understanding of the project’s purpose.

### 27. Should the website include a scope or limitations note?

Question:
Should the website include a short limitations or scope note somewhere, such as that this is a simulation tool and not a real emergency guidance system?

Answer:
Include a brief scope note.

Recommendation:
Add a concise note clarifying that EvacLogix is a simulation and educational or research prototype rather than a real-time emergency guidance system.

Reasoning:
This keeps the project honest, protects against overclaiming, and strengthens the academic framing.

### 28. How should Unity WebGL build files reach the website?

Question:
Should the website plan assume the Unity WebGL build files will eventually be copied manually into `web/public/unity-build/`, or do you want to plan around some automated handoff between Unity output and the web app later?

Answer:
Manual copy first.

Recommendation:
Assume a manual copy workflow for the first version, with Unity WebGL build files placed into `web/public/unity-build/` when ready.

Reasoning:
This keeps the early workflow simple and avoids unnecessary pipeline complexity before the Unity export process is stable.

### 29. Should version one use real routing?

Question:
Should the website’s first implementation include a real router from the beginning, or should it fake multiple pages first and only add routing later?

Answer:
Use a real router from the beginning.

Recommendation:
Set up real routing in the first implementation so the `Home`, `Demo`, and `Technical Approach` pages have a stable structure from the start.

Reasoning:
This matches the intended multi-page architecture and avoids reworking a temporary fake-page setup later.

### 30. How should the Unity demo be isolated in the website code?

Question:
Should the `Demo` page be architected as a reusable `UnityEmbed` component from the start, even if it only shows a placeholder or error state at first?

Answer:
Use a dedicated reusable `UnityEmbed` component from the start.

Recommendation:
Create a dedicated `UnityEmbed` component that owns the black frame, play interaction, loading behavior, and fallback error state.

Reasoning:
This preserves separation of concerns, keeps Unity-specific behavior isolated, and makes the eventual WebGL integration easier to evolve without spreading logic across the page layer.

### 31. How should the website think about scenarios, maps, and editor modes?

Question:
Should the site architecture assume one demo scenario at first, or should it already leave room for multiple building scenarios later?

Answer:
Treat the embedded experience as one game. Switching between maps, editor mode, restarting, and related flows should happen inside Unity rather than in the website.

Recommendation:
Architect the website around a single embedded game entry point, and let Unity own internal menus for scenarios, map selection, editor access, and reset behavior.

Reasoning:
This keeps the web layer clean and preserves separation of concerns. The website should launch and frame the experience, not duplicate game-state navigation.

Stub note:
If a custom map editor is added later, the website can acknowledge that capability at a high level without owning the file format, export flow, or game-side import logic yet.

### 32. Should the website talk about unfinished or future features?

Question:
Should the website expose the existence of the custom map editor in version one, or should it stay invisible until the Unity-side workflow is real?

Answer:
Do not present unfinished or future-scope features on the site.

Recommendation:
Only show what is complete and presentation-ready by demo day. If the custom editor is finished by then, present it as part of the game. If not, leave it out of the website.

Reasoning:
The site should read as a finished presentation artifact, not a roadmap. That keeps the story cleaner and avoids distracting judges with incomplete scope.

### 33. How much should the website emphasize limitations?

Question:
Should the website copy mention technical limitations at all, or should it simply present the completed experience confidently and keep caveats minimal?

Answer:
Present the experience confidently with minimal caveats.

Recommendation:
Write the site as a confident, finished presentation artifact and only keep essential scope framing, such as clarifying that EvacLogix is a simulation rather than a real emergency response system.

Reasoning:
By presentation day, the website should feel complete and polished. Too much limitations language weakens the pitch and distracts from the project’s value.

### 34. What narrative structure should the home page follow?

Question:
Should the homepage structure be driven by a classic presentation narrative like `Problem -> Solution -> Demo -> Team`, or by a more website-like flow such as `Hero -> Features -> Demo -> Technical -> Team`?

Answer:
`Problem -> Solution -> Demo -> Team`

Recommendation:
Structure the homepage around a presentation narrative that begins with the problem, introduces EvacLogix as the solution, points viewers toward the demo, and ends with the team.

Reasoning:
This is the clearest flow for judges and keeps the site aligned with presentation storytelling rather than generic product-marketing patterns.

### 35. Where should the technical content live?

Question:
Should the `Technical Approach` page exist as its own route from the beginning, even if it stays concise, or should that content just live as a section on the homepage until the site grows?

Answer:
Keep it as its own route from the beginning.

Recommendation:
Make `Technical Approach` a dedicated page from the start, even if the content is brief at first.

Reasoning:
This preserves the intended multi-page structure and keeps the homepage focused on narrative and presentation flow.

### 36. How should viewers reach the demo page?

Question:
Should the `Demo` page be reachable from both the main navigation and a large homepage call-to-action, or should it only be accessed through one primary path to keep the flow controlled?

Answer:
Reachable from both the main navigation and the homepage call-to-action.

Recommendation:
Provide access to the demo through both the site navigation and a strong homepage CTA.

Reasoning:
This supports a clean presentation flow while keeping the site easy to navigate during live use.

### 37. How much information should the footer contain?

Question:
Should the website include any persistent footer information, like team attribution and project context, or keep the footer extremely minimal?

Answer:
Include a small but meaningful footer.

Recommendation:
Use a concise footer with light attribution, project context, and presentation-level polish.

Reasoning:
This adds completeness and professionalism without cluttering the main content areas.

### 38. What tone should the website copy use?

Question:
Should the site copy be written in a more formal academic tone, or in a clear modern presentation tone that is still professional but less stiff?

Answer:
Use a clear modern presentation tone.

Recommendation:
Write the site in a professional, presentation-ready voice that is direct, readable, and confident without sounding overly academic.

Reasoning:
This improves clarity for judges and keeps the project feeling polished and approachable.

### 39. How should the project name be styled?

Question:
Should the website name treatment consistently use `EvacLogix` with that capitalization everywhere, even though the proposal file currently says `Evaclogix`?

Answer:
Use `EvacLogix` consistently.

Recommendation:
Standardize the website and presentation branding on `EvacLogix`.

Reasoning:
The capital `L` improves readability and gives the name a clearer, more deliberate identity.

### 40. How many call-to-action buttons should the homepage hero use?

Question:
Should the homepage hero include a single primary button only, or a primary and secondary action like `Launch Demo` plus `Learn More`?

Answer:
Use both a primary and secondary action.

Recommendation:
Include one strong primary CTA for the demo and one supporting CTA for deeper context.

Reasoning:
This supports both presentation flow and audience curiosity without overloading the hero section.

### 41. How should this architecture document be maintained during planning?

Question:
Should the website architecture document keep growing as a running Q&A log exactly like this, or should it eventually be condensed into a shorter final summary once enough decisions are made?

Answer:
Keep the running log for now.

Recommendation:
Continue maintaining this document as a detailed running decision log during planning and early implementation.

Reasoning:
The log helps future agents and teammates understand intent, prevents design drift, and preserves a concrete working model for the codebase as implementation begins.

### 42. Should different pages have different presentation roles?

Question:
Should the website architecture assume the `Home` page is the emotional and persuasive page, while `Technical Approach` is intentionally calmer and more informational, or should all pages share the same presentation intensity?

Answer:
Give each page a different job.

Recommendation:
Let `Home` carry the emotional and persuasive presentation burden, while `Technical Approach` stays calmer, clearer, and more explanatory.

Reasoning:
This makes the site easier to follow, gives each page a clear purpose, and avoids flattening the presentation into one uniform tone.

### 43. How should the `Demo` page balance site styling and game focus?

Question:
Should the `Demo` page visually feel heavier and more utilitarian than the `Home` page, so the embedded game frame becomes the focus, or should it preserve the same strong presentation styling as the rest of the site?

Answer:
Keep a cohesive site style, but let the demo frame dominate.

Recommendation:
Maintain the website’s visual language on the `Demo` page while deliberately giving the embedded game frame the strongest visual emphasis.

Reasoning:
This keeps the site cohesive without letting the surrounding page compete with the simulation.

### 44. How should page copy be handled during implementation?

Question:
Should the website be architected so the copy and page content can be filled in with placeholders first, then refined later, or should implementation wait until the real copy is ready?

Answer:
Build with structured placeholders first.

Recommendation:
Implement the site with deliberate placeholder content in the final content structure, then refine the wording later without changing the page architecture.

Reasoning:
This allows layout, routing, and component boundaries to stabilize early while keeping copy iteration easy closer to presentation day.

### 45. Should all routes share a common page shell?

Question:
Should the website have one shared page shell with common navigation, footer, spacing, and branding across all routes, or should each page be more independently laid out?

Answer:
Use one shared page shell.

Recommendation:
Implement a shared page shell that provides the navigation, footer, branding, and common layout structure across all routes.

Reasoning:
This creates consistency, reduces repeated code, and helps the website feel like one coherent experience.

### 46. How consistent should the visual theme be across pages?

Question:
Should the website architecture assume a single visual theme across all pages, or should the `Home` and `Demo` pages be allowed to diverge more dramatically in color and mood?

Answer:
Use one shared theme with page-specific emphasis.

Recommendation:
Keep a consistent visual theme across the site while allowing each page to shift emphasis through composition, hierarchy, and focus.

Reasoning:
This preserves polish and cohesion without making every page feel visually identical.

### 47. What should the technical page be called in navigation?

Question:
Should the navigation label for the technical page be more formal like `Technical Approach`, or shorter and friendlier like `How It Works`?

Answer:
Use `How It Works`.

Recommendation:
Use `How It Works` as the user-facing page label and navigation text for the technical page.

Reasoning:
This is clearer, more approachable, and better aligned with the decision to keep the page high level and presentation-friendly.

### 48. How should the site branding be treated initially?

Question:
Should the website logo or brand treatment be text-only at first, or should the architecture leave a clear slot for a visual mark or icon even if one does not exist yet?

Answer:
Use text-first branding, but leave room for a future mark.

Recommendation:
Start with a strong text-based `EvacLogix` brand treatment while keeping the header layout flexible enough to accommodate a simple icon or mark later if needed.

Reasoning:
This avoids unnecessary logo work now while preventing the layout from becoming rigid.

### 49. How structured should page composition be?

Question:
Should the site use a small set of reusable content sections, like hero, statement, feature panel, demo frame, and team block, or should each page be composed more freely without trying to standardize sections?

Answer:
Use a small reusable section system.

Recommendation:
Build the site from a compact set of reusable section patterns that can be arranged differently across pages.

Reasoning:
This keeps the code maintainable, supports consistency across the site, and still leaves enough flexibility for each page to have its own role.

### 50. Should the home page include project highlights or metrics?

Question:
Should the website’s `Home` page include a short metrics or highlights strip, like number of supported agents, building layouts, or evacuation variables, or should it stay more narrative and avoid stats-style panels?

Answer:
Include a small highlights strip.

Recommendation:
Add a concise highlights strip on the `Home` page with a few concrete project facts or capabilities.

Reasoning:
This gives judges quick, memorable proof points without turning the homepage into a dashboard.

### 51. How should the home page highlights be phrased?

Question:
Should those homepage highlights be written as polished qualitative points like `Agent-based simulation` and `Building evacuation scenarios`, or as concrete numeric claims only when the numbers are fully trustworthy?

Answer:
Use qualitative highlights first, and only use numbers when they are trustworthy.

Recommendation:
Lead with clear qualitative highlights and introduce numeric claims only when the team is confident they will remain accurate by presentation day.

Reasoning:
This keeps the homepage honest and flexible while still giving the project strong, concrete-feeling structure.

### 52. How should the demo fallback error be worded?

Question:
Should the demo error state literally say `Unity is not responding`, or should it use calmer presentation language like `Simulation unavailable` with a short explanation?

Answer:
Use calmer presentation language.

Recommendation:
Prefer messaging such as `Simulation unavailable` with a short, clear explanation rather than crash-like wording.

Reasoning:
This keeps the demo page polished and intentional if the embedded experience cannot be launched.

### 53. How visible should the Unity Play fallback link be?

Question:
Should the website include a clearly visible backup link to Unity Play on the `Demo` page even when the embed works, or should that fallback stay visually secondary and only matter when needed?

Answer:
Keep the Unity Play backup link visually secondary.

Recommendation:
Treat Unity Play as a backup path that is available but visually subordinate to the embedded WebGL experience.

Reasoning:
The embedded demo should remain the primary presentation flow, while the backup stays accessible without competing for attention.

### 54. How should the site handle its core mission statement?

Question:
Should the site architecture reserve a dedicated place for one short mission statement that can be reused across pages, or should each page describe the mission in its own words?

Answer:
Define one reusable mission statement.

Recommendation:
Create a short shared mission statement in the centralized content layer and reuse it consistently across the website.

Reasoning:
This improves consistency, strengthens branding, and makes it easier to keep the project message aligned across pages.

### 55. Should the home page have a strong visual centerpiece?

Question:
Should the `Home` page include one strong visual centerpiece besides the hero text, like a large mock simulation frame or blueprint-style panel, or should it stay more text-driven until real assets exist?

Answer:
Include one strong visual centerpiece, even if it starts as a styled placeholder.

Recommendation:
Design the homepage around a major visual anchor that supports the hero section and can later be replaced or enriched with project-specific visuals.

Reasoning:
This makes the site feel more intentional and memorable, especially in a judged presentation context.

### 56. How should the `How It Works` page be structured?

Question:
Should the `How It Works` page be structured as a few high-level explanatory panels, or as a step-by-step walkthrough of the simulation flow from fire start to evacuation behavior?

Answer:
Use a few high-level explanatory panels.

Recommendation:
Structure the `How It Works` page as a concise set of high-level panels that explain the simulation model in broad, understandable terms.

Reasoning:
This aligns with the decision to keep the page accessible and high level without overcommitting to detailed implementation sequencing.

### 57. Where should team attribution appear across the site?

Question:
Should the team section appear only on the `Home` page, or should a lighter team attribution also appear elsewhere, such as in the footer?

Answer:
Use a full team section on `Home` and light attribution in the footer.

Recommendation:
Keep the main team presentation on the homepage and reinforce authorship subtly in the shared footer.

Reasoning:
This balances recognition, polish, and consistency without making every page feel repetitive.

### 58. How explicitly should the codebase separate concerns?

Question:
Should the website architecture explicitly separate `content`, `layout`, and `Unity integration` into different code areas from the start, or is that over-structuring for a first version?

Answer:
Separate them from the start.

Recommendation:
Organize the web codebase so content, shared layout, and Unity-specific behavior live in clearly separated areas from the beginning.

Reasoning:
This directly supports the project’s separation-of-concerns goal and makes the frontend easier to maintain as the embedded game integration evolves.
