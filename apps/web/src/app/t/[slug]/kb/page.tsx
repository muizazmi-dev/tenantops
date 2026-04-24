import { serviceUrl } from '@/lib/api';
import { getCurrentTenant } from '@/lib/currentTenant';
import { cookies } from 'next/headers';
import { revalidatePath } from 'next/cache';
import { notFound, redirect } from 'next/navigation';

type Doc = { documentId: string; title: string; createdAt: string; indexedAt: string | null };

async function fetchDocs(): Promise<Doc[]> {
  const token = cookies().get('tenantops_token')?.value;
  if (!token) return [];
  const r = await fetch(`${serviceUrl('core')}/documents`, {
    headers: { authorization: `Bearer ${token}` },
    cache: 'no-store'
  });
  if (!r.ok) return [];
  return r.json();
}

async function createDoc(formData: FormData) {
  'use server';
  const token = cookies().get('tenantops_token')?.value;
  if (!token) redirect('/login');
  const title = String(formData.get('title') ?? '').trim();

  const file = formData.get('file') as File | null;
  let content: string;
  if (file && file.size > 0) {
    content = await file.text();
  } else {
    content = String(formData.get('content') ?? '').trim();
  }

  if (!title || !content) return;
  await fetch(`${serviceUrl('core')}/documents`, {
    method: 'POST',
    headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' },
    body: JSON.stringify({ title, content })
  });
  revalidatePath(`/t/${cookies().get('tenant_slug')?.value ?? ''}/kb`);
}

async function reindex() {
  'use server';
  const token = cookies().get('tenantops_token')?.value;
  if (!token) redirect('/login');
  await fetch(`${serviceUrl('ai')}/documents/index`, {
    method: 'POST',
    headers: { authorization: `Bearer ${token}` }
  });
  revalidatePath(`/t/${cookies().get('tenant_slug')?.value ?? ''}/kb`);
}

export default async function KbPage() {
  const tenant = await getCurrentTenant();
  if (!tenant) notFound();
  const docs = await fetchDocs();
  const isSignedIn = !!cookies().get('tenantops_token')?.value;

  return (
    <div>
      <h1>Knowledge Base — {tenant.name}</h1>

      {!isSignedIn && (
        <div className="card" style={{ borderColor: '#f08a24' }}>
          <p style={{ margin: 0 }}>
            <a href="/login">Sign in</a> to add or manage documents.
          </p>
        </div>
      )}

      {isSignedIn && (
        <div className="card">
          <h3>Upload a document</h3>
          <form action={createDoc}>
            <input name="title" placeholder="Title" required style={{ marginBottom: 8 }} />
            <label style={{ display: 'block', fontSize: 13, color: '#555', marginBottom: 4 }}>
              Upload a .txt file:
            </label>
            <input name="file" type="file" accept=".txt,text/plain" style={{ marginBottom: 8 }} />
            <label style={{ display: 'block', fontSize: 13, color: '#555', marginBottom: 4 }}>
              Or paste content:
            </label>
            <textarea name="content" placeholder="Paste document content here" />
            <div style={{ marginTop: 8 }}><button type="submit">Save document</button></div>
          </form>
        </div>
      )}

      <div className="card">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h3 style={{ margin: 0 }}>Documents ({docs.length})</h3>
          {isSignedIn && (
            <form action={reindex}><button type="submit">Re-index all</button></form>
          )}
        </div>
        <ul style={{ marginTop: 12, paddingLeft: 0, listStyle: 'none' }}>
          {docs.map(d => (
            <li key={d.documentId} style={{ padding: '8px 0', borderBottom: '1px solid #eee' }}>
              <strong>{d.title}</strong>
              <span className="badge" style={{ marginLeft: 8 }}>
                {d.indexedAt ? 'indexed' : 'not indexed'}
              </span>
              <br />
              <small style={{ color: '#888' }}>{new Date(d.createdAt).toLocaleString()}</small>
            </li>
          ))}
          {docs.length === 0 && (
            <li><em>{isSignedIn ? 'No documents yet.' : 'Sign in to see documents.'}</em></li>
          )}
        </ul>
      </div>
    </div>
  );
}
