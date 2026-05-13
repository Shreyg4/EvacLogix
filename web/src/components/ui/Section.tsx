import type { HTMLAttributes, ReactNode } from "react";

type SectionProps = {
  children: ReactNode;
} & HTMLAttributes<HTMLDivElement>;

export function Section({ children, className = "", ...rest }: SectionProps) {
  const mergedClassName = className ? `page-stack ${className}` : "page-stack";

  return (
    <div {...rest} className={mergedClassName}>
      {children}
    </div>
  );
}
