import { api, call } from "../api/client";
import type { SourceRef } from "../api/types";
import type { LocalFile } from "./platform";

/** POST /api/v1/uploads: the bytes go to the local server, the scene keeps only the uploadId. */
export async function upload({ file, path }: LocalFile, signal?: AbortSignal): Promise<SourceRef> {
  if (path) return { kind: "path", path };
  if (!file) throw new Error("A local file has neither bytes nor a path");
  const { uploadId } = await call(
    api.POST("/api/v1/uploads", {
      signal,
      // schema.d.ts types the binary part as a string; the serializer sends the File itself
      body: { file: file as unknown as string },
      bodySerializer: () => {
        const form = new FormData();
        form.append("file", file);
        return form;
      },
    }),
  );
  return { kind: "upload", uploadId };
}
