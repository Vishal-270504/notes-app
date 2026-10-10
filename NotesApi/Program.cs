using NotesApi.Models;
using NotesApi.Middleware;
using Microsoft.AspNetCore.RateLimiting;

// In-memory storage
var notes = new List<Note>();
var lockObj = new object();

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

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("NotesPolicy", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
    options.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.StatusCode = 429;
        await ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "Too Many Requests", message = "Rate limit exceeded: 10 requests per minute per IP on /notes" });
    };
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseRateLimiter();
app.UseCors();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger"));

var notesGroup = app.MapGroup("/notes").RequireRateLimiting("NotesPolicy");

// GET /notes with pagination
notesGroup.MapGet("", (int? page, int? limit) =>
{
    int pageNumber = page.GetValueOrDefault(1);
    int pageSize = limit.GetValueOrDefault(10);
    if (pageNumber < 1) pageNumber = 1;
    if (pageSize < 1) pageSize = 10;
    if (pageSize > 100) pageSize = 100;

    List<Note> snapshot;
    lock (lockObj) { snapshot = notes.OrderByDescending(n => n.CreatedAt).ToList(); }

    int total = snapshot.Count;
    int totalPages = (int)Math.Ceiling(total / (double)pageSize);
    var pagedNotes = snapshot.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

    return Results.Ok(new { page = pageNumber, limit = pageSize, total, totalPages, notes = pagedNotes });
});

// POST /notes with validation
notesGroup.MapPost("", (HttpContext httpContext, NoteCreateDto dto) =>
{
    var traceId = httpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
    var errors = new List<string>();
    var title = dto.Title?.Trim();

    if (string.IsNullOrWhiteSpace(title))
        errors.Add("Title is required");
    else
    {
        if (title.Length < 3) errors.Add("Title must be at least 3 characters");
        if (title.Length > 200) errors.Add("Title too long (max 200 characters)");
    }

    var description = dto.Description?.Trim();
    if (!string.IsNullOrEmpty(description) && description.Length > 500)
        errors.Add("Description too long (max 500 characters)");

    if (errors.Count > 0)
    {
        // 400 Bad Request: client sent invalid data
        // Why 400 exists: client error, request can be fixed by client without server changes
        var message = string.Join("; ", errors);
        return Results.BadRequest(new ErrorResponse(400, "ValidationError", message, traceId));
    }

    var note = new Note
    {
        Id = Guid.NewGuid(),
        Title = title!,
        Description = string.IsNullOrWhiteSpace(description) ? null : description,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    lock (lockObj) { notes.Add(note); }
    return Results.Created($"/notes/{note.Id}", note);
});

// GET /notes/{id}
notesGroup.MapGet("/{id:guid}", (HttpContext httpContext, Guid id) =>
{
    var traceId = httpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
    Note? note;
    lock (lockObj) { note = notes.FirstOrDefault(n => n.Id == id); }

    if (note == null)
    {
        // 404 Not Found: resource does not exist
        // Why 404 exists: client requested a resource that isn't present; correct id needed
        return Results.NotFound(new ErrorResponse(404, "NotFound", $"Note with id {id} not found", traceId));
    }
    return Results.Ok(note);
});

// DELETE /notes/{id}
notesGroup.MapDelete("/{id:guid}", (HttpContext httpContext, Guid id) =>
{
    var traceId = httpContext.Items["TraceId"]?.ToString() ?? Guid.NewGuid().ToString();
    bool removed;
    lock (lockObj)
    {
        var note = notes.FirstOrDefault(n => n.Id == id);
        if (note == null) removed = false;
        else { notes.Remove(note); removed = true; }
    }

    if (!removed)
    {
        // 404 Not Found: resource does not exist
        // Why 404 exists: client tried to delete a resource that isn't present
        return Results.NotFound(new ErrorResponse(404, "NotFound", $"Note with id {id} not found", traceId));
    }
    return Results.NoContent();
});

app.Run();
