import { useEffect, useState } from "react";
import { fetchFile } from "../../../shared/api/client";

/** A rendered file (GET /api/v1/files/{fileId}) as an address a <video> can play: fetched once
 *  with the Bearer header and kept in memory while it is shown. A sticker is at most 256 КБ. */
export function useFileUrl(fileUrl: string | undefined): string | undefined {
  const [url, setUrl] = useState<{ of: string; url: string }>();

  useEffect(() => {
    if (!fileUrl) return undefined;
    const abort = new AbortController();
    let made: string | undefined;
    fetchFile(fileUrl, abort.signal).then(
      (blob) => {
        made = URL.createObjectURL(blob);
        setUrl({ of: fileUrl, url: made });
      },
      () => {}, // no file to show: the tile stays a checkerboard
    );
    return () => {
      abort.abort();
      if (made) URL.revokeObjectURL(made);
    };
  }, [fileUrl]);

  return url && url.of === fileUrl ? url.url : undefined;
}
