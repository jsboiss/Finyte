import { useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { CircleHelp, X } from './Icons'

export function Help({ title = 'How this works', children }: { title?: string; children: ReactNode }) {
  const id = useId()
  const trigger = useRef<HTMLButtonElement>(null)
  const popover = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  useEffect(() => {
    if (!open) { return }
    const position = () => {
      const anchor = trigger.current?.getBoundingClientRect()
      const panel = popover.current
      if (!anchor || !panel) { return }
      const width = panel.offsetWidth
      const height = panel.offsetHeight
      const left = Math.max(12, Math.min(anchor.right - width, window.innerWidth - width - 12))
      const top = anchor.bottom + height + 8 <= window.innerHeight - 12
        ? anchor.bottom + 8 : Math.max(12, anchor.top - height - 8)
      panel.style.left = `${left}px`
      panel.style.top = `${Math.max(12, Math.min(top, window.innerHeight - height - 12))}px`
    }
    position()
    window.addEventListener('resize', position)
    window.addEventListener('scroll', position, true)
    return () => { window.removeEventListener('resize', position); window.removeEventListener('scroll', position, true) }
  }, [open])
  return <span className="context-help">
    <button ref={trigger} className="help-trigger" type="button" aria-label={title} aria-expanded={open} popoverTarget={id}><CircleHelp size={18} /></button>
    {createPortal(<div ref={popover} id={id} popover="auto" role="dialog" aria-label={title} className="help-popover" onToggle={event => setOpen(event.newState === 'open')}>
      <div className="help-popover-heading"><strong>{title}</strong><button type="button" aria-label={`Close ${title}`} popoverTarget={id} popoverTargetAction="hide"><X size={18} /></button></div>
      <div className="context-help-content" onClick={event => { if ((event.target as HTMLElement).closest('a, button')) { popover.current?.hidePopover() } }}>{children}</div>
    </div>, document.body)}
  </span>
}
