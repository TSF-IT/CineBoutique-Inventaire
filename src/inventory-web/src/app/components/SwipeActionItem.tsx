import type { ReactNode } from 'react'
import { useState } from 'react'
import { useSwipeable } from 'react-swipeable'
import clsx from 'clsx'

interface SwipeActionItemProps {
  children: ReactNode
  onEdit?: () => void
  onDelete?: () => void
}

export const SwipeActionItem = ({ children, onEdit, onDelete }: SwipeActionItemProps) => {
  const [revealed, setRevealed] = useState(false)

  const handlers = useSwipeable({
    onSwipedLeft: () => setRevealed(true),
    onSwipedRight: () => setRevealed(false),
    preventScrollOnSwipe: true,
    trackTouch: true,
    trackMouse: true,
  })

  return (
    <div className="relative w-full overflow-hidden rounded-2xl">
      <div className="absolute right-0 top-0 flex h-full items-center gap-2 pr-4">
        {onEdit && (
          <button
            type="button"
            className="rounded-xl bg-blue-600 px-3 py-2 text-sm font-semibold text-white shadow"
            onClick={() => {
              setRevealed(false)
              onEdit()
            }}
          >
            Modifier
          </button>
        )}
        {onDelete && (
          <button
            type="button"
            className="rounded-xl bg-red-600 px-3 py-2 text-sm font-semibold text-white shadow"
            onClick={() => {
              setRevealed(false)
              onDelete()
            }}
          >
            Supprimer
          </button>
        )}
      </div>
      <div
        {...handlers}
        className={clsx(
          'relative z-10 bg-slate-900/70 p-4 transition-transform duration-200',
          revealed ? '-translate-x-40' : 'translate-x-0',
        )}
      >
        {children}
      </div>
    </div>
  )
}
