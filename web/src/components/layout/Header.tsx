import { NavLink } from "react-router-dom";
import { siteContent } from "../../content/siteContent";

export function Header() {
  return (
    <header className="app-header">
      <div className="brand-block">
        <span className="brand-mark" aria-hidden="true">
          EL
        </span>
        <div>
          <p className="brand-name">{siteContent.brandName}</p>
          <p className="brand-tagline">{siteContent.brandTagline}</p>
        </div>
      </div>
      <nav aria-label="Primary">
        <ul className="nav-list">
          {siteContent.navigation.map((item) => (
            <li key={item.to}>
              <NavLink
                to={item.to}
                end={item.end}
                className={({ isActive }) => (isActive ? "nav-link active" : "nav-link")}
              >
                {item.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>
    </header>
  );
}
