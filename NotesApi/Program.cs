using System.Collections.Concurrent;
using NotesApi.Models;

// In-memory storage
// Using ConcurrentDictionary for thread-safety in real apps
// For beginner simplicity, we use a List with lock
var notes = new List<Note>();
var lockObj = new object();

// Seed some data for testing
lock (lockObj)
{
    notes.AddRange(new[]
    {
        new Note { Id = Guid.NewGuid(), Title = "Welcome", Description = "This is your first note", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
        new Note { Id = Guid.NewGuid(), Title = "Pagination", Description = "Why pagination matters...", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
    });
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger"));

// GET /notes - returns all notes with pagination
// Why pagination matters:
// Without pagination, if you have 10,000 notes, the API returns ALL of them at once.
// This causes:
// 1. Slow response time - large JSON payload
// 2. High memory usage on server and client
// 3. Browser/app may freeze or crash
// Pagination limits data per request, making API fast and scalable.
app.MapGet("/notes", (int? page, int? limit) =>
{
    // Edge case handling
    int pageNumber = page.GetValueOrDefault(1);
    int pageSize = limit.GetValueOrDefault(10);

    if (pageNumber < 1) pageNumber = 1;
    if (pageSize < 1) pageSize = 10;
    if (pageSize > 100) pageSize = 100; // max limit to prevent abuse

    List<Note> snapshot;
    lock (lockObj)
    {
        snapshot = notes.OrderByDescending(n => n.CreatedAt).ToList();
    }

    int total = snapshot.Count;
    int totalPages = (int)Math.Ceiling(total / (double)pageSize);
    var pagedNotes = snapshot.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

    var response = new
    {
        page = pageNumber,
        limit = pageSize,
        total,
        totalPages,
        notes = pagedNotes
    };

    return Results.Ok(response);
});

// POST /notes - create a new note WITH VALIDATION
// Why validation matters:
// 1. Security: Prevents malicious payloads, injection attacks, oversized data causing DoS
// 2. Data integrity: Ensures consistent, usable data in your store
// 3. UX: Gives clear, actionable feedback instead of generic 500 errors
// 4. API contract: Documents what clients can send
//
// BAD error message example (without proper validation):
//   return Results.BadRequest(new { error = "Invalid data" }); // vague, client can't fix
//   or worse: unhandled exception -> 500 Internal Server Error
//
// GOOD error message example (with validation):
//   Return 400 with specific field errors so client knows exactly what to fix
//
// Difference: Request without validation vs with validation
// WITHOUT validation: server trusts client, may crash or store garbage data
//     POST /notes { "title": "ab" } -> stores "ab" -> breaks UI assumptions
// WITH validation: server rejects bad data early with clear messages
//     POST /notes { "title": "ab" } -> 400 { error: "Validation failed", details: ["Title must be at least 3 characters"] }
app.MapPost("/notes", (NoteCreateDto dto) =>
{
    // Phase 2: Input validation with detailed error messages
    // Requirements: title required min 3 chars, description optional max 500 chars
    var errors = new List<string>();
    
    var title = dto.Title?.Trim();
    if (string.IsNullOrWhiteSpace(title))
    {
        errors.Add("Title is required");
    }
    else
    {
        if (title.Length < 3)
            errors.Add("Title must be at least 3 characters");
        if (title.Length > 200)
            errors.Add("Title too long (max 200 characters)");
    }

    var description = dto.Description?.Trim();
    if (!string.IsNullOrEmpty(description) && description.Length > 500)
    {
        errors.Add("Description too long (max 500 characters)");
    }

    // If validation fails, return structured error
    if (errors.Count > 0)
    {
        // Good error message: specific, actionable, machine-readable
        return Results.BadRequest(new { 
            error = "Validation failed", 
            details = errors,
            received = new { title = dto.Title, description = dto.Description }
        });
    }

    var note = new Note
    {
        Id = Guid.NewGuid(),
        Title = title!,
        Description = string.IsNullOrWhiteSpace(description) ? null : description,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    lock (lockObj)
    {
        notes.Add(note);
    }

    return Results.Created($"/notes/{note.Id}", note);
});

// GET /notes/{id} - get a single note
app.MapGet("/notes/{id:guid}", (Guid id) =>
{
    Note? note;
    lock (lockObj)
    {
        note = notes.FirstOrDefault(n => n.Id == id);
    }

    if (note == null)
        return Results.NotFound(new { error = $"Note with id {id} not found" });

    return Results.Ok(note);
});

// DELETE /notes/{id} - delete a note
app.MapDelete("/notes/{id:guid}", (Guid id) =>
{
    bool removed;
    lock (lockObj)
    {
        var note = notes.FirstOrDefault(n => n.Id == id);
        if (note == null)
        {
            removed = false;
        }
        else
        {
            notes.Remove(note);
            removed = true;
        }
    }

    if (!removed)
        return Results.NotFound(new { error = $"Note with id {id} not found" });

    return Results.NoContent();
});

app.Run();
