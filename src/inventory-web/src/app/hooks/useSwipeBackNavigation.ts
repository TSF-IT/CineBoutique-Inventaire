import { useNavigate } from 'react-router-dom'
import { useSwipeable } from 'react-swipeable'

const EDGE_THRESHOLD_PX = 32
const MAX_VERTICAL_DRIFT_PX = 45
const MIN_HORIZONTAL_DELTA_PX = 60

interface SwipeBackOptions {
  enabled: boolean
  to: string
}

export const useSwipeBackNavigation = ({ enabled, to }: SwipeBackOptions) => {
  const navigate = useNavigate()

  return useSwipeable({
    trackTouch: true,
    trackMouse: false,
    preventScrollOnSwipe: false,
    delta: MIN_HORIZONTAL_DELTA_PX,
    onSwipedRight: (eventData) => {
      if (!enabled) {
        return
      }

      const originalEvent = eventData.event
      if (!originalEvent || !originalEvent.type.startsWith('touch')) {
        return
      }

      const [startX] = eventData.initial
      if (startX > EDGE_THRESHOLD_PX) {
        return
      }

      if (Math.abs(eventData.deltaY) > MAX_VERTICAL_DRIFT_PX) {
        return
      }

      navigate(to)
    },
  })
}
