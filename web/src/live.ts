import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from "@microsoft/signalr";
import { useEffect, useState } from "react";
import { api, currentToken } from "./api";
import type { LiveView } from "./types";

export type Feed = "connecting" | "live" | "reconnecting" | "off";

/**
 * Follows a trip over SignalR: the driver and passengers with a confirmed seat hear every position the car shares.
 * The hub reconnects by itself; after a reconnect the trip is followed again, since group membership does not
 * survive the connection. While the feed is down the view is polled, so the page never shows a stale car for long.
 */
export function useLive(tripId: string, enabled = true): { view: LiveView | null; feed: Feed; set: (view: LiveView) => void } {
  const [view, setView] = useState<LiveView | null>(null);
  const [feed, setFeed] = useState<Feed>("connecting");

  useEffect(() => {
    if (!enabled) return;
    let stopped = false;
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl("/hubs/trips", { accessTokenFactory: () => currentToken() ?? "" })
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 20_000, 30_000])
      .configureLogging(LogLevel.None)
      .build();

    const follow = async () => {
      const first = await connection.invoke<LiveView>("Follow", tripId);
      if (!stopped) { setView(first); setFeed("live"); }
    };
    connection.on("TripChanged", (next: LiveView) => { if (!stopped && next.tripId === tripId) setView(next); });
    connection.onreconnecting(() => !stopped && setFeed("reconnecting"));
    connection.onreconnected(() => { follow().catch(() => !stopped && setFeed("off")); });
    connection.onclose(() => !stopped && setFeed("off"));
    connection.start().then(follow).catch(() => !stopped && setFeed("off"));

    const poll = setInterval(() => {
      if (connection.state !== HubConnectionState.Connected) api.live(tripId).then((v) => !stopped && setView(v), () => {});
    }, 15_000);

    return () => {
      stopped = true;
      clearInterval(poll);
      void connection.stop();
    };
  }, [tripId, enabled]);

  return { view, feed, set: setView };
}
