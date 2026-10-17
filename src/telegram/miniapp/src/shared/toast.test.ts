import { beforeEach, describe, expect, it } from "vitest";
import { useToast } from "./toast";

beforeEach(() => useToast.setState({ current: undefined }));

describe("popups", () => {
  it("show one at a time: a new one replaces the old", () => {
    const { show } = useToast.getState();
    show({ tone: "danger", title: "Стикер не удалён" });
    show({ tone: "success", title: "Стикер удалён из пака" });
    expect(useToast.getState().current?.title).toBe("Стикер удалён из пака");
  });

  it("closing an old one leaves the new one", () => {
    const { show } = useToast.getState();
    show({ tone: "danger", title: "Стикер не удалён" });
    const old = useToast.getState().current?.id ?? 0;
    show({ tone: "success", title: "Стикер удалён из пака" });
    useToast.getState().close(old);
    expect(useToast.getState().current?.title).toBe("Стикер удалён из пака");
  });
});
