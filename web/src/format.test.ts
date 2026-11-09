import { describe, expect, it } from "vitest";
import { addDays, distanceKm, duration, initials, maxPriceFor, routeKm, shareOf, today, turkeyParts, turkeyTime } from "./format";

describe("format", () => {
  it("shares the seat price like the server: by distance, to the lira, never under 10 TL", () => {
    expect(shareOf(400, 197.5, 395)).toBe(200);
    expect(shareOf(400, 395, 395)).toBe(400);
    expect(shareOf(400, 3, 395)).toBe(10);
    expect(shareOf(8, 1, 395)).toBe(8);
  });

  it("caps a seat at 3 TL a kilometre", () => {
    expect(maxPriceFor(395.4)).toBe(1186);
    expect(maxPriceFor(2)).toBe(10);
  });

  it("measures routes along their legs", () => {
    const samsun = { name: "Samsun", latitude: 41.2867, longitude: 36.33 };
    const ankara = { name: "Ankara", latitude: 39.9334, longitude: 32.8597 };
    expect(distanceKm(samsun, ankara)).toBeGreaterThan(320);
    expect(distanceKm(samsun, ankara)).toBeLessThan(330);
    const havza = { name: "Havza", latitude: 40.97, longitude: 35.6622 };
    expect(routeKm([samsun, havza, ankara])).toBeGreaterThan(distanceKm(samsun, ankara));
  });

  it("keeps Turkey's clock whatever the browser's", () => {
    expect(today(0, Date.parse("2026-11-06T22:30:00Z"))).toBe("2026-11-07");
    expect(addDays("2026-10-31", 1)).toBe("2026-11-01");
    expect(turkeyParts(turkeyTime("2026-11-08", "08:30"))).toEqual({ date: "2026-11-08", time: "08:30" });
    expect(turkeyParts("2026-11-08T21:15:00Z")).toEqual({ date: "2026-11-09", time: "00:15" });
  });

  it("writes durations and initials briefly", () => {
    expect(duration(365)).toBe("6 h 5 min");
    expect(duration(45)).toBe("45 min");
    expect(initials("ilker ışık")).toBe("İI");
  });
});
