# `db/`

## `schema.sql`

The full schema as PostgreSQL DDL — **generated, not hand-maintained**. It is
committed because it is the most direct answer to "what does the database look
like?" for a reviewer who does not want to run the project first, and because it
makes schema changes visible in a diff.

EF Core migrations under
[`src/WaterBilling.Infrastructure/Persistence/Migrations/`](../src/WaterBilling.Infrastructure/Persistence/Migrations/)
remain the single source of truth. Regenerate after any model change:

```bash
dotnet tool restore
dotnet ef migrations script --idempotent \
  --project src/WaterBilling.Infrastructure \
  --startup-project src/WaterBilling.Infrastructure \
  --output db/schema.sql
```

`--idempotent` wraps every statement in an existence check, so the same script is
safe to run against a database at any migration level. That is what makes it usable
as a deployment artifact on a site where nobody knows which version is installed.

## Adding a migration

```bash
dotnet ef migrations add <Name> \
  --project src/WaterBilling.Infrastructure \
  --startup-project src/WaterBilling.Infrastructure \
  --output-dir Persistence/Migrations
```

No database connection is needed — the design-time factory supplies a placeholder
connection string, because EF only needs a provider to generate provider-specific SQL.
