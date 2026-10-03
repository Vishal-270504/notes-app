# Notes API - Beginner Friendly Plan

## Architecture
Backend: ASP.NET Core Minimal API (.NET 8)
Frontend: React + TypeScript + Vite
Storage: In-memory List<Note> (no database)

## API Endpoints

GET /notes?page=1&limit=10
- Returns paginated notes
- Query params validated
- Response includes metadata

POST /notes
- Body: { title, content }
- Validates required fields
- Returns 201 Created

GET /notes/{id}
- GUID validation via route constraint
- Returns 404 if not found

DELETE /notes/{id}
- Returns 204 on success, 404 if not found

## Edge Cases Handled

Backend:
1. Pagination:
   - page < 1 => default to 1
   - limit < 1 => default to 10
   - limit > 100 => cap at 100 to prevent DoS
   - Empty list => returns empty array with totalPages = 0
   - Page beyond totalPages => returns empty array

2. Create:
   - Empty/whitespace title/content => 400 Bad Request
   - Title too long >200 => 400
   - Trim whitespace

3. Get by ID:
   - Invalid GUID format => 404 via route constraint
   - Non-existent ID => 404 with error message

4. Delete:
   - Non-existent ID => 404
   - Concurrent access => lock object prevents race condition

5. Thread safety:
   - lock around list mutations/read snapshot

Frontend:
1. Network errors => show error message
2. Empty notes list => show empty state
3. Invalid user input => prevent submit, show error
4. Pagination controls disabled at boundaries
5. Delete confirmation dialog
6. Loading state prevents duplicate requests
7. API base URL configurable

## Why Pagination Matters
Without pagination:
- GET /notes returns all notes at once
- With 10,000 notes: ~5MB JSON, slow download, high memory, UI freeze
- Mobile users suffer
- Server spends CPU serializing huge list

With pagination:
- Only 10 items per request
- Fast response, small payload
- Scalable to millions of notes
- Better UX with page controls

## Running the Project

Backend:
dotnet run --project C:/Users/Desktop/NotesApi
API runs on http://localhost:5000

Frontend:
cd C:/Users/Desktop/NotesFrontend
npm install
npm run dev
Open http://localhost:5173

## Next Improvements
- Add PUT/PATCH for update
- Add search/filter
- Persist to file/db
- Add authentication
- Add unit tests
