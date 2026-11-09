import type { BookingStatus, Spot, TripStatus } from "./types";

/** Trips run on Turkish time; screens show Istanbul's clock wherever the browser is. */
export const ZONE = "Europe/Istanbul";

const money = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY", minimumFractionDigits: 0, maximumFractionDigits: 0 });
const dayTime = new Intl.DateTimeFormat("en-GB", { weekday: "short", day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", timeZone: ZONE });
const clock = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit", timeZone: ZONE });
const longDay = new Intl.DateTimeFormat("en-GB", { weekday: "long", day: "numeric", month: "long", timeZone: ZONE });
const monthYear = new Intl.DateTimeFormat("en-GB", { month: "long", year: "numeric", timeZone: ZONE });
const isoParts = new Intl.DateTimeFormat("en-CA", { year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone: ZONE });

export const formatMoney = (value: number) => money.format(value);
export const formatDayTime = (iso: string) => dayTime.format(new Date(iso));
export const formatTime = (iso: string) => clock.format(new Date(iso));
export const formatLongDay = (iso: string) => longDay.format(new Date(iso));
export const formatMonthYear = (iso: string) => monthYear.format(new Date(iso));
export const km = (value: number) => (value < 1 ? `${Math.round(value * 1000)} m` : value < 20 ? `${value.toFixed(1)} km` : `${Math.round(value)} km`);

export function duration(minutes: number): string {
  const h = Math.floor(minutes / 60);
  const m = Math.round(minutes % 60);
  return h === 0 ? `${m} min` : m === 0 ? `${h} h` : `${h} h ${m} min`;
}

function parts(now: number) {
  const p = Object.fromEntries(isoParts.formatToParts(new Date(now)).map((x) => [x.type, x.value]));
  return { date: `${p.year}-${p.month}-${p.day}`, time: `${p.hour}:${p.minute}` };
}

/** Today in Turkey as yyyy-mm-dd, plus some days. */
export function today(plusDays = 0, now = Date.now()): string {
  return addDays(parts(now).date, plusDays);
}

export function addDays(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** A day and a time on Turkey's clock (always UTC+3, no summer time) as an instant the API understands. */
export const turkeyTime = (date: string, time: string) => `${date}T${time}:00+03:00`;

/** The day and time on Turkey's clock of an instant, for filling a form. */
export const turkeyParts = (iso: string) => parts(Date.parse(iso));

export function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  return ((words[0]?.[0] ?? "") + (words.length > 1 ? words[words.length - 1]![0] : "")).toLocaleUpperCase("tr-TR") || "?";
}

/** "in 12 min", "3 h ago": how far a moment is from now, roughly. */
export function relative(iso: string, now = Date.now()): string {
  const diff = new Date(iso).getTime() - now;
  const abs = Math.abs(diff);
  if (abs < 45_000) return diff >= 0 ? "now" : "just now";
  const unit = abs < 3_600_000 ? [Math.round(abs / 60_000), "min"] as const : abs < 172_800_000 ? [Math.round(abs / 3_600_000), "h"] as const : [Math.round(abs / 86_400_000), "days"] as const;
  const text = `${Math.max(1, unit[0])} ${unit[1]}`;
  return diff >= 0 ? `in ${text}` : `${text} ago`;
}

export const plural = (n: number, word: string, many = `${word}s`) => `${n} ${n === 1 ? word : many}`;

/** Straight-line distance in kilometres, as the crow flies. */
export function distanceKm(a: { latitude: number; longitude: number }, b: { latitude: number; longitude: number }): number {
  const rad = Math.PI / 180;
  const dLat = (b.latitude - a.latitude) * rad;
  const dLng = (b.longitude - a.longitude) * rad;
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(a.latitude * rad) * Math.cos(b.latitude * rad) * Math.sin(dLng / 2) ** 2;
  return 2 * 6_371.0088 * Math.asin(Math.min(1, Math.sqrt(h)));
}

/** A route of straight legs between stops, as the server measures it (to within a few metres). */
export const routeKm = (stops: Spot[]) => stops.slice(1).reduce((sum, stop, i) => sum + distanceKm(stops[i]!, stop), 0);

/** The most a seat over the whole route may cost: 3 TL a kilometre, in whole lira, never under 10 TL. */
export const maxPriceFor = (lengthKm: number) => Math.max(10, Math.floor(lengthKm * 3));

/** A passenger's share of the seat price for the part they ride, to the nearest lira, at least 10 TL. */
export function shareOf(pricePerSeat: number, partKm: number, lengthKm: number): number {
  const lira = Math.round((pricePerSeat * partKm) / lengthKm);
  return Math.min(pricePerSeat, Math.max(Math.min(10, pricePerSeat), lira));
}

export const bookingLabel: Record<BookingStatus, string> = {
  Requested: "Waiting for the driver", Confirmed: "Confirmed", Declined: "Declined", Expired: "Not answered", Cancelled: "Cancelled",
  Boarded: "On board", NoShow: "Did not come", Completed: "Completed",
};

export const bookingTone: Record<BookingStatus, "plain" | "info" | "ok" | "danger" | "accent"> = {
  Requested: "info", Confirmed: "accent", Declined: "plain", Expired: "plain", Cancelled: "plain", Boarded: "accent", NoShow: "danger", Completed: "ok",
};

export const tripLabel: Record<TripStatus, string> = { Scheduled: "Scheduled", Started: "On the road", Finished: "Finished", Cancelled: "Cancelled" };

export const tripTone: Record<TripStatus, "plain" | "info" | "ok" | "danger" | "accent"> = { Scheduled: "info", Started: "accent", Finished: "ok", Cancelled: "plain" };
