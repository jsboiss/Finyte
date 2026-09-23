// Mirrors OverviewProjector: the absolute amount is split evenly in minor units across the transaction's tags and
// the remainder goes to the first. The API returns tags in the same order the projector uses, so never re-sort here.
export function tagAllocationMinorUnits(amountMinorUnits: number, tagIds: string[], tagId: string) {
  const index = tagIds.indexOf(tagId)
  if (index < 0) {
    return 0
  }

  const amount = Math.abs(amountMinorUnits)
  const share = Math.floor(amount / tagIds.length)
  return share + (index === 0 ? amount % tagIds.length : 0)
}
