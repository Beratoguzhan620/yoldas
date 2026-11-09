import { ArrowDown, ArrowUp, MapPin, Plus, Trash2 } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api } from "../api";
import { MapView, type Pin } from "../components/MapView";
import { PlaceInput } from "../components/PlaceInput";
import { Failed, Loading } from "../components/States";
import { useAccount } from "../context";
import { duration, formatMoney, km, maxPriceFor, routeKm, today, turkeyParts, turkeyTime } from "../format";
import { useLoad } from "../hooks";
import { navigate } from "../router";
import type { Spot, Trip } from "../types";

const MAX_STOPS = 8;

function StopEditor({ stops, onChange }: { stops: Spot[]; onChange: (stops: Spot[]) => void }) {
  const [adding, setAdding] = useState<Spot | null>(null);
  const move = (i: number, by: number) => {
    const next = [...stops];
    [next[i], next[i + by]] = [next[i + by]!, next[i]!];
    onChange(next);
  };
  return (
    <div className="stack-8">
      <ol className="stop-editor">
        {stops.map((s, i) => (
          <li key={`${s.latitude},${s.longitude},${i}`}>
            <span className="stop-no">{i + 1}</span>
            <input className="input" value={s.name} aria-label={`Name of stop ${i + 1}`} maxLength={60}
              onChange={(e) => onChange(stops.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)))} />
            <span className="row gap-6">
              <button type="button" className="icon-button" disabled={i === 0} onClick={() => move(i, -1)} aria-label={`Move ${s.name} up`}><ArrowUp size={15} /></button>
              <button type="button" className="icon-button" disabled={i === stops.length - 1} onClick={() => move(i, 1)} aria-label={`Move ${s.name} down`}><ArrowDown size={15} /></button>
              <button type="button" className="icon-button" onClick={() => onChange(stops.filter((_, j) => j !== i))} aria-label={`Remove ${s.name}`}><Trash2 size={15} /></button>
            </span>
          </li>
        ))}
      </ol>
      {stops.length < MAX_STOPS && (
        <div className="row align-end">
          <div className="grow"><PlaceInput label={stops.length === 0 ? "Where you start" : stops.length === 1 ? "Where you are going" : "Another stop"} value={adding} onChange={setAdding} placeholder="Town or city, or tap the map" /></div>
          <button type="button" className="button" disabled={!adding} onClick={() => { if (adding) { onChange([...stops, adding]); setAdding(null); } }}><Plus size={15} />Add</button>
        </div>
      )}
      <p className="small muted">Stops in the order you pass them, 2 to {MAX_STOPS}: where you start, places you can pick people up or drop them off, and where you end. You can name each one the way you would say it: “Havza çıkışı”.</p>
    </div>
  );
}

/** Offering a new trip, or changing one: the route of a trip never changes, so only the time, seats and price do. */
export function TripEdit({ tripId }: { tripId?: string }) {
  const [trip, reload] = useLoad<Trip | null>(() => (tripId ? api.trip(tripId) : Promise.resolve(null)), [tripId]);
  if (trip.state === "failed" && !trip.data) return <Failed message={trip.error} onRetry={reload} />;
  if (trip.data === undefined) return <Loading />;
  return <TripForm trip={trip.data} />;
}

function TripForm({ trip }: { trip: Trip | null }) {
  const account = useAccount();
  const start = trip ? turkeyParts(trip.departureAt) : { date: today(1), time: "08:00" };
  const [stops, setStops] = useState<Spot[]>(trip ? trip.stops.map((s) => ({ name: s.name, latitude: s.latitude, longitude: s.longitude })) : []);
  const [date, setDate] = useState(start.date);
  const [time, setTime] = useState(start.time);
  const [seats, setSeats] = useState(trip?.seats ?? 3);
  const [price, setPrice] = useState<string>(trip ? String(trip.pricePerSeat) : "");
  const [instant, setInstant] = useState(trip?.instantBook ?? false);
  const [notes, setNotes] = useState(trip?.notes ?? "");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const lengthKm = trip ? trip.lengthKm : routeKm(stops);
  const max = trip ? trip.maxPricePerSeat : maxPriceFor(lengthKm);
  const suggested = Math.max(10, Math.round((lengthKm * 1.1) / 10) * 10);

  function pick(latitude: number, longitude: number) {
    if (trip || stops.length >= MAX_STOPS) return;
    setStops([...stops, { name: `Stop ${stops.length + 1}`, latitude, longitude }]);
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    const body = { departureAt: turkeyTime(date, time), seats, pricePerSeat: Number(price), instantBook: instant, notes };
    try {
      const saved = trip ? await api.change(trip.id, body) : await api.offer({ ...body, stops });
      if (!trip) await account.refresh();
      navigate(`/drive/${saved.id}`);
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }

  const pins: Pin[] = stops.map((s, i) => ({ id: `s${i}`, latitude: s.latitude, longitude: s.longitude, label: s.name, text: String(i + 1), tone: "ink", shape: "square" }));
  const lines = stops.length > 1 ? [{ points: stops.map((s) => [s.latitude, s.longitude] as [number, number]), tone: "accent" as const, weight: 4 }] : [];

  return (
    <form className="stack" onSubmit={submit}>
      <div className="page-head"><div><h1>{trip ? "Change the trip" : "Offer a trip"}</h1><p className="muted">{trip ? "The route stays as it is; offer a new trip for a different one." : "Passengers along your route book the part they need and pay their share in cash."}</p></div></div>
      <div className="trip-grid">
        <div className="stack">
          <MapView pins={pins} lines={lines} height={420} onPick={trip ? undefined : pick} fitKey={trip ? trip.id : String(stops.length)} />
          {!trip && <p className="small muted row"><MapPin size={13} />Tap the map to add a stop there, then give it a name.</p>}
        </div>
        <div className="stack">
          <section className="card stack">
            <h2>Route</h2>
            {trip ? <ol className="stop-editor">{trip.stops.map((s, i) => <li key={s.index}><span className="stop-no">{i + 1}</span><span>{s.name}</span><span className="small muted">{km(s.kmAlong)}</span></li>)}</ol>
              : <StopEditor stops={stops} onChange={setStops} />}
            {stops.length > 1 && <p className="small">{km(lengthKm)} · about {duration((lengthKm / 65) * 60)} at 65 km/h</p>}
          </section>
          <section className="card stack">
            <div className="grid-2">
              <label className="field">Day<input className="input" type="date" min={today()} value={date} onChange={(e) => setDate(e.target.value)} required /></label>
              <label className="field">Leaving at<input className="input" type="time" step={300} value={time} onChange={(e) => setTime(e.target.value)} required /></label>
              <label className="field">Seats offered<select className="select" value={seats} onChange={(e) => setSeats(Number(e.target.value))}>{[1, 2, 3, 4, 5, 6, 7, 8].map((n) => <option key={n} value={n}>{n}</option>)}</select></label>
              <label className="field">Seat for the whole way, TL<input className="input" type="number" inputMode="numeric" min={10} step={1} value={price} onChange={(e) => setPrice(e.target.value)} placeholder={stops.length > 1 ? String(Math.min(suggested, max)) : ""} required /></label>
            </div>
            {stops.length > 1 && <p className="small muted">At most {formatMoney(max)} for this route (3 TL a kilometre): enough to share the fuel, not to make a profit. Passengers pay for the share they ride.</p>}
            <label className="check"><input type="checkbox" checked={instant} onChange={(e) => setInstant(e.target.checked)} /><span><strong>Instant booking</strong><br /><span className="small muted">Seats are confirmed straight away, without asking you first.</span></span></label>
            <label className="field">Notes for passengers <span className="hint">Luggage, pets, music, where exactly you stop.</span><textarea className="textarea short" maxLength={500} value={notes} onChange={(e) => setNotes(e.target.value)} /></label>
            {error && <p className="form-error" role="alert">{error}</p>}
            <div className="row">
              <button className="button primary" disabled={busy || (!trip && stops.length < 2)}>{busy ? "Saving…" : trip ? "Save changes" : "Offer the trip"}</button>
              <button type="button" className="button ghost" onClick={() => history.back()}>Cancel</button>
            </div>
          </section>
        </div>
      </div>
    </form>
  );
}
