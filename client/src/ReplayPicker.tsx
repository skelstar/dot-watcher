import { useEffect, useState } from 'react'

interface Props {
  serverUrl: string
  onSelect: (sessionCode: string) => void
}

export default function ReplayPicker({ serverUrl, onSelect }: Props) {
  const [sessions, setSessions] = useState<string[] | null>(null)
  const [error, setError] = useState(false)
  const [mergeSource, setMergeSource] = useState<string | null>(null)
  const [mergeTarget, setMergeTarget] = useState<string | null>(null)
  const [merging, setMerging] = useState(false)

  const bearerToken = import.meta.env.VITE_BEARER_TOKEN as string | undefined
  const canAdmin = !!bearerToken

  function loadSessions() {
    fetch(`${serverUrl}/sessions`)
      .then(r => r.json() as Promise<string[]>)
      .then(setSessions)
      .catch(() => setError(true))
  }

  useEffect(loadSessions, [serverUrl])

  function handleSessionClick(code: string) {
    if (!mergeSource) {
      onSelect(code)
      return
    }
    if (code === mergeSource) {
      setMergeSource(null)
      return
    }
    setMergeTarget(code)
  }

  async function confirmMerge() {
    if (!mergeSource || !mergeTarget) return
    setMerging(true)
    try {
      await fetch(`${serverUrl}/sessions/${mergeTarget}/merge-from/${mergeSource}`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${bearerToken}` },
      })
      setMergeSource(null)
      setMergeTarget(null)
      setSessions(null)
      loadSessions()
    } catch {
      setError(true)
    } finally {
      setMerging(false)
    }
  }

  function cancelMerge() {
    setMergeSource(null)
    setMergeTarget(null)
  }

  return (
    <div style={overlay}>
      <div style={card}>
        <h1 style={heading}>Replay a Run</h1>

        {mergeSource && !mergeTarget && (
          <p style={mergeHint}>
            Select a session to merge <strong>{mergeSource}</strong> into, or click it again to cancel.
          </p>
        )}

        {mergeTarget && (
          <div style={confirmBox}>
            <p style={confirmText}>
              Merge <strong>{mergeSource}</strong> → <strong>{mergeTarget}</strong>?
              <br />
              <span style={{ color: '#94a3b8', fontSize: '0.8rem' }}>This cannot be undone.</span>
            </p>
            <div style={{ display: 'flex', gap: 8 }}>
              <button style={confirmBtn} onClick={confirmMerge} disabled={merging}>
                {merging ? 'Merging…' : 'Confirm'}
              </button>
              <button style={cancelBtn} onClick={cancelMerge} disabled={merging}>
                Cancel
              </button>
            </div>
          </div>
        )}

        {error && <p style={errorText}>Could not load sessions.</p>}

        {!error && sessions === null && <p style={sub}>Loading…</p>}

        {sessions !== null && sessions.length === 0 && (
          <p style={sub}>No recorded sessions found.</p>
        )}

        {sessions !== null && sessions.length > 0 && (
          <ul style={list}>
            {sessions.map(code => {
              const isSource = code === mergeSource
              return (
                <li key={code} style={{ display: 'flex', gap: 6 }}>
                  <button
                    style={{
                      ...item,
                      flex: 1,
                      ...(isSource ? sourceHighlight : {}),
                      ...(mergeSource && !isSource ? dimmed : {}),
                    }}
                    onClick={() => handleSessionClick(code)}
                  >
                    {code}
                  </button>
                  {canAdmin && !mergeSource && (
                    <button
                      style={mergeTriggerBtn}
                      title={`Merge ${code} into another session`}
                      onClick={() => setMergeSource(code)}
                    >
                      ⤵
                    </button>
                  )}
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </div>
  )
}

const overlay: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'rgba(0,0,0,0.45)',
  zIndex: 10,
}

const card: React.CSSProperties = {
  background: '#fff',
  borderRadius: 12,
  padding: '2rem 1.5rem',
  display: 'flex',
  flexDirection: 'column',
  gap: '0.75rem',
  width: 'min(320px, 90vw)',
  maxHeight: '80vh',
  boxShadow: '0 8px 32px rgba(0,0,0,0.25)',
  overflow: 'hidden',
}

const heading: React.CSSProperties = {
  fontSize: '1.4rem',
  fontWeight: 700,
  textAlign: 'center',
  fontFamily: 'system-ui, sans-serif',
  margin: 0,
}

const sub: React.CSSProperties = {
  fontSize: '0.9rem',
  color: '#555',
  textAlign: 'center',
  fontFamily: 'system-ui, sans-serif',
  margin: 0,
}

const errorText: React.CSSProperties = {
  ...sub,
  color: '#ef4444',
}

const list: React.CSSProperties = {
  listStyle: 'none',
  margin: 0,
  padding: 0,
  display: 'flex',
  flexDirection: 'column',
  gap: 6,
  overflowY: 'auto',
}

const item: React.CSSProperties = {
  width: '100%',
  padding: '0.6rem 0.75rem',
  borderRadius: 8,
  border: '1.5px solid #e2e8f0',
  background: '#f8fafc',
  color: '#1e293b',
  fontSize: '1rem',
  fontFamily: 'monospace',
  fontWeight: 600,
  letterSpacing: '0.05em',
  textAlign: 'left',
  cursor: 'pointer',
}

const sourceHighlight: React.CSSProperties = {
  border: '1.5px solid #3b82f6',
  background: '#eff6ff',
  color: '#1d4ed8',
}

const dimmed: React.CSSProperties = {
  opacity: 0.5,
}

const mergeTriggerBtn: React.CSSProperties = {
  padding: '0.6rem 0.6rem',
  borderRadius: 8,
  border: '1.5px solid #e2e8f0',
  background: '#f8fafc',
  color: '#64748b',
  fontSize: '1rem',
  cursor: 'pointer',
  flexShrink: 0,
}

const mergeHint: React.CSSProperties = {
  fontSize: '0.85rem',
  color: '#475569',
  fontFamily: 'system-ui, sans-serif',
  margin: 0,
  background: '#f0f9ff',
  borderRadius: 8,
  padding: '0.5rem 0.75rem',
}

const confirmBox: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 10,
  background: '#fefce8',
  border: '1.5px solid #fde047',
  borderRadius: 8,
  padding: '0.75rem',
}

const confirmText: React.CSSProperties = {
  fontSize: '0.9rem',
  fontFamily: 'system-ui, sans-serif',
  margin: 0,
  color: '#1e293b',
  lineHeight: 1.5,
}

const confirmBtn: React.CSSProperties = {
  flex: 1,
  padding: '0.4rem 0',
  borderRadius: 6,
  border: 'none',
  background: '#ef4444',
  color: '#fff',
  fontWeight: 600,
  fontSize: '0.9rem',
  cursor: 'pointer',
  fontFamily: 'system-ui, sans-serif',
}

const cancelBtn: React.CSSProperties = {
  flex: 1,
  padding: '0.4rem 0',
  borderRadius: 6,
  border: '1.5px solid #e2e8f0',
  background: '#fff',
  color: '#475569',
  fontWeight: 600,
  fontSize: '0.9rem',
  cursor: 'pointer',
  fontFamily: 'system-ui, sans-serif',
}
