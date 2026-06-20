export interface RunnerPosition {
  runnerName: string
  latitude: number
  longitude: number
  heading: number | null
  timestamp: string
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
  sessionCode: string
  inviteCode: string
  role: 'owner' | 'runner' | 'viewer'
  displayName: string
}

export interface SessionMember {
  userId: string
  role: 'owner' | 'runner' | 'viewer'
  displayName: string
}
