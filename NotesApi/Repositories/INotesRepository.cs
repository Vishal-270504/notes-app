using NotesApi.Models;

namespace NotesApi.Repositories;

// Repository interface: only stores and retrieves notes.
public interface INotesRepository
{
    Task<int> CountByUserIdAsync(Guid userId);
    Task<List<Note>> GetPagedByUserIdAsync(Guid userId, int pageNumber, int pageSize);
    Task<Note?> GetByIdAsync(Guid id);
    Task<Note> AddAsync(Note note);
    Task<bool> RemoveAsync(Guid id, Guid userId);
    Task<int> GetTotalCountAsync();
    Task<List<Note>> GetLatestByUserIdAsync(Guid userId, int count);
}