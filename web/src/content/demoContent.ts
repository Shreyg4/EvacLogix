import type { HeroContent, StatementContent } from "../types/content";
import { appRoutes } from "../utils/routes";

export const demoPageContent: HeroContent = {
  eyebrow: "Demo",
  title: "View the EvacLogix simulation inside the presentation site.",
  body:
    "This page is designed to host the embedded Unity experience directly. The site frames the simulation, but interaction stays inside the game itself."
};

export const demoGuidanceContent: StatementContent = {
  eyebrow: "Notes",
  title: "Demo Route Guidance",
  body: "The website provides context, expectations, and fallback behavior. The simulation itself remains one embedded game, and all operational controls stay inside Unity."
};

export const demoInstructions: string[] = [
  "Press play to attempt launching the embedded simulation.",
  "Use the controls provided inside Unity once the build is connected.",
  "Focus on how evacuation movement, congestion, and exits are presented."
];

export const demoFallbackContent = {
  message: "Simulation unavailable",
  explanation:
    "The current build could not be launched from this embedded frame. The page remains usable, and a secondary backup path can still be provided.",
  backupHref: "https://play.unity.com/",
  backupLabel: "Open Unity Play"
};
