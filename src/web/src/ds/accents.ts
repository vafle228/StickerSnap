export interface Swatch<V extends string = string> {
  value: V;
  label: string;
  color: string;
}

export type AccentName = "indigo" | "violet" | "raspberry" | "teal" | "graphite";

/** The five accents of R20; Индиго is the default. */
export const ACCENTS: readonly Swatch<AccentName>[] = [
  { value: "indigo", label: "Индиго", color: "var(--accent-indigo-primary)" },
  { value: "violet", label: "Фиалка", color: "var(--accent-violet-primary)" },
  { value: "raspberry", label: "Малина", color: "var(--accent-raspberry-primary)" },
  { value: "teal", label: "Морская волна", color: "var(--accent-teal-primary)" },
  { value: "graphite", label: "Графит", color: "var(--accent-graphite-primary)" },
];
