import { createContext, useContext, useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Ellipsis } from './Icons'

type OptionsMenuProps = {
  label: string
  align?: 'left' | 'right'
  trigger?: ReactNode
  className?: string
  children: ReactNode
}

const CloseContext = createContext<() => void>(() => {})

export function OptionsMenu({ label, align = 'left', trigger, className, children }: OptionsMenuProps) {
  const [open, setOpen] = useState(false)
  const [position, setPosition] = useState<CSSProperties>()
  const button = useRef<HTMLButtonElement>(null)
  const popover = useRef<HTMLDivElement>(null)

  useLayoutEffect(() => {
    if (!open || !button.current) {
      return
    }
    const place = () => {
      const rect = button.current!.getBoundingClientRect()
      const top = rect.bottom + 4
      setPosition(align === 'right'
        ? { top, right: Math.max(8, window.innerWidth - rect.right) }
        : { top, left: Math.max(8, rect.left) })
    }
    place()
    window.addEventListener('resize', place)
    window.addEventListener('scroll', place, true)
    return () => { window.removeEventListener('resize', place); window.removeEventListener('scroll', place, true) }
  }, [open, align])

  useEffect(() => {
    if (!open) {
      return
    }
    const onPointer = (event: MouseEvent) => {
      const target = event.target as Node
      if (!button.current?.contains(target) && !popover.current?.contains(target)) {
        setOpen(false)
      }
    }
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') { setOpen(false) } }
    document.addEventListener('mousedown', onPointer)
    document.addEventListener('keydown', onKey)
    return () => { document.removeEventListener('mousedown', onPointer); document.removeEventListener('keydown', onKey) }
  }, [open])

  return (
    <span className="options-menu">
      <button ref={button} type="button" className={className ? `options-menu-button ${className}` : 'options-menu-button'} title={label} aria-label={label} aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(x => !x)}>{trigger ?? <Ellipsis size={16} />}</button>
      {open && position && createPortal(
        <div ref={popover} className={`options-menu-popover is-${align}`} role="menu" style={position}>
          <CloseContext.Provider value={() => setOpen(false)}>{children}</CloseContext.Provider>
        </div>,
        document.body,
      )}
    </span>
  )
}

export function OptionsMenuHeading({ children }: { children: ReactNode }) {
  return <p className="options-menu-heading">{children}</p>
}

export function OptionsMenuItem({ children, disabled, onSelect }: { children: ReactNode; disabled?: boolean; onSelect: () => void }) {
  const close = useContext(CloseContext)
  return <button type="button" role="menuitem" className="options-menu-item" disabled={disabled} onClick={() => { onSelect(); close() }}>{children}</button>
}

export function OptionsMenuNote({ children, role }: { children: ReactNode; role?: string }) {
  return <span className="options-menu-note" role={role}>{children}</span>
}
