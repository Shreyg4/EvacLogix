import { HeroSection } from "../components/sections/HeroSection"
import { StatementSection } from "../components/sections/StatementSection"
import {
  userGuideAudiences,
  userGuideAudienceSection,
  userGuidePageContent,
  userGuideRequirementsSection,
  userGuideSteps,
  userGuideStepsSection,
  userGuideTipsSection
} from "../content/userGuideContent"

export function UserGuidePage() {
  return (
    <div className="page-stack page-user-guide">
      <HeroSection content={userGuidePageContent} />

      <section className="page-card" aria-labelledby="audience-title">
        <p className="eyebrow">{userGuideAudienceSection.eyebrow}</p>
        <h2 id="audience-title">{userGuideAudienceSection.title}</h2>
        <p>{userGuideAudienceSection.body}</p>
        <div className="highlights-grid">
          {userGuideAudiences.map((item) => (
            <article key={item.id} className="sub-card highlight-item">
              <p className="highlight-label">{item.audience}</p>
              <p className="highlight-value">{item.useCase}</p>
            </article>
          ))}
        </div>
      </section>

      <StatementSection
        eyebrow={userGuideRequirementsSection.eyebrow}
        title={userGuideRequirementsSection.title}
        body={userGuideRequirementsSection.body}
      />

      <section className="page-card" aria-labelledby="guide-steps-title">
        <p className="eyebrow">{userGuideStepsSection.eyebrow}</p>
        <h2 id="guide-steps-title">{userGuideStepsSection.title}</h2>
        <p>{userGuideStepsSection.body}</p>
        <ol className="guide-steps">
          {userGuideSteps.map((step) => (
            <li key={step.id} className="sub-card guide-step">
              <span className="guide-step-index" aria-hidden="true">
                {step.step}
              </span>
              <div>
                <h3>{step.title}</h3>
                <p>{step.detail}</p>
              </div>
            </li>
          ))}
        </ol>
      </section>

      <StatementSection
        eyebrow={userGuideTipsSection.eyebrow}
        title={userGuideTipsSection.title}
        body={userGuideTipsSection.body}
      />
    </div>
  )
}
