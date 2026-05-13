import type { AnchorHTMLAttributes, ButtonHTMLAttributes, ReactNode } from "react";

type ButtonVariant = "primary" | "secondary";

type ButtonLinkProps = {
  children: ReactNode;
  variant?: ButtonVariant;
  href: string;
} & AnchorHTMLAttributes<HTMLAnchorElement>;

type ButtonActionProps = {
  children: ReactNode;
  variant?: ButtonVariant;
} & ButtonHTMLAttributes<HTMLButtonElement>;

export function Button(props: ButtonLinkProps | ButtonActionProps) {
  const variant = props.variant ?? "secondary";
  const className = variant === "primary" ? "button-link primary" : "button-link secondary";

  if ("href" in props) {
    const { children, href, variant: _variant, ...rest } = props;
    return (
      <a {...rest} className={className} href={href}>
        {children}
      </a>
    );
  }

  const { children, variant: _variant, ...rest } = props;
  return (
    <button {...rest} className={className} type={rest.type ?? "button"}>
      {children}
    </button>
  );
}
