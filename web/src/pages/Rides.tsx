import { ArrowRight, Ticket } from "lucide-react";
import { useState } from "react";
import { api } from "../api";
import { StatusBadge } from "../components/Badges";
import { Empty, Failed, Loading } from "../components/States";
import { formatDayTime, formatMoney, plural } from "../format";
import { useLoad } from "../hooks";
import { linkProps } from "../router";

/** Seats you have booked as a passenger, upcoming first; past rides on their own tab. */
export function Rides() {
  const [past, setPast] = useState(false);
  const [rides, reload] = useLoad(() => api.myBookings(past), [past]);
  const list = (rides.data ?? []).filter((r) => !r.isDriver);

  return (
    <div className="stack narrow-page">
      <div className="page-head">
        <div><h1>Your rides</h1><p className="muted">Seats you have asked for or booked on other people's trips.</p></div>
        <div className="tabs" role="tablist">
          <button role="tab" aria-selected={!past} className={`tab${!past ? " active" : ""}`} onClick={() => setPast(false)}>Upcoming</button>
          <button role="tab" aria-selected={past} className={`tab${past ? " active" : ""}`} onClick={() => setPast(true)}>Past</button>
        </div>
      </div>
      {rides.state === "failed" && !rides.data ? <Failed message={rides.error} onRetry={reload} />
        : !rides.data ? <Loading />
        : list.length === 0 ? (
          <Empty title={past ? "No past rides" : "No rides coming up"} icon={<Ticket size={24} />}>
            {!past && <a className="button primary" {...linkProps("/")}>Find a ride</a>}
          </Empty>
        ) : (
          <div className="stack-8">
            {list.map((r) => (
              <a key={r.id} className="card ride-row" {...linkProps(`/rides/${r.id}`)}>
                <div className="stack-8 grow">
                  <strong className="row">{r.pickupNear} <ArrowRight size={14} /> {r.dropoffNear}</strong>
                  <span className="small muted">{formatDayTime(r.pickupAt)} · with {r.other.name} · {plural(r.seats, "seat")}</span>
                </div>
                <div className="ride-meta"><StatusBadge status={r.status} /><span className="small">{formatMoney(r.total)}</span></div>
              </a>
            ))}
          </div>
        )}
    </div>
  );
}
