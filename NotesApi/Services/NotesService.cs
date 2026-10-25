using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using NotesApi.Models;
using NotesApi.Repositories;

namespace NotesApi.Services;

// Service layer: validation rules, ownership checks, pagination math. Calls the repository.
public class NotesService
{
    private readonly INotesRepository _repository;
    private readonly IMemoryCache _cache;
    // Per-user version numbers for cache invalidation.
    // Each write increments the user's version, so old cached lists become unreachable.
    // This avoids clearing the whole cache and keeps other users' data intact.
    private static readonly ConcurrentDictionary<Guid, long> _userCacheVersions = new();

    public NotesService(INotesRepository repository, IMemoryCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<(bool Success, List<string> Errors, object? Result, bool CacheHit)> GetNotesAsync(Guid userId, int? page, int? limit)
    {
        int pageNumber = page.GetValueOrDefault(1);
        int pageSize = limit.GetValueOrDefault(10);
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        // Cache key includes userId, page, pageSize, and a per-user version number.
        // The version is incremented on every write (create/delete/seed), invalidating old entries.
        // This ensures users never see each other's notes and writes are immediately visible.
        var version = GetUserCacheVersion(userId);
        var cacheKey = $"notes:{userId}:{pageNumber}:{pageSize}:v{version}";

        if (_cache.TryGetValue(cacheKey, out object? cachedResult))
        {
            return (true, [], cachedResult, true);
        }

        int total = await _repository.CountByUserIdAsync(userId);
        int totalPages = (int)Math.Ceiling(total / (double)pageSize);

        var pagedNotes = await _repository.GetPagedByUserIdAsync(userId, pageNumber, pageSize);

        var result = new { page = pageNumber, limit = pageSize, total, totalPages, notes = pagedNotes };
        _cache.Set(cacheKey, result, TimeSpan.FromSeconds(60));

        return (true, [], result, false);
    }

    private long GetUserCacheVersion(Guid userId)
    {
        return _userCacheVersions.GetOrAdd(userId, 0);
    }

    private void IncrementUserCacheVersion(Guid userId)
    {
        _userCacheVersions.AddOrUpdate(userId, 1, (_, v) => v + 1);
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

        // Server owns Id, UserId, CreatedAt, UpdatedAt — never trust client values
        var note = new Note
        {
            Id = Guid.NewGuid(),
            UserId = userId,              // from token, not request body
            Title = trimmedTitle!,
            Description = string.IsNullOrWhiteSpace(trimmedDescription) ? null : trimmedDescription,
            CreatedAt = DateTime.UtcNow,  // server timestamp
            UpdatedAt = DateTime.UtcNow   // server timestamp
        };

        var createdNote = await _repository.AddAsync(note);
        IncrementUserCacheVersion(userId);
        return (true, [], createdNote);
    }

    public async Task<(bool Success, List<string> Errors, Note? Note)> GetNoteByIdAsync(Guid userId, Guid id)
    {
        var note = await _repository.GetByIdAsync(id);

        if (note == null)
        {
            return (false, [$"Note with id {id} not found"], null);
        }

        // Ownership check: server enforces that users only access their own notes
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

        // Ownership check: server enforces that users only delete their own notes
        if (note.UserId != userId)
        {
            return (false, ["You do not have access to this note"]);
        }

        var removed = await _repository.RemoveAsync(id, userId);
        if (removed)
        {
            IncrementUserCacheVersion(userId);
        }
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
        IncrementUserCacheVersion(userId);
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