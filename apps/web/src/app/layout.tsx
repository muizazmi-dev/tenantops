import type { Metadata } from 'next';
import { cookies } from 'next/headers';
import { getCurrentTenant } from '@/lib/currentTenant';
import Script from 'next/script';
import UIOverlay from '@/components/UIOverlay';
import './globals.css';

export const metadata: Metadata = {
  title: 'TenantOps',
  description: 'Multitenant SaaS Demo — by Muiz'
};

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const tenant    = await getCurrentTenant();
  const isSignedIn = !!cookies().get('tenantops_token')?.value;
  const logoSrc   = tenant?.theme.logo || '/logos/default.svg';

  return (
    <html lang="en" data-theme="dark">
      {/* Runs synchronously before first paint — prevents flash of wrong theme */}
      <Script id="theme-init" strategy="beforeInteractive" dangerouslySetInnerHTML={{
        __html: `try{var t=localStorage.getItem('tenantops-theme');if(t)document.documentElement.setAttribute('data-theme',t)}catch(e){}`
      }} />

      <body>
        <UIOverlay />

        <header className="site-header">
          <div className="brand" style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src={logoSrc} alt={tenant?.name ?? 'TenantOps'}
                 style={{ height: 32, width: 32, objectFit: 'contain',
                          imageRendering: 'pixelated', flexShrink: 0 }} />
            <div>
              <a href={tenant ? `/t/${tenant.slug}` : '/'}>
                <strong>{tenant?.name ?? 'TENANTOPS'}</strong>
              </a>
              {tenant && (
                <div className="slug">ID: {tenant.tenantId}</div>
              )}
            </div>
          </div>

          <nav>
            {tenant && <a href={`/t/${tenant.slug}/kb`}>KB</a>}
            {tenant && <a href={`/t/${tenant.slug}/tickets`}>Tickets</a>}
            {tenant && <a href={`/t/${tenant.slug}/chat`}>Chat</a>}
            {tenant && <a href={`/t/${tenant.slug}/settings`}>Settings</a>}
            {isSignedIn
              ? <a href="/logout" style={{ color: 'var(--purple)' }}>Sign out</a>
              : <a href="/login" style={{ color: 'var(--green)' }}>Sign in</a>
            }
          </nav>
        </header>

        <main className="container">{children}</main>

        <footer className="site-footer">
          TENANTOPS &nbsp;·&nbsp;
          {tenant
            ? <>tenant: <code>{tenant.tenantId}</code></>
            : 'no tenant selected'
          }
        </footer>
      </body>
    </html>
  );
}
