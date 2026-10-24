import { isActive } from "../../../shared/api/live";
import { useDownloads } from "../../../shared/api/queries";
import type { Download } from "../../../shared/api/types";
import { useLinkImport } from "./linkImport";

/** The download the zone shows, while it is on its way. */
export function useShownDownload(): Download | undefined {
  const id = useLinkImport((s) => s.downloadId);
  const download = useDownloads().data?.find((d) => d.id === id);
  return download && isActive(download) ? download : undefined;
}

/** Whether the zone gives its place to the card: a slow probe or a download on its way. */
export function useLinkBusy(): boolean {
  const slow = useLinkImport((s) => s.probing?.slow ?? false);
  const download = useShownDownload();
  return slow || download !== undefined;
}
