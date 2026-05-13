import { expect, test } from "@playwright/test";

test.describe("EvacLogix presentation site", () => {
  test("loads home with landmarks, mission, and CTA flow", async ({ page }) => {
    await page.goto("/");

    await expect(page.getByRole("banner")).toBeVisible();
    await expect(page.getByRole("navigation")).toBeVisible();
    await expect(page.getByRole("main")).toBeVisible();
    await expect(page.getByRole("contentinfo")).toBeVisible();
    await expect(
      page.getByRole("heading", {
        name: "EvacLogix helps viewers explore how people evacuate buildings under pressure."
      })
    ).toBeVisible();
    await expect(
      page.getByText("Explore evacuation behavior through an interactive building-safety simulation.")
    ).toBeVisible();

    await page.getByRole("link", { name: "Launch Demo" }).click();
    await expect(page).toHaveURL(/\/demo$/);
  });

  test("routes to How It Works and shows high-level explanatory panels", async ({ page }) => {
    await page.goto("/");

    await page.getByRole("navigation").getByRole("link", { name: "How It Works", exact: true }).click();

    await expect(page).toHaveURL(/\/how-it-works$/);
    await expect(
      page.getByRole("heading", { name: "A high-level look at what the simulation is meant to show." })
    ).toBeVisible();
    await expect(page.getByRole("heading", { name: "Building Modeling" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Evacuation Behavior" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Hazard and Congestion Context" })).toBeVisible();
  });

  test("falls back gracefully when Unity assets are unavailable", async ({ page }) => {
    await page.goto("/demo");

    await expect(page.locator("#unity-embed-title")).toBeVisible();
    await expect(page.getByRole("button", { name: "Play Simulation" })).toBeVisible();

    await page.getByRole("button", { name: "Play Simulation" }).click();

    await expect(page.getByRole("heading", { name: "Simulation unavailable" })).toBeVisible();
    await expect(
      page.getByText("The current build could not be launched from this embedded frame.")
    ).toBeVisible();
    await expect(page.getByRole("link", { name: "Open Unity Play" })).toBeVisible();
  });

  test("keeps the homepage usable at a smaller viewport", async ({ page }) => {
    await page.setViewportSize({ width: 900, height: 900 });
    await page.goto("/");

    await expect(
      page.getByRole("heading", {
        name: "EvacLogix helps viewers explore how people evacuate buildings under pressure."
      })
    ).toBeVisible();
    await expect(page.getByRole("link", { name: "Launch Demo" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Learn How It Works" })).toBeVisible();
  });
});
