import { useEffect, useId, useRef, useState } from "react";
import { api } from "../api";
import { useDebounced } from "../hooks";
import type { Place, Spot } from "../types";

/**
 * A town or city typed by name, with or without Turkish letters ("corum" finds Çorum). The list comes from the API's
 * built-in gazetteer, so no address leaves the app. A place picked on the map shows up here by its name.
 */
export function PlaceInput({ label, value, onChange, placeholder, autoFocus = false, required = false }: {
  label: string; value: Spot | null; onChange: (spot: Spot | null) => void; placeholder?: string; autoFocus?: boolean; required?: boolean;
}) {
  const id = useId();
  const [text, setText] = useState(value?.name ?? "");
  const [open, setOpen] = useState(false);
  const [found, setFound] = useState<Place[]>([]);
  const [active, setActive] = useState(0);
  const typed = useDebounced(text, 200);
  // Closing on blur waits a moment so a click on a suggestion lands; coming back to the field cancels it.
  const closing = useRef<ReturnType<typeof setTimeout>>();
  const show = () => { clearTimeout(closing.current); setOpen(true); };
  useEffect(() => () => clearTimeout(closing.current), []);

  useEffect(() => setText(value?.name ?? ""), [value]);

  useEffect(() => {
    if (!open || typed.trim().length < 2) {
      setFound([]);
      return;
    }
    let live = true;
    api.places(typed).then((places) => { if (live) { setFound(places); setActive(0); } }, () => live && setFound([]));
    return () => { live = false; };
  }, [typed, open]);

  function choose(place: Place) {
    onChange({ name: place.name, latitude: place.latitude, longitude: place.longitude });
    setText(place.name);
    setFound([]);
    setOpen(false);
  }

  function keyDown(e: React.KeyboardEvent) {
    if (!showing) return;
    if (e.key === "ArrowDown") { e.preventDefault(); setActive((active + 1) % found.length); }
    if (e.key === "ArrowUp") { e.preventDefault(); setActive((active - 1 + found.length) % found.length); }
    if (e.key === "Enter") { e.preventDefault(); choose(found[active]!); }
    if (e.key === "Escape") setOpen(false);
  }

  // Results for an earlier text are not offered while the new one is still being looked up.
  const showing = open && found.length > 0 && typed === text;
  return (
    <label className="place-field">
      {label}
      <input
        className="input" value={text} placeholder={placeholder} autoFocus={autoFocus} required={required} autoComplete="off"
        role="combobox" aria-expanded={showing} aria-controls={`${id}-list`} aria-autocomplete="list"
        aria-activedescendant={showing ? `${id}-${active}` : undefined}
        onChange={(e) => { setText(e.target.value); show(); if (value) onChange(null); }}
        onFocus={show} onBlur={() => { closing.current = setTimeout(() => setOpen(false), 150); }} onKeyDown={keyDown}
      />
      {showing && (
        <ul className="suggestions" role="listbox" id={`${id}-list`}>
          {found.map((place, i) => (
            <li key={place.label} id={`${id}-${i}`} role="option" aria-selected={i === active} className="suggestion"
              onMouseDown={(e) => { e.preventDefault(); choose(place); }}>
              <span>{place.name}</span>
              {place.province !== place.name && <span className="small muted">{place.province}</span>}
            </li>
          ))}
        </ul>
      )}
    </label>
  );
}
