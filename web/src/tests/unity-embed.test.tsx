import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { UnityEmbed } from "../components/unity/UnityEmbed";
import type { UnityBuildConfig } from "../types/unity";
import * as unityUtils from "../utils/unity";
import { renderWithElement } from "./testUtils";

const validConfig: UnityBuildConfig = {
  loaderUrl: "/unity-build/loader.js",
  dataUrl: "/unity-build/data.data",
  frameworkUrl: "/unity-build/framework.js",
  codeUrl: "/unity-build/code.wasm",
  productName: "EvacLogix"
};

describe("unity embed", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders instructions without website-side simulation controls", () => {
    renderWithElement(
      <UnityEmbed
        title="Embedded Simulation Frame"
        instructions={["Step one", "Step two"]}
        fallbackMessage="Simulation unavailable"
      />
    );

    expect(screen.getByText("Step one")).toBeInTheDocument();
    expect(screen.queryByRole("slider")).not.toBeInTheDocument();
    expect(screen.queryByRole("spinbutton")).not.toBeInTheDocument();
  });

  it("supports keyboard activation of the launch control", async () => {
    const user = userEvent.setup();
    vi.spyOn(unityUtils, "fetchUnityBuildConfig").mockResolvedValue(null);

    renderWithElement(
      <UnityEmbed
        title="Embedded Simulation Frame"
        instructions={["Step one"]}
        fallbackMessage="Simulation unavailable"
      />
    );

    await user.tab();
    expect(screen.getByRole("button", { name: "Play Simulation" })).toHaveFocus();
    await user.keyboard("{Enter}");

    await waitFor(() => {
      expect(screen.getByRole("heading", { name: "Simulation unavailable" })).toBeInTheDocument();
    });
  });

  it("enters the ready state when the loader and runtime bridge succeed", async () => {
    vi.spyOn(unityUtils, "fetchUnityBuildConfig").mockResolvedValue(validConfig);
    vi.spyOn(unityUtils, "loadUnityLoader").mockResolvedValue();
    vi.spyOn(unityUtils, "createUnityInstanceBridge").mockResolvedValue({});

    const user = userEvent.setup();
    renderWithElement(
      <UnityEmbed
        title="Embedded Simulation Frame"
        instructions={["Step one"]}
        fallbackMessage="Simulation unavailable"
      />
    );

    await user.click(await screen.findByRole("button", { name: "Play Simulation" }));

    await waitFor(() => {
      expect(screen.getByRole("heading", { name: "Simulation Ready" })).toBeInTheDocument();
    });
  });

  it("falls back when the loader rejects", async () => {
    vi.spyOn(unityUtils, "fetchUnityBuildConfig").mockResolvedValue(validConfig);
    vi.spyOn(unityUtils, "loadUnityLoader").mockRejectedValue(new Error("failed"));

    const user = userEvent.setup();
    renderWithElement(
      <UnityEmbed
        title="Embedded Simulation Frame"
        instructions={["Step one"]}
        fallbackMessage="Simulation unavailable"
      />
    );

    await user.click(await screen.findByRole("button", { name: "Play Simulation" }));

    await waitFor(() => {
      expect(screen.getByRole("heading", { name: "Simulation unavailable" })).toBeInTheDocument();
    });
  });

  it("falls back when the fetched build config is missing", async () => {
    vi.spyOn(unityUtils, "fetchUnityBuildConfig").mockResolvedValue(null);

    const user = userEvent.setup();
    renderWithElement(
      <UnityEmbed
        title="Embedded Simulation Frame"
        instructions={["Step one"]}
        fallbackMessage="Simulation unavailable"
      />
    );

    await user.click(await screen.findByRole("button", { name: "Play Simulation" }));

    await waitFor(() => {
      expect(screen.getByRole("heading", { name: "Simulation unavailable" })).toBeInTheDocument();
    });
  });
});
