export interface User { id: string; email: string; displayName: string }
export interface Session { accessToken: string; expiresAt: string; user: User }
export interface Me { user: User; memberSince: string; hasVehicle: boolean; upcomingTrips: number }

export interface Place { name: string; province: string; label: string; latitude: number; longitude: number }

/** A place picked in a form: from the list of towns, or by tapping the map. */
export interface Spot { name: string; latitude: number; longitude: number }

export interface Vehicle { make: string; model: string; colour: string; plate: string; passengerSeats: number }

export type TripStatus = "Scheduled" | "Started" | "Finished" | "Cancelled";
export type BookingStatus = "Requested" | "Confirmed" | "Declined" | "Expired" | "Cancelled" | "Boarded" | "NoShow" | "Completed";
export type TripSort = "Departure" | "Price" | "Closest";

export interface Driver { id: string; name: string; memberSince: string; rating: number | null; ratingCount: number }
export interface Car { make: string; model: string; colour: string; plate: string | null }
export interface Stop { index: number; name: string; latitude: number; longitude: number; kmAlong: number; at: string }
export interface MatchPoint { latitude: number; longitude: number; kmAlong: number; kmFromYou: number; nearStop: string; at: string }
export interface Match { pickup: MatchPoint; dropoff: MatchPoint; km: number; price: number; seatsFree: number }

export interface TripSummary {
  id: string; from: string; to: string; departureAt: string; arrivalAt: string; driver: Driver; car: Car; seats: number; seatsLeft: number;
  pricePerSeat: number; instantBook: boolean; status: TripStatus; lengthKm: number; match: Match | null;
}

export interface Trip {
  id: string; driver: Driver; car: Car; stops: Stop[]; lengthKm: number; durationMinutes: number; departureAt: string; arrivalAt: string; seats: number;
  seatsLeft: number; pricePerSeat: number; maxPricePerSeat: number; instantBook: boolean; notes: string; status: TripStatus; isMine: boolean;
  match: Match | null; myBookingId: string | null;
}

export interface TripInput { stops: Spot[]; departureAt: string; seats: number; pricePerSeat: number; instantBook: boolean; notes: string }

export interface Person { id: string; name: string }
export interface BookingTrip { id: string; from: string; to: string; departureAt: string; status: TripStatus }
export interface BookingReview { stars: number; comment: string; at: string }

export interface Booking {
  id: string; trip: BookingTrip; driver: Person; passenger: Person; car: Car; seats: number; pickup: MatchPoint; dropoff: MatchPoint; km: number;
  pricePerSeat: number; total: number; status: BookingStatus; message: string; reason: string; cancelledBy: "Passenger" | "Driver" | null;
  lateCancellation: boolean; requestedAt: string; respondBy: string; confirmedAt: string | null; isDriver: boolean; canAccept: boolean; canCancel: boolean;
  boardingCode: string | null; boardedAt: string | null; canBoard: boolean; canMarkNoShow: boolean;
  reviews: { canReview: boolean; mine: BookingReview | null; theirs: BookingReview | null };
}

export interface BookingSummary {
  id: string; trip: BookingTrip; other: Person; seats: number; pickupNear: string; pickupAt: string; dropoffNear: string; total: number;
  status: BookingStatus; isDriver: boolean;
}

export interface Passenger {
  bookingId: string; passenger: Person; passengerRating: number | null; seats: number; pickupNear: string; pickupKm: number; pickupAt: string;
  dropoffNear: string; dropoffKm: number; total: number; status: BookingStatus; message: string; respondBy: string;
}

export interface LegLoad { from: string; to: string; taken: number; seats: number }
export interface TripPassengers { passengers: Passenger[]; legs: LegLoad[] }

export interface LivePosition { latitude: number; longitude: number; kmAlong: number; kmOffRoute: number; offRoute: boolean; kmPerHour: number; at: string }
export interface StopEta { index: number; name: string; kmAlong: number; at: string | null }
export interface LiveView { tripId: string; status: TripStatus; position: LivePosition | null; stops: StopEta[] }

export interface Review { id: string; authorName: string; of: "Driver" | "Passenger"; stars: number; comment: string; at: string }
export interface Profile {
  id: string; name: string; memberSince: string;
  asDriver: { rating: number | null; reviews: number; tripsDriven: number; lateCancellations: number };
  asPassenger: { rating: number | null; reviews: number; rides: number; lateCancellations: number; noShows: number };
  reviews: Review[];
}
