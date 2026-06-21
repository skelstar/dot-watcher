type LegalPageKind = 'privacy' | 'terms'

interface Props {
  kind: LegalPageKind
}

const lastUpdated = '20 Jun 2026'
const contactEmail = 'support@dot-watcher.skelstar.io'

export default function LegalPage({ kind }: Props) {
  const page = kind === 'privacy' ? privacyPage : termsPage

  return (
    <main style={pageShell}>
      <nav style={topNav}>
        <a href="/" style={brandLink}>Dot Watcher</a>
        <span style={navLinks}>
          <a href="/privacy" style={navLink}>Privacy</a>
          <a href="/terms" style={navLink}>Terms</a>
        </span>
      </nav>
      <article style={content}>
        <p style={eyebrow}>Last updated {lastUpdated}</p>
        <h1 style={title}>{page.title}</h1>
        {page.sections.map(section => (
          <section key={section.heading} style={sectionBlock}>
            <h2 style={heading}>{section.heading}</h2>
            {section.paragraphs.map(paragraph => (
              <p key={paragraph} style={paragraphStyle}>{paragraph}</p>
            ))}
          </section>
        ))}
      </article>
    </main>
  )
}

const privacyPage = {
  title: 'Privacy Policy',
  sections: [
    {
      heading: 'Overview',
      paragraphs: [
        'Dot Watcher helps people share live location during a run, race, or similar group activity. The service is currently a beta and may change as we learn from early use.',
        'This policy explains what data Dot Watcher collects, why it is collected, and how it is used. It is intended to be clear about precise location data because that is the core purpose of the app.',
      ],
    },
    {
      heading: 'Data We Collect',
      paragraphs: [
        'Account data: username, display name, password hash, access token metadata, logout revocation records, and timestamps related to account activity.',
        'Session data: session codes, invite codes, membership records, roles such as owner, runner, and viewer, and display names used inside a session.',
        'Location data: precise latitude and longitude, heading when available, GPS capture timestamp, session code, and the display name associated with the authenticated session member.',
        'Operational data: server logs may include session codes, display names, request outcomes, and precise location details needed to debug tracking problems.',
      ],
    },
    {
      heading: 'How We Use Data',
      paragraphs: [
        'We use account and membership data to authenticate users and decide who can read or write locations for a session.',
        'We use location data to show live runner positions, provide participant lists, and support replay/recording features for sessions where the user is a member.',
        'We use operational data to diagnose bugs, investigate abuse, improve reliability, and understand whether the beta service is working as expected.',
      ],
    },
    {
      heading: 'Sharing and Visibility',
      paragraphs: [
        'Precise location data is visible to authenticated users who are members of the same session. Owners can invite other users and can promote trusted viewers to runners.',
        'Invite links are onboarding mechanisms, not long-term credentials. Access after joining is based on stored membership and user authentication.',
        'We do not sell location data. We may disclose data if required by law or if necessary to protect users, the service, or others.',
      ],
    },
    {
      heading: 'Retention and Deletion',
      paragraphs: [
        'Live in-memory location state may be cleared when the server restarts or when an admin clears a session.',
        'You can delete your account from account settings. Account deletion removes your account, memberships, sessions you own, and stored location rows linked to your authenticated account. Other active tokens for the deleted account stop working after deletion.',
        `Historical records without account attribution, admin-uploaded recordings, or operational logs may not be linked to your account. For deletion help with those records, contact ${contactEmail} with enough detail to identify the account or session.`,
      ],
    },
    {
      heading: 'Your Choices',
      paragraphs: [
        'You can stop sharing location by stopping tracking in the iOS app, leaving the app signed out, or revoking location permission in iOS settings.',
        'You should only join, create, or share sessions with people who understand that precise location data will be visible to session members.',
      ],
    },
    {
      heading: 'Contact',
      paragraphs: [
        `Questions or privacy requests can be sent to ${contactEmail}.`,
      ],
    },
  ],
}

const termsPage = {
  title: 'Terms of Use',
  sections: [
    {
      heading: 'Beta Service',
      paragraphs: [
        'Dot Watcher is provided as a beta service. Features may change, break, be unavailable, or lose data. Use the service at your own risk.',
        'The app is not intended for emergency response, rescue, medical, safety-critical tracking, or any situation where delayed, inaccurate, or missing location data could cause harm.',
      ],
    },
    {
      heading: 'Location Accuracy and Availability',
      paragraphs: [
        'Location data can be delayed, inaccurate, incomplete, or unavailable because of GPS limitations, device settings, battery state, network coverage, server issues, or app bugs.',
        'You are responsible for using appropriate backup safety plans for races, runs, events, and travel. Do not rely on Dot Watcher as your only way to locate someone.',
      ],
    },
    {
      heading: 'Accounts and Sessions',
      paragraphs: [
        'You are responsible for your account, password, session invitations, and the people you invite into a session.',
        'Only create, join, share, or track in sessions where everyone involved has consented to the intended location sharing.',
        'Owners can manage member roles. Invite codes do not grant runner or owner privileges by themselves.',
      ],
    },
    {
      heading: 'Acceptable Use',
      paragraphs: [
        'Do not use Dot Watcher to track people without consent, harass others, scrape data, attack the service, or interfere with other users.',
        'Do not share invite links publicly unless you accept that anyone with the link may be able to request membership in the session.',
      ],
    },
    {
      heading: 'No Warranty',
      paragraphs: [
        'Dot Watcher is provided as is and as available, without warranties of any kind. We do not guarantee accuracy, uptime, data retention, or fitness for a particular purpose.',
        'To the maximum extent permitted by law, we are not liable for losses or damages arising from use of the beta service.',
      ],
    },
    {
      heading: 'Changes',
      paragraphs: [
        'We may update these terms as the beta evolves. Continued use of Dot Watcher after an update means you accept the updated terms.',
        `Questions can be sent to ${contactEmail}.`,
      ],
    },
  ],
}

const pageShell: React.CSSProperties = {
  minHeight: '100%',
  overflowY: 'auto',
  background: '#f6f8fa',
  color: '#24292f',
  fontFamily: 'system-ui, sans-serif',
}

const topNav: React.CSSProperties = {
  position: 'sticky',
  top: 0,
  zIndex: 1,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 16,
  padding: '12px 18px',
  background: '#fff',
  boxShadow: '0 1px 0 rgba(0,0,0,0.08)',
}

const brandLink: React.CSSProperties = {
  color: '#24292f',
  textDecoration: 'none',
  fontWeight: 700,
}

const navLinks: React.CSSProperties = {
  display: 'flex',
  gap: 12,
}

const navLink: React.CSSProperties = {
  color: '#1f6feb',
  textDecoration: 'none',
  fontSize: '0.9rem',
}

const content: React.CSSProperties = {
  width: 'min(780px, 100%)',
  margin: '0 auto',
  padding: '32px 18px 56px',
}

const eyebrow: React.CSSProperties = {
  margin: '0 0 8px',
  color: '#57606a',
  fontSize: '0.85rem',
}

const title: React.CSSProperties = {
  margin: '0 0 24px',
  fontSize: '2rem',
}

const sectionBlock: React.CSSProperties = {
  marginBottom: 24,
}

const heading: React.CSSProperties = {
  margin: '0 0 8px',
  fontSize: '1.15rem',
}

const paragraphStyle: React.CSSProperties = {
  margin: '0 0 10px',
  lineHeight: 1.55,
}
