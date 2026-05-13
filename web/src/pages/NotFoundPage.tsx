import { PageHeader } from "../components/layout/PageHeader";

export function NotFoundPage() {
  return (
    <section aria-labelledby="not-found-title" className="page-card">
      <div id="not-found-title">
        <PageHeader
          eyebrow="404"
          title="Page Not Found"
          body="The route exists, but the requested page does not."
        />
      </div>
    </section>
  );
}
