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
  return primaryCategories.find(x => code === x || code.startsWith(`${x}_`))
}

export function categoryLabel(code: string) {
  if (!/^[A-Z0-9_]+$/.test(code)) {
    return code
  }
  const primary = primaryOf(code)
  return words(primary && primary !== code ? code.slice(primary.length + 1) : code)
}

export function categoryFullLabel(code: string) {
  const primary = /^[A-Z0-9_]+$/.test(code) ? primaryOf(code) : undefined
  return primary && primary !== code ? `${words(primary)}: ${categoryLabel(code)}` : categoryLabel(code)
}

export function isIncomeCategory(code: string) {
  return primaryOf(code) === 'INCOME' || primaryOf(code) === 'TRANSFER_IN'
}
