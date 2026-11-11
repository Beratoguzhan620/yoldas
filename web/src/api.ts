import type {
  Booking, BookingSummary, LiveView, Me, Place, Profile, Review, Session, Trip, TripInput, TripPassengers, TripSort, TripSummary, Vehicle,
} from "./types";

/** An API error with the stable code from the problem response, such as "unavailable". */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code: string | null,
    public readonly fields: Record<string, string[]> = {},
  ) {
    super(message);
  }

  /** The request never reached the server: the device is offline or the server is down. */
  get offline() {
    return this.status === 0;
  }
}

let token: string | null = null;
let onUnauthorized: () => void = () => {};

export function setToken(value: string | null, unauthorized: () => void) {
  token = value;
  onUnauthorized = unauthorized;
}

async function send(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  if (typeof init.body === "string") headers.set("Content-Type", "application/json");
  if (token && !headers.has("Authorization")) headers.set("Authorization", `Bearer ${token}`);

  let response: Response;
  try {
    response = await fetch(path, { ...init, headers });
  } catch (e) {
    if ((e as Error).name === "AbortError") throw e;
    throw new ApiError("The server cannot be reached. Check your connection.", 0, null);
  }

  if (response.status === 401 && token) onUnauthorized();
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const fields = (problem?.errors ?? {}) as Record<string, string[]>;
    const fallback = response.status === 429 ? "Too many requests. Wait a minute and try again." : "Something went wrong. Try again.";
    const detail = problem?.detail ?? Object.values(fields).flat()[0] ?? fallback;
    throw new ApiError(detail, response.status, problem?.code ?? null, fields);
  }
  return response;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await send(path, init);
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

const json = (body: unknown) => JSON.stringify(body);
const post = (body: unknown = {}): RequestInit => ({ method: "POST", body: json(body) });
const put = (body: unknown): RequestInit => ({ method: "PUT", body: json(body) });
const bk = (id: string) => `/api/bookings/${id}`;
const tr = (id: string) => `/api/trips/${id}`;

export function query(params: Record<string, string | number | boolean | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && String(value).trim() !== "") search.set(key, String(value).trim());
  }
  const text = search.toString();
  return text ? `?${text}` : "";
}

export interface SearchFilters {
  fromLat: number; fromLng: number; toLat: number; toLng: number; date?: string; seats?: number; radiusKm?: number; sort?: TripSort;
}

/** Where a passenger starts and ends, to see their part of a trip and its price. */
export interface Ends { fromLat: number; fromLng: number; toLat: number; toLng: number }

/** An account of the public demo, with its password, which is public on purpose. */
export interface DemoAccount { role: "Passenger" | "Driver"; name: string; email: string; password: string; description: string }

export const api = {
  register: (body: { email: string; displayName: string; password: string }) => request<Session>("/api/auth/register", post(body)),
  login: (email: string, password: string) => request<Session>("/api/auth/login", post({ email, password })),
  me: () => request<Me>("/api/me"),
  demo: () => request<DemoAccount[]>("/api/demo"),

  places: (q: string, limit = 8) => request<Place[]>(`/api/places${query({ q, limit })}`),
  vehicle: async () => {
    try {
      return await request<Vehicle>("/api/me/vehicle");
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) return null;
      throw e;
    }
  },
  saveVehicle: (body: Omit<Vehicle, "plate"> & { plate: string }) => request<Vehicle>("/api/me/vehicle", put(body)),

  search: (f: SearchFilters) => request<TripSummary[]>(`/api/trips${query({ ...f })}`),
  trip: (id: string, ends?: Ends) => request<Trip>(`${tr(id)}${query({ ...ends })}`),
  myTrips: (past = false) => request<TripSummary[]>(`/api/me/trips${query({ when: past ? "past" : undefined })}`),
  offer: (body: TripInput) => request<Trip>("/api/trips", post(body)),
  change: (id: string, body: Omit<TripInput, "stops">) => request<Trip>(tr(id), put(body)),
  cancelTrip: (id: string) => request<Trip>(`${tr(id)}/cancel`, post()),
  passengers: (id: string) => request<TripPassengers>(`${tr(id)}/passengers`),

  book: (body: Ends & { tripId: string; seats: number; message: string }) => request<Booking>("/api/bookings", post(body)),
  booking: (id: string) => request<Booking>(bk(id)),
  myBookings: (past = false) => request<BookingSummary[]>(`/api/me/bookings${query({ when: past ? "past" : undefined })}`),
  accept: (id: string) => request<Booking>(`${bk(id)}/accept`, post()),
  decline: (id: string, reason: string) => request<Booking>(`${bk(id)}/decline`, post({ reason })),
  cancel: (id: string, reason: string) => request<Booking>(`${bk(id)}/cancel`, post({ reason })),

  start: (id: string) => request<LiveView>(`${tr(id)}/start`, post()),
  position: (id: string, latitude: number, longitude: number) => request<LiveView>(`${tr(id)}/position`, post({ latitude, longitude })),
  live: (id: string) => request<LiveView>(`${tr(id)}/live`),
  finish: (id: string) => request<LiveView>(`${tr(id)}/finish`, post()),
  board: (id: string, code: string) => request<Booking>(`${bk(id)}/board`, post({ code })),
  noShow: (id: string) => request<Booking>(`${bk(id)}/no-show`, post()),
  review: (id: string, stars: number, comment: string) => request<Review>(`${bk(id)}/review`, post({ stars, comment })),
  profile: (id: string) => request<Profile>(`/api/users/${id}`),
};

/** The token for the live feed, which sends it in the address because browsers cannot set headers on WebSockets. */
export const currentToken = () => token;
