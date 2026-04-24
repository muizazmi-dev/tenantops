import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { serviceUrl } from '@/lib/api';
import { decodeJwt, isPlatformAdmin } from '@/lib/jwt';

type TenantRow = {
  tenantId: string; name: string; slug: string; domain: string | null;
};

export default async function AdminPage() {
  const token = cookies().get('tenantops_token')?.value;

  if (!token || !isPlatformAdmin(decodeJwt(token))) {
    redirect('/admin/login');
  }

  let tenants: TenantRow[] = [];
  let err: string | null = null;

  const r = await fetch(`${serviceUrl('tenant')}/admin/tenants`, {
    headers: { authorization: `Bearer ${token}` },
    cache: 'no-store'
  });
  if (r.status === 403) err = 'platform-admin claim not present on this token.';
  else if (!r.ok)       err = `tenant-api returned ${r.status}`;
  else                  tenants = await r.json();

  // Filter out the hidden platform tenant from the UI list
  const visible = tenants.filter(t => t.slug !== 'platform');

  return (
    <div>
      <p style={{ fontSize: 10, color: 'var(--purple)', letterSpacing: 3,
                  textTransform: 'uppercase', marginBottom: 8 }}>
        PLATFORM ADMIN CONSOLE
      </p>
      <h1>ALL TENANTS</h1>
      <p style={{ color: 'var(--muted)', fontSize: 12, marginBottom: 24 }}>
        {visible.length} tenant{visible.length !== 1 ? 's' : ''} registered.
      </p>

      {err && (
        <div className="card" style={{ borderColor: 'var(--purple)', color: 'var(--purple)' }}>
          !! ERROR: {err}
        </div>
      )}

      {visible.map(t => (
        <div key={t.tenantId} className="card">
          <div style={{ display: 'flex', justifyContent: 'space-between',
                        alignItems: 'flex-start', flexWrap: 'wrap', gap: 8 }}>
            <div>
              <strong style={{ color: 'var(--green)', fontSize: 15 }}>{t.name}</strong>
              <span style={{ marginLeft: 10, fontSize: 11, color: 'var(--purple)',
                             textTransform: 'uppercase', letterSpacing: 1 }}>
                {t.slug}
              </span>
            </div>
            <a href={`/t/${t.slug}`} className="btn"
               style={{ fontSize: 10, padding: '4px 12px' }}>
              VIEW →
            </a>
          </div>
          <div style={{ marginTop: 8, fontSize: 12 }}>
            <span style={{ color: 'var(--muted)' }}>TENANT ID: </span>
            <code>{t.tenantId}</code>
          </div>
          {t.domain && (
            <div style={{ marginTop: 4, fontSize: 12 }}>
              <span style={{ color: 'var(--muted)' }}>DOMAIN: </span>
              <code style={{ borderColor: 'var(--purple)', color: 'var(--purple)' }}>
                {t.domain}
              </code>
            </div>
          )}
        </div>
      ))}

      {visible.length === 0 && !err && (
        <div className="card" style={{ color: 'var(--muted)', textAlign: 'center' }}>
          NO TENANTS FOUND
        </div>
      )}
    </div>
  );
}
