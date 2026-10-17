import { create } from "zustand";

/** Telegram's signature did not pass (401 from the service, or no initData at all):
 *  «Не получилось войти» replaces every screen (artboard 14, state 4). */
export const useSession = create<{ signedOut: boolean }>()(() => ({ signedOut: false }));

export function signOut() {
  useSession.setState({ signedOut: true });
}
