import { HeroSection } from "../components/sections/HeroSection";
import { StatementSection } from "../components/sections/StatementSection";
import { UnityEmbed } from "../components/unity/UnityEmbed";
import {
  demoFallbackContent,
  demoGuidanceContent,
  demoInstructions,
  demoPageContent,
} from "../content/demoContent";

export function DemoPage() {
  return (
    <div className="page-stack page-demo">
      <HeroSection content={demoPageContent} />
      <StatementSection
        eyebrow={demoGuidanceContent.eyebrow}
        title={demoGuidanceContent.title}
        body={demoGuidanceContent.body}
      />
      <UnityEmbed
        title="Embedded Simulation Frame"
        instructions={demoInstructions}
        fallbackMessage={demoFallbackContent.message}
        unavailableExplanation={demoFallbackContent.explanation}
        backupHref={demoFallbackContent.backupHref}
        backupLabel={demoFallbackContent.backupLabel}
        launchLabel="Play Simulation"
      />
    </div>
  );
}
