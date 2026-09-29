import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { httpClient } from '../api/httpClient'
import type { TransactionTag } from '../transactions/types'
import { acceptTagSuggestions, getTagSuggestions, type AcceptItem } from './tagSuggestionsApi'

const dismissedKey = 'finyte.generate-tags-dismissed'

function readDismissed() {
  try {
    return window.localStorage.getItem(dismissedKey) === 'true'
  } catch {
    return false
  }
}

export function GenerateTagsPrompt() {
  const client = useQueryClient()
  const tags = useQuery({ queryKey: ['tags'], queryFn: () => httpClient<TransactionTag[]>({ url: '/api/tags' }) })
  const [dismissed, setDismissed] = useState(readDismissed)
  const [notice, setNotice] = useState('')
  const generate = useMutation({
    mutationFn: async () => {
      const suggestions = await getTagSuggestions()
      const items: AcceptItem[] = suggestions.groups.flatMap(group => group.merchants.map(merchant => group.tagId
        ? { merchantName: merchant.ruleMerchantName, tagId: group.tagId, mode: 'rule' as const }
        : { merchantName: merchant.ruleMerchantName, tagName: group.tagName, mode: 'rule' as const }))
      let createdTags = 0
      for (let start = 0; start < items.length; start += 200) {
        createdTags += (await acceptTagSuggestions(items.slice(start, start + 200))).createdTags
      }
      return { merchants: items.length, createdTags }
    },
    onSuccess: async result => {
      setNotice(result.merchants === 0
        ? 'No tag suggestions were found yet. You can create tags yourself from the Tags menu.'
        : `Created ${result.createdTags} ${result.createdTags === 1 ? 'tag' : 'tags'} for ${result.merchants} ${result.merchants === 1 ? 'merchant' : 'merchants'}. Change any payment's tags to adjust them.`)
      await Promise.all(['tag-suggestions', 'tags', 'merchant-tags', 'transactions', 'overview', 'budgets'].map(key => client.invalidateQueries({ queryKey: [key] })))
    },
  })

  if (notice) {
    return <p role="status" className="panel generate-tags">{notice}</p>
  }
  if (dismissed || !tags.data || tags.data.length > 0) {
    return null
  }
  const dismiss = () => {
    try {
      window.localStorage.setItem(dismissedKey, 'true')
    } catch {
      return setDismissed(true)
    }
    setDismissed(true)
  }

  return <section className="panel generate-tags" aria-label="Generate tags">
    <p>You haven’t created any tags yet. Would you like to generate tags based on your existing transactions?</p>
    {generate.isError && <p role="alert">Tags could not be generated. Nothing was changed.</p>}
    <div className="generate-tags-actions">
      <button type="button" disabled={generate.isPending} onClick={() => generate.mutate()}>{generate.isPending ? 'Generating…' : 'Generate tags'}</button>
      <Link to="/tag-suggestions">Review suggestions first</Link>
      <button type="button" className="secondary" disabled={generate.isPending} onClick={dismiss}>Not now</button>
    </div>
  </section>
}
