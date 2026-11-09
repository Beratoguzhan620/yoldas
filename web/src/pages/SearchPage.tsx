import { ArrowRight, Footprints, Route as RouteIcon, Zap } from "lucide-react";
import { useMemo, useState, type FormEvent } from "react";
import { api } from "../api";
import { Rating } from "../components/Badges";
import { MapView, type Pin } from "../components/MapView";
import { PlaceInput } from "../components/PlaceInput";
import { Empty, Failed, Loading } from "../components/States";
import { formatLongDay, formatMoney, formatTime, km, plural, today } from "../format";
import { useLoad } from "../hooks";
import { linkProps, navigate } from "../router";
import type { Spot, TripSort, TripSummary } from "../types";

/** A place kept in the address as name, latitude and longitude, so a search can be bookmarked and shared. */
export function readSpot(params: URLSearchParams, key: string): Spot | null {
  const name = params.get(key);
  const [lat, lng] = (params.get(`${key}At`) ?? "").split(",").map(Number);
  return name && Number.isFinite(lat) && Number.isFinite(lng) && Math.abs(lat!) <= 90 && Math.abs(lng!) <= 180
    ? { name, latitude: lat!, longitude: lng! } : null;
}

export function spotParams(key: string, spot: Spot | null): Record<string, string> {
  return spot ? { [key]: spot.name, [`${key}At`]: `${spot.latitude},${spot.longitude}` } : {};
}

/** The address of a trip with the passenger's two ends, so the page shows their part and its price. */
export function tripHref(tripId: string, from: Spot | null, to: Spot | null) {
  const q = new URLSearchParams({ ...spotParams("from", from), ...spotParams("to", to) }).toString();
  return `/t/${tripId}${q ? `?${q}` : ""}`;
}

const sorts: { value: TripSort; label: string }[] = [
  { value: "Departure", label: "Earliest first" },
  { value: "Price", label: "Cheapest first" },
  { value: "Closest", label: "Least walking" },
];

function SeatDots({ left, seats }: { left: number; seats: number }) {
  return (
    <span className="seat-dots" aria-label={`${plural(left, "seat")} left of ${seats}`}>
      {Array.from({ length: seats }, (_, i) => <i key={i} className={i < seats - left ? "taken" : undefined} />)}
    </span>
  );
}

function TripCard({ trip, from, to, on, onHover }: { trip: TripSummary; from: Spot; to: Spot; on: boolean; onHover: (id: string | null) => void }) {
  const m = trip.match!;
  return (
    <a className={`card trip-card${on ? " on" : ""}`} {...linkProps(tripHref(trip.id, from, to))} onMouseEnter={() => onHover(trip.id)} onMouseLeave={() => onHover(null)}
      onFocus={() => onHover(trip.id)} onBlur={() => onHover(null)}>
      <div className="stack-8">
        <div className="timeline">
          <span className="t">{formatTime(m.pickup.at)}</span><span className="dot on" /><span><strong>{m.pickup.nearStop}</strong> <span className="walk"><Footprints size={12} /> {km(m.pickup.kmFromYou)} from {from.name}</span></span>
          <span /><span className="bar" /><span className="small muted">{km(m.km)} with {trip.driver.name}, on the way from {trip.from} to {trip.to}</span>
          <span className="t">{formatTime(m.dropoff.at)}</span><span className="dot on" /><span><strong>{m.dropoff.nearStop}</strong> <span className="walk"><Footprints size={12} /> {km(m.dropoff.kmFromYou)} to {to.name}</span></span>
        </div>
        <div className="row small muted wrap">
          <Rating value={trip.driver.rating} count={trip.driver.ratingCount} />
          <span>·</span><span>{trip.car.colour} {trip.car.make} {trip.car.model}</span>
          {trip.instantBook && <span className="badge accent"><Zap size={12} />Instant</span>}
        </div>
      </div>
      <div className="stack-8 price-col">
        <span className="price">{formatMoney(m.price)}</span>
        <span className="small muted"><SeatDots left={m.seatsFree} seats={trip.seats} /> {plural(m.seatsFree, "seat")} free</span>
      </div>
    </a>
  );
}

export function SearchPage({ params }: { params: URLSearchParams }) {
  const from = readSpot(params, "from");
  const to = readSpot(params, "to");
  const date = params.get("date") ?? "";
  const seats = Math.min(8, Math.max(1, Number(params.get("seats")) || 1));
  const sort = (sorts.find((s) => s.value === params.get("sort"))?.value ?? "Departure") as TripSort;

  const [draftFrom, setDraftFrom] = useState<Spot | null>(from);
  const [draftTo, setDraftTo] = useState<Spot | null>(to);
  const [draftDate, setDraftDate] = useState(date);
  const [draftSeats, setDraftSeats] = useState(seats);
  const [hover, setHover] = useState<string | null>(null);

  const ready = from && to;
  const key = params.toString();
  const [results, reload] = useLoad<TripSummary[] | null>(
    () => (ready ? api.search({ fromLat: from.latitude, fromLng: from.longitude, toLat: to.latitude, toLng: to.longitude, date: date || undefined, seats, sort }) : Promise.resolve(null)),
    [key],
  );

  function go(next: Record<string, string>) {
    const q = new URLSearchParams(next).toString();
    navigate(q ? `/?${q}` : "/", true);
  }

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!draftFrom || !draftTo) return;
    go({ ...spotParams("from", draftFrom), ...spotParams("to", draftTo), ...(draftDate ? { date: draftDate } : {}), ...(draftSeats > 1 ? { seats: String(draftSeats) } : {}), ...(sort !== "Departure" ? { sort } : {}) });
  }

  const trips = results.data ?? [];
  const shown = trips.find((t) => t.id === hover);
  const pins = useMemo<Pin[]>(() => {
    const list: Pin[] = [];
    if (from) list.push({ id: "from", latitude: from.latitude, longitude: from.longitude, label: `From: ${from.name}`, text: "A", tone: "ink", shape: "square" });
    if (to) list.push({ id: "to", latitude: to.latitude, longitude: to.longitude, label: `To: ${to.name}`, text: "B", tone: "ink", shape: "square" });
    if (shown?.match) {
      list.push({ id: "pickup", latitude: shown.match.pickup.latitude, longitude: shown.match.pickup.longitude, label: `Get in near ${shown.match.pickup.nearStop}`, text: "↑", tone: "accent" });
      list.push({ id: "dropoff", latitude: shown.match.dropoff.latitude, longitude: shown.match.dropoff.longitude, label: `Get out near ${shown.match.dropoff.nearStop}`, text: "↓", tone: "accent" });
    }
    return list;
  }, [from, to, shown]);
  const lines = shown?.match ? [{ points: [[shown.match.pickup.latitude, shown.match.pickup.longitude], [shown.match.dropoff.latitude, shown.match.dropoff.longitude]] as [number, number][], tone: "accent" as const, dashed: true }] : [];

  return (
    <div className="search-page">
      <form className="card search-bar" onSubmit={submit} role="search">
        <PlaceInput label="From" value={draftFrom} onChange={setDraftFrom} placeholder="Town or city" autoFocus={!from} required />
        <PlaceInput label="To" value={draftTo} onChange={setDraftTo} placeholder="Town or city" required />
        <label className="field inline">Day<input className="input" type="date" min={today()} value={draftDate} onChange={(e) => setDraftDate(e.target.value)} /></label>
        <label className="field inline">Seats<select className="select small-input" value={draftSeats} onChange={(e) => setDraftSeats(Number(e.target.value))}>{[1, 2, 3, 4].map((n) => <option key={n} value={n}>{n}</option>)}</select></label>
        <button className="button primary" disabled={!draftFrom || !draftTo}>Find a ride</button>
      </form>

      <div className="search-grid">
        <section className="stack" aria-live="polite">
          {!ready ? (
            <Empty title="Where are you going?" icon={<RouteIcon size={26} />}>
              <span className="muted">Pick where you start and where you are going. Trips that pass near both, in that order, are shown with where you would get in and out, and your share of the price.</span>
            </Empty>
          ) : (
            <>
              <div className="page-head">
                <div>
                  <h1 className="row">{from.name} <ArrowRight size={18} /> {to.name}</h1>
                  <p className="muted">{date ? formatLongDay(`${date}T12:00:00+03:00`) : "Every upcoming day"} · {plural(seats, "seat")}{results.state === "ready" ? ` · ${plural(trips.length, "trip")}` : ""}</p>
                </div>
                <label className="field inline">Sort<select className="select narrow" value={sort} onChange={(e) => go({ ...Object.fromEntries(params), sort: e.target.value })}>{sorts.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}</select></label>
              </div>
              {results.state === "failed" ? <Failed message={results.error} onRetry={reload} />
                : results.state === "loading" && !results.data ? <Loading label="Looking for trips" />
                : trips.length === 0 ? (
                  <Empty title="No trip passes this way yet">
                    <span className="muted">Try another day, fewer seats, or a bigger town nearby.</span>
                  </Empty>
                ) : (
                  <div className="trips">{trips.map((t) => <TripCard key={t.id} trip={t} from={from} to={to} on={t.id === hover} onHover={setHover} />)}</div>
                )}
            </>
          )}
        </section>
        <aside className="search-map">
          <MapView pins={pins} lines={lines} circles={ready ? [from, to].map((s) => ({ latitude: s.latitude, longitude: s.longitude, radiusMeters: 15_000, tone: "muted" as const })) : []} fitKey={`${from?.name}-${to?.name}`} />
          <p className="small muted map-note">Circles show how far from your places a trip may pass. Point at a trip to see where you would get in and out.</p>
        </aside>
      </div>
    </div>
  );
}
