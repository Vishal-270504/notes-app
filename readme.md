# Notes App

Backend: ASP.NET Core Minimal API  
Frontend: React + TypeScript + Vite  
Storage: In-memory lists (users and notes; no database)

Passwords are hashed with ASP.NET Identity `PasswordHasher` (PBKDF2). Notes require a JWT and are scoped to the user who created them.

## Run the API

```powershell
cd NotesApi
dotnet run --urls http://localhost:5000
```

API: `http://localhost:5000`  
Swagger: `http://localhost:5000/swagger`

Frontend (optional; note requests now need a JWT):

```powershell
cd NotesFrontend
npm install
npm run dev
```

Open `http://localhost:5173`

## Auth curl examples

Use `curl.exe` on Windows PowerShell so the command is not aliased to `Invoke-WebRequest`.

### Signup

```powershell
curl.exe -s -X POST http://localhost:5000/auth/signup -H "Content-Type: application/json" --data-raw '{"email":"alice@example.com","password":"Password123!"}'
```

### Login

```powershell
curl.exe -s -X POST http://localhost:5000/auth/login -H "Content-Type: application/json" --data-raw '{"email":"alice@example.com","password":"Password123!"}'
```

Copy the `token` from the login JSON, then:

### Authenticated request

```powershell
curl.exe -s http://localhost:5000/notes -H "Authorization: Bearer PASTE_TOKEN_HERE"
```

Create a note with the same token:

```powershell
curl.exe -s -X POST http://localhost:5000/notes -H "Authorization: Bearer PASTE_TOKEN_HERE" -H "Content-Type: application/json" --data-raw '{"title":"My first note","description":"Owned by alice"}'
```

Missing or invalid tokens return **401**. Accessing another user's note by id returns **403**.

## API Endpoints

POST /auth/signup
- Body: `{ email, password }`
- Password min 8 characters; stored as a hash, never plain text
- Returns 201 Created, 400 ValidationError, or 409 if the email is taken

POST /auth/login
- Body: `{ email, password }`
- Returns a JWT that expires after 24 hours: `{ token, expiresAt, userId, email }`
- Invalid credentials return 401

GET /notes?page=1&limit=10
- Requires `Authorization: Bearer <token>`
- Returns only the current user's notes (paginated)

POST /notes
- Requires a valid token
- Body: `{ title, description? }`
- Title required, min 3 chars, max 200
- Description optional, max 500 chars
- Note is stored with the authenticated user's id
- Returns 201 Created or 400 ValidationError

GET /notes/{id}
- Requires a valid token
- Returns 404 if not found, 403 if the note belongs to another user

DELETE /notes/{id}
- Requires a valid token
- Returns 204 on success, 404 if not found, 403 if the note belongs to another user

## Edge Cases Handled

Backend:
1. Pagination:
   - page < 1 => default to 1
   - limit < 1 => default to 10
   - limit > 100 => cap at 100 to prevent DoS
   - Empty list => returns empty array with totalPages = 0
   - Page beyond totalPages => returns empty array

2. Create:
   - Empty/whitespace title => 400 Bad Request
   - Title <3 chars => 400 Bad Request
   - Title too long >200 => 400
   - Description >500 chars => 400 Bad Request
   - Trim whitespace
   - Returns structured error: { error: "Validation failed", details: [...] }

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
3. Invalid user input => Zod client validation prevents submit, shows specific field errors
4. Server validation still enforced - never trust client
4. Pagination controls disabled at boundaries
5. Delete confirmation dialog
6. Loading state prevents duplicate requests
7. API base URL configurable

## Phase 1 - Why Pagination Matters
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

## Phase 2 - Input Validation Learning

### Why Validation Matters
1. **Security**: Prevents injection, oversized payloads, DoS attacks, malformed data reaching business logic
2. **Data Integrity**: Ensures database/store only contains valid, consistent data
3. **User Experience**: Clear, actionable errors instead of generic 500s
4. **API Contract**: Documents expectations for consumers
5. **Cost**: Catches bad data early, cheaper than fixing corrupted data later

### Bad Error Message vs Good Error Message

**Without validation (BAD):**
```http
POST /notes
{ "title": "ab" }
```
Response: 500 Internal Server Error
Body: { "error": "Object reference not set to an instance of an object" }
→ Client has no idea what went wrong, can't fix it

**With validation (GOOD - Server):**
```http
POST /notes
{ "title": "ab" }
```
Response: 400 Bad Request
Body: {
  "error": "Validation failed",
  "details": ["Title must be at least 3 characters"],
  "received": { "title": "ab", "description": null }
}
→ Client knows exactly what to fix

**With validation (GOOD - Client with Zod):**
Immediate feedback before network request:
`Validation error: Title must be at least 3 characters`
→ No roundtrip, instant UX

### Request Without Validation vs With Validation

**Without validation:**
- Server trusts client input
- Empty title stored → UI breaks
- 1000 char description stored → DB bloat, performance hit
- Unexpected type causes unhandled exception → 500 error
- Hard to debug

**With validation (Backend + Frontend):**
- **Frontend Zod**: Catches errors instantly, user-friendly messages
- **Backend C#**: Final gatekeeper, returns 400 with details
- Edge cases handled: whitespace, min/max length, optional fields
- Thread-safe storage with lock
- Clear API contract

### Validation Libraries Used
- **Backend**: Manual validation in Minimal API with structured error response
  - Could also use FluentValidation, DataAnnotations
- **Frontend**: Zod schema for type-safe parsing
  - `z.string().min(3).max(200)`
  - `z.string().max(500).optional()`

### Edge Cases Handled in Phase 2
Backend:
- Title null/whitespace → "Title is required"
- Title <3 chars → "Title must be at least 3 characters"
- Title >200 → "Title too long (max 200 characters)"
- Description >500 → "Description too long (max 500 characters)"
- Multiple errors returned together
- Trim whitespace before validation/storage

Frontend:
- Zod validates before submit
- Empty description allowed
- Shows first error per field
- Server errors still displayed if bypassed

### Example Requests

Valid:
```json
POST /notes
{
  "title": "Meeting Notes",
  "description": "Discuss project timeline"
}
→ 201 Created
```

Invalid - title too short:
```json
POST /notes
{
  "title": "Hi",
  "description": "..."
}
→ 400 { "error":"Validation failed", "details":["Title must be at least 3 characters"] }
```

Invalid - description too long:
```json
POST /notes
{
  "title": "Valid Title",
  "description": "a".repeat(501)
}
→ 400 { "error":"Validation failed", "details":["Description too long (max 500 characters)"] }
```

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

## Error Handling

Global exception handling middleware catches all unhandled exceptions, logs full details server-side with ILogger, and returns a consistent JSON shape:

{ "status": number, "error": string, "message": string, "traceId": string }

Why each error type exists:
- 400 ValidationError: client sent invalid data. Can be fixed by client. Example: title < 3 chars.
- 401 Unauthorized: missing/invalid JWT, or bad login credentials.
- 403 Forbidden: authenticated user tried to read or delete another user's note.
- 404 NotFound: requested resource does not exist. Client needs correct id.
- 409 Conflict: signup email is already registered.
- 500 InternalServerError: unexpected server error. Generic message to client, full details logged server-side with traceId.

Example requests and responses:

### 400 Bad Request - Validation
How to trigger: POST /notes with title shorter than 3 characters
```http
POST /notes
Content-Type: application/json

{ "title": "Hi", "description": "test" }
```
Response 400:
```json
{
  "status": 400,
  "error": "ValidationError",
  "message": "Title must be at least 3 characters",
  "traceId": "3f1a2b4c-..."
}
```

### 404 Not Found - Note doesn't exist
How to trigger: GET /notes/{id} with a valid GUID that was never created
```http
GET /notes/11111111-1111-1111-1111-111111111111
```
Response 404:
```json
{
  "status": 404,
  "error": "NotFound",
  "message": "Note with id 11111111-1111-1111-1111-111111111111 not found",
  "traceId": "9e2d4f..."
}
```

### 500 Internal Server Error - Unexpected error
How to trigger: Force an unhandled exception (e.g., temporarily introduce bug or call internal error endpoint). In production, any bug not caught by validation will be caught by middleware.
Response 500:
```json
{
  "status": 500,
  "error": "InternalServerError",
  "message": "An unexpected error occurred. Please try again later.",
  "traceId": "a7c9e1..."
}
```
Full exception with stack trace is logged server-side via ILogger, never exposed to client.

## Next Improvements
- Add PUT/PATCH for update
- Add search/filter
- Persist to file/db
- Add unit tests
