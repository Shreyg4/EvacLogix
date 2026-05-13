import type { HeroContent } from "../../types/content";
import { Button } from "../ui/Button";
import { PageHeader } from "../layout/PageHeader";

type HeroSectionProps = {
  content: HeroContent;
};

export function HeroSection({ content }: HeroSectionProps) {
  return (
    <section className="page-card hero-section" aria-labelledby="hero-title">
      <div id="hero-title">
        <PageHeader
          eyebrow={content.eyebrow ?? "Section"}
          title={content.title}
          body={content.body}
        />
      </div>
      {(content.primaryCta || content.secondaryCta) && (
        <div className="cta-row">
          {content.primaryCta ? (
            <Button href={content.primaryCta.to} variant="primary">
              {content.primaryCta.label}
            </Button>
          ) : null}
          {content.secondaryCta ? (
            <Button href={content.secondaryCta.to} variant="secondary">
              {content.secondaryCta.label}
            </Button>
          ) : null}
        </div>
      )}
    </section>
  );
}
