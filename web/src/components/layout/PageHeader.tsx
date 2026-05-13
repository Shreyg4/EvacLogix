import { Eyebrow } from "../ui/Eyebrow";

type PageHeaderProps = {
  eyebrow: string;
  title: string;
  body: string;
};

export function PageHeader({ eyebrow, title, body }: PageHeaderProps) {
  return (
    <>
      <Eyebrow>{eyebrow}</Eyebrow>
      <h1>{title}</h1>
      <p>{body}</p>
    </>
  );
}
