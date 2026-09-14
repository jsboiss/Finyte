import { useEffect, useId, useRef, type ReactNode } from 'react'
import { X } from './Icons'
export function Drawer({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  useEffect(() => {
    const dialog = ref.current!
    const previous = document.activeElement as HTMLElement | null
    const overflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    dialog.showModal()
    return () => { dialog.close(); document.body.style.overflow = overflow; previous?.focus() }
  }, [])
  return <dialog ref={ref} className="app-drawer" aria-labelledby={titleId} onCancel={onClose} onClick={event => { if (event.target === event.currentTarget) { const rect = event.currentTarget.getBoundingClientRect(); if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) { onClose() } } }}>
    <header><h2 id={titleId}>{title}</h2><button type="button" aria-label="Close panel" onClick={onClose}><X /></button></header>
    <div className="drawer-content">{children}</div>
  </dialog>
}
