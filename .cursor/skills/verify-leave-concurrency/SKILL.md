---
name: verify-leave-concurrency
description: Model-check (TLA+ via TLC in Docker) that concurrent leave submissions, HR decisions, cancellation requests and cancellation decisions cannot overspend a leave balance, decide a request twice, open two pending cancellations, approve a cancellation twice, or move a status backwards. Use when a change touches app/api/workspace/route.ts, app/api/cancellations/route.ts, their advisory locks or transactions, or when asked whether leave accounting is safe under concurrency.
disable-model-invocation: true
---

# Verify leave concurrency

**Promise.** One employee's leave can never be held beyond the allowance, decided twice, cancelled twice, or moved backwards, however many HTTP requests run at once.

**Harm if broken.** Two HR tabs or a double-click could approve more leave than the employee earned, send duplicate decision emails, return cancelled hours twice, or bring a rejected request back.

The guard is one rule: each write handler takes `pg_advisory_xact_lock(<employee id>)` before the re-read its checks depend on. The model checks that rule against every interleaving of ten concurrent handler calls.

## Run it

```bash
node .cursor/skills/verify-leave-concurrency/scripts/check.mjs
```

It needs Docker running. The script:

1. Reads the four handlers listed below from the real route files and sets each model constant to TRUE only if an employee-keyed lock comes before that handler's re-read. It prints the line numbers it used.
2. Downloads `tla2tools.jar` to `.verify/tools/` if missing and checks its pinned SHA-256.
3. Runs TLC in `eclipse-temurin:21-jre` and writes the full output to `.verify/formal/last-run.txt`.

Exit 0 prints `PASS ... (1260420 distinct states)` in about 20 seconds. Exit 1 prints `VIOLATION:` and the counterexample. Exit 2 means the code no longer matches the model (a handler or anchor is missing) or a tool is unavailable.

`--force=<Knob>=FALSE` overrides one constant, to see what a lock protects without editing code.

## When to run it

Run it when a change touches any covered file. Run `git diff <sync commit> -- <covered files>`; any output means the verification needs another look.

| Model constant      | Code                                     | Anchor (the re-read)                                             | Guards                            |
| ------------------- | ---------------------------------------- | ---------------------------------------------------------------- | --------------------------------- |
| `SubmitLocks`       | `app/api/workspace/route.ts` `POST`      | `validateLeaveEntries(`                                          | `NoOverspend`                     |
| `DecideLocks`       | `app/api/workspace/route.ts` `PATCH`     | `const target = await tx.requests.findUnique(`                   | `DecidedOnce`, `LegalTransitions` |
| `CancelLocks`       | `app/api/cancellations/route.ts` `POST`  | `const leave = await tx.requests.findUniqueOrThrow(`             | `OnePendingCancellation`          |
| `DecideCancelLocks` | `app/api/cancellations/route.ts` `PATCH` | `const cancellation = await tx.cancellations.findUniqueOrThrow(` | `CancelledOnce`                   |

**Sync commit:** `b227dae` (10 November 2026). At that commit both covered files match the model: each lock directly precedes its anchor. Move the sync commit forward only after you have confirmed that the model still describes the code at the new commit, and the check passes there.

## Reading a result

Read every result with the pstack `/statechart` skill. A violation becomes the shortest path to the broken state in plain words. A pass becomes a chart of how a request moves between states.

To tell a real bug from a modelling error:

- **Real bug.** The printed line numbers show a lock missing or after its anchor, and the counterexample is two handler calls interleaving between a `Read` and a `Write`. Report it in the code's words, for example "two HR approvals both read Pending before either wrote". Do not weaken an invariant or the model to make it pass, and do not change product code unless the user asks.
- **Modelling drift.** Exit 2, or the code now does something the model lacks: a new handler that changes request status or cancellations, a new lock key, a different isolation level, or an anchor that moved. Update `model/LeaveLocks.tla`, the handler table in `scripts/check.mjs`, and this file together, then rerun.

When a change is intended, such as a new write path for leave, add it to the model as a new `Op` with its own constant, add its row to `handlers` in `check.mjs`, and prove the new constant can fail with `--force=<Knob>=FALSE` before trusting a pass.

## Scope

Verified: one employee, two requests of one unit each, an allowance of one, and ten concurrent calls: two submits, three approvals from two HR tabs, one rejection, two cancellation requests and two cancellation approvals. Every interleaving of their lock, read and write steps is checked. Each handler is one Postgres transaction at Read Committed, and the advisory lock is held until commit.

Assumed: locks on different employees never interact (all keys are employee ids; the employee editor uses key `-1`). Prisma's interactive `$transaction` runs the callback in one database transaction.

Not covered:

- The hour arithmetic inside `validateLeaveEntries`: earned accrual, carry-forward split, the 8-hour daily cap. The model treats each request as one unit and only checks that the check runs under the lock.
- The employee editor (`app/api/employees/route.ts`, lock `-1`). An HR edit that lowers an allowance does not re-check leave already held. That is a policy question, not a race: the same state results if the edit lands right after the submit.
- Carry-forward confirmation (`app/api/carry-forward/route.ts`), attachment upload (`app/api/attachments/route.ts`, no lock), timesheets, and the email outbox sender (`scripts/notifications.ts`).

## Files

- `model/LeaveLocks.tla` is the model. Each constant names its handler.
- `scripts/check.mjs` extracts the constants from the code, runs TLC and reports.
- `.verify/formal/` holds the generated config and the last TLC output (git-ignored).
