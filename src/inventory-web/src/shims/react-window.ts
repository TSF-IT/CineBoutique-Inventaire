import React from 'react';

/** Shim minimal : rend toutes les lignes dans un conteneur scrollable.
 *  Suffisant pour la dev quand la lib réelle n'est pas installée. */
type RowArgs = { index: number; style: React.CSSProperties };
type Props = {
  height: number | string;
  width: number | string;
  itemCount: number;
  itemSize: number;
  children: (args: RowArgs) => React.ReactNode;
} & Record<string, any>;

export const FixedSizeList: React.FC<Props> = ({ height, width, itemCount, itemSize, children, className }) => {
  const rows: React.ReactNode[] = [];
  for (let index = 0; index < itemCount; index++) {
    rows.push(
      <div key={index} style={{ height: itemSize, width: '100%' }}>
        {children({ index, style: { height: itemSize, width: '100%' } })}
      </div>
    );
  }
  return (
    <div className={className} style={{ height, width, overflow: 'auto' }}>
      {rows}
    </div>
  );
};
