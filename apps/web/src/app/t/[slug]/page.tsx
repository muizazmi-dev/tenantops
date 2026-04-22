import { getCurrentTenant } from '@/lib/currentTenant';
import { notFound } from 'next/navigation';

export default async function TenantHome({ params }: { params: { slug: string } }) {
  const tenant = await getCurrentTenant();
  if (!tenant) notFound();
  return (
    <div>
      <h1>Welcome, {tenant.name}</h1>
      <p>Your TenantOps workspace. All data below is isolated to tenant <code>{tenant.tenantId}</code>.</p>
      <div className="row">
        <a className="card" href={`/t/${tenant.slug}/kb`}>📚 Knowledge Base</a>
        <a className="card" href={`/t/${tenant.slug}/tickets`}>🎫 Tickets</a>
        <a className="card" href={`/t/${tenant.slug}/chat`}>💬 Chat with your data</a>
      </div>
    </div>
  );
}
