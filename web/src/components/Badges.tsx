import { Star } from "lucide-react";
import { bookingLabel, bookingTone, tripLabel, tripTone } from "../format";
import type { BookingStatus, TripStatus } from "../types";

export function StatusBadge({ status }: { status: BookingStatus }) {
  return <span className={`badge ${bookingTone[status]}`}>{bookingLabel[status]}</span>;
}

export function TripBadge({ status }: { status: TripStatus }) {
  return <span className={`badge ${tripTone[status]}`}>{tripLabel[status]}</span>;
}

/** "4.6 (12)", or "New" before the first review. */
export function Rating({ value, count }: { value: number | null; count: number }) {
  if (value === null || count === 0) return <span className="small muted">New</span>;
  return (
    <span className="rating" aria-label={`Rated ${value.toFixed(1)} out of 5 by ${count}`}>
      <Star size={13} aria-hidden="true" />{value.toFixed(1)}<span className="muted">({count})</span>
    </span>
  );
}

/** The stars of one review. */
export function Stars({ value }: { value: number }) {
  return (
    <span className="rating" aria-label={`${value} out of 5`}>
      {Array.from({ length: value }, (_, i) => <Star key={i} size={13} aria-hidden="true" />)}
    </span>
  );
}

/** Five stars to pick from, usable with the keyboard as a radio group. */
export function StarPicker({ value, onChange }: { value: number; onChange: (stars: number) => void }) {
  return (
    <div className="stars" role="radiogroup" aria-label="Stars">
      {[1, 2, 3, 4, 5].map((n) => (
        <button key={n} type="button" role="radio" aria-checked={value === n} aria-label={`${n} of 5`} className={`star${n <= value ? " on" : ""}`} onClick={() => onChange(n)}>
          <Star size={22} aria-hidden="true" />
        </button>
      ))}
    </div>
  );
}
