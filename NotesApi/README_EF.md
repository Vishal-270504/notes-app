# Notes API - EF Core + SQLite + Index Performance

This API has been extended with Entity Framework Core and SQLite. Authentication, validation and error handling are unchanged. Notes are persisted in `notes.db` with a composite index on `UserId, CreatedAt`.

## Prerequisites

- .NET 10 SDK (project targets net10.0)
- SQLite provider via EF Core

## Run the API

```powershell
cd NotesApi
dotnet run --urls http://localhost:5000
```

Swagger: http://localhost:5000/swagger

## Migrations

The Notes table is created with EF Core migrations.

Create a new migration after model changes:

```powershell
cd NotesApi
dotnet ef migrations add <MigrationName>
```

Apply migrations to the database:

```powershell
dotnet ef database update
```

Initial migration created in this repo:

```powershell
dotnet ef migrations add InitialCreate
dotnet ef database update
```

The migration creates `Notes` table and composite index `IX_Notes_UserId_CreatedAt` on `(UserId, CreatedAt)`.

### Why the index helps

*Listing user's notes* is the hot path: `WHERE UserId = ? ORDER BY CreatedAt DESC LIMIT ? OFFSET ?`

Without index:
- SQLite performs a full table scan and sorts in memory: O(n log n)
- Query time grows linearly with total rows

With composite index `IX_Notes_UserId_CreatedAt`:
- SQLite can seek directly to the user's first row in the index
- Rows are already ordered by `CreatedAt`, so no extra sort is needed
- Pagination is O(log n + k) where k is page size

### Cost on writes

- Each INSERT/UPDATE/DELETE must also update the B-tree index
- Additional CPU and I/O per write, increased storage
- For read-heavy workloads like notes listing, tradeoff is strongly positive

Comments explaining this are in `Data/NotesDbContext.cs` and `Program.cs`.

## Seeding 100,000 notes

First sign up and login to obtain a JWT.

```powershell
curl.exe -s -X POST http://localhost:5000/auth/signup -H "Content-Type: application/json" --data-raw '{"email":"alice@example.com","password":"Password123!"}'
curl.exe -s -X POST http://localhost:5000/auth/login -H "Content-Type: application/json" --data-raw '{"email":"alice@example.com","password":"Password123!"}'
```

Replace `PASTE_TOKEN_HERE` with the token from login.

Seed notes for the authenticated user:

```powershell
curl.exe -s -X POST http://localhost:5000/notes/seed -H "Authorization: Bearer PASTE_TOKEN_HERE"
```

This inserts 100,000 notes in 1,000-row batches. Response includes `seeded` count and `elapsedMs`.

**Note:** Seeding is a heavy write operation and will exercise index maintenance cost.

## Timing query performance

Benchmark the listing query with Stopwatch:

```powershell
curl.exe -s http://localhost:5000/notes/benchmark -H "Authorization: Bearer PASTE_TOKEN_HERE"
```

Response example:

```json
{
  "elapsedMs": 45,
  "totalNotes": 100000,
  "sampleReturned": 10,
  "indexUsed": "IX_Notes_UserId_CreatedAt",
  "note": "Measure before/after adding index by creating migration without index, test, then add index migration and re-test."
}
```

You can also measure via Swagger GET `/notes?page=1&limit=10` and watch EF Core SQL logs printed to console.

## Expected before/after results

These numbers are indicative on SQLite with 100,000 notes for one user.

**Before index** (full scan + sort):
- First page query: ~800-1500 ms
- Increases with table size
- SQL log shows `SCAN TABLE Notes` and `USE TEMP B-TREE FOR ORDER BY`

**After index** `IX_Notes_UserId_CreatedAt`:
- First page query: ~20-80 ms
- Stable as rows grow
- SQL log shows `SEARCH TABLE Notes USING INDEX IX_Notes_UserId_CreatedAt`

Exact timings depend on hardware, but the improvement is typically 10-50x for large data sets.

Pagination remains fast:
```powershell
curl.exe -s "http://localhost:5000/notes?page=1&limit=10" -H "Authorization: Bearer PASTE_TOKEN_HERE"
```

## API Endpoints Summary

- POST /auth/signup
- POST /auth/login
- GET /notes?page=&limit=  - paginated, user-scoped
- POST /notes  - create with validation
- GET /notes/{id}
- DELETE /notes/{id}
- POST /notes/seed  - seed 100k notes for performance testing
- GET /notes/benchmark  - timing check with Stopwatch

All notes endpoints require `Authorization: Bearer <token>`.

## Files changed

- `Data/NotesDbContext.cs` - DbContext with composite index
- `Models/Note.cs` - class entity for EF Core
- `Program.cs` - EF registration, async handlers, seed & benchmark endpoints
- `Migrations/` - EF Core migrations
- `notes.db` - SQLite database created at runtime

Keep existing authentication in-memory; notes are persisted via EF Core.
