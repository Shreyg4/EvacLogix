import type { HTMLAttributes, ReactNode } from "react";

type CardProps = {
  children: ReactNode;
} & HTMLAttributes<HTMLElement>;

export function Card({ children, className = "", ...rest }: CardProps) {
  const mergedClassName = className ? `page-card ${className}` : "page-card";

  return (
    <section {...rest} className={mergedClassName}>
      {children}
    </section>
  );
}
