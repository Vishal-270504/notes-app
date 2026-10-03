# Validation Demo - Bad vs Good Error Messages

## Setup
Backend running on http://localhost:5000

## 1. Bad Error Message Example (Without Validation)
Simulate what would happen if we didn't validate:

```bash
curl -X POST http://localhost:5000/notes \
  -H "Content-Type: application/json" \
  -d '{"title":"Hi"}'
```
Without validation, server might:
- Store "Hi" -> breaks UI assumption title >=3
- Throw NullReferenceException if Description null not handled -> 500
- Return generic `{"error":"Invalid data"}`

## 2. Good Error Message Example (With Validation)

### Invalid title too short
```bash
curl -X POST http://localhost:5000/notes \
  -H "Content-Type: application/json" \
  -d '{"title":"Hi","description":"test"}'
```
Response 400:
```json
{
  "error": "Validation failed",
  "details": ["Title must be at least 3 characters"],
  "received": { "title":"Hi","description":"test" }
}
```

### Invalid description too long
```bash
curl -X POST http://localhost:5000/notes \
  -H "Content-Type: application/json" \
  -d '{"title":"Valid Title","description":"a'.repeat(501)}'
```
Response 400:
```json
{
  "error": "Validation failed",
  "details": ["Description too long (max 500 characters)"]
}
```

### Valid request
```bash
curl -X POST http://localhost:5000/notes \
  -H "Content-Type: application/json" \
  -d '{"title":"Meeting Notes","description":"Discuss Q4 goals"}'
```
Response 201:
```json
{
  "id":"...",
  "title":"Meeting Notes",
  "description":"Discuss Q4 goals",
  "createdAt":"...",
  "updatedAt":"..."
}
```

## Frontend Zod Validation
Client validates before network request:
- Title <3 chars → immediate error: "Title must be at least 3 characters"
- Description >500 → immediate error: "Description too long (max 500 characters)"
- No network call made for invalid data

Server validation is still required as final guard.
