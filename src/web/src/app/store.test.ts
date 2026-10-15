import { beforeEach, describe, expect, it } from "vitest";
import { useApp } from "./store";

describe("app store", () => {
  beforeEach(() =>
    useApp.setState({ section: "projects", projectId: undefined, returnTo: "editor" }),
  );

  it("opens a project in the editor from any section", () => {
    useApp.getState().openProject("p-1");
    expect(useApp.getState()).toMatchObject({ section: "editor", projectId: "p-1" });
  });

  it("closes the project and stays where the user is", () => {
    useApp.getState().openProject("p-1");
    useApp.getState().setSection("settings");
    useApp.getState().closeProject();

    expect(useApp.getState().projectId).toBeUndefined();
    expect(useApp.getState().section).toBe("settings");
  });

  describe("«Назад»", () => {
    it("closes the project and returns to the section it was opened from", () => {
      useApp.setState({ section: "packs" });
      useApp.getState().openProject("p-1");
      useApp.getState().goBack();

      expect(useApp.getState()).toMatchObject({ section: "packs", projectId: undefined });
    });

    it("leads to the start screen for a project opened there", () => {
      useApp.setState({ section: "editor" });
      useApp.getState().openProject("p-1");
      useApp.getState().goBack();

      expect(useApp.getState()).toMatchObject({ section: "editor", projectId: undefined });
    });

    it("keeps the way back when another project opens from inside the editor", () => {
      useApp.getState().openProject("p-1");
      useApp.getState().openProject("p-2");
      useApp.getState().goBack();

      expect(useApp.getState().section).toBe("projects");
    });

    it("forgets the way back once used, so the next project from the start screen returns there", () => {
      useApp.getState().openProject("p-1");
      useApp.getState().goBack();
      useApp.getState().setSection("editor");
      useApp.getState().openProject("p-2");
      useApp.getState().goBack();

      expect(useApp.getState().section).toBe("editor");
    });
  });
});
