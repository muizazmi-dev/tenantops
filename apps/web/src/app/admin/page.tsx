import { cookies } from 'next/headers';
import { serviceUrl } from '@/lib/api';

export default async function AdminPage() {
  const token = cookies().get('tenantops_token')?.value;
  let tenants: any[] = [];
  let err: string | null = null;
  if (!token) {
    err = 'Sign in required (platform-admin role).';
  } else {
    const r = await fetch(`${serviceUrl('tenant')}/admin/tenants`, {
      headers: { authorization: `Bearer ${token}` },
      cache: 'no-store'
    });
    if (r.status === 403) err = 'You are not a platform-admin on this account.';
    else if (!r.ok) err = `tenant-api ${r.status}`;
    else tenants = await r.json();
  }

  return (
    <div>
      <h1>Platform Admin</h1>
      <p>Lists all tenants. Access requires the <code>platform-admin</code> role claim.</p>
      {err && <div className="card" style={{ color: '#c00' }}>{err}</div>}
      {tenants.map(t => (
        <div key={t.tenantId} className="card">
          <strong>{t.name}</strong> — <code>{t.slug}</code> — domain: <code>{t.domain ?? '(none)'}</code>
          <br /><small>{t.tenantId}</small>
        </div>
      ))}
    </div>
  );
}
