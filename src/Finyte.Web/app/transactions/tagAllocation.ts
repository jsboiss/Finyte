// tagIds arrive in the projector's own order, so sorting them here would move the remainder under another collation.
export function tagAllocationMinorUnits(amountMinorUnits: number, tagIds: string[], tagId: string) {
  const index = tagIds.indexOf(tagId)
  if (index < 0) {
    return 0
  }

  const amount = Math.abs(amountMinorUnits)
  const share = Math.floor(amount / tagIds.length)
  return share + (index === 0 ? amount % tagIds.length : 0)
}
