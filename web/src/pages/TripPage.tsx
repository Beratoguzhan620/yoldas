import { ArrowRight, Car, Clock, Footprints, Info, Zap } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api } from "../api";
import { Rating, TripBadge } from "../components/Badges";
import { MapView, type Pin } from "../components/MapView";
import { PlaceInput } from "../components/PlaceInput";
import { Failed, Loading } from "../components/States";
import { useMaybeAccount } from "../context";
import { duration, formatDayTime, formatLongDay, formatMoney, formatMonthYear, formatTime, initials, km, plural } from "../format";
import { useLoad } from "../hooks";
import { linkProps, navigate, signInHere } from "../router";
import type { Spot, Trip } from "../types";
import { readSpot, tripHref } from "./SearchPage";

/** The route on the map: the legs between stops, the stops, and the passenger's part when there is one. */
export function routeLayers(trip: Trip, extra: Pin[] = []) {
  const pins: Pin[] = trip.stops.map((s, i) => ({
    id: `stop-${s.index}`, latitude: s.latitude, longitude: s.longitude, label: `${s.name} · ${formatTime(s.at)}`, text: String(i + 1),
    tone: "ink", shape: "square",
  }));
  const lines = [{ points: trip.stops.map((s) => [s.latitude, s.longitude] as [number, number]), tone: "accent" as const, weight: 4 }];
  const m = trip.match;
  if (m) {
    pins.push({ id: "pickup", latitude: m.pickup.latitude, longitude: m.pickup.longitude, label: `You get in · ${formatTime(m.pickup.at)}`, text: "↑", tone: "accent" });
    pins.push({ id: "dropoff", latitude: m.dropoff.latitude, longitude: m.dropoff.longitude, label: `You get out · ${formatTime(m.dropoff.at)}`, text: "↓", tone: "accent" });
  }
  return { pins: [...pins, ...extra], lines };
}

export function StopList({ trip, passedKm = -1 }: { trip: Trip; passedKm?: number }) {
  const m = trip.match;
  return (
    <ol className="stop-list">
      {trip.stops.map((s) => {
        const mine = m && (s.name === m.pickup.nearStop || s.name === m.dropoff.nearStop);
        return (
          <li key={s.index} className={`${s.kmAlong <= passedKm ? "passed" : ""}${mine ? " mine" : ""}`}>
            <span className="t">{formatTime(s.at)}</span><span className="dot" /><span>{s.name}</span><span className="small muted">{km(s.kmAlong)}</span>
          </li>
        );
      })}
    </ol>
  );
}

function BookPanel({ trip, from, to }: { trip: Trip; from: Spot; to: Spot }) {
  const account = useMaybeAccount();
  const m = trip.match!;
  const [seats, setSeats] = useState(1);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function book(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const booking = await api.book({ tripId: trip.id, fromLat: from.latitude, fromLng: from.longitude, toLat: to.latitude, toLng: to.longitude, seats, message });
      navigate(`/rides/${booking.id}`);
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }

  return (
    <form className="card stack" onSubmit={book}>
      <div className="row between"><span className="price">{formatMoney(m.price * seats)}</span><span className="small muted">{seats > 1 ? `${formatMoney(m.price)} a seat` : "for your part"}</span></div>
      <dl className="sums">
        <div><dt><Footprints size={13} />Get in</dt><dd>{m.pickup.nearStop}, {formatTime(m.pickup.at)}<br /><span className="small muted">{km(m.pickup.kmFromYou)} from {from.name}</span></dd></div>
        <div><dt><Footprints size={13} />Get out</dt><dd>{m.dropoff.nearStop}, {formatTime(m.dropoff.at)}<br /><span className="small muted">{km(m.dropoff.kmFromYou)} to {to.name}</span></dd></div>
        <div><dt>Your part</dt><dd>{km(m.km)} of {km(trip.lengthKm)}</dd></div>
        <div className="total"><dt>Paid in cash in the car</dt><dd>{formatMoney(m.price * seats)}</dd></div>
      </dl>
      {trip.isMine ? <p className="notice">This is your trip.</p>
        : trip.myBookingId ? <a className="button primary wide" {...linkProps(`/rides/${trip.myBookingId}`)}>See your booking</a>
        : trip.status !== "Scheduled" ? <p className="notice">This trip can no longer be booked.</p>
        : m.seatsFree === 0 ? <p className="notice">Every seat is taken on your part of the route.</p>
        : !account ? <a className="button primary wide" {...linkProps(signInHere())}>Sign in to book</a>
        : (
          <>
            <label className="field">Seats<select className="select" value={seats} onChange={(e) => setSeats(Number(e.target.value))}>{Array.from({ length: m.seatsFree }, (_, i) => i + 1).map((n) => <option key={n} value={n}>{plural(n, "seat")}</option>)}</select></label>
            <label className="field">A note for {trip.driver.name} <span className="hint">Optional: luggage, where exactly you will wait.</span><textarea className="textarea short" maxLength={300} value={message} onChange={(e) => setMessage(e.target.value)} /></label>
            {error && <p className="form-error" role="alert">{error}</p>}
            <button className="button primary wide" disabled={busy}>{busy ? "Booking…" : trip.instantBook ? "Book" : "Ask for seats"}</button>
            <p className="small muted">{trip.instantBook ? "The seats are yours straight away." : `${trip.driver.name} answers within 12 hours, and always before the trip.`} Calling off within 24 hours of the pickup is shown on your profile.</p>
          </>
        )}
    </form>
  );
}

export function TripPage({ tripId, params }: { tripId: string; params: URLSearchParams }) {
  const from = readSpot(params, "from");
  const to = readSpot(params, "to");
  const ends = from && to ? { fromLat: from.latitude, fromLng: from.longitude, toLat: to.latitude, toLng: to.longitude } : undefined;
  const [trip, reload] = useLoad(() => api.trip(tripId, ends), [tripId, params.toString()]);
  const [pickFrom, setPickFrom] = useState<Spot | null>(from);
  const [pickTo, setPickTo] = useState<Spot | null>(to);

  if (trip.state === "failed" && !trip.data) return <Failed message={trip.error} onRetry={reload} />;
  if (!trip.data) return <Loading />;
  const t = trip.data;
  const first = t.stops[0]!;
  const last = t.stops[t.stops.length - 1]!;
  const { pins, lines } = routeLayers(t);

  return (
    <div className="stack">
      <div className="page-head">
        <div>
          <h1 className="row">{first.name} <ArrowRight size={18} /> {last.name}</h1>
          <p className="muted">{formatLongDay(t.departureAt)} · leaves {formatTime(t.departureAt)} · about {duration(t.durationMinutes)} · {km(t.lengthKm)}</p>
        </div>
        <div className="row">
          <TripBadge status={t.status} />
          {t.instantBook && <span className="badge accent"><Zap size={12} />Instant</span>}
          {t.isMine && <a className="button small" {...linkProps(`/drive/${t.id}`)}>Manage</a>}
        </div>
      </div>
      <div className="trip-grid">
        <div className="stack">
          <MapView pins={pins} lines={lines} height={380} fitKey={t.id} />
          <section className="card stack-8">
            <h2>Stops</h2>
            <StopList trip={t} />
            <p className="small muted"><Clock size={12} /> Times assume about 65 km/h; the driver shares where they are on the day.</p>
          </section>
          {t.notes && <section className="card stack-8"><h2>From the driver</h2><p className="quote-text">{t.notes}</p></section>}
          <a className="card person-card" {...linkProps(`/u/${t.driver.id}`)}>
            <span className="avatar">{initials(t.driver.name)}</span>
            <span className="stack-8 grow"><strong>{t.driver.name}</strong><span className="small muted">Driving since {formatMonthYear(t.driver.memberSince)}</span></span>
            <Rating value={t.driver.rating} count={t.driver.ratingCount} />
          </a>
          <section className="card row"><Car size={18} /><span>{t.car.colour} {t.car.make} {t.car.model}{t.car.plate ? <> · <span className="mono">{t.car.plate}</span></> : null}</span><span className="grow" /><span className="small muted">{plural(t.seatsLeft, "seat")} left of {t.seats}</span></section>
        </div>
        <aside className="trip-side">
          {t.match && from && to ? <BookPanel trip={t} from={from} to={to} /> : (
            <form className="card stack" onSubmit={(e) => { e.preventDefault(); if (pickFrom && pickTo) navigate(tripHref(t.id, pickFrom, pickTo), true); }}>
              <div className="row"><Info size={16} /><strong>Where are you getting in and out?</strong></div>
              {from && to && !t.match && <p className="notice">This trip does not pass near both of those, in that order.</p>}
              <PlaceInput label="From" value={pickFrom} onChange={setPickFrom} required />
              <PlaceInput label="To" value={pickTo} onChange={setPickTo} required />
              <button className="button primary wide" disabled={!pickFrom || !pickTo}>See your price</button>
              <p className="small muted">A seat for the whole way is {formatMoney(t.pricePerSeat)}; you pay for the share of the route you ride. Leaves {formatDayTime(t.departureAt)}.</p>
            </form>
          )}
        </aside>
      </div>
    </div>
  );
}
