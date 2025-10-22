import React from 'react';

type RowRendererArgs = { index: number; style: React.CSSProperties };

export type FixedSizeListHandle = {
  scrollToItem: (index: number) => void;
};

export type FixedSizeListProps = {
  height: number | string;
  width: number | string;
  itemCount: number;
  itemSize: number;
  className?: string;
  children: (args: RowRendererArgs) => React.ReactNode;
};

export const FixedSizeList = React.forwardRef<FixedSizeListHandle, FixedSizeListProps>(
  ({ height, width, itemCount, itemSize, className, children }, ref) => {
    const containerRef = React.useRef<HTMLDivElement | null>(null);

    React.useImperativeHandle(
      ref,
      () => ({
        scrollToItem: (index: number) => {
          const container = containerRef.current;
          if (!container) return;
          const child = container.children[index] as HTMLElement | undefined;
          child?.scrollIntoView({ block: 'nearest' });
        },
      }),
      [],
    );

    const rows = React.useMemo(
      () =>
        Array.from({ length: itemCount }, (_, index) => (
          <div key={index} style={{ height: itemSize, width: '100%' }}>
            {children({ index, style: { height: itemSize, width: '100%' } })}
          </div>
        )),
      [children, itemCount, itemSize],
    );

    return (
      <div ref={containerRef} className={className} style={{ height, width, overflow: 'auto' }}>
        {rows}
      </div>
    );
  },
);

FixedSizeList.displayName = 'FixedSizeList';
