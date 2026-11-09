// A holiday rush, measured against the running stack: hundreds of people searching for rides out of Samsun at once
// and booking seats on the same few dozen trips, each for their own part of the route, while drivers accept every
// request in random order. Afterwards it checks what must hold however the requests interleaved: on no part of any
// route are more seats taken than the car has, every passenger the driver sees is counted, and nothing failed.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=100000 docker compose up --build -d
//        node tests/load/load.mjs [passengers=400] [drivers=40]
const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const PASSENGERS = Number(process.argv[2] ?? 400);
const DRIVERS = Number(process.argv[3] ?? 40);
const run = Date.now().toString(36);
const password = "load-test-password";

const latencies = { search: [], book: [], accept: [], read: [] };
const statuses = new Map();
let errors5xx = 0;

async function call(path, token, body, method = body ? "POST" : "GET", bucket) {
  const started = performance.now();
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(body ? { "Content-Type": "application/json" } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  const ms = performance.now() - started;
  if (bucket) latencies[bucket].push(ms);
  statuses.set(response.status, (statuses.get(response.status) ?? 0) + 1);
  if (response.status >= 500) errors5xx++;
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { json = null; }
  return { status: response.status, json, ms };
}

async function must(path, token, body, method, bucket) {
  const r = await call(path, token, body, method, bucket);
  if (r.status >= 400) throw new Error(`${method ?? (body ? "POST" : "GET")} ${path}: ${r.status} ${JSON.stringify(r.json)}`);
  return r.json;
}

async function pool(items, limit, task) {
  const results = new Array(items.length);
  let next = 0;
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) {
      const i = next++;
      results[i] = await task(items[i], i);
    }
  }));
  return results;
}

function stats(values) {
  const sorted = [...values].sort((a, b) => a - b);
  const at = (p) => sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))] ?? 0;
  return { count: sorted.length, p50: Math.round(at(50)), p95: Math.round(at(95)), p99: Math.round(at(99)), max: Math.round(sorted.at(-1) ?? 0) };
}

const report = (name, data) => console.log(`${name.padEnd(36)} ${typeof data === "string" ? data : JSON.stringify(data)}`);
const pick = (list) => list[Math.floor(Math.random() * list.length)];
const failures = [];
const check = (ok, what) => { report(ok ? "ok" : "FAILED", what); if (!ok) failures.push(what); };

function day(plus) {
  const istanbul = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Istanbul", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
  const d = new Date(`${istanbul}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + plus);
  return d.toISOString().slice(0, 10);
}

async function signIn(email, name) {
  const r = await call("/api/auth/register", null, { email, displayName: name, password });
  if (r.status === 201) return r.json.accessToken;
  return (await must("/api/auth/login", null, { email, password })).accessToken;
}

const stop = (name, latitude, longitude) => ({ name, latitude, longitude });
const routes = [
  [stop("Samsun Otogar", 41.2601, 36.2847), stop("Havza", 40.97, 35.6622), stop("Çorum", 40.5506, 34.9556), stop("AŞTİ", 39.918, 32.81)],
  [stop("Atakum", 41.333, 36.271), stop("Havza", 40.97, 35.6622), stop("Merzifon", 40.8733, 35.4633), stop("Çorum", 40.5506, 34.9556), stop("Kırıkkale", 39.8468, 33.5153), stop("AŞTİ", 39.918, 32.81)],
  [stop("Samsun Otogar", 41.2601, 36.2847), stop("Kavak", 41.0797, 36.0425), stop("Merzifon", 40.8733, 35.4633), stop("Çorum", 40.5506, 34.9556)],
];

const setupStarted = performance.now();
// Half the trips book instantly, half wait for the driver; three, four or five seats; over the next three days.
const trips = await pool(Array.from({ length: DRIVERS }, (_, i) => i), 8, async (i) => {
  const token = await signIn(`surucu${i}.${run}@example.com`, `Sürücü ${i}`);
  const seats = 3 + (i % 3);
  await must("/api/me/vehicle", token, { make: "Fiat", model: "Egea", colour: "Gri", plate: `55 LT ${100 + i}`, passengerSeats: seats }, "PUT");
  const departure = `${day(1 + (i % 3))}T${String(6 + (i % 14)).padStart(2, "0")}:00:00+03:00`;
  const trip = await must("/api/trips", token, { stops: routes[i % routes.length], departureAt: departure, seats, pricePerSeat: 350, instantBook: i % 2 === 0, notes: "" });
  return { token, trip };
});
const passengers = await pool(Array.from({ length: PASSENGERS }, (_, i) => i), 24, async (i) => ({ token: await signIn(`yolcu${i}.${run}@example.com`, `Yolcu ${i}`) }));
report("setup", `${DRIVERS} trips and ${PASSENGERS} passengers in ${Math.round((performance.now() - setupStarted) / 1000)} s`);

// Everyone goes from one place on the way to a later one, a few hundred metres from the stop: heavy overlap on few
// trips, each passenger on their own part of the route.
const jitter = () => (Math.random() - 0.5) * 0.006;
const wishes = passengers.map((p) => {
  const route = pick(routes);
  const a = Math.floor(Math.random() * (route.length - 1));
  const b = a + 1 + Math.floor(Math.random() * (route.length - 1 - a));
  return { ...p, from: route[a], to: route[b], seats: Math.random() < 0.8 ? 1 : 2, date: day(1 + Math.floor(Math.random() * 3)) };
});

// Only this run's trips: the stack may hold others from earlier walkthroughs.
const byTrip = new Map(trips.map((t) => [t.trip.id, t]));
const rushStarted = performance.now();
const results = await pool(wishes, 64, async (w) => {
  const q = `fromLat=${w.from.latitude + jitter()}&fromLng=${w.from.longitude + jitter()}&toLat=${w.to.latitude + jitter()}&toLng=${w.to.longitude + jitter()}`;
  const found = await call(`/api/trips?${q}&date=${w.date}&seats=${w.seats}`, w.token, null, "GET", "search");
  const ours = found.status === 200 ? found.json.filter((t) => byTrip.has(t.id)) : [];
  if (ours.length === 0) return { ...w, status: "none" };
  const trip = pick(ours.slice(0, 3));
  const [fromLat, fromLng, toLat, toLng] = [trip.match.pickup.latitude, trip.match.pickup.longitude, trip.match.dropoff.latitude, trip.match.dropoff.longitude];
  const r = await call("/api/bookings", w.token, { tripId: trip.id, fromLat, fromLng, toLat, toLng, seats: w.seats, message: "" }, "POST", "book");
  return { ...w, status: r.status, booking: r.json };
});
report("rush", `${wishes.length} searches and bookings in ${Math.round(performance.now() - rushStarted)} ms`);

// Drivers answer everything waiting at once, in random order; a request whose seats have gone is refused.
const waiting = results.filter((r) => r.status === 201 && r.booking.status === "Requested").sort(() => Math.random() - 0.5);
const acceptStarted = performance.now();
const answers = await pool(waiting, 32, (w) => call(`/api/bookings/${w.booking.id}/accept`, byTrip.get(w.booking.trip.id).token, {}, "POST", "accept"));
report("accept", `${waiting.length} acceptances in ${Math.round(performance.now() - acceptStarted)} ms`);

const count = (list, status) => list.filter((x) => x.status === status).length;
report("bookings", { created: count(results, 201), seatsGone: count(results, 409), noTrip: count(results, "none"), other: results.length - count(results, 201) - count(results, 409) - count(results, "none") });
report("acceptances", { ok: count(answers, 200), seatsGone: count(answers, 409), other: answers.length - count(answers, 200) - count(answers, 409) });

// Whatever the interleaving, no point of any route carries more passengers than the car has seats, and the
// load the driver sees matches the seats confirmed.
let worst = 0;
let overbooked = 0;
let mismatched = 0;
let confirmedSeats = 0;
for (const { token, trip } of trips) {
  const view = await must(`/api/trips/${trip.id}/passengers`, token, null, "GET", "read");
  const holding = view.passengers.filter((p) => p.status === "Confirmed" || p.status === "Boarded");
  confirmedSeats += holding.reduce((sum, p) => sum + p.seats, 0);
  const points = [...new Set(holding.flatMap((p) => [p.pickupKm, p.dropoffKm]))];
  const peak = Math.max(0, ...points.map((km) => holding.filter((p) => p.pickupKm <= km && km < p.dropoffKm).reduce((sum, p) => sum + p.seats, 0)));
  worst = Math.max(worst, peak / trip.seats);
  if (peak > trip.seats) overbooked++;
  if (view.legs.some((l) => l.taken > l.seats) || peak > Math.max(0, ...view.legs.map((l) => l.taken))) mismatched++;
}
check(overbooked === 0, `no part of any route has more passengers than seats (fullest: ${Math.round(worst * 100)}% of the car)`);
check(mismatched === 0, `the load per leg drivers see matches the confirmed seats (${confirmedSeats} seats confirmed)`);
check(errors5xx === 0, `no server errors (${errors5xx})`);

report("latency: search (ms)", stats(latencies.search));
report("latency: book (ms)", stats(latencies.book));
report("latency: accept (ms)", stats(latencies.accept));
report("latency: reads (ms)", stats(latencies.read));
report("responses", Object.fromEntries([...statuses].sort((a, b) => a[0] - b[0])));
process.exit(failures.length === 0 ? 0 : 1);
