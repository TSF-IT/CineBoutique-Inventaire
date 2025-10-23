import type { HTMLAttributes } from "react";
import clsx from "clsx";

export const Card = ({
  className,
  ...props
}: HTMLAttributes<HTMLDivElement>) => (
  <div
    className={clsx("card card--elev1 p-6 text-(--text-strong)", className)}
    {...props}
  />
);
