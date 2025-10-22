import React from 'react';

type RowRendererArgs = { index: number; style: React.CSSProperties };

export type FixedSizeListProps = {
  height: number | string;
  width: number | string;
  itemCount: number;
  itemSize: number;
  className?: string;
  children: (args: RowRendererArgs) => React.ReactNode;
};

export const FixedSizeList: React.FC<FixedSizeListProps> = ({
  height,
  width,
  itemCount,
  itemSize,
  className,
  children,
}) => {
  const rows = Array.from({ length: itemCount }, (_, index) => (
    <div key={index} style={{ height: itemSize, width: '100%' }}>
      {children({ index, style: { height: itemSize, width: '100%' } })}
    </div>
  ));

  return (
    <div className={className} style={{ height, width, overflow: 'auto' }}>
      {rows}
    </div>
  );
};
