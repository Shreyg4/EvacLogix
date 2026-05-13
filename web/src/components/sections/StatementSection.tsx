import { Eyebrow } from "../ui/Eyebrow";

type StatementSectionProps = {
  eyebrow?: string;
  title: string;
  body: string;
};

export function StatementSection({ eyebrow, title, body }: StatementSectionProps) {
  return (
    <section className="page-card statement-section" aria-labelledby={`statement-${title}`}>
      {eyebrow ? <Eyebrow>{eyebrow}</Eyebrow> : null}
      <h2 id={`statement-${title}`}>{title}</h2>
      <p>{body}</p>
    </section>
  );
}
