import { useEffect, useState } from 'react'

// Page Visibility API, not window focus/blur: `document.hidden` correctly reflects a
// backgrounded tab *and* a locked phone screen, whereas focus/blur also fires for
// same-page distractions (e.g. dev tools grabbing focus) that shouldn't pause polling.
export function usePageVisible(): boolean {
  const [visible, setVisible] = useState(() => !document.hidden)

  useEffect(() => {
    const onChange = () => setVisible(!document.hidden)
    document.addEventListener('visibilitychange', onChange)
    return () => document.removeEventListener('visibilitychange', onChange)
  }, [])

  return visible
}
