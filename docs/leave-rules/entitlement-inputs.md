# Entitlement inputs

Recorded 9 October 2026. Statutory rules and confirmed company decisions are in `../../LEAVE_POLICY.md`.

## Decisions

- HR maintains the employment details that entitlement calculations need: joining date, position/level, employment type, and applicable probation details.
- If an input required for a calculation is null, skip that employee's calculation until HR supplies it. Show it as not calculated, pending HR details. Do not substitute zero or a default, and do not treat missing data as proof of ineligibility. Preserve existing balance and history.
- Required inputs are specific to each calculation; optional or unrelated fields do not block an allowance.
- Service-based allowance changes take effect on each completed-service anniversary, assessed on today's Malaysia calendar date.

| Input | Needed for |
| --- | --- |
| Date Joined | Completed service. Use the employment joining date, never an account creation date. |
| Employee Level | The company's 14/14/15/18-day annual base. |
| Employment Type | Full-time / part-time / intern classification for Medical and other rules. |
| Probation Completed On | Probation-dependent category eligibility, such as Marriage. |
| Leaving date | Last-year proration, which is on hold. |
| Position | Informational; any automatic title-to-level mapping needs confirmation. |

## Annual entitlement calculation

1. Resolve the employee's level to base days: 14 / 14 / 15 / 18.
2. Calculate completed continuous service from the employment joining date to today's Malaysia calendar date.
3. For service below eight years, add the documented reward: +0 below two years; +1 from two to below four; +2 from four to below six; +3 from six to below eight.
4. Preserve the statutory annual minimum from `LEAVE_POLICY.md`: full allowance = max(company base + service reward, statutory minimum). This is a full-year rate, not an available balance; proration and accrual are separate rules.
5. Convert days to hours at eight hours per standard day. Per-date use is exactly four or eight hours.

Rewards for eight or more years and last-year proration remain on hold. Medical entitlement needs both verified service date and employment type.
