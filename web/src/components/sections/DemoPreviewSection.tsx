import { Eyebrow } from "../ui/Eyebrow"

type DemoPreviewSectionProps = {
  eyebrow?: string
  title: string
  body: string
  placeholderLabel: string
}

export function DemoPreviewSection({
  eyebrow,
  title,
  body,
  placeholderLabel
}: DemoPreviewSectionProps) {
  return (
    <section className="page-card demo-preview-section" aria-labelledby="demo-preview-title">
      {eyebrow ? <Eyebrow>{eyebrow}</Eyebrow> : null}
      <h2 id="demo-preview-title">{title}</h2>
      <p>{body}</p>
      <div className="demo-placeholder" aria-label={placeholderLabel}>
        <span>{placeholderLabel}</span>
      </div>
    </section>
  )
}
