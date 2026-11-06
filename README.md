# Leave and claim workspace

Next.js employee dashboard with hourly leave, expense claims, HR review, and monthly timesheets backed by PostgreSQL and Prisma 7.

## Local setup

1. Open Docker Desktop, then run `docker compose up -d` in this directory.
2. Create `.env.local` with `DATABASE_URL=postgresql://leave_app:YOUR_URL_ENCODED_PASSWORD@localhost:5432/leave_and_claim`. Use the credentials configured in Compose. Keep real environment files out of Git.
3. Run:

```bash
npm install
npm run db:generate
npm run db:deploy
npm run db:seed
npm run dev -- --hostname 127.0.0.1
```

Open http://localhost:3000. Prisma migrations are tracked in `_prisma_migrations`; rerunning `db:deploy` applies only pending migrations. Seeding creates fictional employee profiles if absent and never resets existing data. No sample requests are inserted automatically.

`npm run db:seed` runs `prisma/seed.ts` using Prisma and creates the local preview identities Amirul, HR, System Admin, Sarah Tan, and Daniel Lee using `example.test` emails. Their join dates are demo values.

## Roles and employee management

Select **HR** or **Admin**, then open **Employees** to add a profile with name, email, and join date. HR creates only Employee profiles and edits ordinary employee records. Admin can assign Employee, HR, or Admin roles. Existing profile emails are read-only. At least one Admin must remain.

The `20261006000200_split_roles` migration changes the old combined role to HR and permits three distinct roles. Apply it with `npm run db:migrate`, then run `npm run db:seed` to create the separate Admin preview profile. No reset is required.

## Stored data

- `employees`: profiles, join dates, and roles.
- `requests`: leave/claim requests and HR decision history fields.
- `leave_entries`: requested hours for each date.
- `claims`: reimbursement amounts.
- `timesheet_entries`: worked hours per employee/date.

Leave and claim submissions and HR decisions save immediately. Timesheet edits are drafts until **Save timesheet** is clicked. Approved leave is derived from saved requests; it is not duplicated into worked hours. Pending leave remains separate. Save a draft before changing month or reviewing/submitting requests.

## Development boundary

Login is not implemented. The role dropdown selects a local preview identity (Employee, HR, or Admin). API handlers check that identity’s database role: Employee sees their own requests; HR reviews requests and creates/edits Employee profiles; Admin can also create privileged profiles and change roles. These are preview permission checks, not authenticated access control. Employee profiles do not have login credentials yet. The API rejects production requests and non-localhost access. Do not use this preview with real HR data or deploy it as a live employee system. Proper sessions, authenticated identities, and role authorization must replace the demo identities before deployment.

Leave balances currently cover the 2026 preview period. Company holidays, accrual/carry-forward, medical document uploads, maternity/paternity workflows, and payroll/overtime calculations are pending. See LEAVE_POLICY.md for confirmed policy and unresolved decisions.

Database credentials stay on the server; DATABASE_URL must never use a NEXT_PUBLIC_ prefix. The Docker volume preserves records across container stops; removing its volume deletes them.

## Checks

```bash
npm run lint
npx tsc --noEmit
npm test
```

With the dev server and local database running, `node scripts/check-persistence.mjs` checks a temporary 2.5-hour leave request, HR approval, persisted worked hours, and conflict rejection. It removes only its temporary records afterward. Its reserved test date (29 December 2026) must be unused. `node scripts/check-roles.mjs` verifies employee creation, editing, role restrictions, and request isolation, then removes its temporary profiles.

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
# leave-and-claim-system
# leave-and-claim-system
