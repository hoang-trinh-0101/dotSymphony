# Playwright Test Quality Rules

Apply these rules to every `.spec.ts` file generated or modified in this project.

## 1. No Arbitrary Delays

**Forbidden:** `page.waitForTimeout(n)` where `n > 0` is a fixed millisecond guess.

**Replace with:**
- `page.waitForResponse(url => url.includes('/api/...'))` — wait for backend
- `page.waitForSelector('.cds--loading:has-text(...)')` then `toBeHidden()` — wait for loading spinner
- `expect(locator).toBeVisible({ timeout: 10000 })` — wait for element with explicit timeout
- `page.waitForURL(/.../)` — wait for navigation

**Exception:** `waitForTimeout(100)` or less is acceptable for UI micro-interactions (focus ripple, dropdown animation). Document with comment `// ht: micro-interaction`.

## 2. Deterministic Selections

**Forbidden:** Selecting "first option" or "first row" without knowing what it is.

**Replace with:**
- `page.getByRole('option', { name: '契約中' })` — select known value
- `page.locator('table tbody tr').filter({ hasText: 'CON000001' }).first()` — target known entity

**Exception:** When the test purpose is "any item works", wrap in `test.step('select first available option')` and assert the option text is not empty.

## 3. Scoped Locators (No Strict Mode Violations)

**Forbidden:** `page.getByText('需要家名')` when the text appears in multiple places (table header, filter label, modal).

**Replace with:**
- `page.locator('table').getByRole('columnheader', { name: '需要家名' })` — scope to table
- `page.getByRole('dialog').getByRole('button', { name: 'キャンセル' })` — scope to modal
- `page.locator('[role="listbox"]').getByRole('option', { name: '契約中' })` — scope to dropdown

## 4. Data Structure Validation

Every list/detail test must verify data integrity:
- IDs match expected regex: `/^CON\d{6}$/`
- Dates match expected format: `/^\d{4}\/\d{2}\/\d{2}$/`
- Numeric fields parse correctly: `expect(Number(supplyCount)).toBeGreaterThanOrEqual(0)`
- Status values belong to known set: `['契約中', '契約終了', ...]`

## 5. Negative Cases

Every positive filter test must have a negative counterpart:
- Search with no results → verify empty state message or zero rows
- Invalid date range → verify error message
- Cancel action → verify no side effect (URL unchanged, no API call)

## 6. Network Assertions

For any action that triggers backend:
```ts
const [response] = await Promise.all([
  page.waitForResponse(res => res.url().includes('/api/consumers') && res.request().method() === 'GET'),
  page.locator('button[type="submit"]').click(),
]);
expect(response.status()).toBe(200);
```

## 7. Carbon Design System (cds) Specifics

- **Modal:** Do NOT assert `getByRole('dialog')` — Carbon renders hidden portal dialogs. Assert visible buttons/text inside modal instead: `page.getByRole('button', { name: 'キャンセル' })`.
- **Dropdown:** Click the input first, wait for `[role="listbox"]` then select option.
- **DataTable:** Rows are `table tbody tr` inside `.cds--data-table`. Header cells use `role="columnheader"`.
- **Loading:** Look for `.cds--loading` or `.cds--skeleton` during data fetch.

## 8. Test Independence

Each test must be runnable alone. Do NOT rely on state from previous tests in the same `describe` block.

## 9. Tagging Convention

| Component | Tag Format | Example |
|-----------|-----------|---------|
| Area | `@area:<code>_<name>` | `@area:2C_consumer` |
| Priority | `@priority:high\nmedium\nlow` | `@priority:high` |
| Audit | `@audit` | `@audit` |

## 10. Self-Heal Checklist

Before calling a test "done", verify:
- [ ] No `waitForTimeout` > 100ms
- [ ] No `.first()` on ambiguous selectors without filter
- [ ] No `getByText` without `{ exact: true }` or scoped parent
- [ ] Negative case exists for every positive filter
- [ ] Data format assertions exist for list rows
- [ ] Network assertions exist for mutating actions
