# ArtAround .NET backend (preview branch)

All roadmap stages (3b–9), written by Claude on `claude-dotnet-preview` as a reference to
compare against, not as a replacement for building it yourself on `main`. Everything
below is a decision to review, not a decision taken for you.

ASP.NET Core 10 (controllers), PostgreSQL 17, EF Core + Npgsql, Dapper, JWT bearer,
SignalR, xUnit + `WebApplicationFactory` + Testcontainers.

## Run

With Docker (Postgres, migrations + seed, API on http://localhost:8001):

```bash
docker compose up dotnet_app
```

Locally, against any Postgres matching `appsettings.json` (user, password and database `artaround`):

```bash
cd backend-dotnet && dotnet tool restore
cd ArtAround.Api && dotnet run -- migrate && dotnet run -- seed && dotnet run
```

The API serves both frontends like Node did: `/marketplace`, the navigator's `dist/` at
`/`, and `/uploads`. Build the navigator first (`npm run build` in `frontend-navigator/`),
or run `npm run dev` there: Vite proxies to 8001 (`API_TARGET=http://localhost:8000` for Node).

Demo users: `autore1`, `autore2`, `visitatore1`, `visitatore2`, `docente1`, password `12345678`.

## Tests

```bash
# Contract tests: black box, against a running backend (Node on 8000, .NET on 8001).
cd contract-tests/ArtAround.ContractTests && API_PORT=8001 dotnet test

# Integration tests: the API in memory over a throwaway Postgres container.
cd backend-dotnet && dotnet test ArtAround.Api.Tests
```

The 73 contract tests pass against both Node and .NET. The 9 integration tests cover what
a black-box suite cannot pin down: concurrent purchases, deadlock-free lock ordering, lost
updates, xmin conflicts, the `updated_at` trigger, case-insensitive uniqueness. Removing
`FOR UPDATE` from `PurchaseService` makes two of them fail (double charge, deadlock).
Testcontainers pulls `postgres:17`; set `TEST_POSTGRES_IMAGE` to reuse another image.

## Layout

| Folder | What |
|---|---|
| `Domain/` | EF entities and enums. No attributes except Postgres/JSON enum names. |
| `Data/` | `AppDbContext`, one `IEntityTypeConfiguration` per table, `Migrations/`. |
| `Dtos/` | Request (validated) and response records. Entities never reach JSON. |
| `Controllers/` | Thin: bind, call a service, shape the response. |
| `Services/` | Rules: purchases (Dapper), items, visits, sessions, tokens. |
| `Realtime/` | `SessionHub` (room membership) and `SessionNotifier` (broadcasts). |
| `Infrastructure/` | Error shape, exception handler, JSON conventions, slug. |
| `Seeding/` | `dotnet run -- seed`: museums upserted from `data/museums/*.json`, demo data from `data/demo-visits.json`. |

## Stage by stage

- **3b Setup.** DI registrations and the middleware order are in `Program.cs`.
  `migrate` and `seed` are one-shot commands, never run at web startup.
- **4 EF Core.** Snake-case naming convention; Postgres ENUMs via `MapEnum`; CHECKs via
  `HasCheckConstraint`; `lower(...)` indexes and the trigger as raw SQL in the migration.
  Reads use projections (`VisitService.Summaries`, `MuseumsController.List`) or
  `Include` + `AsSplitQuery`. `ItemReader` avoids N+1: N items cost 3 queries.
  `COLLATE "C"` gives the ordinal name sort the contract tests expect.
- **5 Auth.** `PasswordHasher<User>` (PBKDF2, rehash on upgrade), HS256 JWT with `sub`.
  `OnTokenValidated` rejects tokens of users that no longer exist; login hashes even for
  unknown users so timing does not reveal which usernames exist. Ownership checks are
  resource-based, in the services; there are no role policies because there are no roles.
- **6 Writes.** DataAnnotations + `IValidatableObject`; `TrimmedStringConverter` trims
  during deserialization, so validation sees the stored value. One error shape: RFC 9457
  ProblemDetails plus `error` (what both frontends display) and, for validation,
  `details[{field, message}]`. `ApiExceptionHandler` maps unique/FK violations to 409.
- **7 Transactions.** `PurchaseService` in Dapper: items `FOR SHARE`, users `FOR UPDATE`
  in id order, purchases re-read after the lock, `unnest()` bulk writes, the
  `wallet_balance >= 0` CHECK as a safety net. Visit save and its charge share one
  transaction. xmin guards item updates and session navigation. Wallet recharge and view
  counts are single atomic `UPDATE`s. Section answers are one `INSERT ... ON CONFLICT`.
- **8 Real-time.** SignalR at `/hubs/session`, token via `?access_token=`. Broadcast only:
  every mutation is REST, then `SessionNotifier` sends the same event names Node used.
  Groups are keyed by session id, not code, because codes are reused once a session ends.
- **9 Seed and Docker.** `backend-dotnet/Dockerfile` (navigator build, SDK publish,
  runtime image); `docker-compose.yaml` adds `postgres_db`, `dotnet_setup`, `dotnet_app`.

## Schema vs `note/schema-draft.sql`

Same tables and constraints for museums, contents, users, items, purchases. Differences:

- Npgsql emits ENUM labels alphabetically (`tone` is `complex, easy, medium`), so
  `ORDER BY tone` no longer means easy → complex. The code sorts tones in C#.
- `points_of_interest` also gets an FK on `museum_id` alone (the `Museum.PointsOfInterest`
  navigation needs it), besides the composite FK to `floor_plans`.
- `updated_at` decided: a trigger (`set_updated_at`), since Dapper bypasses EF. On
  `visits` it ignores `view_count`, which changes on every read.
- New tables for what the draft left for later: `item_associated_contents`, `visits`,
  `visit_stops`, `visit_blocks`, `visit_block_items`, `visit_block_questions`
  (options as `text[]`), `visit_quiz_questions`, `saved_visits`, `sessions`
  (unique code among active ones: a partial index), `session_participants`,
  `session_quiz_answers`, `session_activities`, `session_messages`, `section_responses`.
- Derived, so not stored: `myVisits` (visits by creator), `salesCount`/`revenue`
  (from purchases), `activeSession`, denormalised usernames in session data.
- Every DEFAULT column is mapped `ValueGeneratedNever`; see the comment in
  `VisitConfiguration`. Without it, `isPublic: false` silently became `true`.

## Contract changes (frontends and contract tests updated)

- **Item texts**: `descriptions: [{ tone, text }]`, one per tone (your `item_texts`
  decision). The navigator lost the 15/30/60s length axis (swipe, dots, "shorter"
  command); the marketplace editor has one textarea per tone. The seed keeps the
  Italian 30s text of each tone.
- **targetAudience** is a keyword; the marketplace uses a select.
- **Socket.io → SignalR** in `frontend-navigator/src/session.js` (same `connectSession` API).
- **Marketplace API origin**: same-origin instead of a hard-coded `localhost:8000`.
- **Ids** are JSON strings (`IdConverter`: every `long` is an id). Malformed item ids are
  400 with the usual validation body.
- **Dropped endpoints** no frontend calls: museum create/update, save/unsave museum,
  content create. **Dropped events**: `session:participant-joined/left` (unused).
- **Tightened**: logging an activity requires being in the session; the
  `session:participants` broadcast no longer leaks everyone's quiz answers; deleting a
  purchased item is 409 instead of silently removing it from buyers.
- **Kept from Node**: `GET /visits/:id` is readable by anyone with the link.

The item contract tests send both item shapes so the suite still runs against Node.
Drop the `texts[]` field when Node is retired.
