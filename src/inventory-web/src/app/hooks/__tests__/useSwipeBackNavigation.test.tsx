import { fireEvent, render } from '@testing-library/react'
import { describe, expect, it, beforeEach, vi } from 'vitest'
import { useSwipeBackNavigation } from '../useSwipeBackNavigation'

const navigateMock = vi.fn()

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom')
  return {
    ...actual,
    useNavigate: () => navigateMock,
  }
})

const SwipeTester = ({ enabled = true }: { enabled?: boolean }) => {
  const handlers = useSwipeBackNavigation({ enabled, to: '/select-user' })
  return (
    <div data-testid="swipe-zone" {...handlers}>
      zone
    </div>
  )
}

describe('useSwipeBackNavigation', () => {
  beforeEach(() => {
    navigateMock.mockClear()
  })

  const performSwipe = (element: HTMLElement, start: [number, number], end: [number, number]) => {
    const [startX, startY] = start
    const [endX, endY] = end

    fireEvent.touchStart(element, {
      touches: [{ clientX: startX, clientY: startY }],
      changedTouches: [{ clientX: startX, clientY: startY }],
    })

    fireEvent.touchMove(element, {
      touches: [{ clientX: endX, clientY: endY }],
      changedTouches: [{ clientX: endX, clientY: endY }],
    })

    fireEvent.touchEnd(element, {
      touches: [],
      changedTouches: [{ clientX: endX, clientY: endY }],
    })
  }

  it('déclenche la navigation lorsque le balayage part du bord gauche', () => {
    const { getByTestId } = render(<SwipeTester />)
    const zone = getByTestId('swipe-zone')

    performSwipe(zone, [12, 160], [140, 165])

    expect(navigateMock).toHaveBeenCalledWith('/select-user')
  })

  it("ignore les balayages qui ne partent pas du bord", () => {
    const { getByTestId } = render(<SwipeTester />)
    const zone = getByTestId('swipe-zone')

    performSwipe(zone, [80, 150], [160, 150])

    expect(navigateMock).not.toHaveBeenCalled()
  })

  it('désactive la navigation quand le hook est désactivé', () => {
    const { getByTestId } = render(<SwipeTester enabled={false} />)
    const zone = getByTestId('swipe-zone')

    performSwipe(zone, [12, 160], [140, 165])

    expect(navigateMock).not.toHaveBeenCalled()
  })
})
