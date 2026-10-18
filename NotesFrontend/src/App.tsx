import React, { useEffect, useState } from 'react';
import { z } from 'zod';
import {
  Note,
  fetchNotes,
  createNote,
  deleteNote,
  signup,
  login,
  getToken,
  getEmail,
  clearSession
} from './api';

const noteSchema = z.object({
  title: z.string().trim().min(3, 'Title must be at least 3 characters').max(200, 'Title too long (max 200)'),
  description: z.string().trim().max(500, 'Description too long (max 500 characters)').optional().or(z.literal(''))
});

const authSchema = z.object({
  email: z.string().trim().email('Enter a valid email'),
  password: z.string().min(8, 'Password must be at least 8 characters')
});

function shortenToken(token: string) {
  if (token.length < 24) return token;
  return `${token.slice(0, 18)}…${token.slice(-10)}`;
}

function App() {
  const [authed, setAuthed] = useState(() => !!getToken());
  const [email, setEmail] = useState(() => getEmail() ?? '');
  const [mode, setMode] = useState<'login' | 'signup'>('signup');
  const [authEmail, setAuthEmail] = useState('');
  const [authPassword, setAuthPassword] = useState('');
  const [authMessage, setAuthMessage] = useState<string | null>(null);

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
      setTotalPages(data.totalPages || 1);
      setPage(data.page);
    } catch (e: any) {
      if (!getToken()) setAuthed(false);
      setError(e.message || 'Failed to load notes');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (authed) loadNotes(page);
  }, [page, authed]);

  const handleAuth = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setAuthMessage(null);

    const parsed = authSchema.safeParse({ email: authEmail, password: authPassword });
    if (!parsed.success) {
      setError(parsed.error.issues.map(i => i.message).join(', '));
      return;
    }

    try {
      if (mode === 'signup') {
        await signup(parsed.data.email, parsed.data.password);
        setMode('login');
        setAuthMessage('Account created. Log in next — that is when the API gives you a JWT.');
        return;
      }

      const session = await login(parsed.data.email, parsed.data.password);
      setEmail(session.email);
      setAuthed(true);
      setPage(1);
      setAuthPassword('');
    } catch (err: any) {
      setError(err.message || 'Authentication failed');
    }
  };

  const handleLogout = () => {
    clearSession();
    setAuthed(false);
    setNotes([]);
    setError(null);
    setAuthMessage('Logged out. The token is gone, so /notes will return 401 until you log in again.');
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    const parseResult = noteSchema.safeParse({ title, description });
    if (!parseResult.success) {
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
      if (!getToken()) setAuthed(false);
      setError(e.message || 'Failed to create note');
    }
  };

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this note?')) return;
    try {
      await deleteNote(id);
      loadNotes(page);
    } catch (e: any) {
      if (!getToken()) setAuthed(false);
      setError(e.message || 'Failed to delete note');
    }
  };

  const token = getToken();

  if (!authed) {
    return (
      <div style={{ maxWidth: 480, margin: '40px auto', padding: 20, fontFamily: 'system-ui, sans-serif' }}>
        <h1 style={{ marginBottom: 8 }}>Notes — Auth demo</h1>
        <p style={{ color: '#444', lineHeight: 1.5 }}>
          Authorization in one sentence: <strong>prove who you are, then the API only lets you touch your own notes.</strong>
        </p>
        <ol style={{ color: '#333', lineHeight: 1.6, paddingLeft: 20 }}>
          <li><strong>Sign up</strong> creates a user. The password is hashed on the server.</li>
          <li><strong>Log in</strong> checks that password and returns a JWT (a signed pass).</li>
          <li><strong>Notes</strong> requests send <code>Authorization: Bearer &lt;token&gt;</code>. No token → 401.</li>
        </ol>

        <div style={{ display: 'flex', gap: 8, margin: '20px 0' }}>
          <button
            type="button"
            onClick={() => { setMode('signup'); setError(null); }}
            style={{
              padding: '8px 16px',
              fontWeight: mode === 'signup' ? 700 : 400,
              border: mode === 'signup' ? '2px solid #222' : '1px solid #ccc',
              background: mode === 'signup' ? '#222' : '#fff',
              color: mode === 'signup' ? '#fff' : '#222',
              cursor: 'pointer'
            }}
          >
            Sign up
          </button>
          <button
            type="button"
            onClick={() => { setMode('login'); setError(null); }}
            style={{
              padding: '8px 16px',
              fontWeight: mode === 'login' ? 700 : 400,
              border: mode === 'login' ? '2px solid #222' : '1px solid #ccc',
              background: mode === 'login' ? '#222' : '#fff',
              color: mode === 'login' ? '#fff' : '#222',
              cursor: 'pointer'
            }}
          >
            Log in
          </button>
        </div>

        <form onSubmit={handleAuth}>
          <input
            type="email"
            placeholder="Email"
            value={authEmail}
            onChange={e => setAuthEmail(e.target.value)}
            autoComplete="email"
            style={{ width: '100%', marginBottom: 8, padding: 8, boxSizing: 'border-box' }}
          />
          <input
            type="password"
            placeholder="Password (min 8 characters)"
            value={authPassword}
            onChange={e => setAuthPassword(e.target.value)}
            autoComplete={mode === 'signup' ? 'new-password' : 'current-password'}
            style={{ width: '100%', marginBottom: 8, padding: 8, boxSizing: 'border-box' }}
          />
          <button type="submit" style={{ padding: '8px 16px' }}>
            {mode === 'signup' ? 'Create account' : 'Log in and get a token'}
          </button>
        </form>

        {authMessage && <div style={{ color: '#0a6', marginTop: 12 }}>{authMessage}</div>}
        {error && <div style={{ color: 'red', marginTop: 12 }}>{error}</div>}
      </div>
    );
  }

  return (
    <div style={{ maxWidth: 800, margin: '0 auto', padding: 20, fontFamily: 'system-ui, sans-serif' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
        <h1 style={{ margin: 0 }}>Your notes</h1>
        <div>
          <span style={{ marginRight: 12 }}>{email}</span>
          <button onClick={handleLogout} style={{ padding: '6px 12px' }}>Log out</button>
        </div>
      </div>

      <div style={{ background: '#f4f4f4', padding: 12, borderRadius: 6, margin: '16px 0', fontSize: 14, lineHeight: 1.5 }}>
        <div><strong>Who:</strong> {email} — the API reads this from the JWT, not from a field you type on each note.</div>
        <div style={{ marginTop: 6, wordBreak: 'break-all' }}>
          <strong>Token sent on every notes request:</strong> {token ? shortenToken(token) : '(none)'}
        </div>
        <div style={{ marginTop: 6 }}>
          Log in as a second user in another browser (or after logout) to show they cannot see these notes.
        </div>
      </div>

      <form onSubmit={handleCreate} style={{ marginBottom: 20 }}>
        <input
          placeholder="Title (min 3 chars)"
          value={title}
          onChange={e => setTitle(e.target.value)}
          style={{ width: '100%', marginBottom: 8, padding: 8, boxSizing: 'border-box' }}
        />
        <textarea
          placeholder="Description (optional, max 500 chars)"
          value={description}
          onChange={e => setDescription(e.target.value)}
          style={{ width: '100%', marginBottom: 8, padding: 8, minHeight: 80, boxSizing: 'border-box' }}
        />
        <button type="submit" style={{ padding: '8px 16px' }}>Create Note</button>
      </form>

      {error && <div style={{ color: 'red', marginBottom: 10 }}>{error}</div>}
      {loading && <div>Loading...</div>}

      {!loading && notes.length === 0 && (
        <p style={{ color: '#555' }}>No notes yet for this user. Create one — it will be stored with your user id.</p>
      )}

      <ul style={{ listStyle: 'none', padding: 0 }}>
        {notes.map(n => (
          <li key={n.id} style={{ border: '1px solid #ccc', padding: 12, marginBottom: 10, borderRadius: 6 }}>
            <h3 style={{ marginTop: 0 }}>{n.title}</h3>
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
