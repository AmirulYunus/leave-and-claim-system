# Leave and claim workspace

Next.js employee dashboard with leave entitlements, expense claims, HR review, and monthly timesheets backed by PostgreSQL and Prisma 7.

## Local setup

1. Open Docker Desktop, then run `docker compose up -d` in this directory.
2. Create `.env.local` with `DATABASE_URL=postgresql://leave_app:YOUR_URL_ENCODED_PASSWORD@localhost:5432/leave_and_claim` and `SEED_PASSWORD=<12-128 characters>`. Use the credentials configured in Compose. Keep real environment files out of Git.
3. Run:

```bash
npm install
npm run db:generate
npm run db:deploy
npm run db:seed
npm run dev -- --hostname 127.0.0.1
```

Open http://localhost:3000. Prisma migrations are tracked in `_prisma_migrations`; rerunning `db:deploy` applies only pending migrations. Seeding creates fictional employee profiles if absent and never resets existing data. No sample requests are inserted automatically.

`npm run db:seed` runs `prisma/seed.ts` using Prisma and creates the local demo accounts Amirul (`amirul@example.test`, Employee), HR (`hr@example.test`), System Admin (`admin@example.test`), Sarah Tan, and Daniel Lee. Accounts without a password get `SEED_PASSWORD`; existing passwords are never changed. Their join dates are demo values.

## Sign-in

Users sign in with their email address and password. Sessions last 12 hours and are stored in the `sessions` table; the browser holds only a random token in an `httpOnly`, `SameSite=Lax` cookie, and the database stores its SHA-256 digest. Passwords are hashed with scrypt and must be 12–128 characters.

When HR or Admin creates an account they set a temporary password and share it with the employee, who must choose a new password at first sign-in before using the app. Resetting the temporary password or deactivating an employee signs them out everywhere.

## Roles and employee management

Sign in as HR or Admin, then open **Employees** to add a profile with name, email, join date, and temporary password. HR creates Employee and HR profiles and edits ordinary employee records. Admin creates Admin profiles and assigns Employee, HR, or Admin roles. Existing profile emails are read-only. At least one active Admin must remain.

HR also maintains each profile's employment details: position, employment type, employee level (1–4), probation completion date, leaving date, active status, department and cost centre. They are blank until HR supplies them, and leave entitlement rules will treat a blank input as pending HR details rather than zero. Departments and cost centres are maintained on the Employees page; deactivate entries instead of deleting them.

The `20261006000200_split_roles` migration changes the old combined role to HR and permits three distinct roles. Apply it with `npm run db:migrate`, then run `npm run db:seed` to create the separate Admin preview profile. No reset is required.

## Stored data

- `employees`: profiles, join dates, roles, and HR-maintained employment details.
- `departments`, `cost_centres`: HR-maintained classification lists.
- `requests`: leave/claim requests and HR decision history fields.
- `leave_entries`: requested hours for each date.
- `claims`: reimbursement amounts.
- `timesheet_entries`: worked hours per employee/date.

Leave and claim submissions and HR decisions save immediately. Timesheet edits are drafts until **Save timesheet** is clicked. Approved leave is derived from saved requests; it is not duplicated into worked hours. Pending leave remains separate. Save a draft before changing month or reviewing/submitting requests.

## Development boundary

Every API handler requires a signed-in session and checks the employee's current database role: Employee sees their own requests; HR reviews requests and maintains Employee and HR profiles; Admin can also create Admin profiles and change roles. Cross-origin requests are rejected. Before staging or production, add sign-in rate limiting and serve the app over HTTPS so the session cookie is marked `Secure`.

## Leave entitlements

`app/lib/entitlement.ts` implements the entitlement rules carried over from the Dataverse solution; `tests/entitlement.test.mjs` contains the original test cases. Balances run per calendar year in Malaysia time, and new requests must fall in the current year.

- **Annual leave:** a full-year rate of the employee-level base (14/14/15/18 days for levels 1–4) plus a service reward (+1 day from 2 completed years, +2 from 4, +3 from 6), never below the statutory minimum (8/12/16 days). It is earned monthly: each completed calendar month adds its rate, the joining month counts once it ends, and the running total is rounded up once to the next half day. Only leave earned by today can be used, including for future dates.
- **Medical leave:** 14/18/22 days for under 2, 2–5 and 5 or more completed years. **Hospitalisation leave:** a separate 60 days.
- **Held, not zero:** missing HR details, part-time and intern staff, 8 or more years' service, a 29 February join date and the leaving year show a pending reason and block that category until resolved. Unpaid leave has no limit.

## Leave types

`app/lib/leave-types.ts` holds the category rules carried over from the Dataverse solution (see `scripts/dataverse/LEAVE_RULES_RECHECK_2026-10-25.md`). HR approves every type.

- **Durations:** annual, medical, hospitalisation, emergency, training and unpaid leave cover every weekday from the start to the end date, with one duration for every date: half day (4 hours) or full day (8 hours). Fractional hours and weekend dates are refused.
- **Medical and hospitalisation leave:** HR cannot approve them until a medical certificate is attached, and must check that it is valid. Medical leave must be submitted within 48 hours of its start date, measured from midnight Malaysia time.
- **Emergency leave:** may be requested after the leave. HR cannot approve it until a supporting document is attached. Approved hours are charged to Annual Leave.
- **Training leave:** the employee chooses Company Required (not charged) or Employee-Requested External (charged to Annual Leave).
- **Maternity leave:** 98 consecutive calendar days from the start date, with the expected confinement date and formal notice date. The 60-day notice window is left to HR's judgement rather than enforced.
- **Paternity leave:** 7 consecutive calendar days that must start on the verified birth date, with a notice date and an expected or actual birth date. Approval requires 12 months of service and refuses a repeat approval for the same birth or a sixth confinement. HR enters the verified birth date when approving if the employee could not.
- Maternity and paternity leave may cross a year end; other types must fall in one calendar year.

## Supporting documents

Employees attach PDF, PNG or JPEG files up to 10 MB to their own pending leave requests, either in the request form or from the request list. The file's content must match its extension. The owner and HR or Admin can download documents; anyone else gets "not found". Files are stored under random names in `.storage/uploads` (set `UPLOAD_DIR` to change it); S3 storage for staging and production is still to be added behind `app/lib/storage.ts`.

Carry-forward, public holidays, cancellation, and marriage and compassionate leave are pending. See LEAVE_POLICY.md for confirmed policy and unresolved decisions.

Database credentials stay on the server; DATABASE_URL must never use a NEXT_PUBLIC_ prefix. The Docker volume preserves records across container stops; removing its volume deletes them.

## Checks

```bash
npm run lint
npx tsc --noEmit
npm test
```

With the dev server and local database running, `node scripts/check-persistence.mjs` checks a temporary 2.5-hour leave request, HR approval, persisted worked hours, and conflict rejection. It removes only its temporary records afterward. Its reserved test date (29 December 2026) must be unused. `node scripts/check-roles.mjs` verifies sign-in, employee creation, editing, role restrictions, and request isolation, then removes its temporary profiles. `node scripts/check-entitlements.mjs` verifies the entitlement summary and that missing HR details block entitled leave. `node scripts/check-leave-types.mjs` verifies the maternity, paternity, training and emergency rules and document access. The scripts sign in with `SEED_PASSWORD`.

## Prisma schema changes

The schema lives in `prisma/schema.prisma`. Edit its models, then create and apply a development migration:

```bash
npm run db:migrate -- --name add_employee_login
npm run db:generate
```

Review and commit both the schema and the generated `prisma/migrations/` SQL. Use `npm run db:deploy` to apply committed migrations in another environment; use `npm run db:status` to inspect history. `npm run db:studio` opens Prisma's database browser.

The schema is created entirely by Prisma migrations. The initial migration creates the application tables and their PostgreSQL constraints. There is no legacy migration runner or baseline compatibility layer.

For a fresh empty database, run `npm run db:deploy` followed by `npm run db:seed`. To deliberately erase local data and rebuild all tables, run `npm run db:reset`, then `npm run db:generate` and `npm run db:seed`. Reset deletes all existing application records; seeding is explicit in Prisma 7.

PostgreSQL CHECK constraints are maintained in the migration SQL because Prisma schema syntax does not represent them. Preserve them in future SQL migrations. Never reset a database to resolve drift without first reviewing the cause and preserving needed data. Prisma does not provide Laravel-style automatic rollback; use a corrective migration for ordinary schema fixes.

Prisma Client is generated into `app/generated/prisma/` and ignored by Git. `npm install` generates it through the postinstall script, so configure `.env.local` first. Database initialization is in `app/lib/prisma.ts`; business logic stays in `app/api/workspace/route.ts`. Advisory locks still use parameterized SQL inside Prisma transactions.
