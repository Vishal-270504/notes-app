export interface Note {
  id: string;
  title: string;
  description?: string | null;
  createdAt: string;
  updatedAt: string;
  userId?: string;
}

export interface PagedResponse {
  page: number;
  limit: number;
  total: number;
  totalPages: number;
  notes: Note[];
}

export interface LoginResponse {
  token: string;
  expiresAt: string;
  userId: string;
  email: string;
}

// Thrown when the API says the note does not exist (404), so the UI can react to it specifically.
export class NotFoundError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'NotFoundError';
  }
}

const BASE_URL = 'http://localhost:5000';
const TOKEN_KEY = 'notes.token';
const EMAIL_KEY = 'notes.email';

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function getEmail(): string | null {
  return localStorage.getItem(EMAIL_KEY);
}

export function saveSession(token: string, email: string) {
  localStorage.setItem(TOKEN_KEY, token);
  localStorage.setItem(EMAIL_KEY, email);
}

export function clearSession() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(EMAIL_KEY);
}

// Wraps fetch so a network failure (no connection, server down, CORS) becomes a friendly message
// instead of the raw browser text "Failed to fetch".
async function send(url: string, init?: RequestInit): Promise<Response> {
  try {
    return await fetch(url, init);
  } catch {
    throw new Error("Can't reach the server. Check your connection.");
  }
}

async function readError(res: Response, fallback: string): Promise<string> {
  try {
    const body = await res.json();
    return body.message || body.error || fallback;
  } catch {
    return fallback;
  }
}

function authHeaders(extra: Record<string, string> = {}): Record<string, string> {
  const token = getToken();
  return {
    ...extra,
    ...(token ? { Authorization: `Bearer ${token}` } : {})
  };
}

export async function signup(email: string, password: string): Promise<void> {
  const res = await send(`${BASE_URL}/auth/signup`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password })
  });
  if (!res.ok) {
    throw new Error(await readError(res, 'Signup failed'));
  }
}

export async function login(email: string, password: string): Promise<LoginResponse> {
  const res = await send(`${BASE_URL}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password })
  });
  if (!res.ok) {
    throw new Error(await readError(res, 'Login failed'));
  }
  const data: LoginResponse = await res.json();
  saveSession(data.token, data.email);
  return data;
}

export async function fetchNotes(page = 1, limit = 10): Promise<PagedResponse> {
  const res = await send(`${BASE_URL}/notes?page=${page}&limit=${limit}`, {
    headers: authHeaders()
  });
  if (res.status === 401) {
    clearSession();
    throw new Error('Please log in. Notes require a valid token.');
  }
  if (!res.ok) {
    throw new Error(await readError(res, "We couldn't load your notes."));
  }
  return res.json();
}

export async function createNote(title: string, description?: string): Promise<Note> {
  const res = await send(`${BASE_URL}/notes`, {
    method: 'POST',
    headers: authHeaders({ 'Content-Type': 'application/json' }),
    body: JSON.stringify({ title, description })
  });
  if (res.status === 401) {
    clearSession();
    throw new Error('Please log in. Notes require a valid token.');
  }
  if (!res.ok) {
    throw new Error(await readError(res, 'Failed to create note'));
  }
  return res.json();
}

export async function deleteNote(id: string): Promise<void> {
  const res = await send(`${BASE_URL}/notes/${id}`, {
    method: 'DELETE',
    headers: authHeaders()
  });
  if (res.status === 401) {
    clearSession();
    throw new Error('Please log in. Notes require a valid token.');
  }
  if (res.status === 404) {
    throw new NotFoundError('This note no longer exists. The list has been refreshed.');
  }
  if (!res.ok && res.status !== 204) {
    throw new Error(await readError(res, 'Failed to delete note'));
  }
}
