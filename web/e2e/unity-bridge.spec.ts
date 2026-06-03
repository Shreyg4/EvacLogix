import { expect, test } from "@playwright/test"
import { promises as fs } from "node:fs"

test.describe("Unity browser bridge", () => {
  test("blocks sandbox file commands on the default demo profile", async ({ page }) => {
    await page.goto("/demo")

    const response = await page.evaluate(async () => {
      return window.EvacLogixSandboxBridge!.executeImportRequest({
        command: "ImportProjectJson",
        importPolicy: {
          allowedMimeTypes: ["application/json"],
          allowedExtensions: [".json"],
          maxSizeBytes: 1024 * 1024
        }
      })
    })

    expect(response.outcome).toBe("Error")
    expect(response.errorCode).toBe("BridgeUnavailable")
    expect(response.message).toContain("not allowed")
  })

  test("imports project json through the sandbox-editor bridge", async ({ page }) => {
    await page.goto("/demo?app=sandbox-editor")

    const expectedJson = JSON.stringify({
      projectId: "playwright-project",
      floors: [{ floorId: "floor-1", name: "Floor 1" }]
    })
    const expectedBase64 = Buffer.from(expectedJson, "utf8").toString("base64")

    const fileChooserPromise = page.waitForEvent("filechooser")
    const responsePromise = page.evaluate(async () => {
      return window.EvacLogixSandboxBridge!.executeImportRequest({
        command: "ImportProjectJson",
        importPolicy: {
          allowedMimeTypes: ["application/json", "text/json", "text/plain"],
          allowedExtensions: [".json"],
          maxSizeBytes: 5 * 1024 * 1024
        }
      })
    })

    const fileChooser = await fileChooserPromise
    await fileChooser.setFiles({
      name: "sandbox-project.json",
      mimeType: "application/json",
      buffer: Buffer.from(expectedJson, "utf8")
    })

    const response = await responsePromise
    expect(response.outcome).toBe("Success")
    expect(response.errorCode).toBe("None")
    expect(response.payload?.fileName).toBe("sandbox-project.json")
    expect(response.payload?.mimeType).toBe("application/json")
    expect(response.payload?.payloadBase64).toBe(expectedBase64)
  })

  test("imports blueprint images through the sandbox-editor bridge", async ({ page }) => {
    await page.goto("/demo?app=sandbox-editor")

    const pngBase64 =
      "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9sX2VJ8AAAAASUVORK5CYII="
    const pngBuffer = Buffer.from(pngBase64, "base64")

    const fileChooserPromise = page.waitForEvent("filechooser")
    const responsePromise = page.evaluate(async () => {
      return window.EvacLogixSandboxBridge!.executeImportRequest({
        command: "ImportBlueprintImage",
        importPolicy: {
          allowedMimeTypes: [
            "image/png",
            "image/jpeg",
            "image/webp",
            "image/bmp",
            "image/gif",
            "image/tiff"
          ],
          allowedExtensions: [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff"],
          maxSizeBytes: 25 * 1024 * 1024
        }
      })
    })

    const fileChooser = await fileChooserPromise
    await fileChooser.setFiles({
      name: "blueprint.png",
      mimeType: "image/png",
      buffer: pngBuffer
    })

    const response = await responsePromise
    expect(response.outcome).toBe("Success")
    expect(response.errorCode).toBe("None")
    expect(response.payload?.fileName).toBe("blueprint.png")
    expect(response.payload?.mimeType).toBe("image/png")
    expect(response.payload?.sizeBytes).toBe(pngBuffer.byteLength)
    expect(response.payload?.payloadBase64).toBe(pngBase64)
  })

  test("downloads exported project json through the sandbox-editor bridge", async ({
    page
  }, testInfo) => {
    await page.goto("/demo?app=sandbox-editor")

    const exportJson = JSON.stringify({
      projectId: "exported-project",
      name: "Sandbox Export"
    })

    const downloadPromise = page.waitForEvent("download")
    const responsePromise = page.evaluate(async (payloadBase64) => {
      return window.EvacLogixSandboxBridge!.executeExportRequest({
        command: "ExportProjectJson",
        exportPayload: {
          fileName: "sandbox-export.json",
          mimeType: "application/json",
          sizeBytes: payloadBase64.length,
          payloadBase64
        }
      })
    }, Buffer.from(exportJson, "utf8").toString("base64"))

    const [download, response] = await Promise.all([downloadPromise, responsePromise])
    expect(response.outcome).toBe("Success")
    expect(response.errorCode).toBe("None")
    expect(download.suggestedFilename()).toBe("sandbox-export.json")

    const savedPath = testInfo.outputPath("sandbox-export.json")
    await download.saveAs(savedPath)
    const downloadedContent = await fs.readFile(savedPath, "utf8")
    expect(downloadedContent).toBe(exportJson)
  })

  test("resolves cancelled imports and allows another import afterward", async ({ page }) => {
    await page.goto("/demo?app=sandbox-editor")

    await page.evaluate(() => {
      const win = window as Window & {
        __evacOriginalFileClick?: typeof HTMLInputElement.prototype.click
        __evacCancelNextFilePicker?: boolean
      }

      if (!win.__evacOriginalFileClick) {
        win.__evacOriginalFileClick = HTMLInputElement.prototype.click
        HTMLInputElement.prototype.click = function patchedFileClick(this: HTMLInputElement) {
          if (this.type === "file" && win.__evacCancelNextFilePicker) {
            win.__evacCancelNextFilePicker = false
            window.setTimeout(() => {
              this.dispatchEvent(new Event("cancel"))
              window.dispatchEvent(new Event("focus"))
            }, 0)
            return
          }

          return win.__evacOriginalFileClick!.call(this)
        }
      }

      win.__evacCancelNextFilePicker = true
    })

    const cancelledResponse = await page.evaluate(async () => {
      return window.EvacLogixSandboxBridge!.executeImportRequest({
        command: "ImportProjectJson",
        importPolicy: {
          allowedMimeTypes: ["application/json"],
          allowedExtensions: [".json"],
          maxSizeBytes: 5 * 1024 * 1024
        }
      })
    })

    expect(cancelledResponse.outcome).toBe("Cancelled")
    expect(cancelledResponse.errorCode).toBe("None")

    const secondJson = JSON.stringify({ projectId: "after-cancel" })
    const chooserPromise = page.waitForEvent("filechooser")
    const successPromise = page.evaluate(async () => {
      return window.EvacLogixSandboxBridge!.executeImportRequest({
        command: "ImportProjectJson",
        importPolicy: {
          allowedMimeTypes: ["application/json"],
          allowedExtensions: [".json"],
          maxSizeBytes: 5 * 1024 * 1024
        }
      })
    })

    const chooser = await chooserPromise
    await chooser.setFiles({
      name: "after-cancel.json",
      mimeType: "application/json",
      buffer: Buffer.from(secondJson, "utf8")
    })

    const successResponse = await successPromise
    expect(successResponse.outcome).toBe("Success")
    expect(successResponse.payload?.fileName).toBe("after-cancel.json")
  })
})
