// A driver, a passenger and a rival, in separate browsers, against the whole Compose stack: a car is added and a trip
// from Samsun to Ankara offered by stops; the passenger finds it from Samsun to Çorum, asks for a seat and is
// accepted. On a second trip leaving now, two people take the last seat at the same moment and one gets it; the
// driver starts, drives the route in demo mode, and the passenger follows the car over the live feed, boards with
// the code on their screen, and reviews the driver once the trip is finished.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=1000 docker compose up --build -d
//        cd tests/e2e && npm install && npx playwright install chromium && node yoldas.mjs
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = new URL("./output/", import.meta.url).pathname;
mkdirSync(OUT, { recursive: true });

const run = Date.now().toString(36);
// With SCREENSHOTS=1, every page is kept after every step, for looking over the screens by eye.
const SCREENSHOTS = process.env.SCREENSHOTS === "1";
let stepNo = 0;
const password = "e2e-test-password";
const driver = { name: "Mehmet Yıldız", email: `mehmet.${run}@example.com` };
const passenger = { name: "Ayşe Kaya", email: `ayse.${run}@example.com` };
const rival = { name: "Can Demir", email: `can.${run}@example.com` };

const browser = await chromium.launch();
const errors = [];
const pages = {};
let failures = 0;

async function step(name, action) {
  try {
    await action();
    console.log(`ok   ${name}`);
    stepNo++;
    if (SCREENSHOTS) for (const [label, page] of Object.entries(pages)) await page.screenshot({ path: `${OUT}${stepNo}-${label}.png` }).catch(() => {});
  } catch (error) {
    failures++;
    console.log(`FAIL ${name}\n     ${String(error.message ?? error).split("\n").slice(0, 3).join("\n     ")}`);
    for (const [label, page] of Object.entries(pages)) await page.screenshot({ path: `${OUT}${label}-${failures}.png`, fullPage: true }).catch(() => {});
  }
}

async function open(label, viewport = { width: 1360, height: 900 }, options = {}) {
  const context = await browser.newContext({ viewport, locale: "en-GB", timezoneId: "Europe/Istanbul", ...options });
  const page = await context.newPage();
  page.on("pageerror", (error) => errors.push(`${label}: ${error.message}`));
  page.on("console", (message) => {
    // Refused requests the test makes on purpose show up as failed resources; they are checked on screen instead.
    if (message.type() === "error" && !/Failed to load resource|tile\.openstreetmap/.test(message.text())) errors.push(`${label}: ${message.text()}`);
  });
  pages[label] = page;
  return page;
}

/** Calls the API as whoever is signed in on the page. Returns the status and body; never throws for an HTTP error. */
async function call(page, method, path, body) {
  return page.evaluate(async ({ method, path, body }) => {
    const session = JSON.parse(localStorage.getItem("yoldas.session") ?? "null");
    const headers = { "Content-Type": "application/json" };
    if (session) headers.Authorization = `Bearer ${session.accessToken}`;
    const response = await fetch(path, { method, headers, body: body ? JSON.stringify(body) : undefined });
    const text = await response.text();
    return { status: response.status, body: text ? JSON.parse(text) : null };
  }, { method, path, body });
}

async function register(who) {
  const response = await fetch(`${BASE}/api/auth/register`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ displayName: who.name, email: who.email, password }) });
  if (!response.ok) throw new Error(`register ${who.email}: ${response.status}`);
}

async function signIn(page, who, next) {
  await page.goto(`${BASE}/signin?next=${encodeURIComponent(next)}`);
  await page.getByLabel("Email").fill(who.email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.waitForURL((url) => url.pathname === next.split("?")[0]);
}

/** Types into a place field and picks the suggestion with that name. */
async function place(page, label, typed, name, scope = page) {
  await scope.getByLabel(label, { exact: true }).fill(typed);
  await page.getByRole("option").filter({ hasText: new RegExp(`^${name}`) }).first().click();
}

const day = (plus) => new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Istanbul" }).format(new Date(Date.now() + plus * 86_400_000));

for (const who of [driver, passenger, rival]) await register(who);
const desk = await open("driver");
const phone = await open("passenger", { width: 390, height: 844 }, { isMobile: true, hasTouch: true });
let tripId;
let bookingId;

await step("the driver adds their car and offers Samsun to Ankara by Havza and Çorum", async () => {
  await signIn(desk, driver, "/drive");
  await desk.getByRole("link", { name: "Add your car first" }).click();
  await desk.getByLabel("Make").fill("Renault");
  await desk.getByLabel("Model").fill("Clio");
  await desk.getByLabel("Colour").fill("Beyaz");
  await desk.getByLabel("Plate").fill("55 abc 123");
  await desk.getByLabel("Seats for passengers").selectOption("3");
  await desk.getByRole("button", { name: "Save" }).click();
  await desk.waitForURL(`${BASE}/drive/new`);
  for (const [typed, name, label] of [["sams", "Samsun", "Where you start"], ["havza", "Havza", "Where you are going"], ["corum", "Çorum", "Another stop"], ["ankara", "Ankara", "Another stop"]]) {
    await place(desk, label, typed, name);
    await desk.getByRole("button", { name: "Add", exact: true }).click();
  }
  await desk.getByText(/about \d+ h/).waitFor();
  await desk.getByLabel("Day").fill(day(1));
  await desk.getByLabel("Leaving at").fill("08:30");
  await desk.getByLabel("Seat for the whole way, TL").fill("2000");
  await desk.getByRole("button", { name: "Offer the trip" }).click();
  await desk.getByText(/costs 10 to \d+ TL/).waitFor();
  await desk.getByLabel("Seat for the whole way, TL").fill("400");
  await desk.getByLabel("Notes for passengers").fill("Bagaj geniş. Havza'da çay molası.");
  await desk.getByRole("button", { name: "Offer the trip" }).click();
  await desk.waitForURL(/\/drive\/[0-9a-f-]{36}$/);
  tripId = desk.url().split("/").pop();
  await desk.getByText("Nobody has booked yet.").waitFor();
});

await step("the passenger finds the trip from Samsun to Çorum and sees their share of the price", async () => {
  await phone.goto(BASE);
  await place(phone, "From", "samsun", "Samsun");
  await place(phone, "To", "corum", "Çorum");
  await phone.getByLabel("Day").fill(day(1));
  await phone.getByRole("button", { name: "Find a ride" }).click();
  const card = phone.locator(".trip-card").filter({ hasText: driver.name });
  await card.waitFor();
  // Samsun to Çorum is a little under half of the route, so a little under half of 400 TL.
  const price = Number((await card.locator(".price").innerText()).replace(/\D/g, ""));
  if (price < 150 || price > 220) throw new Error(`share of the price was ${price}`);
  await card.click();
  await phone.waitForURL(new RegExp(`/t/${tripId}`));
  await phone.getByText("Bagaj geniş.").waitFor();
  await phone.getByRole("link", { name: "Sign in to book" }).click();
  await phone.getByLabel("Email").fill(passenger.email);
  await phone.getByLabel("Password").fill(password);
  await phone.getByRole("button", { name: "Sign in" }).click();
  await phone.waitForURL(new RegExp(`/t/${tripId}`));
  await phone.getByLabel(/A note for/).fill("Otogarın önünde beklerim.");
  await phone.getByRole("button", { name: "Ask for seats" }).click();
  await phone.waitForURL(/\/rides\/[0-9a-f-]{36}$/);
  bookingId = phone.url().split("/").pop();
  await phone.getByText("Waiting for the driver").first().waitFor();
});

await step("the driver sees the request, accepts, and the seat is counted on the legs it covers", async () => {
  await desk.reload();
  await desk.locator(".leg").filter({ hasText: "Samsun – Havza" }).getByText("0 / 3").waitFor();
  await desk.locator(".passenger").filter({ hasText: passenger.name }).click();
  await desk.getByText("Otogarın önünde beklerim.").waitFor();
  await desk.getByRole("button", { name: "Accept" }).click();
  await desk.locator(".badge").filter({ hasText: "Confirmed" }).waitFor();
  await desk.getByRole("link", { name: "Back to the trip" }).click();
  await desk.locator(".leg").filter({ hasText: "Samsun – Havza" }).getByText("1 / 3").waitFor();
  await desk.locator(".leg").filter({ hasText: "Havza – Çorum" }).getByText("1 / 3").waitFor();
  await desk.locator(".leg").filter({ hasText: "Çorum – Ankara" }).getByText("0 / 3").waitFor();
  await phone.reload();
  await phone.getByText("Show this to").waitFor();
  await phone.getByText("55 ABC 123").waitFor();
});

let liveId;
let liveBooking;
let code;

await step("two people take the last seat of a trip leaving now at the same moment: one gets it, the other is told", async () => {
  const at = new Date(Date.now() + 31 * 60_000).toISOString();
  const stops = [
    { name: "Atakum", latitude: 41.333, longitude: 36.271 },
    { name: "Samsun Otogar", latitude: 41.2601, longitude: 36.2847 },
    { name: "Havza", latitude: 40.97, longitude: 35.6622 },
  ];
  const offered = await call(desk, "POST", "/api/trips", { stops, departureAt: at, seats: 1, pricePerSeat: 150, instantBook: true, notes: "" });
  if (offered.status !== 201) throw new Error(`offering the live trip gave ${offered.status}: ${JSON.stringify(offered.body)}`);
  liveId = offered.body.id;
  const other = await open("rival");
  await signIn(other, rival, "/rides");
  const ends = "?from=Atakum&fromAt=41.333,36.271&to=Havza&toAt=40.97,35.6622";
  await Promise.all([phone.goto(`${BASE}/t/${liveId}${ends}`), other.goto(`${BASE}/t/${liveId}${ends}`)]);
  await Promise.all([phone.getByRole("button", { name: "Book" }).waitFor(), other.getByRole("button", { name: "Book" }).waitFor()]);
  await Promise.all([phone.getByRole("button", { name: "Book" }).click(), other.getByRole("button", { name: "Book" }).click()]);
  const won = await Promise.race([
    phone.waitForURL(/\/rides\//, { timeout: 15_000 }).then(() => "passenger"),
    other.waitForURL(/\/rides\//, { timeout: 15_000 }).then(() => "rival"),
  ]);
  const loser = won === "passenger" ? other : phone;
  await loser.getByRole("alert").filter({ hasText: /seat/i }).waitFor();
  if (won !== "passenger") {
    // The rival won the race; the passenger takes a seat on the same trip's last free part instead, so the rest of
    // the walkthrough follows them.
    const theirs = (await call(other, "GET", "/api/me/bookings")).body.find((b) => b.trip.id === liveId);
    await call(other, "POST", `/api/bookings/${theirs.id}/cancel`, { reason: "Planım değişti." });
    await phone.reload();
    await phone.getByRole("button", { name: "Book" }).click();
    await phone.waitForURL(/\/rides\//);
  }
  liveBooking = phone.url().split("/").pop();
  code = (await phone.locator(".code-big").innerText()).trim();
  if (!/^\d{4}$/.test(code)) throw new Error(`boarding code was "${code}"`);
});

await step("the driver starts the trip and drives the route; the passenger follows the car live", async () => {
  // A trip can be started from half an hour before it leaves; this one leaves in 31 minutes.
  await desk.goto(`${BASE}/drive/${liveId}`);
  await desk.waitForTimeout(65_000);
  await desk.getByRole("button", { name: "Start the trip" }).click();
  await desk.getByText("On the road").first().waitFor();
  await phone.reload();
  await phone.getByText("The car, live").waitFor();
  await phone.locator(".feed.on").waitFor({ timeout: 20_000 });
  await desk.getByRole("radio", { name: "Demo: drive the route" }).click();
  await phone.locator(".eta strong, .notice").first().waitFor({ timeout: 30_000 });
  await phone.locator(".leaflet-marker-icon .pin").first().waitFor();
  await desk.getByText(/km\/h, shared/).waitFor({ timeout: 30_000 });
});

await step("the driver lets the passenger in with the code from their screen, after a wrong one", async () => {
  await desk.getByRole("radio", { name: "Not sharing" }).click();
  await desk.locator(".passenger").filter({ hasText: passenger.name }).click();
  const wrong = code === "0000" ? "1111" : "0000";
  await desk.getByLabel("Boarding code").fill(wrong);
  await desk.getByRole("button", { name: "On board" }).click();
  await desk.getByRole("alert").waitFor();
  await desk.getByLabel("Boarding code").fill(code);
  await desk.getByRole("button", { name: "On board" }).click();
  await desk.locator(".badge").filter({ hasText: "On board" }).waitFor();
  await phone.reload();
  await phone.getByText("You are on board.").waitFor({ timeout: 20_000 });
});

await step("the driver finishes; the passenger's ride is completed and they review the driver", async () => {
  await desk.goto(`${BASE}/drive/${liveId}`);
  await desk.getByRole("button", { name: "Finish the trip" }).click();
  await desk.getByText("Finished").first().waitFor();
  await phone.goto(`${BASE}/rides/${liveBooking}`);
  await phone.getByText("Completed").first().waitFor();
  await phone.getByRole("radio", { name: "4 of 5" }).click();
  await phone.getByLabel(/A few words/).fill("Zamanında geldi, rahat bir yolculuktu.");
  await phone.getByRole("button", { name: "Send review" }).click();
  await phone.getByText("You wrote").waitFor();
  const me = (await call(desk, "GET", "/api/me")).body;
  await phone.goto(`${BASE}/u/${me.user.id}`);
  await phone.getByText("Zamanında geldi").waitFor();
  await phone.getByText("1 trip driven").waitFor();
});

await step("the live feed and other people's rides stay private", async () => {
  const stranger = pages.rival;
  const peek = await call(stranger, "GET", `/api/bookings/${bookingId}`);
  if (peek.status !== 404) throw new Error(`someone else's booking gave ${peek.status}`);
  const live = await call(stranger, "GET", `/api/trips/${tripId}/live`);
  if (live.status !== 404) throw new Error(`someone else's live trip gave ${live.status}`);
  const anonymous = await fetch(`${BASE}/hubs/trips/negotiate?negotiateVersion=1`, { method: "POST" });
  if (anonymous.status !== 401) throw new Error(`the live feed let in a visitor: ${anonymous.status}`);
});

if (errors.length > 0) {
  failures++;
  console.log(`FAIL the browsers reported errors\n     ${[...new Set(errors)].join("\n     ")}`);
}

await browser.close();
console.log(failures === 0 ? "\nAll steps passed." : `\n${failures} step(s) failed.`);
process.exit(failures === 0 ? 0 : 1);
