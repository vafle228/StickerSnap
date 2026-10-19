import { Component, type ErrorInfo, type ReactNode } from "react";
import { Button, Icon } from "@stickersnap/ds";
import { t } from "../strings/ru";
import { captureError, shortNumber, type ReportedError } from "./errors";
import styles from "./report.module.css";
import { reportError } from "./reporter";
import { trail } from "./trail";

/** The time on the fallen screen: 13:22 */
const clock = (iso: string) =>
  new Date(iso).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });

/** 19·3 «Что-то сломалось»: no white screen, the main action reports what was already collected,
 *  the code under it names the report in a conversation. */
export function CrashScreen({ error, onRestart }: { error: ReportedError; onRestart: () => void }) {
  return (
    <div className={styles.crash} role="alert">
      <span className={styles.icon} aria-hidden>
        <Icon name="error-circle" size={32} />
      </span>
      <h2 className={styles.title}>{t("report.crash.title")}</h2>
      <p className={styles.text}>{t("report.crash.text")}</p>
      <div className={styles.actions}>
        <Button variant="primary" icon="bug" onClick={() => reportError(error)}>
          {t("report.full")}
        </Button>
        <Button icon="refresh" onClick={onRestart}>
          {t("report.crash.restart")}
        </Button>
      </div>
      <span className={styles.code}>
        {t("report.crash.code", { code: shortNumber(error.traceId), time: clock(error.at) })}
      </span>
    </div>
  );
}

interface Props {
  children: ReactNode;
  /** A new value (the section) brings the screen back by itself */
  resetKey?: string;
  /** The section is kept but not shown (the editor): its fallen screen is not shown either */
  hidden?: boolean;
}

interface State {
  /** Something under it threw: the section is not drawn until a restart */
  failed: boolean;
  /** The error as it is reported, once componentDidCatch has its component stack */
  error?: ReportedError;
  resetKey?: string;
}

/** Catches what a section throws while it draws (ErrorBoundary): the section gives way to 19·3,
 *  the rest of the window keeps working. */
export class ScreenBoundary extends Component<Props, State> {
  state: State = { failed: false, resetKey: this.props.resetKey };

  static getDerivedStateFromProps(props: Props, state: State): Partial<State> | null {
    return props.resetKey === state.resetKey
      ? null
      : { failed: false, error: undefined, resetKey: props.resetKey };
  }

  static getDerivedStateFromError(): Partial<State> {
    return { failed: true };
  }

  componentDidCatch(error: unknown, info: ErrorInfo) {
    const thrown = error instanceof Error ? error : new Error(String(error));
    trail.record(`Экран упал: ${thrown.name}`);
    this.setState({
      error: captureError({
        code: "internal",
        title: t("report.crash.title"),
        source: "interface",
        message: `${thrown.name}: ${thrown.message}`,
        stack: [thrown.stack, info.componentStack].filter(Boolean).join("\n"),
      }),
    });
  }

  render() {
    const { failed, error } = this.state;
    if (!failed) return this.props.children;
    if (!error || this.props.hidden) return null;
    return (
      <CrashScreen
        error={error}
        onRestart={() => this.setState({ failed: false, error: undefined })}
      />
    );
  }
}
