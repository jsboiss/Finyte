import { Loader2 } from 'lucide-react'

type TransactionPaginationProps = {
  isLoading: boolean
  onNext: () => void
  onPrevious: () => void
  page: number
  totalCount: number
  totalPages: number
  visibleCount: number
}

export function TransactionPagination({ isLoading, onNext, onPrevious, page, totalCount, totalPages, visibleCount }: TransactionPaginationProps) {
  return (
    <footer className="transaction-results-footer">
      <div className="transaction-count">
        <Loader2 className={isLoading ? 'spin-visible' : ''} aria-hidden="true" />
        <span>{visibleCount} of {totalCount} transactions · Page {page} of {totalPages}</span>
      </div>
      <div className="transaction-pagination">
        <button className="secondary-button" disabled={page <= 1 || isLoading} onClick={onPrevious} type="button">
          Previous
        </button>
        <button className="secondary-button" disabled={page >= totalPages || isLoading} onClick={onNext} type="button">
          Next
        </button>
      </div>
    </footer>
  )
}
