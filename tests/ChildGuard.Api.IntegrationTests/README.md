# API integration tests

These tests boot the real API in-process (`WebApplicationFactory`) against a **new,
throwaway database per test class**, named `childguard_it_<random>`, which is dropped
when the class finishes. They cover authentication/JWT validation, role authorization,
RFID duplicate and concurrent scans, scanner credentials, pickup approval/rejection
races, verification expiry, school time zone and late-arrival rules, journey report
pagination, invalid requests, historical-record delete protection and rate limiting.

## Requirements

A local MySQL 8 / MariaDB server. By default the tests connect as
`Server=127.0.0.1;Port=3306;User=root;Password=;` (XAMPP default). Override with:

```
set CHILDGUARD_IT_MYSQL=Server=127.0.0.1;Port=3306;User=it_runner;Password=...;
```

The account needs `CREATE`/`DROP` on databases matching `childguard_it_%`.

## Safety

* The database name is generated, never read from configuration, so the tests cannot
  touch `childguard_db` or any production database.
* Non-loopback servers are refused unless `CHILDGUARD_IT_ALLOW_REMOTE=1` is set (use
  only for a dedicated test server).
* After start-up the factory checks the API is really connected to the generated
  database and aborts otherwise.
* Email, push and the face service are disabled/faked; the API clock is controlled by
  the test.

## Run

```
dotnet test tests/ChildGuard.Api.IntegrationTests
```
