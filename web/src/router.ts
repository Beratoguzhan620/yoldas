import { useEffect, useState } from "react";

/** The pages of the app. Ids and filters are in the address, so every page can be bookmarked and shared. */
export type Route =
  | { page: "signin"; next?: string }
  | { page: "register"; next?: string }
  | { page: "search"; params: URLSearchParams }
  | { page: "trip"; tripId: string; params: URLSearchParams }
  | { page: "rides" }
  | { page: "ride"; bookingId: string }
  | { page: "drive" }
  | { page: "offer" }
  | { page: "trip-edit"; tripId: string }
  | { page: "trip-day"; tripId: string }
  | { page: "car"; next?: string }
  | { page: "profile"; userId: string }
  | { page: "not-found" };

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const isId = (value: string | undefined): value is string => !!value && guid.test(value);

/** Only addresses within the app are followed after signing in. */
export function safeNext(value: string | null): string | undefined {
  return value && value.startsWith("/") && !value.startsWith("//") ? value : undefined;
}

export function parseRoute(pathname: string, search = ""): Route {
  const params = new URLSearchParams(search);
  const parts = pathname.split("/").filter(Boolean).map(decodeURIComponent);
  const [a, b, c] = parts;
  const n = parts.length;

  if (n === 0) return { page: "search", params };
  if (n === 1 && a === "signin") return { page: "signin", next: safeNext(params.get("next")) };
  if (n === 1 && a === "register") return { page: "register", next: safeNext(params.get("next")) };
  if (n === 2 && a === "t" && isId(b)) return { page: "trip", tripId: b, params };
  if (n === 1 && a === "rides") return { page: "rides" };
  if (n === 2 && a === "rides" && isId(b)) return { page: "ride", bookingId: b };
  if (n === 1 && a === "drive") return { page: "drive" };
  if (n === 2 && a === "drive" && b === "new") return { page: "offer" };
  if (n === 3 && a === "drive" && isId(b) && c === "edit") return { page: "trip-edit", tripId: b };
  if (n === 2 && a === "drive" && isId(b)) return { page: "trip-day", tripId: b };
  if (n === 1 && a === "car") return { page: "car", next: safeNext(params.get("next")) };
  if (n === 2 && a === "u" && isId(b)) return { page: "profile", userId: b };
  return { page: "not-found" };
}

/** Pages anyone can open, signed in or not. */
export const isPublic = (route: Route) => ["search", "trip", "profile", "not-found"].includes(route.page);

export function navigate(path: string, replace = false) {
  if (replace) history.replaceState(null, "", path);
  else history.pushState(null, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
  if (!replace) window.scrollTo({ top: 0 });
}

export function useRoute(): Route {
  const read = () => parseRoute(location.pathname, location.search);
  const [route, setRoute] = useState<Route>(read);
  useEffect(() => {
    const update = () => setRoute(read());
    window.addEventListener("popstate", update);
    return () => window.removeEventListener("popstate", update);
  }, []);
  return route;
}

/** Props for an anchor that moves within the app without reloading the page. */
export function linkProps(href: string) {
  return {
    href,
    onClick: (e: React.MouseEvent) => {
      if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
      e.preventDefault();
      navigate(href);
    },
  };
}

/** The sign-in page, coming back here afterwards. */
export const signInHere = () => `/signin?next=${encodeURIComponent(location.pathname + location.search)}`;
