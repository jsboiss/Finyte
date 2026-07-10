import { ChevronDown } from 'lucide-react'
import type { SelectHTMLAttributes } from 'react'

export function AppSelect(props: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <span className="app-select">
      <select {...props} />
      <ChevronDown aria-hidden="true" className="app-select-icon" />
    </span>
  )
}
