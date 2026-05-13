type EyebrowProps = {
  children: string;
};

export function Eyebrow({ children }: EyebrowProps) {
  return <p className="eyebrow">{children}</p>;
}
