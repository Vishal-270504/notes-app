import React, { useEffect, useRef, useState } from 'react';
import { z } from 'zod';
import {
  Note,
  fetchNotes,
  createNote,
  deleteNote,
  NotFoundError,
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

type Field = 'title' | 'description' | 'email' | 'password';
type FieldErrors = Partial<Record<Field, string>>;

// Validates one field with the same zod rules used on submit.
function checkField(field: Field, value: string): string | undefined {
  const result =
    field === 'title' ? noteSchema.shape.title.safeParse(value)
    : field === 'description' ? noteSchema.shape.description.safeParse(value)
    : field === 'email' ? authSchema.shape.email.safeParse(value)
    : authSchema.shape.password.safeParse(value);
  return result.success ? undefined : result.error.issues[0]?.message;
}

const globalCss = `
@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
.skeleton { background: #e0e0e0; border-radius: 4px; animation: pulse 1.2s ease-in-out infinite; }
button:focus-visible, input:focus-visible, textarea:focus-visible {
  outline: 2px solid #2563eb;
  outline-offset: 2px;
}
`;

const labelStyle: React.CSSProperties = { display: 'block', fontWeight: 600, marginBottom: 4, color: '#333' };
const helperStyle: React.CSSProperties = { display: 'block', fontSize: 12, color: '#666', marginBottom: 6 };
const errorStyle: React.CSSProperties = { display: 'block', fontSize: 13, color: '#b00020', marginBottom: 8 };
const inputStyle: React.CSSProperties = { width: '100%', marginBottom: 4, padding: 8, boxSizing: 'border-box', border: '1px solid #888', borderRadius: 4 };

function shortenToken(token: string) {
  if (token.length < 24) return token;
  return `${token.slice(0, 18)}…${token.slice(-10)}`;
}

type Toast = { id: number; text: string };

function App() {
  const [authed, setAuthed] = useState(() => !!getToken());
  const [email, setEmail] = useState(() => getEmail() ?? '');
  const [mode, setMode] = useState<'login' | 'signup'>('signup');
  const [authEmail, setAuthEmail] = useState('');
  const [authPassword, setAuthPassword] = useState('');
  const [authMessage, setAuthMessage] = useState<string | null>(null);
  const [authErrors, setAuthErrors] = useState<FieldErrors>({});

  const [notes, setNotes] = useState<Note[]>([]);
  const [page, setPage] = useState(1);
  const [limit] = useState(10);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(false);
  const [skeleton, setSkeleton] = useState(false); // true only for first load / page change
  const [loadError, setLoadError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [online, setOnline] = useState(() => navigator.onLine);
  const [pendingDelete, setPendingDelete] = useState<string | null>(null);
  const [toasts, setToasts] = useState<Toast[]>([]);
  const titleRef = useRef<HTMLInputElement>(null);
  const toastId = useRef(0);

  const showToast = (text: string) => {
    const id = ++toastId.current;
    setToasts(list => [...list, { id, text }]);
    setTimeout(() => setToasts(list => list.filter(t => t.id !== id)), 3000);
  };

  const loadNotes = async (pageNum: number, withSkeleton = false) => {
    setLoading(true);
    setSkeleton(withSkeleton);
    setLoadError(null);
    try {
      const data = await fetchNotes(pageNum, limit);
      setNotes(data.notes);
      setTotalPages(data.totalPages || 1);
      setPage(data.page);
    } catch (e: any) {
      console.error(e);
      if (!getToken()) setAuthed(false);
      setLoadError(e.message || "We couldn't load your notes.");
    } finally {
      setLoading(false);
      setSkeleton(false);
    }
  };

  useEffect(() => {
    if (authed) loadNotes(page, true);
  }, [page, authed]);

  // Going back online reloads the notes. The ref keeps the listener using the latest page and auth state.
  const reloadRef = useRef<() => void>(() => {});
  reloadRef.current = () => {
    if (authed) loadNotes(page);
  };

  useEffect(() => {
    const goOnline = () => {
      setOnline(true);
      reloadRef.current();
    };
    const goOffline = () => setOnline(false);
    window.addEventListener('online', goOnline);
    window.addEventListener('offline', goOffline);
    return () => {
      window.removeEventListener('online', goOnline);
      window.removeEventListener('offline', goOffline);
    };
  }, []);

  // Escape closes the delete dialog.
  useEffect(() => {
    if (!pendingDelete) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setPendingDelete(null);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [pendingDelete]);

  const handleAuth = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setAuthMessage(null);

    const parsed = authSchema.safeParse({ email: authEmail, password: authPassword });
    if (!parsed.success) {
      const errs: FieldErrors = {};
      for (const issue of parsed.error.issues) {
        const key = issue.path[0] as Field;
        errs[key] ??= issue.message;
      }
      setAuthErrors({ email: errs.email, password: errs.password });
      return;
    }
    setAuthErrors({});

    try {
      if (mode === 'signup') {
        await signup(parsed.data.email, parsed.data.password);
        setMode('login');
        setAuthMessage('Account created. Please log in.');
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
    setLoadError(null);
    setAuthMessage('Logged out.');
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    const parseResult = noteSchema.safeParse({ title, description });
    if (!parseResult.success) {
      const errs: FieldErrors = {};
      for (const issue of parseResult.error.issues) {
        const key = issue.path[0] as Field;
        errs[key] ??= issue.message;
      }
      setFieldErrors({ title: errs.title, description: errs.description });
      return;
    }
    setFieldErrors({});

    try {
      await createNote(parseResult.data.title, parseResult.data.description || undefined);
      setTitle('');
      setDescription('');
      showToast('Note created');
      loadNotes(page); // no skeleton after a create
    } catch (e: any) {
      if (!getToken()) setAuthed(false);
      setError(e.message || 'Failed to create note');
      showToast('Something went wrong. Try again.');
    }
  };

  // Delete now opens a dialog first. Nothing is deleted until the red button is clicked.
  const confirmDelete = async () => {
    const id = pendingDelete;
    setPendingDelete(null);
    if (!id) return;
    setError(null);
    try {
      await deleteNote(id);
      showToast('Note deleted');
      loadNotes(page);
    } catch (e: any) {
      if (e instanceof NotFoundError) {
        setError(e.message);
        loadNotes(page);
        return;
      }
      if (!getToken()) setAuthed(false);
      setError(e.message || 'Failed to delete note');
      showToast('Something went wrong. Try again.');
    }
  };

  const token = getToken();

  if (!authed) {
    return (
      <div style={{ maxWidth: 480, margin: '40px auto', padding: 20, fontFamily: 'system-ui, sans-serif', color: '#333' }}>
        <style>{globalCss}</style>
        <h1 style={{ marginBottom: 16 }}>Notes</h1>

        <div style={{ display: 'flex', gap: 8, marginBottom: 16 }}>
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

        <form onSubmit={handleAuth} noValidate>
          <label htmlFor="auth-email" style={labelStyle}>Email</label>
          <input
            id="auth-email"
            type="email"
            value={authEmail}
            onChange={e => {
              setAuthEmail(e.target.value);
              if (authErrors.email) setAuthErrors(a => ({ ...a, email: checkField('email', e.target.value) }));
            }}
            onBlur={() => setAuthErrors(a => ({ ...a, email: checkField('email', authEmail) }))}
            autoComplete="email"
            style={inputStyle}
          />
          {authErrors.email && <span style={errorStyle}>{authErrors.email}</span>}

          <label htmlFor="auth-password" style={labelStyle}>Password</label>
          <input
            id="auth-password"
            type="password"
            value={authPassword}
            onChange={e => {
              setAuthPassword(e.target.value);
              if (authErrors.password) setAuthErrors(a => ({ ...a, password: checkField('password', e.target.value) }));
            }}
            onBlur={() => setAuthErrors(a => ({ ...a, password: checkField('password', authPassword) }))}
            autoComplete={mode === 'signup' ? 'new-password' : 'current-password'}
            style={inputStyle}
          />
          <span style={helperStyle}>At least 8 characters</span>
          {authErrors.password && <span style={errorStyle}>{authErrors.password}</span>}

          <button type="submit" style={{ padding: '8px 16px', marginTop: 8 }}>
            {mode === 'signup' ? 'Create account' : 'Log in and get a token'}
          </button>
        </form>

        {authMessage && <div style={{ color: '#0a6', marginTop: 12 }}>{authMessage}</div>}
        {error && <div style={{ color: '#b00020', marginTop: 12 }}>{error}</div>}
      </div>
    );
  }

  return (
    <div style={{ maxWidth: 800, margin: '0 auto', padding: 20, fontFamily: 'system-ui, sans-serif', color: '#333' }}>
      <style>{globalCss}</style>

      {!online && (
        <div role="status" style={{ background: '#fff3cd', border: '1px solid #e0b400', padding: 10, borderRadius: 6, marginBottom: 12 }}>
          You're offline. Changes will fail until you reconnect.
        </div>
      )}

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
      </div>

      <form onSubmit={handleCreate} noValidate style={{ marginBottom: 20 }}>
        <label htmlFor="note-title" style={labelStyle}>Title</label>
        <input
          id="note-title"
          ref={titleRef}
          value={title}
          onChange={e => {
            setTitle(e.target.value);
            if (fieldErrors.title) setFieldErrors(f => ({ ...f, title: checkField('title', e.target.value) }));
          }}
          onBlur={() => setFieldErrors(f => ({ ...f, title: checkField('title', title) }))}
          style={inputStyle}
        />
        <span style={helperStyle}>At least 3 characters</span>
        {fieldErrors.title && <span style={errorStyle}>{fieldErrors.title}</span>}

        <label htmlFor="note-description" style={labelStyle}>Description (optional)</label>
        <textarea
          id="note-description"
          value={description}
          onChange={e => {
            setDescription(e.target.value);
            if (fieldErrors.description) setFieldErrors(f => ({ ...f, description: checkField('description', e.target.value) }));
          }}
          onBlur={() => setFieldErrors(f => ({ ...f, description: checkField('description', description) }))}
          style={{ ...inputStyle, minHeight: 80 }}
        />
        {fieldErrors.description && <span style={errorStyle}>{fieldErrors.description}</span>}

        <span style={{ ...helperStyle, marginTop: 4 }}>A note is a short piece of text saved to your account. Only you can see it.</span>
        <button type="submit" style={{ padding: '8px 16px', marginTop: 4 }}>Create Note</button>
      </form>

      {error && <div style={{ color: '#b00020', marginBottom: 10 }}>{error}</div>}

      {loading && skeleton ? (
        <div aria-label="Loading notes">
          {[0, 1, 2].map(i => (
            <div key={i} style={{ border: '1px solid #ccc', padding: 12, marginBottom: 10, borderRadius: 6 }}>
              <div className="skeleton" style={{ height: 18, width: '40%', marginBottom: 10 }} />
              <div className="skeleton" style={{ height: 12, width: '90%', marginBottom: 6 }} />
              <div className="skeleton" style={{ height: 12, width: '70%' }} />
            </div>
          ))}
        </div>
      ) : loadError ? (
        <div style={{ border: '1px solid #f5c2c7', background: '#fff5f5', padding: 16, borderRadius: 6, marginBottom: 10 }}>
          <strong>Couldn't load your notes</strong>
          <p style={{ margin: '6px 0 12px' }}>{loadError}</p>
          <button onClick={() => loadNotes(page)} style={{ padding: '6px 12px' }}>Retry</button>
        </div>
      ) : notes.length === 0 ? (
        <div style={{ border: '1px dashed #aaa', padding: 24, borderRadius: 6, textAlign: 'center' }}>
          <h3 style={{ marginTop: 0 }}>No notes yet</h3>
          <p style={{ color: '#555' }}>Create your first note to get started.</p>
          <button onClick={() => titleRef.current?.focus()} style={{ padding: '8px 16px' }}>
            Create your first note
          </button>
        </div>
      ) : (
        <ul style={{ listStyle: 'none', padding: 0 }}>
          {notes.map(n => (
            <li key={n.id} style={{ border: '1px solid #ccc', padding: 12, marginBottom: 10, borderRadius: 6 }}>
              <h3 style={{ marginTop: 0 }}>{n.title}</h3>
              <p>{n.description || <em>No description</em>}</p>
              <small>{new Date(n.createdAt).toLocaleString()}</small>
              <br />
              <button
                onClick={() => setPendingDelete(n.id)}
                style={{ marginTop: 8, color: 'red', fontSize: 14, padding: '8px 12px' }}
              >
                Delete
              </button>
            </li>
          ))}
        </ul>
      )}

      {pendingDelete && (
        <div
          style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.4)', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 16 }}
          onClick={() => setPendingDelete(null)}
        >
          <div
            role="dialog"
            aria-modal="true"
            aria-labelledby="delete-title"
            onClick={e => e.stopPropagation()}
            style={{ background: '#fff', padding: 20, borderRadius: 8, maxWidth: 360, width: '100%' }}
          >
            <h2 id="delete-title" style={{ marginTop: 0, fontSize: 20 }}>Delete this note?</h2>
            <p style={{ color: '#444' }}>This can't be undone.</p>
            <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
              <button autoFocus onClick={() => setPendingDelete(null)} style={{ padding: '8px 12px' }}>Cancel</button>
              <button onClick={confirmDelete} style={{ padding: '8px 12px', background: '#c62828', color: '#fff', border: 'none', borderRadius: 4 }}>Delete</button>
            </div>
          </div>
        </div>
      )}

      <div aria-live="polite" style={{ position: 'fixed', right: 16, bottom: 16, display: 'flex', flexDirection: 'column', gap: 8, zIndex: 1100 }}>
        {toasts.map(t => (
          <div key={t.id} style={{ background: '#222', color: '#fff', padding: '10px 14px', borderRadius: 6, boxShadow: '0 2px 8px rgba(0,0,0,0.3)' }}>
            {t.text}
          </div>
        ))}
      </div>

      <div style={{ marginTop: 20 }}>
        <button disabled={page <= 1 || loading} onClick={() => setPage(p => p - 1)}>Previous</button>
        <span style={{ margin: '0 10px' }}>Page {page} of {totalPages}</span>
        <button disabled={page >= totalPages || loading} onClick={() => setPage(p => p + 1)}>Next</button>
      </div>
    </div>
  );
}

export default App;
