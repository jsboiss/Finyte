import type { ReactNode } from 'react'
import { CircleHelp } from './Icons'
export function Help({ title = 'How this works', children }: { title?: string; children: ReactNode }) {
  return <details className="context-help"><summary><CircleHelp size={16} />{title}</summary><div className="context-help-content">{children}</div></details>
}
