import { Listeners, type EventHub } from "../shared/api/hub";

/** The hub of the demo data: connects at once and plays the events the handlers emit. */
export const fakeHub = new Listeners();

export function createFakeHub(): EventHub {
  return {
    on: (event, handler) => fakeHub.on(event, handler),
    onState: (handler) => fakeHub.onState(handler),
    start: () => setTimeout(() => fakeHub.setState("connected"), 0),
    stop: () => fakeHub.setState("disconnected"),
  };
}
