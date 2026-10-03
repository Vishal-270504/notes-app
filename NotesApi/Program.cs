using System.Collections.Concurrent;

// Model for a Note
public record Note
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

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
        new Note { Id = Guid.NewGuid(), Title = "Welcome", Content = "This is your first note", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
        new Note { Id = Guid.NewGuid(), Title = "Pagination", Content = "Why pagination matters...", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
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

// POST /notes - create a new note
app.MapPost("/notes", (NoteCreateDto dto) =>
{
    // Edge cases: empty title/content, too long
    if (string.IsNullOrWhiteSpace(dto.Title))
        return Results.BadRequest(new { error = "Title is required" });
    if (string.IsNullOrWhiteSpace(dto.Content))
        return Results.BadRequest(new { error = "Content is required" });
    if (dto.Title.Length > 200)
        return Results.BadRequest(new { error = "Title too long (max 200)" });

    var note = new Note
    {
        Id = Guid.NewGuid(),
        Title = dto.Title.Trim(),
        Content = dto.Content.Trim(),
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

record NoteCreateDto(string Title, string Content);

app.Run();
