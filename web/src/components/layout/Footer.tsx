import { siteContent } from "../../content/siteContent"

export function Footer() {
  return (
    <footer className="app-footer">
      <p>{siteContent.footer.attribution}</p>
      <p>{siteContent.footer.context}</p>
    </footer>
  )
}
