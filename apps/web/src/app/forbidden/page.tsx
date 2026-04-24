export default function ForbiddenPage({
  searchParams,
}: {
  searchParams: { returnTo?: string; tenant?: string };
}) {
  const returnTo = searchParams.returnTo ?? '/';
  const tenantName = searchParams.tenant ?? 'that tenant';

  return (
    <div style={{ minHeight: '75vh', display: 'flex', flexDirection: 'column',
                  alignItems: 'center', justifyContent: 'center', padding: '40px 24px',
                  textAlign: 'center' }}>

      <div className="pixel-box" style={{ maxWidth: 560, width: '100%' }}>
        {/* Glitch header */}
        <p style={{ fontSize: 11, color: 'var(--purple)', letterSpacing: 3,
                    textTransform: 'uppercase', marginBottom: 8 }}>
          !! SYSTEM ALERT !!
        </p>

        <h1 style={{ fontSize: 'clamp(1.6rem, 6vw, 3rem)', color: 'var(--purple)',
                     textShadow: 'var(--glow-purple)', letterSpacing: 6, marginBottom: 4 }}>
          403
        </h1>
        <h2 style={{ fontSize: '1rem', marginBottom: 20 }}>ACCESS DENIED</h2>

        <pre style={{ fontSize: 11, color: 'var(--muted)', lineHeight: 1.4, marginBottom: 24 }}>{
`╔════════════════════════════════════╗
║  SECURITY VIOLATION DETECTED       ║
║  You do not have clearance to      ║
║  access [ ${tenantName.toUpperCase().padEnd(22)} ]  ║
╚════════════════════════════════════╝`
        }</pre>

        <p style={{ color: 'var(--muted)', fontSize: 12, marginBottom: 28 }}>
          Your credentials only grant access to your own workspace.<span className="cursor"> █</span>
        </p>

        <a href={returnTo} className="btn" style={{ display: 'inline-block' }}>
          ▶ RETURN TO YOUR WORKSPACE
        </a>
      </div>

      <p style={{ marginTop: 24, fontSize: 10, color: 'var(--muted)', letterSpacing: 2,
                  textTransform: 'uppercase' }}>
        TENANTOPS SECURITY v1.0 — INCIDENT LOGGED
      </p>
    </div>
  );
}
