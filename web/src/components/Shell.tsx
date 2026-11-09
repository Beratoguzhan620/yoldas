import { Car, LogOut, Menu, Route as RouteIcon, Search, Ticket, UserRound, X } from "lucide-react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { useMaybeAccount } from "../context";
import { initials } from "../format";
import { linkProps, navigate, signInHere, type Route } from "../router";

function UserMenu() {
  const account = useMaybeAccount()!;
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, [open]);

  const go = (href: string) => (e: React.MouseEvent) => { e.preventDefault(); setOpen(false); navigate(href); };

  return (
    <div className="user-menu" ref={ref}>
      <button className="avatar-button" onClick={() => setOpen(!open)} aria-expanded={open} aria-haspopup="menu" aria-label={`Account: ${account.session.user.displayName}`}>
        <span className="avatar">{initials(account.session.user.displayName)}</span>
      </button>
      {open && (
        <div className="menu" role="menu">
          <div className="menu-head">
            <strong className="ellipsis block">{account.session.user.displayName}</strong>
            <span className="small muted ellipsis block">{account.session.user.email}</span>
          </div>
          <a role="menuitem" className="menu-item" href={`/u/${account.session.user.id}`} onClick={go(`/u/${account.session.user.id}`)}><UserRound size={15} />Your profile</a>
          <a role="menuitem" className="menu-item" href="/car" onClick={go("/car")}><Car size={15} />Your car</a>
          <div className="menu-divider" />
          <button role="menuitem" className="menu-item" onClick={account.signOut}><LogOut size={15} />Sign out</button>
        </div>
      )}
    </div>
  );
}

const sections = [
  { href: "/", label: "Find a ride", icon: Search, pages: ["search", "trip"] },
  { href: "/rides", label: "Your rides", icon: Ticket, pages: ["rides", "ride"] },
  { href: "/drive", label: "Drive", icon: Car, pages: ["drive", "offer", "trip-edit", "trip-day", "car"] },
] as const;

export function Brand({ light = false }: { light?: boolean }) {
  return <a className={`brand${light ? " light" : ""}`} {...linkProps("/")} aria-label="Yoldaş, home"><span className="brand-mark"><RouteIcon size={16} /></span>Yoldaş</a>;
}

/** The frame: finding a ride, your rides and driving along the top, the page below. Visitors can search without an account. */
export function Shell({ route, children, wide = false }: { route: Route; children: ReactNode; wide?: boolean }) {
  const account = useMaybeAccount();
  const [menu, setMenu] = useState(false);
  useEffect(() => setMenu(false), [route]);

  const links = account && sections.map((s) => {
    const active = (s.pages as readonly string[]).includes(route.page);
    const Icon = s.icon;
    return (
      <a key={s.href} {...linkProps(s.href)} className={`top-link${active ? " active" : ""}`} aria-current={active ? "page" : undefined}>
        <Icon size={15} />{s.label}
      </a>
    );
  });

  return (
    <div className="app">
      <header className="topbar">
        <div className="topbar-inner">
          <Brand />
          <nav className="top-links" aria-label="Main">{links}</nav>
          <div className="grow" />
          {account ? <UserMenu /> : (
            <div className="row gap-6">
              <a className="button small ghost" {...linkProps(signInHere())}>Sign in</a>
              <a className="button small primary" {...linkProps("/register")}>Create an account</a>
            </div>
          )}
          {links && (
            <button className="icon-button only-sm" onClick={() => setMenu(!menu)} aria-label={menu ? "Close menu" : "Open menu"} aria-expanded={menu}>
              {menu ? <X size={20} /> : <Menu size={20} />}
            </button>
          )}
        </div>
        {menu && links && <nav className="mobile-links" aria-label="Main">{links}</nav>}
      </header>
      <main className={`page${wide ? " wide" : ""}`}>{children}</main>
    </div>
  );
}
