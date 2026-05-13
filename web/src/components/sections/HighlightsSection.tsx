import { Eyebrow } from "../ui/Eyebrow";
import type { HighlightItem } from "../../types/content";

type HighlightsSectionProps = {
  eyebrow?: string;
  title: string;
  items: HighlightItem[];
};

export function HighlightsSection({ eyebrow, title, items }: HighlightsSectionProps) {
  return (
    <section className="page-card highlights-section" aria-labelledby="highlights-title">
      {eyebrow ? <Eyebrow>{eyebrow}</Eyebrow> : null}
      <h2 id="highlights-title">{title}</h2>
      <div className="highlights-grid">
        {items.map((item) => (
          <article key={item.id} className="sub-card highlight-item">
            <p className="highlight-label">{item.label}</p>
            {item.value ? <p className="highlight-value">{item.value}</p> : null}
          </article>
        ))}
      </div>
    </section>
  );
}
