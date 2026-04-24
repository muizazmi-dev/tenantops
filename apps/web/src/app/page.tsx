export default function Home() {
  return (
    <div style={{ minHeight: '80vh', display: 'flex', flexDirection: 'column',
                  alignItems: 'center', justifyContent: 'center', padding: '40px 24px' }}>

      {/* ── Hero box ── */}
      <div className="pixel-box" style={{ maxWidth: 640, width: '100%', textAlign: 'center' }}>

        {/* ASCII corner decoration */}
        <pre style={{ fontSize: 10, lineHeight: 1.2, color: 'var(--purple)',
                      marginBottom: 20, userSelect: 'none', letterSpacing: 0 }}>{
`╔══╗ ╔══╗ ╔═╗ ╔══╗ ╔═╗ ╔══╗ ╔══╗ ╔══╗
║    ╠══╣ ║ ║ ╠══╝ ║ ║ ╠══╝ ╠══╣ ╠══╝
╚══╝ ╩  ╩ ╚═╝ ╩   ╚═╝ ╩   ╩  ╩ ╩  `
        }</pre>

        <h1 className="glow-pulse" style={{ fontSize: 'clamp(1rem, 4vw, 1.6rem)',
                                            letterSpacing: 4, marginBottom: 8 }}>
          MULTITENANT SAAS DEMO
        </h1>
        <p style={{ color: 'var(--purple)', fontSize: 13, letterSpacing: 3,
                    textTransform: 'uppercase', marginBottom: 4 }}>
          — by Muiz —
        </p>

        <p style={{ color: 'var(--muted)', fontSize: 11, letterSpacing: 2,
                    marginBottom: 32, textTransform: 'uppercase' }}>
          ▶ &nbsp;INSERT COIN TO CONTINUE&nbsp; ◀&nbsp;<span className="cursor">█</span>
        </p>

        {/* CTA buttons */}
        <div style={{ display: 'flex', gap: 16, justifyContent: 'center', flexWrap: 'wrap' }}>
          <a href="/login" className="btn" style={{ minWidth: 180, textAlign: 'center' }}>
            [ SIGN IN ]
          </a>
          <a href="/tenant-signup" className="btn btn-purple"
             style={{ minWidth: 180, textAlign: 'center' }}>
            [ SIGN UP ]
          </a>
        </div>

        <hr style={{ marginTop: 32 }} />

        {/* Sub-links */}
        <p style={{ fontSize: 15, color: 'var(--muted)', marginTop: 16, marginBottom: 0 }}>
          <a href="/admin/login" style={{ color: 'var(--muted)', fontSize: 11 }}>
            Platform admin
          </a>
          &nbsp;·&nbsp;
          <a href="/signup" style={{ color: 'var(--muted)', fontSize: 11 }}>
            Add user to existing tenant
          </a>
        </p>
      </div>

      {/* Bottom flavour text */}
      <p style={{ marginTop: 32, fontSize: 15, color: 'var(--muted)', letterSpacing: 2,
                  textTransform: 'uppercase', textAlign: 'center' }}>
        © 1994 TENANTOPS INDUSTRIES &nbsp;·&nbsp; ALL RIGHTS RESERVED &nbsp;·&nbsp;
        PRESS START
      </p>
    </div>
  );
}
