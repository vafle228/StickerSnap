/* Numbers the Russian way (design system README): a decimal comma, a thin space in thousands. */

const formats = new Map<number, Intl.NumberFormat>();

/** 2.84 → «2,84»; `digits` is the most decimals shown, trailing zeros are kept. */
export function num(value: number, digits = 0): string {
  let format = formats.get(digits);
  if (!format) {
    format = new Intl.NumberFormat("ru-RU", {
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
    });
    formats.set(digits, format);
  }
  return format.format(value);
}

/** 30 → «30», 29.97 → «29,97». */
export function fps(value: number): string {
  return Number.isInteger(value) ? String(value) : num(value, 2);
}

/** 12.4 → «12,4», 8 → «8»: seconds on the ruler, at most two decimals. */
export function seconds(value: number): string {
  return num(value, 2).replace(/,?0+$/, "");
}

/** 1.25 → «1,25×». */
export function speed(value: number): string {
  return `${num(value, Number.isInteger(value) ? 0 : 2).replace(/,?0$/, "")}×`;
}

/** 188416 → «184 КБ»: a kilobyte is 1024 bytes, as Telegram's 256 КБ. */
export function kilobytes(bytes: number): string {
  return `${num(Math.round(bytes / 1024))} КБ`;
}

/** 2.64 → «00:02.640». */
export function timecode(seconds: number): string {
  const ms = Math.max(0, Math.round(seconds * 1000));
  const minutes = Math.floor(ms / 60_000);
  const rest = ms % 60_000;
  const whole = Math.floor(rest / 1000);
  const pad = (n: number, width: number) => String(n).padStart(width, "0");
  return `${pad(minutes, 2)}:${pad(whole, 2)}.${pad(rest % 1000, 3)}`;
}
