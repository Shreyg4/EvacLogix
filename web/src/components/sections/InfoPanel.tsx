type InfoPanelProps = {
  title: string;
  body: string;
};

export function InfoPanel({ title, body }: InfoPanelProps) {
  return (
    <article className="sub-card info-panel">
      <h3>{title}</h3>
      <p>{body}</p>
    </article>
  );
}
