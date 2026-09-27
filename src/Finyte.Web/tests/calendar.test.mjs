import assert from 'node:assert/strict'
import { test } from 'node:test'
import { dateLabel, localDate, shiftDate } from '../app/shared/calendar.ts'

test('local date uses the calendar day, not the UTC day', () => {
  const lateEvening = new Date(2026, 5, 1, 23, 30)
  assert.equal(localDate(lateEvening), '2026-06-01')
})

test('shifting dates crosses month ends and leap days without touching time zones', () => {
  assert.equal(shiftDate('2026-03-01', -1), '2026-02-28')
  assert.equal(shiftDate('2024-02-28', 1), '2024-02-29')
  assert.equal(shiftDate('2026-12-31', 1), '2027-01-01')
  assert.equal(shiftDate('2026-06-15', -89), '2026-03-18')
})

test('labels render the given day regardless of the browser zone', () => {
  assert.equal(dateLabel('2026-09-01'), '1 Sept 2026')
})
