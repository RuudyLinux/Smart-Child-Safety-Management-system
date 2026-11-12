# PostgreSQL migrations

Migration set for `ChildGuardDbContext` on PostgreSQL (Neon or self-hosted). The MySQL migrations in
`ChildGuard.Infrastructure/Persistence/Migrations` are separate and are never edited.

Both providers share one model. Everything PostgreSQL does differently (column types, `citext`) is in
`ChildGuardDbContext.ApplyPostgresMappings`, applied only for Npgsql.

## Add a migration

The API is the startup project: it supplies the model and the provider. `migrations add` does not connect.

```
set Database__Provider=Postgres
set ConnectionStrings__ChildGuardDb=Host=127.0.0.1;Database=design_only;Username=design_only
set Jwt__Secret=design-time-only
dotnet ef migrations add <Name> --project src/ChildGuard.Infrastructure.Postgres --startup-project src/ChildGuard.Api --output-dir Migrations
```

After any model change, both of these must report no pending changes:

```
dotnet ef migrations has-pending-model-changes --project src/ChildGuard.Infrastructure.Postgres --startup-project src/ChildGuard.Api     (with Database__Provider=Postgres)
dotnet ef migrations has-pending-model-changes --project src/ChildGuard.Infrastructure --startup-project src/ChildGuard.Api             (MySQL, default)
```

## Apply

Use the **direct** (non-pooled) connection string, never the `-pooler` one: migrations need session state.

```
set Database__Provider=Postgres
set ConnectionStrings__ChildGuardDb=<direct postgresql:// URI or Npgsql string>
dotnet ef database update --project src/ChildGuard.Infrastructure.Postgres --startup-project src/ChildGuard.Api
```

Back up first. Never apply to a database that holds real data without an explicit decision.
