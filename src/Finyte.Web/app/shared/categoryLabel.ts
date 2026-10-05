const primaryCategories = [
  'GOVERNMENT_AND_NON_PROFIT', 'RENT_AND_UTILITIES', 'GENERAL_MERCHANDISE', 'HOME_IMPROVEMENT', 'GENERAL_SERVICES',
  'TRANSPORTATION', 'LOAN_PAYMENTS', 'FOOD_AND_DRINK', 'ENTERTAINMENT', 'PERSONAL_CARE', 'TRANSFER_OUT', 'TRANSFER_IN',
  'MERCHANDISE', 'BANK_FEES', 'SERVICES', 'MEDICAL', 'INCOME', 'TRAVEL',
]

function words(code: string) {
  const text = code.toLowerCase().replaceAll('_', ' ').replace(/\btv\b/g, 'TV').replace(/\batm\b/g, 'ATM')
  return text.charAt(0).toUpperCase() + text.slice(1)
}

function primaryOf(code: string) {
  return /^[A-Z0-9_]+$/.test(code) ? primaryCategories.find(x => code === x || code.startsWith(`${x}_`)) : undefined
}

export function categoryFullLabel(code: string) {
  const primary = primaryOf(code)
  if (!primary) {
    return /^[A-Z0-9_]+$/.test(code) ? words(code) : code
  }
  return primary === code ? words(code) : `${words(primary)}: ${words(code.slice(primary.length + 1))}`
}

export function categoryLabel(code: string) {
  const primary = primaryOf(code)
  if (!primary || primary === code || code === `${primary}_OTHER`) {
    return categoryFullLabel(code)
  }
  return words(code.slice(primary.length + 1))
}

export function isIncomeCategory(code: string) {
  const primary = primaryOf(code)
  return primary === 'INCOME' || primary === 'TRANSFER_IN'
}
