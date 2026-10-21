using NotesApi.Models;
using NotesApi.Repositories;

namespace NotesApi.Services;

// Service layer: validation rules, ownership checks, pagination math. Calls the repository.
public class NotesService
{
    private readonly INotesRepository _repository;

    public NotesService(INotesRepository repository)
    {
        _repository = repository;
    }

    public async Task<(bool Success, List<string> Errors, object? Result)> GetNotesAsync(Guid userId, int? page, int? limit)
    {
        int pageNumber = page.GetValueOrDefault(1);
        int pageSize = limit.GetValueOrDefault(10);
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        int total = await _repository.CountByUserIdAsync(userId);
        int totalPages = (int)Math.Ceiling(total / (double)pageSize);

        var pagedNotes = await _repository.GetPagedByUserIdAsync(userId, pageNumber, pageSize);

        return (true, [], new { page = pageNumber, limit = pageSize, total, totalPages, notes = pagedNotes });
    }

    public async Task<(bool Success, List<string> Errors, Note? Note)> CreateNoteAsync(Guid userId, string? title, string? description)
    {
        var errors = new List<string>();
        var trimmedTitle = title?.Trim();

        if (string.IsNullOrWhiteSpace(trimmedTitle))
            errors.Add("Title is required");
        else
        {
            if (trimmedTitle.Length < 3) errors.Add("Title must be at least 3 characters");
            if (trimmedTitle.Length > 200) errors.Add("Title too long (max 200 characters)");
        }

        var trimmedDescription = description?.Trim();
        if (!string.IsNullOrEmpty(trimmedDescription) && trimmedDescription.Length > 500)
            errors.Add("Description too long (max 500 characters)");

        if (errors.Count > 0)
        {
            return (false, errors, null);
        }

        var note = new Note
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = trimmedTitle!,
            Description = string.IsNullOrWhiteSpace(trimmedDescription) ? null : trimmedDescription,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var createdNote = await _repository.AddAsync(note);
        return (true, [], createdNote);
    }

    public async Task<(bool Success, List<string> Errors, Note? Note)> GetNoteByIdAsync(Guid userId, Guid id)
    {
        var note = await _repository.GetByIdAsync(id);

        if (note == null)
        {
            return (false, [$"Note with id {id} not found"], null);
        }

        if (note.UserId != userId)
        {
            return (false, ["You do not have access to this note"], null);
        }

        return (true, [], note);
    }

    public async Task<(bool Success, List<string> Errors)> DeleteNoteAsync(Guid userId, Guid id)
    {
        var note = await _repository.GetByIdAsync(id);

        if (note == null)
        {
            return (false, [$"Note with id {id} not found"]);
        }

        if (note.UserId != userId)
        {
            return (false, ["You do not have access to this note"]);
        }

        var removed = await _repository.RemoveAsync(id, userId);
        return (removed, []);
    }

    public async Task<(bool Success, List<string> Errors, object? Result)> SeedNotesAsync(Guid userId)
    {
        const int totalCount = 100_000;
        const int batchSize = 1_000;

        var existing = await _repository.CountByUserIdAsync(userId);
        if (existing >= totalCount)
        {
            return (false, [$"User already has {existing} notes. Remove notes before re-seeding."], null);
        }

        var now = DateTime.UtcNow;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int inserted = 0;

        for (int i = 0; i < totalCount; i += batchSize)
        {
            var batch = new List<Note>();
            int currentBatch = Math.Min(batchSize, totalCount - i);
            for (int j = 0; j < currentBatch; j++)
            {
                int idx = i + j;
                batch.Add(new Note
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = $"Seed Note {idx}",
                    Description = $"Auto-generated seed note {idx} for performance testing",
                    CreatedAt = now.AddSeconds(-idx),
                    UpdatedAt = now
                });
            }
            foreach (var note in batch)
            {
                await _repository.AddAsync(note);
            }
            inserted += batch.Count;
        }

        sw.Stop();
        return (true, [], new { seeded = inserted, elapsedMs = sw.ElapsedMilliseconds, message = "Seeding complete. Use GET /notes/benchmark to measure query time." });
    }

    public async Task<(bool Success, List<string> Errors, object? Result)> BenchmarkAsync(Guid userId)
    {
        await _repository.GetLatestByUserIdAsync(userId, 1); // Warm up

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var total = await _repository.CountByUserIdAsync(userId);
        var sample = await _repository.GetLatestByUserIdAsync(userId, 10);
        sw.Stop();

        return (true, [], new
        {
            elapsedMs = sw.ElapsedMilliseconds,
            totalNotes = total,
            sampleReturned = sample.Count,
            indexUsed = "In-memory (no index)",
            note = "In-memory repository: O(n) scan for user's notes. For large datasets, a database with composite index on UserId+CreatedAt would be faster."
        });
    }
}