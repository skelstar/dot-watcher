export interface RunnerPosition {
  runnerName: string
  latitude: number
  longitude: number
  heading: number | null
  timestamp: string
  // Self-reported: when this runner's device expects to post next, at its current cadence
  // (normal or a slower one, e.g. satellite). Absent for older clients/recordings that predate
  // this field, or the demo runner. See .ai/plans/POST-nextExpectedAt.md.
  nextExpectedAt?: string | null
  // Self-reported NWPath.isUltraConstrained at capture time — see server/Models/LocationUpdate.cs
  // for why this isn't called isSatellite. False (not absent) for older clients, the demo runner,
  // or recordings, since the server itself defaults it the same way.
  isUltraConstrained?: boolean
}

export interface AuthenticatedUser {
  userId: string
  username: string
  displayName: string
}

export interface AuthResponse {
  accessToken: string
  expiresAt: string
  user: AuthenticatedUser
}

export interface SessionMembership {
  sessionId: string
  sessionName: string
  inviteCode: string
  role: 'owner' | 'runner' | 'viewer'
  displayName: string
  ownerDisplayName: string
}

export interface SessionMember {
  userId: string
  role: 'owner' | 'runner' | 'viewer'
  displayName: string
}
