// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DropZone } from "./DropZone";

/* The import zone with controls of its own (artboard 20·2): the link field sits in the zone but
   is not part of its button, and a link dropped from a browser reaches onText, not onFiles. */

afterEach(cleanup);

/** A drop of `types` the way a browser carries them. */
function drop(target: Element, data: Record<string, string>, files: File[] = []) {
  fireEvent.drop(target, {
    dataTransfer: { files, types: Object.keys(data), getData: (type: string) => data[type] ?? "" },
  });
}

describe("DropZone", () => {
  it("is one button without children", () => {
    const onClick = vi.fn();
    render(<DropZone title="Перетащите видео" action="Выбрать видео…" onClick={onClick} />);

    fireEvent.click(screen.getByRole("button", { name: /Перетащите видео/ }));

    expect(onClick).toHaveBeenCalledOnce();
  });

  it("keeps its children out of the button: a click on them opens no file picker", () => {
    const onClick = vi.fn();
    const onOpen = vi.fn();
    render(
      <DropZone title="Перетащите видео" action="Выбрать видео…" onClick={onClick}>
        <button type="button" onClick={onOpen}>
          Открыть
        </button>
      </DropZone>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Открыть" }));
    expect(onOpen).toHaveBeenCalledOnce();
    expect(onClick).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: /Перетащите видео/ }));
    expect(onClick).toHaveBeenCalledOnce();
    expect(screen.getByRole("button", { name: /Перетащите видео/ }).textContent).not.toContain(
      "Открыть",
    );
  });

  it("gives a dropped link to onText and a dropped file to onFiles", () => {
    const onText = vi.fn();
    const onFiles = vi.fn();
    const { container } = render(
      <DropZone title="Перетащите видео" onText={onText} onFiles={onFiles}>
        <span>поле</span>
      </DropZone>,
    );
    const zone = container.querySelector(".ss-drop") as Element;

    drop(zone, { "text/uri-list": "https://youtu.be/abc\r\nhttps://youtu.be/second" });
    expect(onText).toHaveBeenCalledWith("https://youtu.be/abc");

    const file = new File(["x"], "cat.mp4");
    drop(zone, { Files: "" }, [file]);
    expect(onFiles).toHaveBeenCalledWith([file]);
  });

  it("takes no drop while busy", () => {
    const onText = vi.fn();
    const { container } = render(<DropZone busy onText={onText} />);

    drop(container.querySelector(".ss-drop") as Element, { "text/uri-list": "https://youtu.be/a" });

    expect(onText).not.toHaveBeenCalled();
  });
});
