import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderAppAt } from "./testUtils";

describe("demo page", () => {
  beforeEach(() => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(null, {
        status: 404
      })
    );
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("shows a visible launch control on first render", async () => {
    renderAppAt("/demo");

    expect(await screen.findByRole("button", { name: "Play Simulation" })).toBeInTheDocument();
    expect(screen.getByLabelText("Unity simulation frame")).toBeInTheDocument();
  });

  it("transitions to the unavailable fallback after launch", async () => {
    const user = userEvent.setup();
    renderAppAt("/demo");

    await user.click(await screen.findByRole("button", { name: "Play Simulation" }));

    await waitFor(() => {
      expect(screen.getByRole("heading", { name: "Simulation unavailable" })).toBeInTheDocument();
    });

    expect(screen.getByText(/current build could not be launched/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Unity Play" })).toBeInTheDocument();
  });

  it("keeps guidance visible throughout the fallback flow", async () => {
    const user = userEvent.setup();
    renderAppAt("/demo");

    await user.click(await screen.findByRole("button", { name: "Play Simulation" }));

    await waitFor(() => {
      expect(screen.getByText("Press play to attempt launching the embedded simulation.")).toBeInTheDocument();
    });
  });
});
