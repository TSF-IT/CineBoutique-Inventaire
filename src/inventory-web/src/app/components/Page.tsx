import { clsx } from 'clsx'
import type { HTMLAttributes, ReactNode } from "react";
import { Link } from "react-router-dom";

import { useSwipeBackNavigation } from "../hooks/useSwipeBackNavigation";

import { PageShell } from "./PageShell";
import { ThemeToggle } from "./ThemeToggle";

type PageProps = HTMLAttributes<HTMLDivElement> & {
  showHomeLink?: boolean;
  homeLinkLabel?: string;
  homeLinkTo?: string;
  headerAction?: ReactNode;
  mobileNav?: ReactNode;
};

export const Page = ({
  className,
  children,
  showHomeLink = false,
  homeLinkLabel = "Retour à l’accueil",
  homeLinkTo = "/",
  headerAction,
  mobileNav,
  ...rest
}: PageProps) => {
  const swipeHandlers = useSwipeBackNavigation({
    enabled: showHomeLink,
    to: homeLinkTo,
  });

  // On filtre simplement les props onTouch* si elles existent
  // (elles ne sont plus utilisées depuis react-swipeable v7)
  const { onTouchStart, onTouchMove, onTouchEnd, onTouchCancel, ...restProps } =
    rest;
  void onTouchStart;
  void onTouchMove;
  void onTouchEnd;
  void onTouchCancel;

  return (
    <PageShell
      {...restProps}
      {...swipeHandlers}
      mainClassName={clsx(
        "page-content cb-surface-panel relative flex w-full flex-col gap-8 px-5 py-6 text-base shadow-panel-soft backdrop-blur-sm transition-colors duration-300 sm:px-10 sm:py-12",
        className
      )}
      nav={mobileNav}
      header={
        <div className="page-header flex w-full items-center gap-4">
          {(showHomeLink || headerAction) && (
            <div className="flex items-center gap-3">
              {showHomeLink && (
                <Link
                  to={homeLinkTo}
                  className="inline-flex h-11 w-11 items-center justify-center rounded-full border border-(--cb-border-soft) bg-(--cb-surface-soft) text-lg font-semibold text-brand-600 shadow-panel-soft transition-all duration-200 hover:-translate-y-0.5 hover:text-brand-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-300 focus-visible:ring-offset-2 focus-visible:ring-offset-(--cb-surface) dark:text-brand-200"
                  data-testid="btn-go-home"
                  aria-label={homeLinkLabel}
                >
                  <span aria-hidden="true">←</span>
                  <span className="sr-only">{homeLinkLabel}</span>
                </Link>
              )}
              {headerAction}
            </div>
          )}
          <div className="ml-auto shrink-0">
            <ThemeToggle />
          </div>
        </div>
      }
    >
      {children}
    </PageShell>
  );
};
