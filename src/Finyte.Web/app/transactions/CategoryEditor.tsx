import { useState } from 'react'
import type { Transaction } from './types'

type CategoryEditorProps = {
  disabled: boolean
  onChange: (category: string | null) => void
  suggestions: string[]
  transaction: Transaction
}

export function CategoryEditor({ disabled, onChange, suggestions, transaction }: CategoryEditorProps) {
  const [isOpen, setIsOpen] = useState(false)
  const [draft, setDraft] = useState(transaction.category)
  const listId = `categories-${transaction.id}`

  if (!isOpen) {
    return (
      <button
        aria-label={`Category ${transaction.category}. Change category`}
        className="category-chip"
        disabled={disabled}
        onClick={() => { setDraft(transaction.category); setIsOpen(true) }}
        type="button"
      >
        {transaction.category}
        {transaction.hasCategoryOverride && <small>edited</small>}
      </button>
    )
  }

  const trimmed = draft.trim()
  return (
    <form
      className="category-editor"
      onSubmit={event => { event.preventDefault(); if (trimmed) { onChange(trimmed); setIsOpen(false) } }}
    >
      <input
        aria-label="Category"
        autoFocus
        list={listId}
        maxLength={128}
        onChange={event => setDraft(event.target.value)}
        value={draft}
      />
      <datalist id={listId}>
        {suggestions.map(x => <option key={x} value={x} />)}
      </datalist>
      <button disabled={disabled || !trimmed} type="submit">Save</button>
      {transaction.hasCategoryOverride && (
        <button disabled={disabled} onClick={() => { onChange(null); setIsOpen(false) }} type="button">Use imported</button>
      )}
      <button onClick={() => setIsOpen(false)} type="button">Cancel</button>
    </form>
  )
}
