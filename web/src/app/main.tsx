import React from "react"
import ReactDOM from "react-dom/client"
import { RouterProvider } from "react-router-dom"
import { router } from "./router"
import { installUnityBrowserBridge } from "../services/unityBrowserBridge"
import "../styles/tokens.css"
import "../styles/globals.css"
import "../styles/utilities.css"
import "../styles/docs.css"

installUnityBrowserBridge(window)

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <RouterProvider router={router} />
  </React.StrictMode>
)
