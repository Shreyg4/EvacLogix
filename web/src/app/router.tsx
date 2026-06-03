import { createBrowserRouter } from "react-router-dom"
import { App } from "./App"
import { appRoutes } from "../utils/routes"
import { DemoPage } from "../pages/DemoPage"
import { HomePage } from "../pages/HomePage"
import { HowItWorksPage } from "../pages/HowItWorksPage"
import { NotFoundPage } from "../pages/NotFoundPage"

export const router = createBrowserRouter([
  {
    path: "/",
    element: <App />,
    children: [
      {
        index: true,
        element: <HomePage />
      },
      {
        path: appRoutes.demo.slice(1),
        element: <DemoPage />
      },
      {
        path: appRoutes.howItWorks.slice(1),
        element: <HowItWorksPage />
      },
      {
        path: "*",
        element: <NotFoundPage />
      }
    ]
  }
])
