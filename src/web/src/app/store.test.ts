import { beforeEach, describe, expect, it } from "vitest";
import { useApp } from "./store";

describe("app store", () => {
  beforeEach(() => useApp.setState({ section: "projects", projectId: undefined }));

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
});
