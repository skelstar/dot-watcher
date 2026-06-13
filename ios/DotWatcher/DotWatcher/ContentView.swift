import SwiftUI

struct ContentView: View {
    @State private var location = LocationManager()

    var body: some View {
        VStack(spacing: 24) {
            Text("DotWatcher")
                .font(.largeTitle.bold())

            VStack(spacing: 8) {
                Label(location.runnerName, systemImage: "figure.run")
                Label(location.sessionCode, systemImage: "tag")
            }
            .foregroundStyle(.secondary)

            Text(location.status)
                .font(.headline)

            if let sent = location.lastSent {
                Text("Last sent \(sent.formatted(date: .omitted, time: .standard))")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
            }

            Button(location.isTracking ? "Stop" : "Start Tracking") {
                location.isTracking ? location.stop() : location.start()
            }
            .buttonStyle(.borderedProminent)
            .tint(location.isTracking ? .red : .green)
        }
        .padding()
    }
}

#Preview {
    ContentView()
}
