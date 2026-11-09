import { ArrowRight, Flag, LocateFixed, Pencil, Play, Users, X } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { api } from "../api";
import { StatusBadge, TripBadge } from "../components/Badges";
import { Dialog } from "../components/Dialog";
import { MapView } from "../components/MapView";
import { Empty, Failed, Loading } from "../components/States";
import { formatDayTime, formatMoney, formatTime, km, plural, relative } from "../format";
import { useLoad, useNow } from "../hooks";
import { useLive } from "../live";
import { linkProps } from "../router";
import type { LiveView, Trip, TripPassengers } from "../types";
import { carPin, FeedState } from "./RidePage";
import { routeLayers, StopList } from "./TripPage";

/** A point some kilometres along a route of straight legs, for the demo drive. */
function along(trip: Trip, kmAlong: number): [number, number] {
  const s = trip.stops;
  for (let i = 1; i < s.length; i++) {
    const a = s[i - 1]!;
    const b = s[i]!;
    if (kmAlong <= b.kmAlong || i === s.length - 1) {
      const f = Math.min(1, Math.max(0, (kmAlong - a.kmAlong) / (b.kmAlong - a.kmAlong)));
      return [a.latitude + (b.latitude - a.latitude) * f, a.longitude + (b.longitude - a.longitude) * f];
    }
  }
  return [s[0]!.latitude, s[0]!.longitude];
}

type Sharing = "off" | "device" | "demo";

/**
 * Sends the car's position while the trip is under way: from the phone's location, at most every 10 seconds, or, to
 * try the app without driving, by moving along the route a few kilometres at a time.
 */
function useSharing(trip: Trip | null, view: LiveView | null, mode: Sharing, onSent: (v: LiveView) => void, onError: (m: string) => void) {
  const last = useRef(0);
  const position = useRef(view?.position?.kmAlong ?? 0);
  position.current = view?.position?.kmAlong ?? position.current;

  useEffect(() => {
    if (!trip) return;
    if (mode === "device") {
      if (!("geolocation" in navigator)) { onError("This browser cannot share its location."); return; }
      const watch = navigator.geolocation.watchPosition(
        (p) => {
          if (Date.now() - last.current < 10_000) return;
          last.current = Date.now();
          api.position(trip.id, p.coords.latitude, p.coords.longitude).then(onSent, (e: Error) => onError(e.message));
        },
        (e) => onError(e.code === e.PERMISSION_DENIED ? "Location is blocked for this site. Allow it in the browser to share where you are." : "The location could not be read."),
        { enableHighAccuracy: true, maximumAge: 5_000 },
      );
      return () => navigator.geolocation.clearWatch(watch);
    }
    if (mode === "demo") {
      const timer = setInterval(() => {
        const next = Math.min(trip.lengthKm, position.current + Math.max(2, trip.lengthKm / 40));
        const [lat, lng] = along(trip, next);
        api.position(trip.id, lat, lng).then(onSent, (e: Error) => onError(e.message));
      }, 2_500);
      return () => clearInterval(timer);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, trip?.id]);
}

function Passengers({ data }: { data: TripPassengers }) {
  const active = data.passengers.filter((p) => !["Declined", "Expired", "Cancelled"].includes(p.status));
  const gone = data.passengers.length - active.length;
  return (
    <section className="card stack">
      <h2 className="row"><Users size={16} />Passengers</h2>
      {active.length === 0 ? <p className="muted">Nobody has booked yet.</p> : active.map((p) => (
        <a key={p.bookingId} className="passenger" {...linkProps(`/rides/${p.bookingId}`)} style={{ color: "inherit", textDecoration: "none" }}>
          <div className="row between"><strong>{p.passenger.name}</strong><StatusBadge status={p.status} /></div>
          <span className="small row">{formatTime(p.pickupAt)} {p.pickupNear} <ArrowRight size={12} /> {p.dropoffNear}</span>
          <span className="small muted">{plural(p.seats, "seat")} · {formatMoney(p.total)} in cash{p.status === "Requested" ? ` · answer by ${formatDayTime(p.respondBy)}` : ""}</span>
        </a>
      ))}
      {gone > 0 && <p className="small muted">{plural(gone, "request")} declined, called off or not answered.</p>}
      {data.legs.length > 0 && (
        <div className="leg-loads">
          <h3 className="small muted">Seats taken, stop to stop</h3>
          {data.legs.map((l) => (
            <div key={`${l.from}-${l.to}`} className="leg">
              <span>{l.from} – {l.to}</span><span className="muted">{l.taken} / {l.seats}</span>
              <span className="meter"><span style={{ width: `${(100 * l.taken) / Math.max(1, l.seats)}%` }} /></span>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}

/** The driver's page for a trip: who is coming, and on the day, starting, sharing where the car is and finishing. */
export function TripDay({ tripId }: { tripId: string }) {
  const [trip, reload, setTrip] = useLoad(() => api.trip(tripId), [tripId]);
  const [passengers, reloadPassengers] = useLoad(() => api.passengers(tripId), [tripId]);
  const status = trip.data?.status;
  const { view, feed, set } = useLive(tripId, status === "Started");
  const [sharing, setSharing] = useState<Sharing>("off");
  const [error, setError] = useState<string | null>(null);
  const [cancelling, setCancelling] = useState(false);
  const now = useNow(15_000);

  useSharing(trip.data ?? null, view, status === "Started" ? sharing : "off", set, setError);

  if (trip.state === "failed" && !trip.data) return <Failed message={trip.error} onRetry={reload} />;
  if (!trip.data) return <Loading />;
  const t = trip.data;
  if (!t.isMine) return <Empty title="This is not your trip"><a className="button" {...linkProps(`/t/${t.id}`)}>See the trip</a></Empty>;
  const first = t.stops[0]!;
  const last = t.stops[t.stops.length - 1]!;
  const { pins, lines } = routeLayers(t, carPin(view));

  const act = async (work: () => Promise<unknown>) => {
    setError(null);
    try { await work(); reload(); reloadPassengers(); } catch (e) { setError((e as Error).message); }
  };

  return (
    <div className="stack">
      <div className="page-head">
        <div>
          <h1 className="row">{first.name} <ArrowRight size={18} /> {last.name}</h1>
          <p className="muted">{formatDayTime(t.departureAt)} · {km(t.lengthKm)} · {formatMoney(t.pricePerSeat)} a seat · {plural(t.seatsLeft, "seat")} left of {t.seats}</p>
        </div>
        <div className="row wrap">
          <TripBadge status={t.status} />
          {t.status === "Scheduled" && <a className="button small" {...linkProps(`/drive/${t.id}/edit`)}><Pencil size={14} />Change</a>}
          {t.status === "Scheduled" && <button className="button small danger" onClick={() => setCancelling(true)}><X size={14} />Cancel trip</button>}
        </div>
      </div>
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="trip-grid">
        <div className="stack">
          <MapView pins={pins} lines={lines} height={400} fitKey={t.id} />
          {t.status === "Scheduled" && (
            <section className="card row between wrap">
              <span>Start the trip from half an hour before you leave. Passengers then see the car on their map.</span>
              <button className="button primary" onClick={() => act(() => api.start(t.id))}><Play size={15} />Start the trip</button>
            </section>
          )}
          {t.status === "Started" && (
            <section className="card stack next">
              <div className="row between"><h2 className="row"><LocateFixed size={16} />Where the car is</h2><FeedState feed={feed} /></div>
              {view?.position ? <p>{km(view.position.kmAlong)} along, {Math.round(view.position.kmPerHour)} km/h, shared {relative(view.position.at, now)}.{view.position.offRoute ? ` Off the route by ${km(view.position.kmOffRoute)}.` : ""}</p>
                : <p className="muted">Nothing shared yet. Passengers see the car once you share where you are.</p>}
              <div className="tabs" role="radiogroup" aria-label="Sharing">
                {(["off", "device", "demo"] as const).map((m) => (
                  <button key={m} role="radio" aria-checked={sharing === m} className={`tab${sharing === m ? " active" : ""}`} onClick={() => { setError(null); setSharing(m); }}>
                    {{ off: "Not sharing", device: "Share my location", demo: "Demo: drive the route" }[m]}
                  </button>
                ))}
              </div>
              <button className="button align-start" onClick={() => { setSharing("off"); void act(() => api.finish(t.id)); }}><Flag size={15} />Finish the trip</button>
              <p className="small muted">Finishing stops sharing and clears the car's position. Anyone who boarded has made it; a confirmed seat nobody boarded counts as a no-show.</p>
            </section>
          )}
          <section className="card stack-8">
            <h2>Stops</h2>
            <StopList trip={t} passedKm={view?.position?.kmAlong ?? -1} />
            {view && t.status === "Started" && <p className="small muted">Expected: {view.stops.filter((s) => s.at).map((s) => `${s.name} ${formatTime(s.at!)}`).join(" · ") || "every stop passed"}</p>}
          </section>
        </div>
        <aside className="trip-side">
          {passengers.data ? <Passengers data={passengers.data} /> : passengers.state === "failed" ? <Failed message={passengers.error} onRetry={reloadPassengers} /> : <Loading />}
        </aside>
      </div>
      {cancelling && (
        <Dialog title="Cancel this trip?" onClose={() => setCancelling(false)}>
          <div className="stack">
            <p>Every passenger's seat is called off and they are told. Within 24 hours of a pickup, it shows on your profile as a late cancellation.</p>
            <div className="dialog-actions">
              <button className="button" onClick={() => setCancelling(false)}>Keep the trip</button>
              <button className="button danger" onClick={async () => { setCancelling(false); await act(async () => setTrip(await api.cancelTrip(t.id))); }}>Cancel the trip</button>
            </div>
          </div>
        </Dialog>
      )}
    </div>
  );
}
