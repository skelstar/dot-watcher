const TESTFLIGHT_URL = 'https://testflight.apple.com/join/1bhK7eG1'

export default function LandingPage() {
  return (
    <main style={page}>
      <style>{responsiveCss}</style>
      <div className="dw-wrap" style={wrap}>
        <nav className="dw-nav" style={navRow}>
          <div style={brand}>
            <img src="/landing/assets/app-icon.png" alt="" style={brandIcon} />
            dot-watchr
          </div>
          <div className="dw-nav-links" style={navLinks}>
            <a href="#dw-how" style={navLink}>How it works</a>
            <a href="#dw-features" style={navLink}>Features</a>
            <a href="#dw-faq" style={navLink}>FAQ</a>
          </div>
          <a href={TESTFLIGHT_URL} target="_blank" rel="noopener" style={navCta}>Join the beta</a>
        </nav>

        <section className="dw-hero" style={hero}>
          <div>
            <div style={heroBadgeRow}>
              <img src="/landing/assets/app-icon.png" alt="" style={heroIcon} />
              <div style={betaPill}>
                <span style={betaDot} /> In TestFlight now
              </div>
            </div>
            <h1 className="dw-hero-headline" style={headline}>Your run group,<br />one shared dot each.</h1>
            <p style={subhead}>
              Dot-watchr keeps every runner visible on one live map — so supporters know where to cheer, and nobody gets left behind.
            </p>
            <div style={heroCtaRow}>
              <a href={TESTFLIGHT_URL} target="_blank" rel="noopener" style={heroCta}>Join the beta</a>
            </div>
          </div>

          <div className="dw-phones" style={phones}>
            <div className="dw-phone-a" style={phoneA}>
              <img src="/landing/assets/dw-sending-screenshot.png" alt="Sending location in the Dot Watcher iOS app" style={phoneAImg} />
            </div>
            <div className="dw-phone-b" style={phoneB}>
              <video src="/landing/uploads/dw-sending-demo.mp4" style={phoneBVideo} autoPlay loop muted playsInline preload="auto" controls />
            </div>
          </div>
        </section>

        <section id="dw-how" className="dw-section" style={sectionStyle}>
          <h2 style={eyebrowHeading}>How it works</h2>
          <div style={stepList}>
            <Step n={1} title="Sign in and create a session" body="Give it a name — you'll get a 6-character invite code instantly." />
            <Step n={2} title="Send the code to your group" body="Runners join from the app, supporters just open the link in a browser." />
            <Step n={3} title="Start tracking, watch it live" body="Every runner shows up as a dot with heading, updating every few seconds." last />
          </div>
        </section>

        <section id="dw-features" className="dw-features" style={features}>
          <Feature color="#3B82F6" title="Live map for everyone" body="Named dots, auto-framed bounds — supporters don't need the app to watch." />
          <Feature color="#22C55E" title="Real compass headings" body="Each dot's arrow points where its runner is actually facing." />
          <Feature color="#F59E0B" title="One code, easy invites" body="Share it once — rejoin anytime without asking again." />
        </section>

        <section id="dw-faq" className="dw-faq" style={faq}>
          <h2 style={eyebrowHeading}>FAQ</h2>
          <div style={stepList}>
            <FaqItem q="Is dot-watchr only for iPhone?" a="Right now, yes — runners join from an iPhone. Anyone watching a session, though, can do it from any browser, no app needed." />
            <FaqItem q="How accurate is the location?" a="It uses your phone's GPS, updated every few seconds. Like any GPS app, accuracy can vary with weather, buildings, or signal." />
            <FaqItem q="How many people can join a session?" a="Sessions support small groups comfortably today — great for a run club outing or a race-day crew." />
            <FaqItem q="Is it free?" a="Yes, dot-watchr is free to use during the TestFlight beta." last />
          </div>
        </section>

        <footer style={footer}>
          <span style={footerText}>© 2026 Sean Kelly</span>
          <a href="mailto:dotwatchr@skelstar.io" style={footerText}>dotwatchr@skelstar.io</a>
        </footer>
      </div>
    </main>
  )
}

function Step({ n, title, body, last }: { n: number; title: string; body: string; last?: boolean }) {
  return (
    <div style={{ ...stepRow, marginBottom: last ? 0 : 10 }}>
      <div style={stepBadge}>{n}</div>
      <div>
        <div style={stepTitle}>{title}</div>
        <div style={stepBody}>{body}</div>
      </div>
    </div>
  )
}

function Feature({ color, title, body }: { color: string; title: string; body: string }) {
  return (
    <div style={featureCard}>
      <div style={{ height: 5, background: color }} />
      <div style={featureBody}>
        <div style={featureTitle}>{title}</div>
        <div style={featureText}>{body}</div>
      </div>
    </div>
  )
}

function FaqItem({ q, a, last }: { q: string; a: string; last?: boolean }) {
  return (
    <div style={{ ...faqCard, marginBottom: last ? 0 : 10 }}>
      <div style={faqQuestion}>{q}</div>
      <div style={faqAnswer}>{a}</div>
    </div>
  )
}

const responsiveCss = `
  .dw-phones { max-width: 100%; }
  @media (max-width: 980px) {
    .dw-nav { flex-wrap:wrap; padding:22px 28px 0 !important; }
    .dw-nav-links { order:3; width:100%; justify-content:center; padding-top:14px; }
    .dw-hero { grid-template-columns:1fr !important; padding:36px 28px 12px !important; }
    .dw-hero-headline { font-size:36px !important; }
    .dw-phones { margin-top:32px; }
    .dw-section { padding:8px 28px !important; }
    .dw-features { padding:28px 28px 44px !important; grid-template-columns:1fr !important; }
    .dw-faq { padding:0 28px 44px !important; }
  }
  @media (max-width: 520px) {
    .dw-hero-headline { font-size:29px !important; letter-spacing:-0.5px; }
  }
`

const page: React.CSSProperties = {
  height: '100%',
  overflowY: 'auto',
  WebkitOverflowScrolling: 'touch',
  background: '#F0F1F5',
  fontFamily: '-apple-system, BlinkMacSystemFont, "SF Pro Text", system-ui, sans-serif',
}

const wrap: React.CSSProperties = {
  maxWidth: 1340,
  margin: '0 auto',
}

const navRow: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  padding: '26px 48px 0',
  gap: 16,
}

const brand: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  fontSize: 20,
  fontWeight: 800,
  color: '#14151A',
  letterSpacing: '-0.5px',
  whiteSpace: 'nowrap',
  flex: 'none',
}

const brandIcon: React.CSSProperties = {
  width: 26,
  height: 26,
  borderRadius: 7,
  boxShadow: '0 1px 3px rgba(20,21,26,.2)',
}

const navLinks: React.CSSProperties = {
  display: 'flex',
  gap: 26,
  fontSize: 14.5,
  fontWeight: 600,
  color: '#4B4D57',
}

const navLink: React.CSSProperties = {
  color: 'inherit',
  textDecoration: 'none',
}

const navCta: React.CSSProperties = {
  textDecoration: 'none',
  padding: '10px 20px',
  borderRadius: 999,
  background: '#14151A',
  color: '#fff',
  fontSize: 13.5,
  fontWeight: 700,
  flex: 'none',
}

const hero: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: '1fr 1.15fr',
  gap: 24,
  padding: '48px 48px 24px',
  alignItems: 'center',
}

const heroBadgeRow: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 12,
  marginBottom: 20,
  flexWrap: 'wrap',
}

const heroIcon: React.CSSProperties = {
  width: 44,
  height: 44,
  borderRadius: 11,
  boxShadow: '0 3px 10px rgba(20,21,26,.2)',
}

const betaPill: React.CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 6,
  padding: '6px 14px',
  borderRadius: 999,
  background: '#fff',
  fontSize: 12.5,
  fontWeight: 700,
  color: '#F59E0B',
  whiteSpace: 'nowrap',
}

const betaDot: React.CSSProperties = {
  width: 7,
  height: 7,
  borderRadius: '50%',
  background: '#F59E0B',
  flex: 'none',
}

const headline: React.CSSProperties = {
  margin: '0 0 18px',
  lineHeight: 1.1,
  fontWeight: 800,
  color: '#14151A',
  letterSpacing: '-1px',
  fontSize: 46,
}

const subhead: React.CSSProperties = {
  margin: '0 0 28px',
  fontSize: 17,
  lineHeight: 1.55,
  color: '#5B5D68',
  maxWidth: 440,
}

const heroCtaRow: React.CSSProperties = {
  display: 'flex',
  gap: 14,
  marginBottom: 30,
}

const heroCta: React.CSSProperties = {
  textDecoration: 'none',
  padding: '15px 28px',
  borderRadius: 999,
  background: '#22C55E',
  color: '#fff',
  fontSize: 15,
  fontWeight: 700,
  boxShadow: '0 8px 20px rgba(34,197,94,.3)',
}

const phones: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'center',
  alignItems: 'flex-start',
}

const phoneA: React.CSSProperties = {
  width: '58%',
  maxWidth: 242,
  boxShadow: '0 20px 60px rgba(20,21,26,.35)',
  borderRadius: 34,
}

const phoneAImg: React.CSSProperties = {
  width: '100%',
  borderRadius: 34,
  display: 'block',
}

const phoneB: React.CSSProperties = {
  width: '58%',
  maxWidth: 242,
  marginLeft: '-24%',
  marginTop: 60,
  position: 'relative',
  zIndex: 1,
  borderRadius: 34,
  overflow: 'hidden',
  boxShadow: '0 20px 60px rgba(20,21,26,.35)',
}

const phoneBVideo: React.CSSProperties = {
  width: '100%',
  display: 'block',
}

const sectionStyle: React.CSSProperties = {
  padding: '8px 48px 8px',
}

const eyebrowHeading: React.CSSProperties = {
  fontSize: 14,
  fontWeight: 800,
  letterSpacing: '1.5px',
  textTransform: 'uppercase',
  color: '#8B8D97',
  margin: '0 0 22px',
}

const stepList: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 2,
}

const stepRow: React.CSSProperties = {
  display: 'flex',
  gap: 18,
  alignItems: 'flex-start',
  background: '#fff',
  borderRadius: 14,
  padding: '20px 22px',
}

const stepBadge: React.CSSProperties = {
  width: 30,
  height: 30,
  borderRadius: '50%',
  background: '#3B82F6',
  color: '#fff',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  fontWeight: 800,
  fontSize: 13,
  flex: 'none',
}

const stepTitle: React.CSSProperties = {
  fontSize: 15.5,
  fontWeight: 700,
  color: '#14151A',
  marginBottom: 3,
}

const stepBody: React.CSSProperties = {
  fontSize: 13.5,
  color: '#6B6D77',
}

const features: React.CSSProperties = {
  padding: '36px 48px 56px',
  display: 'grid',
  gridTemplateColumns: 'repeat(3, 1fr)',
  gap: 20,
}

const featureCard: React.CSSProperties = {
  background: '#fff',
  borderRadius: 16,
  overflow: 'hidden',
}

const featureBody: React.CSSProperties = {
  padding: 22,
}

const featureTitle: React.CSSProperties = {
  fontSize: 15.5,
  fontWeight: 700,
  color: '#14151A',
  marginBottom: 6,
}

const featureText: React.CSSProperties = {
  fontSize: 13.5,
  color: '#6B6D77',
  lineHeight: 1.5,
}

const faq: React.CSSProperties = {
  padding: '0 48px 56px',
}

const faqCard: React.CSSProperties = {
  background: '#fff',
  borderRadius: 14,
  padding: '20px 22px',
}

const faqQuestion: React.CSSProperties = {
  fontSize: 15.5,
  fontWeight: 700,
  color: '#14151A',
  marginBottom: 6,
}

const faqAnswer: React.CSSProperties = {
  fontSize: 13.5,
  lineHeight: 1.5,
  color: '#6B6D77',
}

const footer: React.CSSProperties = {
  borderTop: '1px solid rgba(20,21,26,.08)',
  padding: '20px 48px',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
}

const footerText: React.CSSProperties = {
  fontSize: 13,
  color: '#8B8D97',
  textDecoration: 'none',
}
