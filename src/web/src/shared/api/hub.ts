import { HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import { serverConnection } from "../platform/connection";
import { HUB_EVENTS, HUB_PATH, type HubEvent, type HubEvents } from "./events";

export type HubState = "connecting" | "connected" | "reconnecting" | "disconnected";

/** The event hub as the SPA sees it: typed events in, no calls out (all commands are REST). */
export interface EventHub {
  on<E extends HubEvent>(event: E, handler: (payload: HubEvents[E]) => void): () => void;
  onState(handler: (state: HubState) => void): () => void;
  start(): void;
  stop(): void;
}

/** Shared listener bookkeeping for the real hub and the fake one of the demo data. */
export class Listeners {
  private events = new Map<HubEvent, Set<(payload: never) => void>>();
  private states = new Set<(state: HubState) => void>();

  on<E extends HubEvent>(event: E, handler: (payload: HubEvents[E]) => void) {
    const set = this.events.get(event) ?? new Set();
    set.add(handler);
    this.events.set(event, set);
    return () => void set.delete(handler);
  }

  onState(handler: (state: HubState) => void) {
    this.states.add(handler);
    return () => void this.states.delete(handler);
  }

  emit<E extends HubEvent>(event: E, payload: HubEvents[E]) {
    for (const handler of this.events.get(event) ?? [])
      (handler as (p: HubEvents[E]) => void)(payload);
  }

  setState(state: HubState) {
    for (const handler of this.states) handler(state);
  }
}

const retryDelay = (attempt: number) => Math.min(30_000, 1_000 * 2 ** attempt);

/** /hubs/events over SignalR. It never gives up: after a drop it reconnects with a growing
 *  pause up to 30 s, and a failed first start is retried the same way. */
export function signalRHub(): EventHub {
  const { baseUrl, token } = serverConnection();
  const listeners = new Listeners();
  const connection = new HubConnectionBuilder()
    .withUrl(new URL(HUB_PATH, baseUrl).href, token ? { accessTokenFactory: () => token } : {})
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (ctx) => retryDelay(ctx.previousRetryCount),
    })
    .configureLogging(LogLevel.Warning)
    .build();

  for (const event of HUB_EVENTS) {
    connection.on(event, (payload: HubEvents[typeof event]) => listeners.emit(event, payload));
  }

  let stopped = false;
  let timer: ReturnType<typeof setTimeout> | undefined;

  const start = (attempt: number) => {
    listeners.setState(attempt === 0 ? "connecting" : "reconnecting");
    connection.start().then(
      () => listeners.setState("connected"),
      () => {
        if (stopped) return;
        listeners.setState("disconnected");
        timer = setTimeout(() => start(attempt + 1), retryDelay(attempt));
      },
    );
  };

  connection.onreconnecting(() => listeners.setState("reconnecting"));
  connection.onreconnected(() => listeners.setState("connected"));
  connection.onclose(() => {
    listeners.setState("disconnected");
    if (!stopped) start(1);
  });

  return {
    on: (event, handler) => listeners.on(event, handler),
    onState: (handler) => listeners.onState(handler),
    start() {
      stopped = false;
      if (connection.state === HubConnectionState.Disconnected) start(0);
    },
    stop() {
      stopped = true;
      clearTimeout(timer);
      void connection.stop();
    },
  };
}

let factory: () => EventHub = signalRHub;

/** The demo data (?mock) swaps in a fake hub that plays events by timer. */
export function setHubFactory(fake: () => EventHub) {
  factory = fake;
}

export function createHub(): EventHub {
  return factory();
}
