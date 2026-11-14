export type LegalPageKind = 'privacy' | 'terms' | 'support'

interface Props {
  kind: LegalPageKind
}

const lastUpdated = '12 Nov 2026'
const contactEmail = 'dotwatchr@skelstar.io'

export default function LegalPage({ kind }: Props) {
  const page = { privacy: privacyPage, terms: termsPage, support: supportPage }[kind]

  return (
    <main style={pageShell}>
      <nav style={topNav}>
        <a href="/" style={brandLink}>Dot Watcher</a>
        <span style={navLinks}>
          <a href="/privacy" style={navLink}>Privacy</a>
          <a href="/terms" style={navLink}>Terms</a>
          <a href="/support" style={navLink}>Support</a>
        </span>
      </nav>
      <article style={content}>
        {kind !== 'support' && <p style={eyebrow}>Last updated {lastUpdated}</p>}
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
        'Dot Watcher helps people share live location during a run, race, or similar group activity. The service is under active development and may change as we learn from early use.',
        'This policy explains what data Dot Watcher collects, why it is collected, and how it is used. It is intended to be clear about precise location data because that is the core purpose of the app.',
      ],
    },
    {
      heading: 'Data We Collect',
      paragraphs: [
        'Account data: email address or username, display name (including the name your crew sees), password hash, access token metadata, logout revocation records, and timestamps related to account activity.',
        'Session data: session codes, invite codes, membership records (including your role — runner or viewer — in each session), which sessions you own, and display names used inside a session.',
        'Crew and visibility data: who is in your crew, and your "Who can see me" choices for runs started with Go.',
        'Location data: precise latitude and longitude, heading when available, GPS capture timestamp, session code, and the display name associated with the authenticated session member.',
        'Device status sent with each location update: battery level (iOS, when available), whether the phone is on a very limited connection such as satellite, and when the app expects to send its next update.',
        'Routes: GPX route files you choose to add to a session or a run.',
        'Operational data: server logs may include session codes, display names, request outcomes, and precise location details needed to debug tracking problems.',
      ],
    },
    {
      heading: 'How We Use Data',
      paragraphs: [
        'We use account and membership data to authenticate users and decide who can read or write locations for a session.',
        'We use location data to show live runner positions, provide participant lists, and support replay/recording features for sessions where the user is a member.',
        'We use device status to show the people who can see you how much battery your phone has left, and to tell a slow but working connection apart from one that has stopped.',
        'We use routes to draw the planned course on the map for the people who can see your session or run.',
        'We use operational data to diagnose bugs, investigate abuse, improve reliability, and understand whether the service is working as expected.',
      ],
    },
    {
      heading: 'Sharing and Visibility',
      paragraphs: [
        "Precise location data is visible to authenticated users who are members of the same session. Anyone with a session's invite code can join it directly and choose to join as a runner (shares location) or a viewer (does not) — there is no owner approval step.",
        'Runs started with Go: by default your crew can see you by name, and people who are not in your crew cannot see you. If you turn on visibility to people outside your crew, they see you only as an anonymous dot, only while they are out themselves and within 5 km of you, and by default not until you are 300 m from where your run began. You can turn off both settings so that nobody sees you. Anyone you give a watch link to can see your run on the web until the run ends.',
        'Battery level is visible to the same people who can see your location. A route added to a session can be downloaded by anyone with that session\'s invite code. A route added to a run started with Go is visible to you, your crew, and anyone with a watch link, and is deleted when the run ends.',
        'Invite links are onboarding mechanisms, not long-term credentials. Access after joining is based on stored membership and user authentication.',
        'You can block another user from account settings. Blocking ends any session membership you share with them immediately, prevents them from joining any session you own in the future, and stops your location and theirs from being visible to each other.',
        'We do not sell location data. We may disclose data if required by law or if necessary to protect users, the service, or others.',
      ],
    },
    {
      heading: 'Retention and Deletion',
      paragraphs: [
        'Live in-memory location state may be cleared when the server restarts or when an admin clears a session.',
        'You can delete your account from account settings in the app, or at https://dot-watcher.skelstar.io/delete-account. Account deletion removes your account, memberships, sessions you own, and stored location rows linked to your authenticated account. Other active tokens for the deleted account stop working after deletion.',
        `Historical records without account attribution, admin-uploaded recordings, or operational logs may not be linked to your account. For deletion help with those records, contact ${contactEmail} with enough detail to identify the account or session.`,
      ],
    },
    {
      heading: 'Your Choices',
      paragraphs: [
        'Before your location is first shared with other members of a session, the iOS app asks you to explicitly confirm you want to share — you can decline and still use the app as a viewer.',
        'You can stop sharing location at any time by tapping Stop in the iOS app, leaving the app signed out, or revoking location permission in iOS settings. Sharing also stops automatically after 24 hours if not stopped sooner.',
        'You can block another user from account settings at any time. See "Sharing and Visibility" above for what blocking does.',
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

// The App Store Support URL points here, so the contact email comes first.
const supportPage = {
  title: 'Support',
  sections: [
    {
      heading: 'Contact',
      paragraphs: [
        `Email ${contactEmail} with questions, problems, or feedback about Dot Watcher. Include your display name and, if it is about a session or run, roughly when it happened, so we can find it.`,
      ],
    },
    {
      heading: 'Why does Dot Watcher need background location?',
      paragraphs: [
        'Your phone is usually locked in a pocket while you run, so the app needs location access in the background to keep your dot moving for the people watching. It only tracks you after you start a run with Go or join a session as a runner, and it stops when you stop.',
      ],
    },
    {
      heading: 'Who can see me?',
      paragraphs: [
        'On a run started with Go, your crew sees you by name and nobody else sees you unless you allow it. Tap "Who can see me" before or during a run to change this, including turning off both settings so that nobody sees you. In a session, the other members of that session can see you while you are a runner in it. The Privacy Policy has the full details.',
      ],
    },
    {
      heading: 'How do I block someone?',
      paragraphs: [
        'On the session map, open the list of people, then touch and hold their name and choose Block. Blocked people cannot see your location, join sessions you own, or appear in sessions you are both in. On iPhone, you can unblock people in Settings > Blocked Users.',
      ],
    },
    {
      heading: 'How do I delete my account?',
      paragraphs: [
        'On iPhone, go to Settings > Delete account. On any device, you can also delete it at dot-watcher.skelstar.io/delete-account. Deleting your account removes your account, your memberships, the sessions you own, and the location data linked to your account.',
      ],
    },
  ],
}

const termsPage = {
  title: 'Terms of Use',
  sections: [
    {
      heading: 'Service Availability',
      paragraphs: [
        'Dot Watcher is under active development. Features may change, and the service may occasionally be unavailable or affected by bugs. Use the service at your own risk.',
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
        "Anyone with a session's invite code can join it and choose to join as a runner, without owner approval — only share invite codes with people you want tracking in your session.",
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
        'To the maximum extent permitted by law, we are not liable for losses or damages arising from use of the service.',
      ],
    },
    {
      heading: 'Changes',
      paragraphs: [
        'We may update these terms as the service evolves. Continued use of Dot Watcher after an update means you accept the updated terms.',
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
