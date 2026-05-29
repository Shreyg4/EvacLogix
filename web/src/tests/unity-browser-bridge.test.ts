import {
  createUnityBrowserBridge,
  installUnityBrowserBridge
} from "../services/unityBrowserBridge";

function makeFile(contents: string, name: string, type: string): File {
  return new File([contents], name, { type });
}

describe("unity browser bridge", () => {
  const originalCreateElement = document.createElement.bind(document);

  afterEach(() => {
    vi.restoreAllMocks();
    delete window.EvacLogixSandboxBridge;
  });

  it("installs the bridge on window", () => {
    const bridge = installUnityBrowserBridge(window);
    expect(window.EvacLogixSandboxBridge).toBe(bridge);
    expect(bridge.isBridgeAvailable).toBe(true);
    expect(typeof bridge.setAllowedCommands).toBe("function");
  });

  it("returns cancel when the picker is closed without a file", async () => {
    vi.spyOn(document, "createElement").mockImplementation(((tagName: string) => {
      if (tagName !== "input") {
        return originalCreateElement(tagName);
      }

      const input = originalCreateElement("input") as HTMLInputElement;
      vi.spyOn(input, "click").mockImplementation(() => {
        input.dispatchEvent(new Event("change"));
      });
      return input;
    }) as typeof document.createElement);

    const bridge = createUnityBrowserBridge();
    bridge.setAllowedCommands(["ImportProjectJson"]);
    const result = await bridge.executeImportRequest({
      command: "ImportProjectJson",
      importPolicy: {
        allowedMimeTypes: ["application/json"],
        allowedExtensions: [".json"],
        maxSizeBytes: 1024
      }
    });

    expect(result.outcome).toBe("Cancelled");
  });

  it("rejects files that violate the declared import policy", async () => {
    const wrongFile = makeFile("hello", "notes.txt", "text/plain");

    vi.spyOn(document, "createElement").mockImplementation(((tagName: string) => {
      if (tagName !== "input") {
        return originalCreateElement(tagName);
      }

      const input = originalCreateElement("input") as HTMLInputElement;
      vi.spyOn(input, "files", "get").mockReturnValue([wrongFile] as unknown as FileList);
      vi.spyOn(input, "click").mockImplementation(() => {
        input.dispatchEvent(new Event("change"));
      });
      return input;
    }) as typeof document.createElement);

    const bridge = createUnityBrowserBridge();
    bridge.setAllowedCommands(["ImportProjectJson"]);
    const result = await bridge.executeImportRequest({
      command: "ImportProjectJson",
      importPolicy: {
        allowedMimeTypes: ["application/json"],
        allowedExtensions: [".json"],
        maxSizeBytes: 1024
      }
    });

    expect(result.outcome).toBe("Error");
    expect(result.errorCode).toBe("UnsupportedType");
  });

  it("returns metadata and base64 contents for allowed imports", async () => {
    const goodFile = makeFile('{"ok":true}', "project.json", "application/json");

    class FakeFileReader {
      public result: string | null = null;
      public onload: null | (() => void) = null;
      public onerror: null | (() => void) = null;

      readAsDataURL(file: File) {
        void file;
        this.result = "data:application/json;base64,eyJvayI6dHJ1ZX0=";
        this.onload?.();
      }
    }

    vi.stubGlobal("FileReader", FakeFileReader);
    vi.spyOn(document, "createElement").mockImplementation(((tagName: string) => {
      if (tagName !== "input") {
        return originalCreateElement(tagName);
      }

      const input = originalCreateElement("input") as HTMLInputElement;
      vi.spyOn(input, "files", "get").mockReturnValue([goodFile] as unknown as FileList);
      vi.spyOn(input, "click").mockImplementation(() => {
        input.dispatchEvent(new Event("change"));
      });
      return input;
    }) as typeof document.createElement);

    const bridge = createUnityBrowserBridge();
    bridge.setAllowedCommands(["ImportProjectJson"]);
    const result = await bridge.executeImportRequest({
      command: "ImportProjectJson",
      importPolicy: {
        allowedMimeTypes: ["application/json"],
        allowedExtensions: [".json"],
        maxSizeBytes: 1024
      }
    });

    expect(result.outcome).toBe("Success");
    expect(result.payload).toMatchObject({
      fileName: "project.json",
      mimeType: "application/json"
    });
    expect(result.payload?.payloadBase64).toBe("eyJvayI6dHJ1ZX0=");
  });

  it("waits for file change when focus returns before the picker resolves", async () => {
    vi.useFakeTimers();
    const goodFile = makeFile("png", "floor.png", "image/png");

    class FakeFileReader {
      public result: string | null = null;
      public onload: null | (() => void) = null;
      public onerror: null | (() => void) = null;

      readAsDataURL(file: File) {
        void file;
        this.result = "data:image/png;base64,cG5n";
        this.onload?.();
      }
    }

    vi.stubGlobal("FileReader", FakeFileReader);
    vi.spyOn(document, "createElement").mockImplementation(((tagName: string) => {
      if (tagName !== "input") {
        return originalCreateElement(tagName);
      }

      const input = originalCreateElement("input") as HTMLInputElement;
      vi.spyOn(input, "files", "get").mockReturnValue([goodFile] as unknown as FileList);
      vi.spyOn(input, "click").mockImplementation(() => {
        window.dispatchEvent(new Event("focus"));
        window.setTimeout(() => {
          input.dispatchEvent(new Event("change"));
        }, 100);
      });
      return input;
    }) as typeof document.createElement);

    const bridge = createUnityBrowserBridge();
    bridge.setAllowedCommands(["ImportBlueprintImage"]);
    const resultPromise = bridge.executeImportRequest({
      command: "ImportBlueprintImage",
      importPolicy: {
        allowedMimeTypes: ["image/png"],
        allowedExtensions: [".png"],
        maxSizeBytes: 1024
      }
    });

    await vi.advanceTimersByTimeAsync(100);
    const result = await resultPromise;

    expect(result.outcome).toBe("Success");
    expect(result.payload).toMatchObject({
      fileName: "floor.png",
      mimeType: "image/png",
      payloadBase64: "cG5n"
    });

    vi.useRealTimers();
  });

  it("downloads export payloads through a browser link", async () => {
    const clickSpy = vi.fn();
    const objectUrl = "blob:evaclogix-test";
    const originalCreateObjectUrl = URL.createObjectURL;
    const originalRevokeObjectUrl = URL.revokeObjectURL;
    URL.createObjectURL = vi.fn(() => objectUrl);
    URL.revokeObjectURL = vi.fn();

    vi.spyOn(document, "createElement").mockImplementation(((tagName: string) => {
      if (tagName !== "a") {
        return originalCreateElement(tagName);
      }

      const anchor = originalCreateElement("a") as HTMLAnchorElement;
      vi.spyOn(anchor, "click").mockImplementation(clickSpy);
      return anchor;
    }) as typeof document.createElement);

    const bridge = createUnityBrowserBridge();
    bridge.setAllowedCommands(["ExportProjectJson"]);
    const result = await bridge.executeExportRequest({
      command: "ExportProjectJson",
      exportPayload: {
        fileName: "sandbox-project.json",
        mimeType: "application/json",
        sizeBytes: 14,
        payloadBase64: "eyJvayI6dHJ1ZX0="
      }
    });

    expect(result.outcome).toBe("Success");
    expect(clickSpy).toHaveBeenCalledTimes(1);
    expect(URL.createObjectURL).toHaveBeenCalledTimes(1);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith(objectUrl);

    URL.createObjectURL = originalCreateObjectUrl;
    URL.revokeObjectURL = originalRevokeObjectUrl;
  });

  it("rejects commands that are not allowed for the active unity target", async () => {
    const bridge = createUnityBrowserBridge();
    bridge.setAllowedCommands([]);

    const result = await bridge.executeImportRequest({
      command: "ImportProjectJson",
      importPolicy: {
        allowedMimeTypes: ["application/json"],
        allowedExtensions: [".json"],
        maxSizeBytes: 1024
      }
    });

    expect(result.outcome).toBe("Error");
    expect(result.errorCode).toBe("BridgeUnavailable");
  });
});
