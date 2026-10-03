import { useEffect, useState } from 'react';
import { Note, fetchNotes, createNote, deleteNote } from './api';

function App() {
  const [notes, setNotes] = useState<Note[]>([]);
  const [page, setPage] = useState(1);
  const [limit] = useState(10);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [title, setTitle] = useState('');
  const [content, setContent] = useState('');

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
    if (!title.trim() || !content.trim()) {
      setError('Title and content are required');
      return;
    }
    try {
      await createNote(title, content);
      setTitle('');
      setContent('');
      loadNotes(page);
    } catch (e: any) {
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
          placeholder="Title"
          value={title}
          onChange={e => setTitle(e.target.value)}
          style={{ width: '100%', marginBottom: 8, padding: 8 }}
        />
        <textarea
          placeholder="Content"
          value={content}
          onChange={e => setContent(e.target.value)}
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
            <p>{n.content}</p>
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
