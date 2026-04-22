import { serviceUrl } from '@/lib/api';
import { getCurrentTenant } from '@/lib/currentTenant';
import { cookies } from 'next/headers';
import { revalidatePath } from 'next/cache';
import { notFound, redirect } from 'next/navigation';

type Ticket = { ticketId: string; title: string; body: string | null; status: string; createdAt: string };

async function fetchTickets(): Promise<Ticket[]> {
  const token = cookies().get('tenantops_token')?.value;
  if (!token) return [];
  const r = await fetch(`${serviceUrl('core')}/tickets`, {
    headers: { authorization: `Bearer ${token}` },
    cache: 'no-store'
  });
  if (!r.ok) return [];
  return r.json();
}

async function createTicket(formData: FormData) {
  'use server';
  const token = cookies().get('tenantops_token')?.value;
  if (!token) redirect('/login');
  const title = String(formData.get('title') ?? '').trim();
  const body = String(formData.get('body') ?? '').trim();
  if (!title) return;
  await fetch(`${serviceUrl('core')}/tickets`, {
    method: 'POST',
    headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' },
    body: JSON.stringify({ title, body })
  });
  revalidatePath(`/t/${cookies().get('tenant_slug')?.value ?? ''}/tickets`);
}

export default async function TicketsPage() {
  const tenant = await getCurrentTenant();
  if (!tenant) notFound();
  const tickets = await fetchTickets();

  return (
    <div>
      <h1>Tickets — {tenant.name}</h1>
      <div className="card">
        <h3>New ticket</h3>
        <form action={createTicket}>
          <input name="title" placeholder="Title" required />
          <textarea name="body" placeholder="Describe the issue" style={{ marginTop: 8 }} />
          <div style={{ marginTop: 8 }}><button type="submit">Create</button></div>
        </form>
      </div>
      <div className="card">
        <h3>Open tickets</h3>
        {tickets.length === 0 && <p><em>No tickets yet. You may need to sign in.</em></p>}
        {tickets.map(t => (
          <div key={t.ticketId} style={{ padding: '8px 0', borderBottom: '1px solid #eee' }}>
            <strong>{t.title}</strong>
            <span className={`badge ${t.status}`} style={{ marginLeft: 8 }}>{t.status}</span>
            {t.body && <p style={{ margin: '4px 0', color: '#555' }}>{t.body}</p>}
            <small>{new Date(t.createdAt).toLocaleString()}</small>
          </div>
        ))}
      </div>
    </div>
  );
}
