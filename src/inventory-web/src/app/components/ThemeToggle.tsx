import { useMemo } from "react";
import clsx from "clsx";
import { useTheme } from "../../theme/ThemeProvider";

export const ThemeToggle = () => {
  const { theme, toggleTheme } = useTheme();
  const isDark = theme === "dark";

  const label = useMemo(
    () => (isDark ? "Basculer en thème clair" : "Basculer en thème sombre"),
    [isDark]
  );

  return (
    <button
      type="button"
      onClick={toggleTheme}
      role="switch"
      aria-label={label}
      aria-checked={isDark}
      title={`Thème: ${isDark ? "sombre" : "clair"}`}
      className={clsx(
        "group relative inline-flex h-11 w-24 items-center overflow-hidden rounded-full border border-(--cb-border-strong) bg-(--cb-surface-soft) px-1 transition-all duration-200 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 focus-visible:ring-offset-2 focus-visible:ring-offset-(--cb-surface-strong)",
        isDark ? "shadow-(--cb-card-shadow-soft)" : "shadow-sm"
      )}
      style={{ minHeight: "var(--tap-min)" }}
    >
      <span className="sr-only">{label}</span>
      <span
        aria-hidden
        className={clsx(
          "pointer-events-none absolute inset-0 rounded-full transition-all duration-300 ease-out",
          isDark
            ? "bg-(--cb-toggle-track-dark)"
            : "bg-(--cb-toggle-track-light)"
        )}
      />
      <span
        aria-hidden
        className="pointer-events-none absolute inset-0 flex items-center justify-between px-3 text-lg"
      >
        <span
          className={clsx(
            "transition-all duration-300 ease-out",
            isDark
              ? "-translate-x-1 scale-75 opacity-50"
              : "translate-x-0 scale-100 opacity-100 text-amber-400"
          )}
        >
          ☀️
        </span>
        <span
          className={clsx(
            "transition-all duration-300 ease-out",
            isDark
              ? "translate-x-0 scale-100 opacity-100 text-amber-200"
              : "translate-x-1 scale-75 opacity-50"
          )}
        >
          🌙
        </span>
      </span>
      <span
        aria-hidden
        className={clsx(
          "pointer-events-none absolute inset-y-1 left-1 flex h-9 w-9 items-center justify-center rounded-full transition-all duration-300 ease-out shadow-[0_18px_38px_-20px_rgba(15,23,42,0.6)]",
          isDark
            ? "translate-x-12 bg-(--cb-toggle-knob-dark)"
            : "translate-x-0 bg-(--cb-toggle-knob-light)"
        )}
      />
    </button>
  );
};
