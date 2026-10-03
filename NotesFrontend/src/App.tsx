import { useEffect, useState } from 'react';
import { z } from 'zod';
import { Note, fetchNotes, createNote, deleteNote } from './api';

// Phase 2: Input validation using Zod
// Why client-side validation matters:
// 1. Immediate feedback without network roundtrip
// 2. Reduces server load
// 3. Better UX
// Server validation is still required - never trust client!
const noteSchema = z.object({
  title: z.string().trim().min(3, 'Title must be at least 3 characters').max(200, 'Title too long (max 200)'),
  description: z.string().trim().max(500, 'Description too long (max 500 characters)').optional().or(z.literal(''))
});

function App() {
  const [notes, setNotes] = useState<Note[]>([]);
  const [page, setPage] = useState(1);
  const [limit] = useState(10);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');

  const loadNotes = async (pageNum: number) => {
    setLoading(true);
    setError(null);
    try {
      const data = await fetchNotes(pageNum, limit);
      setNotes(data.notes);
      setTotalPages(data.totalPages);
      setPage(data.page);
    } catch (e: any) {
      setError(e.message || 'Failed to load notes');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadNotes(page);
  }, [page]);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    
    // Client-side validation with Zod
    // Good error message: specific field errors before hitting server
    const parseResult = noteSchema.safeParse({ title, description });
    if (!parseResult.success) {
      // Zod gives structured errors
      const messages = parseResult.error.issues.map(i => i.message).join(', ');
      setError(`Validation error: ${messages}`);
      return;
    }

    try {
      await createNote(parseResult.data.title, parseResult.data.description || undefined);
      setTitle('');
      setDescription('');
      loadNotes(page);
    } catch (e: any) {
      // Bad error message example: generic server error
      // Good error message: server returns details array
      setError(e.message || 'Failed to create note');
    }
  };

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this note?')) return;
    try {
      await deleteNote(id);
      loadNotes(page);
    } catch (e: any) {
      setError(e.message || 'Failed to delete note');
    }
  };

  return (
    <div style={{ maxWidth: 800, margin: '0 auto', padding: 20 }}>
      <h1>Notes API Demo</h1>

      <form onSubmit={handleCreate} style={{ marginBottom: 20 }}>
        <input
          placeholder="Title (min 3 chars)"
          value={title}
          onChange={e => setTitle(e.target.value)}
          style={{ width: '100%', marginBottom: 8, padding: 8 }}
        />
        <textarea
          placeholder="Description (optional, max 500 chars)"
          value={description}
          onChange={e => setDescription(e.target.value)}
          style={{ width: '100%', marginBottom: 8, padding: 8, minHeight: 80 }}
        />
        <button type="submit" style={{ padding: '8px 16px' }}>Create Note</button>
      </form>

      {error && <div style={{ color: 'red', marginBottom: 10 }}>{error}</div>}
      {loading && <div>Loading...</div>}

      <ul style={{ listStyle: 'none', padding: 0 }}>
        {notes.map(n => (
          <li key={n.id} style={{ border: '1px solid #ccc', padding: 12, marginBottom: 10, borderRadius: 6 }}>
            <h3>{n.title}</h3>
            <p>{n.description || <em>No description</em>}</p>
            <small>{new Date(n.createdAt).toLocaleString()}</small>
            <br />
            <button onClick={() => handleDelete(n.id)} style={{ marginTop: 8, color: 'red' }}>Delete</button>
          </li>
        ))}
      </ul>

      <div style={{ marginTop: 20 }}>
        <button disabled={page <= 1 || loading} onClick={() => setPage(p => p - 1)}>Previous</button>
        <span style={{ margin: '0 10px' }}>Page {page} of {totalPages}</span>
        <button disabled={page >= totalPages || loading} onClick={() => setPage(p => p + 1)}>Next</button>
      </div>
    </div>
  );
}

export default App;
