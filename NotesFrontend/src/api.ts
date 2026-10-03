export interface Note {
  id: string;
  title: string;
  content: string;
  createdAt: string;
  updatedAt: string;
}

export interface PagedResponse {
  page: number;
  limit: number;
  total: number;
  totalPages: number;
  notes: Note[];
}

const BASE_URL = 'http://localhost:5000';

export async function fetchNotes(page = 1, limit = 10): Promise<PagedResponse> {
  const res = await fetch(`${BASE_URL}/notes?page=${page}&limit=${limit}`);
  if (!res.ok) {
    throw new Error('Failed to fetch notes');
  }
  return res.json();
}

export async function createNote(title: string, content: string): Promise<Note> {
  const res = await fetch(`${BASE_URL}/notes`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ title, content })
  });
  if (!res.ok) {
    const err = await res.json();
    throw new Error(err.error || 'Failed to create note');
  }
  return res.json();
}

export async function deleteNote(id: string): Promise<void> {
  const res = await fetch(`${BASE_URL}/notes/${id}`, { method: 'DELETE' });
  if (!res.ok && res.status !== 204) {
    const err = await res.json();
    throw new Error(err.error || 'Failed to delete note');
  }
}
