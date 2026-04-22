import { apiFetch, serviceUrl } from '@/lib/api';
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
  if (!token) redirect(`/login`);
  const title = String(formData.get('title') ?? '').trim();
  const content = String(formData.get('content') ?? '').trim();
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
  if (!token) redirect(`/login`);
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

  return (
    <div>
      <h1>Knowledge Base — {tenant.name}</h1>

      <div className="card">
        <h3>Add a document</h3>
        <form action={createDoc}>
          <input name="title" placeholder="Title" required />
          <textarea name="content" placeholder="Content" required style={{ marginTop: 8 }} />
          <div style={{ marginTop: 8 }}><button type="submit">Save</button></div>
        </form>
      </div>

      <div className="card">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h3 style={{ margin: 0 }}>Documents ({docs.length})</h3>
          <form action={reindex}><button type="submit">Re-index all</button></form>
        </div>
        <ul>
          {docs.map(d => (
            <li key={d.documentId}>
              <strong>{d.title}</strong>
              <span className="badge" style={{ marginLeft: 8 }}>
                {d.indexedAt ? 'indexed' : 'not indexed'}
              </span>
            </li>
          ))}
          {docs.length === 0 && <li><em>No documents yet. You may need to sign in.</em></li>}
        </ul>
      </div>
    </div>
  );
}
