import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { keys } from "../../../shared/api/queries";
import type { Project } from "../../../shared/api/types";
import { startAutosave } from "./autosave";

/** Autosave for as long as the editor is mounted. A saved scene goes into the project's cached
 *  copy too: the editor reads it from there when the project is opened again. */
export function useAutosave() {
  const client = useQueryClient();
  useEffect(
    () =>
      startAutosave({
        onSaved: (id, scene, { revision, savedAt }) =>
          client.setQueryData<Project>(keys.project(id), (project) =>
            project ? { ...project, scene, revision, savedAt } : project,
          ),
      }),
    [client],
  );
}
