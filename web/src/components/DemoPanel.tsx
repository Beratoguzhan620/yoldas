import { Car, LogIn, Ticket } from "lucide-react";
import { useEffect, useState } from "react";
import { api, type DemoAccount } from "../api";
import { useApp } from "../context";
import { navigate } from "../router";

let cached: Promise<DemoAccount[]> | null = null;

/** The public demo's accounts, asked for once; an empty list everywhere else. */
export function useDemoAccounts(): DemoAccount[] {
  const [accounts, setAccounts] = useState<DemoAccount[]>([]);
  useEffect(() => {
    cached ??= api.demo().catch(() => []);
    let live = true;
    cached.then((list) => live && setAccounts(list));
    return () => { live = false; };
  }, []);
  return accounts;
}

/**
 * On the public demo, one click signs in as the passenger or the driver, each in the middle of something: a car on
 * its way to pick Ayşe up, a trip of Mehmet's with passengers and a request to answer. Nothing shows anywhere else.
 */
export function DemoPanel({ compact = false, next }: { compact?: boolean; next?: string }) {
  const accounts = useDemoAccounts();
  const { signedIn } = useApp();
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  if (accounts.length === 0) return null;

  async function use(account: DemoAccount) {
    setBusy(account.email);
    setError(null);
    try {
      signedIn(await api.login(account.email, account.password));
      navigate(next ?? (account.role === "Driver" ? "/drive" : "/rides"), true);
    } catch (err) {
      setError((err as Error).message);
      setBusy(null);
    }
  }

  return (
    <section className={`card demo-panel${compact ? " compact" : ""}`} aria-labelledby="demo-title">
      <div>
        <h2 id="demo-title">Try the demo</h2>
        <p className="small muted">Sign in as one of them; nobody needs an account. The data is made up and starts over now and then.</p>
      </div>
      <div className="demo-accounts">
        {accounts.map((account) => (
          <button key={account.email} type="button" className="demo-account" onClick={() => use(account)} disabled={busy !== null}>
            <span className="demo-icon">{account.role === "Driver" ? <Car size={18} /> : <Ticket size={18} />}</span>
            <span className="grow">
              <strong>{account.name}</strong> <span className="badge plain">{account.role === "Driver" ? "Driver" : "Passenger"}</span>
              {!compact && <span className="small muted block">{account.description}</span>}
            </span>
            <LogIn size={16} aria-hidden="true" />
          </button>
        ))}
      </div>
      {error && <p className="form-error" role="alert">{error}</p>}
    </section>
  );
}
