// Screenshots and a walkthrough for the README, against the running Compose stack, with believable trips out of
// Samsun. Trips leaving in half an hour are started, driven and finished on the way, so drivers have reviews and one
// car is on the road when the pictures are taken.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=100000 docker compose up --build -d
//        cd tests/e2e && node media.mjs [output folder]
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = process.argv[2] ?? new URL("./output/media/", import.meta.url).pathname;
mkdirSync(`${OUT}frames`, { recursive: true });
const run = Date.now().toString(36);
const password = "media-password-123";

async function call(path, token, body, method = body ? "POST" : "GET") {
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(body ? { "Content-Type": "application/json" } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${text}`);
  return text ? JSON.parse(text) : null;
}

async function person(name, car) {
  const email = `${name.split(" ")[0].toLowerCase().replace(/[^a-z]/g, "")}.${run}@example.com`;
  const session = await call("/api/auth/register", null, { email, displayName: name, password });
  const who = { name, email, token: session.accessToken, session };
  if (car) await call("/api/me/vehicle", who.token, car, "PUT");
  return who;
}

function day(plus) {
  const istanbul = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Istanbul", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
  const d = new Date(`${istanbul}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + plus);
  return d.toISOString().slice(0, 10);
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const stop = (name, latitude, longitude) => ({ name, latitude, longitude });
const S = {
  atakum: stop("Atakum", 41.333, 36.271), otogar: stop("Samsun Otogar", 41.2601, 36.2847), kavak: stop("Kavak", 41.0797, 36.0425),
  havza: stop("Havza", 40.97, 35.6622), merzifon: stop("Merzifon", 40.8733, 35.4633), corum: stop("Çorum", 40.5506, 34.9556),
  sungurlu: stop("Sungurlu", 40.1667, 34.3833), kirikkale: stop("Kırıkkale", 39.8468, 33.5153), asti: stop("AŞTİ", 39.918, 32.81),
  amasya: stop("Amasya", 40.6499, 35.8353), tokat: stop("Tokat", 40.3167, 36.55),
};

const mehmet = await person("Mehmet Yıldız", { make: "Renault", model: "Clio", colour: "Beyaz", plate: "55 ABC 123", passengerSeats: 3 });
const elif = await person("Elif Şahin", { make: "Toyota", model: "Corolla", colour: "Gri", plate: "55 ES 410", passengerSeats: 4 });
const burak = await person("Burak Aydın", { make: "Fiat", model: "Egea", colour: "Kırmızı", plate: "34 BRK 72", passengerSeats: 3 });
const zeynep = await person("Zeynep Arslan", { make: "Volkswagen", model: "Golf", colour: "Lacivert", plate: "06 ZA 1905", passengerSeats: 4 });
const ayse = await person("Ayşe Kaya");
const can = await person("Can Demir");
const selin = await person("Selin Öztürk");
const emre = await person("Emre Koç");

const offer = (who, stops, at, seats, price, instantBook, notes = "") => call("/api/trips", who.token, { stops, departureAt: at, seats, pricePerSeat: price, instantBook, notes });
const ends = (from, to) => ({ fromLat: from.latitude, fromLng: from.longitude, toLat: to.latitude, toLng: to.longitude });
const book = (who, trip, from, to, seats = 1, message = "") => call("/api/bookings", who.token, { tripId: trip.id, ...ends(from, to), seats, message });
const soon = () => new Date(Date.now() + 31 * 60_000).toISOString();

// Trips leaving in half an hour: two that will be finished for reviews, and Mehmet's, which will be on the road.
const offeredAt = Date.now();
const done1 = await offer(elif, [S.otogar, S.havza, S.merzifon], soon(), 3, 120, true);
const done2 = await offer(mehmet, [S.atakum, S.otogar, S.kavak], soon(), 3, 60, true);
const done3 = await offer(burak, [S.otogar, S.amasya, S.tokat], soon(), 3, 180, true);
const live = await offer(mehmet, [S.atakum, S.otogar, S.kavak, S.havza, S.merzifon], soon(), 3, 150, true, "Havza'da kısa bir çay molası veriyorum.");
const doneRides = [
  [done1, elif, can, S.otogar, S.merzifon], [done1, elif, selin, S.havza, S.merzifon], [done2, mehmet, selin, S.atakum, S.kavak],
  [done2, mehmet, emre, S.otogar, S.kavak], [done3, burak, emre, S.otogar, S.tokat], [done3, burak, can, S.amasya, S.tokat],
];
const doneBookings = [];
for (const [trip, , who, from, to] of doneRides) doneBookings.push(await book(who, trip, from, to));
const liveRide = await book(ayse, live, S.havza, S.merzifon, 1, "Havza otogarının önündeyim.");
await book(emre, live, S.otogar, S.havza);

// Tomorrow's trips out of Samsun, for searching.
const at = (plus, time) => `${day(plus)}T${time}:00+03:00`;
const main = await offer(mehmet, [S.otogar, S.havza, S.corum, S.sungurlu, S.kirikkale, S.asti], at(1, "08:30"), 3, 380, false, "Bagaj geniş. Sigara içilmiyor; Çorum'da kısa bir mola var.");
await offer(elif, [S.atakum, S.havza, S.merzifon, S.corum, S.asti], at(1, "07:15"), 4, 360, true, "Kadın yolculara öncelik.");
await offer(zeynep, [S.otogar, S.kavak, S.havza, S.corum], at(1, "10:00"), 3, 170, true);
await offer(burak, [S.otogar, S.havza, S.corum, S.kirikkale, S.asti], at(1, "13:45"), 3, 350, false);
await offer(zeynep, [S.otogar, S.amasya, S.tokat], at(2, "09:00"), 4, 180, true);
await call(`/api/bookings/${(await book(can, main, S.otogar, S.havza)).id}/accept`, mehmet.token, {});
await call(`/api/bookings/${(await book(selin, main, S.havza, S.asti, 1, "Ankara'da okula dönüyorum.")).id}/accept`, mehmet.token, {});
await book(emre, main, S.corum, S.asti, 1, "Kırıkkale'de de inebilirim.");

/** Moves a trip's car some kilometres along its stops, as the demo drive does. */
function along(trip, kmAlong) {
  const s = trip.stops;
  for (let i = 1; i < s.length; i++) {
    if (kmAlong <= s[i].kmAlong || i === s.length - 1) {
      const f = Math.min(1, Math.max(0, (kmAlong - s[i - 1].kmAlong) / (s[i].kmAlong - s[i - 1].kmAlong)));
      return { latitude: s[i - 1].latitude + (s[i].latitude - s[i - 1].latitude) * f, longitude: s[i - 1].longitude + (s[i].longitude - s[i - 1].longitude) * f };
    }
  }
  return s[0];
}

const codeOf = async (who, bookingId) => (await call(`/api/bookings/${bookingId}`, who.token)).boardingCode;
const wait = 65_000 - (Date.now() - offeredAt);
if (wait > 0) await sleep(wait);
for (const [trip, driver] of [[done1, elif], [done2, mehmet], [done3, burak]]) await call(`/api/trips/${trip.id}/start`, driver.token, {});
for (const [i, [trip, driver, who]] of doneRides.entries()) {
  await call(`/api/trips/${trip.id}/position`, driver.token, along(trip, 2));
  await call(`/api/bookings/${doneBookings[i].id}/board`, driver.token, { code: await codeOf(who, doneBookings[i].id) });
}
for (const [trip, driver] of [[done1, elif], [done2, mehmet], [done3, burak]]) await call(`/api/trips/${trip.id}/finish`, driver.token, {});
const words = [
  [5, "Çok rahat bir yolculuktu, tam saatinde geldi."], [4, "Güvenli sürüş, sohbet de keyifliydi."], [5, "Atakum'dan almak için yolunu bile uzattı."],
  [5, "Dakik ve kibar."], [4, "Araç temizdi, müzik biraz yüksekti."], [5, "Tokat'a kadar sorunsuz."],
];
for (const [i, [, driver, who]] of doneRides.entries()) {
  await call(`/api/bookings/${doneBookings[i].id}/review`, who.token, { stars: words[i][0], comment: words[i][1] });
  await call(`/api/bookings/${doneBookings[i].id}/review`, driver.token, { stars: 5, comment: "Zamanında hazırdı, tavsiye ederim." });
}
await call(`/api/trips/${live.id}/start`, mehmet.token, {});

const browser = await chromium.launch();
async function signedIn(who, viewport, options = {}) {
  const context = await browser.newContext({ viewport, deviceScaleFactor: 2, locale: "en-GB", timezoneId: "Europe/Istanbul", ...options });
  const page = await context.newPage();
  if (who) {
    await page.goto(`${BASE}/signin`);
    await page.evaluate((s) => localStorage.setItem("yoldas.session", JSON.stringify(s)), who.session);
  }
  return page;
}
const settle = async (page, ms = 1500) => { await page.waitForLoadState("networkidle").catch(() => {}); await page.waitForTimeout(ms); };
const phoneOptions = { isMobile: true, hasTouch: true };
const spot = (key, s) => `${key}=${encodeURIComponent(s.name)}&${key}At=${s.latitude},${s.longitude}`;
const samsun = stop("Samsun", 41.2867, 36.33);
const corum = stop("Çorum", 40.5506, 34.9556);

const visitor = await signedIn(null, { width: 1440, height: 900 });
await visitor.goto(`${BASE}/?${spot("from", samsun)}&${spot("to", corum)}&date=${day(1)}`);
await visitor.locator(".trip-card").nth(2).waitFor();
await visitor.locator(`.trip-card[href^="/t/${main.id}"]`).hover();
await settle(visitor, 2500);
await visitor.screenshot({ path: `${OUT}search.png` });

const desk = await signedIn(mehmet, { width: 1440, height: 900 });
await desk.goto(`${BASE}/drive/${main.id}`);
await desk.locator(".leg").first().waitFor();
await settle(desk, 2500);
await desk.screenshot({ path: `${OUT}driver-trip.png` });
await desk.goto(`${BASE}/drive/new`);
for (const s of [S.otogar, S.havza, S.corum, S.asti]) {
  await desk.getByLabel(/Where you start|Where you are going|Another stop/).fill(s.name === "Samsun Otogar" ? "samsun" : s.name === "AŞTİ" ? "ankara" : s.name.toLowerCase().replace("ç", "c"));
  await desk.getByRole("listbox").getByRole("option").first().click();
  await desk.getByRole("button", { name: "Add", exact: true }).click();
}
await desk.getByLabel("Name of stop 1").fill("Samsun Otogar");
await desk.getByLabel("Seat for the whole way, TL").fill("380");
await desk.getByLabel("Notes for passengers").fill("Bagaj geniş. Sigara içilmiyor.");
await desk.evaluate(() => { (document.activeElement)?.blur(); window.scrollTo(0, 0); });
await settle(desk, 2000);
await desk.screenshot({ path: `${OUT}offer.png`, fullPage: true });

// The walkthrough: Ayşe finds a ride on her phone and asks for a seat; Mehmet accepts on his laptop; on the day she
// follows his car to her stop and gets in with the code on her screen.
const laptop = await signedIn(mehmet, { width: 1280, height: 800 });
const phone = await signedIn(ayse, { width: 390, height: 844 }, phoneOptions);
let frame = 0;
const shoot = async (label) => {
  const n = String(frame++).padStart(2, "0");
  await laptop.screenshot({ path: `${OUT}frames/${n}-desk.png` });
  await phone.screenshot({ path: `${OUT}frames/${n}-phone.png` });
  console.log(`frame ${n}: ${label}`);
};
await laptop.goto(`${BASE}/drive`);
await laptop.locator(".ride-row").first().waitFor();
await phone.goto(`${BASE}/`);
await phone.getByLabel("From").fill("sams");
await phone.getByRole("listbox").getByRole("option").first().waitFor();
await settle(phone, 600);
await shoot("typing where she starts");
await phone.getByRole("listbox").getByRole("option").first().click();
await phone.getByLabel("To").fill("corum");
await phone.getByRole("listbox").getByRole("option").first().click();
await phone.getByLabel("Day").fill(day(1));
await phone.getByRole("button", { name: "Find a ride" }).click();
await phone.locator(".trip-card").nth(1).waitFor();
await settle(phone, 1500);
await phone.locator(".trips").scrollIntoViewIfNeeded();
await settle(phone, 600);
await shoot("trips passing Samsun and Çorum, with her share of the price");
await phone.locator(`.trip-card[href^="/t/${main.id}"]`).click();
await phone.locator(".trip-side .price").waitFor();
await settle(phone, 2000);
await shoot("the route, the stops and the driver");
await phone.locator(".trip-side").scrollIntoViewIfNeeded();
await settle(phone, 800);
await phone.screenshot({ path: `${OUT}trip-phone.png` });
await shoot("where she gets in and out, and the price for her part");
await phone.getByLabel(/A note for/).fill("Otogarın önünde beklerim.");
await phone.getByRole("button", { name: "Ask for seats" }).click();
await phone.waitForURL(/\/rides\//);
const asked = phone.url().split("/").pop();
await settle(phone, 1200);
await laptop.goto(`${BASE}/drive/${main.id}`);
await laptop.locator(".passenger").filter({ hasText: "Ayşe Kaya" }).waitFor();
await settle(laptop, 1500);
await shoot("the request reaches the driver");
await laptop.goto(`${BASE}/rides/${asked}`);
await laptop.getByRole("button", { name: "Accept" }).click();
await laptop.locator(".badge").filter({ hasText: "Confirmed" }).waitFor();
await phone.reload();
await phone.locator(".code-big").waitFor();
await settle(phone, 1200);
await shoot("accepted: her boarding code and the car's plate");

// On the day: Mehmet's car is on the road towards Havza, where Ayşe waits.
await laptop.goto(`${BASE}/drive/${live.id}`);
await laptop.getByText("Where the car is").waitFor();
await phone.goto(`${BASE}/rides/${liveRide.id}`);
await phone.locator(".feed.on").waitFor({ timeout: 20_000 });
for (const kmAlong of [6, 22, 38, 52, 61]) {
  await call(`/api/trips/${live.id}/position`, mehmet.token, along(live, kmAlong));
  await phone.locator(".eta strong").waitFor();
  await settle(phone, 1200);
  await settle(laptop, 300);
  await shoot(`the car at ${kmAlong} km, live on both screens`);
  if (kmAlong === 38) {
    await phone.screenshot({ path: `${OUT}live-phone.png` });
    await laptop.screenshot({ path: `${OUT}live-driver.png` });
  }
}
await call(`/api/trips/${live.id}/position`, mehmet.token, along(live, live.stops[3].kmAlong));
await laptop.goto(`${BASE}/rides/${liveRide.id}`);
await laptop.getByLabel("Boarding code").fill(await codeOf(ayse, liveRide.id));
await settle(laptop, 400);
await shoot("Mehmet types in the code on her screen");
await laptop.getByRole("button", { name: "On board" }).click();
await laptop.locator(".badge").filter({ hasText: "On board" }).waitFor();
await phone.reload();
await phone.getByText("You are on board.").waitFor();
await settle(phone, 1200);
await shoot("on board, and how far to her stop");

const profile = await signedIn(ayse, { width: 390, height: 844 }, phoneOptions);
await profile.goto(`${BASE}/u/${mehmet.session.user.id}`);
await profile.getByText("What people said").waitFor();
await settle(profile, 1200);
await profile.screenshot({ path: `${OUT}profile-phone.png` });

await browser.close();
console.log(`media written to ${OUT} (${frame} frames)`);
