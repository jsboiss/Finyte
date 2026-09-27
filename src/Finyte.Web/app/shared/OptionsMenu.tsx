import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
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
  const root = useRef<HTMLSpanElement>(null)

  useEffect(() => {
    if (!open) {
      return
    }
    const onPointer = (event: MouseEvent) => { if (!root.current?.contains(event.target as Node)) { setOpen(false) } }
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') { setOpen(false) } }
    document.addEventListener('mousedown', onPointer)
    document.addEventListener('keydown', onKey)
    return () => { document.removeEventListener('mousedown', onPointer); document.removeEventListener('keydown', onKey) }
  }, [open])

  return (
    <span className="options-menu" ref={root}>
      <button type="button" className={className ? `options-menu-button ${className}` : 'options-menu-button'} title={label} aria-label={label} aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(x => !x)}>{trigger ?? <Ellipsis size={16} />}</button>
      {open && <div className={`options-menu-popover is-${align}`} role="menu"><CloseContext.Provider value={() => setOpen(false)}>{children}</CloseContext.Provider></div>}
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
