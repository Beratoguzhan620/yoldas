import { ArrowRight, Car, Plus } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api } from "../api";
import { TripBadge } from "../components/Badges";
import { Empty, Failed, Loading } from "../components/States";
import { useAccount } from "../context";
import { formatDayTime, formatMoney, km, plural } from "../format";
import { useLoad } from "../hooks";
import { linkProps, navigate } from "../router";
import type { Vehicle } from "../types";

/** Trips you drive, upcoming first; past ones on their own tab. */
export function Drive() {
  const account = useAccount();
  const [past, setPast] = useState(false);
  const [trips, reload] = useLoad(() => api.myTrips(past), [past]);

  return (
    <div className="stack narrow-page">
      <div className="page-head">
        <div><h1>Drive</h1><p className="muted">Offer the empty seats on a trip you are making anyway.</p></div>
        {account.me.hasVehicle
          ? <a className="button primary" {...linkProps("/drive/new")}><Plus size={15} />Offer a trip</a>
          : <a className="button primary" {...linkProps("/car?next=/drive/new")}><Car size={15} />Add your car first</a>}
      </div>
      <div className="tabs" role="tablist">
        <button role="tab" aria-selected={!past} className={`tab${!past ? " active" : ""}`} onClick={() => setPast(false)}>Upcoming</button>
        <button role="tab" aria-selected={past} className={`tab${past ? " active" : ""}`} onClick={() => setPast(true)}>Past</button>
      </div>
      {trips.state === "failed" && !trips.data ? <Failed message={trips.error} onRetry={reload} />
        : !trips.data ? <Loading />
        : trips.data.length === 0 ? <Empty title={past ? "No past trips" : "No trips coming up"}><span className="muted">Trips you offer show up here, with who is coming along.</span></Empty>
        : (
          <div className="stack-8">
            {trips.data.map((t) => (
              <a key={t.id} className="card ride-row" {...linkProps(`/drive/${t.id}`)}>
                <div className="stack-8 grow">
                  <strong className="row">{t.from} <ArrowRight size={14} /> {t.to}</strong>
                  <span className="small muted">{formatDayTime(t.departureAt)} · {km(t.lengthKm)} · {formatMoney(t.pricePerSeat)} a seat</span>
                </div>
                <div className="ride-meta"><TripBadge status={t.status} /><span className="small">{plural(t.seats - t.seatsLeft, "seat")} booked of {t.seats}</span></div>
              </a>
            ))}
          </div>
        )}
    </div>
  );
}

/** Your car: shown to passengers, the plate in full only once their seat is confirmed. */
export function CarPage({ next }: { next?: string }) {
  const account = useAccount();
  const [vehicle, reload] = useLoad(() => api.vehicle(), []);
  if (vehicle.state === "failed" && vehicle.data === undefined) return <Failed message={vehicle.error} onRetry={reload} />;
  if (vehicle.data === undefined) return <Loading />;
  return <CarForm initial={vehicle.data} onSaved={async () => { await account.refresh(); navigate(next ?? "/drive"); }} />;
}

function CarForm({ initial, onSaved }: { initial: Vehicle | null; onSaved: () => Promise<void> }) {
  const [car, setCar] = useState<Vehicle>(initial ?? { make: "", model: "", colour: "", plate: "", passengerSeats: 3 });
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const field = (key: keyof Vehicle) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    setCar({ ...car, [key]: key === "passengerSeats" ? Number(e.target.value) : e.target.value });

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.saveVehicle(car);
      await onSaved();
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }

  return (
    <form className="stack narrow-page" onSubmit={submit}>
      <div className="page-head"><div><h1>Your car</h1><p className="muted">Passengers see the make, model and colour; the plate only once their seat is confirmed, so they can find you.</p></div></div>
      <section className="card stack">
        <div className="grid-2">
          <label className="field">Make<input className="input" value={car.make} onChange={field("make")} placeholder="Renault" required maxLength={40} /></label>
          <label className="field">Model<input className="input" value={car.model} onChange={field("model")} placeholder="Clio" required maxLength={40} /></label>
          <label className="field">Colour<input className="input" value={car.colour} onChange={field("colour")} placeholder="Beyaz" required maxLength={30} /></label>
          <label className="field">Plate<input className="input mono" value={car.plate} onChange={field("plate")} placeholder="55 ABC 123" required maxLength={12} /></label>
        </div>
        <label className="field">Seats for passengers<select className="select" value={car.passengerSeats} onChange={field("passengerSeats")}>{[1, 2, 3, 4, 5, 6, 7, 8].map((n) => <option key={n} value={n}>{n}</option>)}</select></label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <button className="button primary align-start" disabled={busy}>{busy ? "Saving…" : "Save"}</button>
      </section>
    </form>
  );
}
