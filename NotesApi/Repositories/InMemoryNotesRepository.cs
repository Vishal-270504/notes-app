using NotesApi.Models;

namespace NotesApi.Repositories;

// In-memory implementation of INotesRepository: only stores and retrieves notes.
public class InMemoryNotesRepository : INotesRepository
{
    private readonly List<Note> _notes = [];
    private readonly object _lock = new();

    public Task<int> CountByUserIdAsync(Guid userId)
    {
        lock (_lock)
        {
            return Task.FromResult(_notes.Count(n => n.UserId == userId));
        }
    }

    public Task<List<Note>> GetPagedByUserIdAsync(Guid userId, int pageNumber, int pageSize)
    {
        lock (_lock)
        {
            var notes = _notes
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            return Task.FromResult(notes);
        }
    }

    public Task<Note?> GetByIdAsync(Guid id)
    {
        lock (_lock)
        {
            var note = _notes.FirstOrDefault(n => n.Id == id);
            return Task.FromResult(note);
        }
    }

    public Task<Note> AddAsync(Note note)
    {
        lock (_lock)
        {
            _notes.Add(note);
            return Task.FromResult(note);
        }
    }

    public Task<bool> RemoveAsync(Guid id, Guid userId)
    {
        lock (_lock)
        {
            // Ownership check at repository level: only delete if both id and userId match
            var note = _notes.FirstOrDefault(n => n.Id == id && n.UserId == userId);
            if (note == null)
            {
                return Task.FromResult(false);
            }
            _notes.Remove(note);
            return Task.FromResult(true);
        }
    }

    public Task<int> GetTotalCountAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(_notes.Count);
        }
    }

    public Task<List<Note>> GetLatestByUserIdAsync(Guid userId, int count)
    {
        lock (_lock)
        {
            var notes = _notes
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(count)
                .ToList();
            return Task.FromResult(notes);
        }
    }
}