# iOS

Native Swift app that sends GPS positions to the Dot Watcher server at a configurable interval.

---

## Position recording strategy

Phones must not record on a simple repeating timer from app launch — if Runner A starts tracking at `17:46:03` and Runner B at `17:46:47`, their positions are always ~44 seconds apart even though they share the same interval.

**Use clock-aligned intervals instead.** On each tick, snap to the next wall-clock boundary:

```swift
func scheduleNextCapture() {
    let interval: TimeInterval = 60 // configurable
    let now = Date().timeIntervalSince1970
    let nextTick = (floor(now / interval) + 1) * interval
    let delay = nextTick - now

    DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
        self?.captureAndPost()
        self?.scheduleNextCapture()
    }
}
```

This means all phones independently fire at `17:47:00`, `17:48:00`, etc. iOS NTP sync keeps clocks within ~50–200 ms of each other — at jogging pace that's under 60 cm of position error, well within GPS accuracy.

**The `timestamp` in the POST payload is the GPS capture time, not the send time.** Record it when `CLLocation` is read, before the network request. This way retried or delayed POSTs still carry the correct position, and the viewer always shows a coherent snapshot of where everyone was at the same moment.
