import { Eyebrow } from "../ui/Eyebrow"
import type { TeamMember } from "../../types/content"

type TeamSectionProps = {
  eyebrow?: string
  title: string
  body?: string
  members: TeamMember[]
}

export function TeamSection({ eyebrow, title, body, members }: TeamSectionProps) {
  return (
    <section className="page-card team-section" aria-labelledby="team-title">
      {eyebrow ? <Eyebrow>{eyebrow}</Eyebrow> : null}
      <h2 id="team-title">{title}</h2>
      {body ? <p>{body}</p> : null}
      <div className="content-stack">
        {members.map((member) => (
          <article key={member.name} className="sub-card">
            <h3>{member.name}</h3>
            <p className="member-role">{member.role}</p>
            <p>{member.responsibility}</p>
          </article>
        ))}
      </div>
    </section>
  )
}
