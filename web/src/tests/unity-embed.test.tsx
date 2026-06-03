import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { UnityEmbed } from "../components/unity/UnityEmbed"
import type { UnityBuildConfig } from "../types/unity"
import * as unityUtils from "../utils/unity"
import { renderWithElement } from "./testUtils"

const validConfig: UnityBuildConfig = {
  loaderUrl: "/unity-build/loader.js",
  dataUrl: "/unity-build/data.data",
  frameworkUrl: "/unity-build/framework.js",
  codeUrl: "/unity-build/code.wasm",
  productName: "EvacLogix"
}

describe("unity embed", () => {
  afterEach(() => {
    vi.restoreAllMocks()
    window.localStorage.clear()
  })

  it("renders instructions without website-side simulation controls", () => {
    renderWithElement(
      <UnityEmbed
        title="Embedded Simulation Frame"
        instructions={["Step one", "Step two"]}
        fallbackMessage="Simulation unavailable"
      />
    )

    expect(screen.getByText("Step one")).toBeInTheDocument()
    expect(screen.queryByRole("slider")).not.toBeInTheDocument()
    expect(screen.queryByRole("spinbutton")).not.toBeInTheDocument()
  })

  it("lets users choose and persist the simulation aspect ratio", async () => {
    const user = userEvent.setup()

    renderWithElement(
      <UnityEmbed
        title="Embedded Simulation Frame"
        instructions={["Step one"]}
        fallbackMessage="Simulation unavailable"
      />
    )

    await user.click(screen.getByRole("button", { name: "4:3" }))

    expect(screen.getByRole("button", { name: "4:3" })).toHaveAttribute("aria-pressed", "true")
    expect(window.localStorage.getItem("evaclogix:unity-aspect-ratio")).toBe("4 / 3")
  })

  it("restores the saved simulation aspect ratio", () => {
    window.localStorage.setItem("evaclogix:unity-aspect-ratio", "3 / 2")

    renderWithElement(
      <UnityEmbed
        title="Embedded Simulation Frame"
        instructions={["Step one"]}
        fallbackMessage="Simulation unavailable"
      />
    )

    expect(screen.getByRole("button", { name: "3:2" })).toHaveAttribute("aria-pressed", "true")
  })

  it("lets users increase and decrease the simulator UI size", async () => {
    const user = userEvent.setup()

    renderWithElement(
      <UnityEmbed
        title="Embedded Simulation Frame"
        instructions={["Step one"]}
        fallbackMessage="Simulation unavailable"
      />
    )

    await user.click(screen.getByRole("button", { name: "Increase simulator UI size" }))
    expect(screen.getByText("125%")).toBeInTheDocument()
    expect(window.localStorage.getItem("evaclogix:unity-ui-scale")).toBe("1.25")

    await user.click(screen.getByRole("button", { name: "Decrease simulator UI size" }))
    expect(screen.getByText("100%")).toBeInTheDocument()
    expect(window.localStorage.getItem("evaclogix:unity-ui-scale")).toBe("1")
  })

  it("supports keyboard activation of the launch control", async () => {
    const user = userEvent.setup()
    vi.spyOn(unityUtils, "fetchUnityBuildConfig").mockResolvedValue(null)

    renderWithElement(
      <UnityEmbed
        title="Embedded Simulation Frame"
        instructions={["Step one"]}
        fallbackMessage="Simulation unavailable"
      />
    )

    for (let tabIndex = 0; tabIndex < 7; tabIndex += 1) {
      await user.tab()
    }
    expect(screen.getByRole("button", { name: "Play Simulation" })).toHaveFocus()
    await user.keyboard("{Enter}")

    await waitFor(() => {
      expect(screen.getByRole("heading", { name: "Simulation unavailable" })).toBeInTheDocument()
    })
  })

  it("enters the ready state when the loader and runtime bridge succeed", async () => {
    vi.spyOn(unityUtils, "fetchUnityBuildConfig").mockResolvedValue(validConfig)
    vi.spyOn(unityUtils, "loadUnityLoader").mockResolvedValue()
    vi.spyOn(unityUtils, "createUnityInstanceBridge").mockResolvedValue({})

    const user = userEvent.setup()
    renderWithElement(
      <UnityEmbed
        title="Embedded Simulation Frame"
        instructions={["Step one"]}
        fallbackMessage="Simulation unavailable"
      />
    )

    await user.click(await screen.findByRole("button", { name: "Play Simulation" }))

    await waitFor(() => {
      expect(screen.getByRole("heading", { name: "Simulation Ready" })).toBeInTheDocument()
    })
  })

  it("falls back when the loader rejects", async () => {
    vi.spyOn(unityUtils, "fetchUnityBuildConfig").mockResolvedValue(validConfig)
    vi.spyOn(unityUtils, "loadUnityLoader").mockRejectedValue(new Error("failed"))

    const user = userEvent.setup()
    renderWithElement(
      <UnityEmbed
        title="Embedded Simulation Frame"
        instructions={["Step one"]}
        fallbackMessage="Simulation unavailable"
      />
    )

    await user.click(await screen.findByRole("button", { name: "Play Simulation" }))

    await waitFor(() => {
      expect(screen.getByRole("heading", { name: "Simulation unavailable" })).toBeInTheDocument()
    })
  })

  it("falls back when the fetched build config is missing", async () => {
    vi.spyOn(unityUtils, "fetchUnityBuildConfig").mockResolvedValue(null)

    const user = userEvent.setup()
    renderWithElement(
      <UnityEmbed
        title="Embedded Simulation Frame"
        instructions={["Step one"]}
        fallbackMessage="Simulation unavailable"
      />
    )

    await user.click(await screen.findByRole("button", { name: "Play Simulation" }))

    await waitFor(() => {
      expect(screen.getByRole("heading", { name: "Simulation unavailable" })).toBeInTheDocument()
    })
  })
})
