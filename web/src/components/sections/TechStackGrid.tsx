import type { TechStackItem } from "../../types/content"

type TechStackGridProps = {
  items: TechStackItem[]
}

export function TechStackGrid({ items }: TechStackGridProps) {
  return (
    <div className="tech-grid">
      {items.map((item) => (
        <article key={item.id} className="sub-card tech-item">
          <h3>{item.name}</h3>
          <p>{item.role}</p>
        </article>
      ))}
    </div>
  )
}
