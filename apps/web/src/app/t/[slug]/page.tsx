import { cookies } from 'next/headers';
import { getCurrentTenant } from '@/lib/currentTenant';
import { notFound } from 'next/navigation';

export default async function TenantHome({ params }: { params: { slug: string } }) {
  const tenant = await getCurrentTenant();
  if (!tenant) notFound();

  const isSignedIn = !!cookies().get('tenantops_token')?.value;

  return (
    <div>
      <h1>{tenant.name}</h1>

      {/* Tenant identity panel */}
      <div className="card" style={{ display: 'flex', gap: 16, alignItems: 'flex-start', flexWrap: 'wrap' }}>
        {tenant.theme.logo && !tenant.theme.logo.startsWith('/logos/') && (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={tenant.theme.logo} alt={`${tenant.name} logo`}
               style={{ height: 72, width: 72, objectFit: 'contain', borderRadius: 8,
                        border: '1px solid #e2e2e2', padding: 4, flexShrink: 0 }} />
        )}
        <div>
          <p style={{ margin: '0 0 4px' }}><strong>{tenant.name}</strong></p>
          <p style={{ margin: '0 0 4px', fontSize: 13, color: '#555' }}>
            Slug: <code>{tenant.slug}</code>
          </p>
          <p style={{ margin: 0, fontSize: 13, color: '#555' }}>
            Tenant ID:{' '}
            <code style={{ fontSize: 12, wordBreak: 'break-all' }}>{tenant.tenantId}</code>
            <span style={{ marginLeft: 6, color: '#aaa', fontSize: 11 }}>unique across all tenants</span>
          </p>
        </div>
      </div>

      {!isSignedIn && (
        <div className="card" style={{ borderColor: 'var(--tenant-accent, #f08a24)', borderWidth: 2 }}>
          <p style={{ margin: 0 }}>
            <a href="/login">Sign in</a> to access your workspace,
            or <a href="/signup">create a user account</a> under this tenant.
          </p>
        </div>
      )}

      <div className="row" style={{ marginTop: 16 }}>
        <a className="card" href={`/t/${tenant.slug}/kb`}>
          <strong>Knowledge Base</strong>
          <p style={{ margin: '4px 0 0', fontSize: 13, color: '#888' }}>Upload and manage tenant documents</p>
        </a>
        <a className="card" href={`/t/${tenant.slug}/tickets`}>
          <strong>Tickets</strong>
          <p style={{ margin: '4px 0 0', fontSize: 13, color: '#888' }}>Create and track support requests</p>
        </a>
        <a className="card" href={`/t/${tenant.slug}/chat`}>
          <strong>AI Chat</strong>
          <p style={{ margin: '4px 0 0', fontSize: 13, color: '#888' }}>Chat grounded in your documents</p>
        </a>
      </div>

      {isSignedIn && (
        <p style={{ marginTop: 16, fontSize: 13 }}>
          <a href={`/t/${tenant.slug}/settings`}>Settings</a> — upload your logo or view tenant details.
        </p>
      )}
    </div>
  );
}
