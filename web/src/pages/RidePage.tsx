import { ArrowRight, Car, Check, Clock, Navigation, X } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api } from "../api";
import { StarPicker, Stars, StatusBadge } from "../components/Badges";
import { Dialog } from "../components/Dialog";
import { MapView, type Pin } from "../components/MapView";
import { Failed, Loading } from "../components/States";
import { formatDayTime, formatMoney, formatTime, initials, km, plural, relative } from "../format";
import { useLoad, useNow } from "../hooks";
import { useLive, type Feed } from "../live";
import { linkProps } from "../router";
import type { Booking, LiveView, Trip } from "../types";
import { routeLayers } from "./TripPage";

/** When the car should reach a point of the route, from where it is now and how fast it has been going. */
export function etaAt(view: LiveView | null, kmAlong: number): string | null {
  const p = view?.position;
  if (!p || kmAlong < p.kmAlong) return null;
  return new Date(Date.parse(p.at) + ((kmAlong - p.kmAlong) / Math.max(15, p.kmPerHour)) * 3_600_000).toISOString();
}

export function FeedState({ feed }: { feed: Feed }) {
  const text = { connecting: "Connecting", live: "Live", reconnecting: "Reconnecting", off: "Updates every 15 s" }[feed];
  return <span className={`feed${feed === "live" ? " on" : ""}`} role="status"><i />{text}</span>;
}

export function carPin(view: LiveView | null): Pin[] {
  const p = view?.position;
  return p ? [{ id: "car", latitude: p.latitude, longitude: p.longitude, label: `The car, ${relative(p.at)}`, text: "", tone: p.offRoute ? "warn" : "accent" }] : [];
}

function LivePanel({ booking, trip }: { booking: Booking; trip: Trip }) {
  const { view, feed } = useLive(trip.id);
  const now = useNow(15_000);
  const eta = etaAt(view, booking.pickup.kmAlong);
  const passed = view?.position && view.position.kmAlong >= booking.pickup.kmAlong;
  const { pins, lines } = routeLayers(trip, carPin(view));

  return (
    <section className="card stack next">
      <div className="row between"><h2 className="row"><Navigation size={16} />The car, live</h2><FeedState feed={feed} /></div>
      <MapView pins={pins} lines={lines} height={320} fitKey={`${trip.id}-${view?.position ? "car" : "route"}`} />
      {view?.status === "Started" && view.position ? (
        booking.status === "Boarded" ? <p>You are on board. About {km(Math.max(0, booking.dropoff.kmAlong - view.position.kmAlong))} to {booking.dropoff.nearStop}{etaAt(view, booking.dropoff.kmAlong) ? `, around ${formatTime(etaAt(view, booking.dropoff.kmAlong)!)}` : ""}.</p>
        : passed ? <p className="notice">The car has passed where you get in. Call the driver if you have not met.</p>
        : eta && (
          <div className="eta"><strong>{formatTime(eta)}</strong><span className="muted">at {booking.pickup.nearStop}, {relative(eta, now)} · {km(booking.pickup.kmAlong - view.position.kmAlong)} away</span></div>
        )
      ) : <p className="muted">{view?.status === "Finished" ? "The trip has finished." : `The car shows up here once ${booking.driver.name} starts the trip.`}</p>}
      {view?.position?.offRoute && <p className="notice">The car is off the route for now: {km(view.position.kmOffRoute)} away from it.</p>}
    </section>
  );
}

function ReviewForm({ booking, done }: { booking: Booking; done: () => void }) {
  const [stars, setStars] = useState(5);
  const [comment, setComment] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const other = booking.isDriver ? booking.passenger.name : booking.driver.name;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      await api.review(booking.id, stars, comment);
      done();
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }

  return (
    <form className="card stack" onSubmit={submit}>
      <h2>How was the ride with {other}?</h2>
      <StarPicker value={stars} onChange={setStars} />
      <label className="field">A few words <span className="hint">Shown on {other}'s profile.</span><textarea className="textarea short" maxLength={500} value={comment} onChange={(e) => setComment(e.target.value)} /></label>
      {error && <p className="form-error" role="alert">{error}</p>}
      <button className="button primary align-start" disabled={busy}>Send review</button>
    </form>
  );
}

function ReasonDialog({ title, action, onClose, onDone }: { title: string; action: (reason: string) => Promise<Booking>; onClose: () => void; onDone: (b: Booking) => void }) {
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  return (
    <Dialog title={title} onClose={onClose}>
      <form className="stack" onSubmit={async (e) => {
        e.preventDefault();
        setBusy(true);
        try { onDone(await action(reason)); } catch (err) { setError((err as Error).message); setBusy(false); }
      }}>
        <label className="field">Why? <span className="hint">Optional; the other side sees it.</span><textarea className="textarea short" maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} autoFocus /></label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="dialog-actions"><button type="button" className="button" onClick={onClose}>Keep it</button><button className="button danger" disabled={busy}>{title}</button></div>
      </form>
    </Dialog>
  );
}

function DriverActions({ booking, set }: { booking: Booking; set: (b: Booking) => void }) {
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [dialog, setDialog] = useState(false);
  const act = async (work: () => Promise<Booking>) => {
    setError(null);
    try { set(await work()); } catch (err) { setError((err as Error).message); }
  };

  if (!booking.canAccept && !booking.canBoard && !booking.canMarkNoShow) return null;
  return (
    <section className="card stack next">
      {booking.canAccept && (
        <>
          <p>{booking.passenger.name} asks for {plural(booking.seats, "seat")} from {booking.pickup.nearStop} to {booking.dropoff.nearStop}. Answer by {formatDayTime(booking.respondBy)}.</p>
          <div className="row">
            <button className="button primary" onClick={() => act(() => api.accept(booking.id))}><Check size={15} />Accept</button>
            <button className="button" onClick={() => setDialog(true)}><X size={15} />Decline</button>
          </div>
        </>
      )}
      {booking.canBoard && (
        <form className="stack-8" onSubmit={(e) => { e.preventDefault(); void act(() => api.board(booking.id, code)); }}>
          <label className="field">Code on {booking.passenger.name}'s screen</label>
          <div className="code-form">
            <input className="input" inputMode="numeric" pattern="[0-9]{4}" maxLength={4} value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))} aria-label="Boarding code" />
            <button className="button primary" disabled={code.length !== 4}>On board</button>
          </div>
        </form>
      )}
      {booking.canMarkNoShow && <button className="button align-start" onClick={() => act(() => api.noShow(booking.id))}>{booking.passenger.name} did not come</button>}
      {error && <p className="form-error" role="alert">{error}</p>}
      {dialog && <ReasonDialog title="Decline" action={(r) => api.decline(booking.id, r)} onClose={() => setDialog(false)} onDone={(b) => { setDialog(false); set(b); }} />}
    </section>
  );
}

/** A booking, for the passenger who made it or the driver of the trip. */
export function RidePage({ bookingId }: { bookingId: string }) {
  const [booking, reload, set] = useLoad(() => api.booking(bookingId), [bookingId]);
  const tripId = booking.data?.trip.id;
  const [trip] = useLoad<Trip | null>(() => (tripId ? api.trip(tripId) : Promise.resolve(null)), [tripId]);
  const [cancelling, setCancelling] = useState(false);

  if (booking.state === "failed" && !booking.data) return <Failed message={booking.error} onRetry={reload} />;
  if (!booking.data) return <Loading />;
  const b = booking.data;
  const other = b.isDriver ? b.passenger : b.driver;
  const following = !b.isDriver && (b.status === "Confirmed" || b.status === "Boarded") && (b.trip.status === "Scheduled" || b.trip.status === "Started");

  return (
    <div className="ride-page narrow-page">
      <div className="page-head">
        <div>
          <h1 className="row">{b.pickup.nearStop} <ArrowRight size={18} /> {b.dropoff.nearStop}</h1>
          <p className="muted">{formatDayTime(b.pickup.at)} · on {b.driver.name}'s trip from {b.trip.from} to {b.trip.to}</p>
        </div>
        <StatusBadge status={b.status} />
      </div>

      {!b.isDriver && b.status === "Requested" && <p className="notice"><Clock size={15} />Waiting for {b.driver.name}, who answers by {formatDayTime(b.respondBy)}.</p>}
      {b.boardingCode && b.status === "Confirmed" && (
        <div className="code-show">
          <span className="small">Show this to {b.driver.name} when you get in</span>
          <span className="code-big mono" aria-label={`Boarding code ${b.boardingCode.split("").join(" ")}`}>{b.boardingCode}</span>
        </div>
      )}
      {b.isDriver && <DriverActions booking={b} set={set} />}
      {following && trip.data && <LivePanel booking={b} trip={trip.data} />}
      {(b.status === "Declined" || b.status === "Cancelled") && (b.reason || b.lateCancellation) && (
        <p className="notice">{b.status === "Cancelled" ? `Called off by the ${b.cancelledBy?.toLowerCase() ?? "trip"}` : "Declined"}{b.reason ? `: “${b.reason}”` : "."}{b.lateCancellation ? " Late, so it shows on their profile." : ""}</p>
      )}

      <section className="card stack">
        <dl className="sums">
          <div><dt>Get in</dt><dd>{b.pickup.nearStop}, {formatTime(b.pickup.at)}</dd></div>
          <div><dt>Get out</dt><dd>{b.dropoff.nearStop}, {formatTime(b.dropoff.at)}</dd></div>
          <div><dt>Distance</dt><dd>{km(b.km)}</dd></div>
          <div><dt>Seats</dt><dd>{b.seats} × {formatMoney(b.pricePerSeat)}</dd></div>
          <div className="total"><dt>{b.isDriver ? "Paid to you in cash" : "Paid in cash in the car"}</dt><dd>{formatMoney(b.total)}</dd></div>
        </dl>
        {b.message && <p className="quote-text">{b.message}</p>}
        <div className="row"><Car size={16} /><span>{b.car.colour} {b.car.make} {b.car.model}{b.car.plate && <> · <span className="mono">{b.car.plate}</span></>}</span></div>
      </section>

      <a className="card person-card" {...linkProps(`/u/${other.id}`)}>
        <span className="avatar">{initials(other.name)}</span>
        <span className="grow"><strong>{other.name}</strong><br /><span className="small muted">{b.isDriver ? "Passenger" : "Driver"}</span></span>
      </a>

      {b.reviews.canReview && <ReviewForm booking={b} done={reload} />}
      {(b.reviews.mine || b.reviews.theirs) && (
        <section className="card stack-8">
          {b.reviews.mine && <div className="review"><Stars value={b.reviews.mine.stars} /><span className="small muted">You wrote</span>{b.reviews.mine.comment && <p>{b.reviews.mine.comment}</p>}</div>}
          {b.reviews.theirs && <div className="review"><Stars value={b.reviews.theirs.stars} /><span className="small muted">{other.name} wrote</span>{b.reviews.theirs.comment && <p>{b.reviews.theirs.comment}</p>}</div>}
        </section>
      )}

      {b.canCancel && <button className="button ghost danger-text align-start" onClick={() => setCancelling(true)}>Call off {b.isDriver ? "this passenger's seats" : "your seats"}</button>}
      {cancelling && <ReasonDialog title="Call off" action={(r) => api.cancel(b.id, r)} onClose={() => setCancelling(false)} onDone={(x) => { setCancelling(false); set(x); }} />}
      {b.isDriver && <a className="small" {...linkProps(`/drive/${b.trip.id}`)}>Back to the trip</a>}
    </div>
  );
}
