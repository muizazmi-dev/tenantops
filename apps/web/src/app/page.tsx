export default function Home() {
  return (
    <div>
      <h1>TenantOps</h1>
      <p>Multi-tenant SaaS demo on Azure. Pick a tenant to enter:</p>
      <div className="row">
        <a className="card" href="/t/contoso">
          <strong>Contoso HVAC</strong><br />
          <span>/t/contoso</span>
        </a>
        <a className="card" href="/t/fabrikam">
          <strong>Fabrikam Services</strong><br />
          <span>/t/fabrikam</span>
        </a>
      </div>
      <p style={{ marginTop: 24 }}>
        For custom-domain routing, add entries like
        <code> 127.0.0.1 contoso.localtest.me</code> to your hosts file.
      </p>
    </div>
  );
}
