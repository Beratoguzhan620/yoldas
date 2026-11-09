import { Car, Ticket } from "lucide-react";
import { api } from "../api";
import { Rating, Stars } from "../components/Badges";
import { Failed, Loading } from "../components/States";
import { formatMonthYear, initials, plural } from "../format";
import { useLoad } from "../hooks";

/** Someone's record as a driver and as a passenger, and what the people they rode with said. */
export function Profile({ userId }: { userId: string }) {
  const [profile, reload] = useLoad(() => api.profile(userId), [userId]);
  if (profile.state === "failed" && !profile.data) return <Failed message={profile.error} onRetry={reload} />;
  if (!profile.data) return <Loading />;
  const p = profile.data;

  return (
    <div className="stack narrow-page">
      <div className="profile-head">
        <span className="avatar big">{initials(p.name)}</span>
        <div><h1>{p.name}</h1><p className="muted">Member since {formatMonthYear(p.memberSince)}</p></div>
      </div>
      <div className="grid-2">
        <section className="card stack-8">
          <h2 className="row"><Car size={16} />As a driver</h2>
          <Rating value={p.asDriver.rating} count={p.asDriver.reviews} />
          <span className="small muted">{plural(p.asDriver.tripsDriven, "trip")} driven{p.asDriver.lateCancellations ? ` · ${plural(p.asDriver.lateCancellations, "late cancellation")}` : ""}</span>
        </section>
        <section className="card stack-8">
          <h2 className="row"><Ticket size={16} />As a passenger</h2>
          <Rating value={p.asPassenger.rating} count={p.asPassenger.reviews} />
          <span className="small muted">
            {plural(p.asPassenger.rides, "ride")}
            {p.asPassenger.lateCancellations ? ` · ${plural(p.asPassenger.lateCancellations, "late cancellation")}` : ""}
            {p.asPassenger.noShows ? ` · ${plural(p.asPassenger.noShows, "no-show")}` : ""}
          </span>
        </section>
      </div>
      <section className="card stack-8">
        <h2>What people said</h2>
        {p.reviews.length === 0 ? <p className="muted">No reviews yet.</p> : p.reviews.map((r) => (
          <div key={r.id} className="review">
            <div className="row"><Stars value={r.stars} /><span className="small muted">{r.authorName}, about them as a {r.of === "Driver" ? "driver" : "passenger"} · {formatMonthYear(r.at)}</span></div>
            {r.comment && <p>{r.comment}</p>}
          </div>
        ))}
      </section>
    </div>
  );
}
