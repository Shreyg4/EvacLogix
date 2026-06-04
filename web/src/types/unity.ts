export type UnityBuildConfig = {
  loaderUrl: string
  dataUrl: string
  frameworkUrl: string
  codeUrl: string
  companyName?: string
  productName: string
  productVersion?: string
  autoSyncPersistentDataPath?: boolean
  // Per-file cache policy honored by the Unity loader. "must-revalidate"/"immutable" route the file
  // through Unity's IndexedDB cache; "no-store" (the loader's default for the wasm) re-downloads it
  // every visit. Set in code (it's a function, so it cannot come from the build JSON).
  cacheControl?: (url: string) => "must-revalidate" | "immutable" | "no-store"
}
