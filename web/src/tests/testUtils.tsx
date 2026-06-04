import { ReactElement } from "react"
import { render } from "@testing-library/react"
import { createMemoryRouter, RouterProvider } from "react-router-dom"
import { App } from "../app/App"
import { DemoPage } from "../pages/DemoPage"
import { HomePage } from "../pages/HomePage"
import { HowItWorksPage } from "../pages/HowItWorksPage"
import { TechnicalDocsPage } from "../pages/TechnicalDocsPage"
import { UserGuidePage } from "../pages/UserGuidePage"
import { NotFoundPage } from "../pages/NotFoundPage"

const routes = [
  {
    path: "/",
    element: <App />,
    children: [
      { index: true, element: <HomePage /> },
      { path: "demo", element: <DemoPage /> },
      { path: "how-it-works", element: <HowItWorksPage /> },
      { path: "architecture", element: <TechnicalDocsPage /> },
      { path: "user-guide", element: <UserGuidePage /> },
      { path: "*", element: <NotFoundPage /> }
    ]
  }
]

export function renderAppAt(route: string): ReturnType<typeof render> {
  const router = createMemoryRouter(routes, {
    initialEntries: [route]
  })

  return render(<RouterProvider router={router} />)
}

export function renderWithElement(element: ReactElement): ReturnType<typeof render> {
  return render(element)
}
