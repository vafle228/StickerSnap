// @vitest-environment happy-dom
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it } from "vitest";
import { useMockServer, withProviders } from "../../test/dom";
import { useApp, type Section } from "../../app/store";
import { EditorHeader } from "./EditorHeader";

/* «Назад» in the editor's header: it names where it leads and takes the user there. */

function renderOpenedFrom(section: Section) {
  useApp.setState({ section, projectId: undefined, returnTo: "editor" });
  useApp.getState().openProject("p-1");
  render(<EditorHeader ready />, { wrapper: withProviders() });
}

describe("«Назад» in the editor", () => {
  beforeEach(() => {
    useMockServer("ok");
  });

  it("says «Проекты» and returns there with the project closed", async () => {
    renderOpenedFrom("projects");

    await userEvent.click(screen.getByRole("button", { name: "Назад: Проекты" }));

    expect(useApp.getState()).toMatchObject({ section: "projects", projectId: undefined });
  });

  it("says «Мои паки» for a project opened from a pack", async () => {
    renderOpenedFrom("packs");

    await userEvent.click(screen.getByRole("button", { name: "Назад: Мои паки" }));

    expect(useApp.getState().section).toBe("packs");
  });

  it("says «Назад» and leads to the start screen for a project opened there", async () => {
    renderOpenedFrom("editor");

    await userEvent.click(screen.getByRole("button", { name: "Назад" }));

    expect(useApp.getState()).toMatchObject({ section: "editor", projectId: undefined });
  });
});
