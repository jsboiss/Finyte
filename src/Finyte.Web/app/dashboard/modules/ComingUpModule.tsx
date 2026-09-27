import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { getUpcoming, money, shortDate } from '../../recurring/recurringApi'
import { DashboardModuleFrame } from '../components/DashboardModuleFrame'
import './ComingUpModule.css'

export function ComingUpModule({ accountId }: { accountId: string | null }) {
  const upcoming = useQuery({ queryKey: ['recurring', 'upcoming', 7, accountId ?? ''], queryFn: () => getUpcoming(7, accountId ?? undefined), staleTime: 60_000 })
  if (upcoming.isError) {
    return <DashboardModuleFrame eyebrow="Next 7 days" title="Coming up"><p role="alert">Unable to load recurring payments.</p></DashboardModuleFrame>
  }
  if (!upcoming.data || upcoming.data.activeSeriesCount === 0) {
    return null
  }
  return <DashboardModuleFrame eyebrow="Next 7 days" title="Coming up" actions={<Link to="/recurring">All recurring</Link>}>
    {upcoming.data.items.length === 0
      ? <p>Nothing due in the next 7 days.</p>
      : <ul className="coming-up-list">{upcoming.data.items.map(item => <li key={`${item.seriesId}-${item.date}`}>
        <time dateTime={item.date}>{shortDate(item.date)}</time>
        <span><strong>{item.name}</strong><small>{item.accountName}</small></span>
        <span className="coming-up-amount">{money(item.expectedAmount, item.currency)}</span>
      </li>)}</ul>}
  </DashboardModuleFrame>
}
