import { Route as RouteIcon } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api, setToken } from "./api";
import { Shell } from "./components/Shell";
import { Empty, Failed, Loading } from "./components/States";
import { AccountCtx, type Account } from "./context";
import { Register, SignIn } from "./pages/Auth";
import { CarPage, Drive } from "./pages/Drive";
import { Profile } from "./pages/Profile";
import { RidePage } from "./pages/RidePage";
import { Rides } from "./pages/Rides";
import { SearchPage } from "./pages/SearchPage";
import { TripDay } from "./pages/TripDay";
import { TripEdit } from "./pages/TripEdit";
import { TripPage } from "./pages/TripPage";
import { isPublic, linkProps, navigate, signInHere, useRoute, type Route } from "./router";
import { loadSession, saveSession } from "./session";
import type { Me, Session } from "./types";

function NotFound() {
  return (
    <Empty title="This page does not exist" icon={<RouteIcon size={26} />}>
      <span className="muted">The address may be mistyped, or the trip was taken down.</span>
      <a className="button" {...linkProps("/")}>Find a ride</a>
    </Empty>
  );
}

function Page({ route, account }: { route: Route; account: Account | null }) {
  switch (route.page) {
    case "search": return <SearchPage params={route.params} />;
    case "trip": return <TripPage key={route.tripId} tripId={route.tripId} params={route.params} />;
    case "profile": return <Profile key={route.userId} userId={route.userId} />;
    case "not-found": return <NotFound />;
    default: break;
  }
  if (!account) return null;
  switch (route.page) {
    case "rides": return <Rides />;
    case "ride": return <RidePage key={route.bookingId} bookingId={route.bookingId} />;
    case "drive": return <Drive />;
    case "offer": return account.me.hasVehicle ? <TripEdit /> : <CarPage next="/drive/new" />;
    case "trip-edit": return <TripEdit key={route.tripId} tripId={route.tripId} />;
    case "trip-day": return <TripDay key={route.tripId} tripId={route.tripId} />;
    case "car": return <CarPage next={route.next} />;
    default: return <NotFound />;
  }
}

export function App() {
  const route = useRoute();
  const [session, setSession] = useState<Session | null>(() => loadSession());
  const [me, setMe] = useState<Me | null>(null);
  const [error, setError] = useState<string | null>(null);

  const signOut = useCallback(() => {
    saveSession(null);
    setToken(null, () => {});
    setSession(null);
    setMe(null);
    navigate("/", true);
  }, []);

  if (session) setToken(session.accessToken, signOut);

  const refresh = useCallback(async () => {
    const value = await api.me();
    setMe(value);
    return value;
  }, []);

  useEffect(() => {
    if (!session) return;
    setError(null);
    refresh().catch((e: Error) => setError(e.message));
  }, [session, refresh]);

  const open = isPublic(route);
  useEffect(() => {
    const auth = route.page === "signin" || route.page === "register";
    if (!session && !auth && !open) navigate(signInHere(), true);
    if (session && auth) navigate(route.next ?? "/", true);
  }, [session, route, open]);

  const signedIn = useCallback((value: Session) => {
    saveSession(value);
    setToken(value.accessToken, signOut);
    setSession(value);
  }, [signOut]);

  const account = useMemo<Account | null>(() => (session && me ? { session, me, refresh, signOut } : null), [session, me, refresh, signOut]);
  const context = useMemo(() => ({ account, signedIn }), [account, signedIn]);

  if (!session && (route.page === "signin" || route.page === "register")) {
    return <AccountCtx.Provider value={context}>{route.page === "register" ? <Register next={route.next} /> : <SignIn next={route.next} />}</AccountCtx.Provider>;
  }

  if (session && !me) {
    return <div className="center">{error ? <Failed message={error} onRetry={() => refresh().catch((e: Error) => setError(e.message))} /> : <Loading />}</div>;
  }

  return (
    <AccountCtx.Provider value={context}>
      <Shell route={route} wide={route.page === "search"}><Page route={route} account={account} /></Shell>
    </AccountCtx.Provider>
  );
}
