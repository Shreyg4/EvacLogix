import { useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import { HeroSection } from "../components/sections/HeroSection";
import { StatementSection } from "../components/sections/StatementSection";
import { UnityEmbed } from "../components/unity/UnityEmbed";
import {
  demoGuidanceContent,
  demoInstructions,
  demoPageContent,
  defaultDemoProfile,
} from "../content/demoContent";
import { getUnityAppProfile } from "../content/unityAppProfiles";

export function DemoPage() {
  const [searchParams] = useSearchParams();
  const selectedProfile = useMemo(() => {
    const requestedProfileId = searchParams.get("app");
    return getUnityAppProfile(requestedProfileId) ?? defaultDemoProfile;
  }, [searchParams]);

  return (
    <div className="page-stack page-demo">
      <HeroSection content={demoPageContent} />
      <StatementSection
        eyebrow={demoGuidanceContent.eyebrow}
        title={demoGuidanceContent.title}
        body={demoGuidanceContent.body}
      />
      <UnityEmbed
        title={selectedProfile.embedTitle}
        instructions={demoInstructions}
        fallbackMessage={selectedProfile.fallbackMessage}
        unavailableExplanation={selectedProfile.fallbackExplanation}
        backupHref={selectedProfile.backupHref}
        backupLabel={selectedProfile.backupLabel}
        launchLabel={selectedProfile.launchLabel}
        buildConfigPath={selectedProfile.buildConfigPath}
        allowedBridgeCommands={selectedProfile.allowedBridgeCommands}
      />
    </div>
  );
}
