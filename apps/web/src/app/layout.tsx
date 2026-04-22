import type { Metadata } from 'next';
import { getCurrentTenant } from '@/lib/currentTenant';
import './globals.css';

export const metadata: Metadata = {
  title: 'TenantOps',
  description: 'Multi-tenant SaaS demo on Azure'
};

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const tenant = await getCurrentTenant();
  const theme = tenant?.theme ?? { primary: '#0b5cad', accent: '#f08a24', logo: '/logos/default.svg' };

  return (
    <html lang="en">
      <body style={{ ['--tenant-primary' as any]: theme.primary, ['--tenant-accent' as any]: theme.accent }}>
        <header className="site-header">
          <div className="brand">
            <strong>{tenant?.name ?? 'TenantOps'}</strong>
            <span className="slug">{tenant ? `@${tenant.slug}` : 'select a tenant'}</span>
          </div>
          <nav>
            <a href={tenant ? `/t/${tenant.slug}/kb` : '/'}>Knowledge Base</a>
            <a href={tenant ? `/t/${tenant.slug}/tickets` : '/'}>Tickets</a>
            <a href={tenant ? `/t/${tenant.slug}/chat` : '/'}>Chat</a>
            <a href="/admin">Admin</a>
          </nav>
        </header>
        <main className="container">{children}</main>
        <footer className="site-footer">
          TenantOps · tenant-id: <code>{tenant?.tenantId ?? '(none)'}</code>
        </footer>
      </body>
    </html>
  );
}
