import { memo, type ReactNode } from 'react'

export const DashboardModuleFrame = memo(function DashboardModuleFrame({
  actions,
  children,
  eyebrow,
  icon,
  title,
}: {
  actions?: ReactNode
  children: ReactNode
  eyebrow?: string
  icon?: ReactNode
  title: string
}) {
  return (
    <section className="panel dashboard-module">
      <div className="panel-header dashboard-module-header">
        <div>
          {eyebrow && <p>{eyebrow}</p>}
          <h2>{title}</h2>
        </div>
        <div className="dashboard-module-actions">
          {actions}
          {icon}
        </div>
      </div>
      {children}
    </section>
  )
})
