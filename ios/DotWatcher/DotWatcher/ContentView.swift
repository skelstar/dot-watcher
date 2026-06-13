import SwiftUI

private let intervalOptions: [(label: String, seconds: TimeInterval)] = [
    ("3 seconds", 3),
    ("5 seconds", 5),
    ("10 seconds", 10),
    ("15 seconds", 15),
    ("30 seconds", 30),
    ("60 seconds", 60),
    ("5 minutes", 300),
]

struct ContentView: View {
    @State private var location = LocationManager()

    var body: some View {
        VStack(spacing: 24) {
            Text("DotWatcher")
                .font(.largeTitle.bold())

            Label(location.runnerName, systemImage: "figure.run")
                .foregroundStyle(.secondary)

            HStack {
                Label("Session", systemImage: "tag")
                    .foregroundStyle(.secondary)
                TextField("session code", text: $location.sessionCode)
                    .textFieldStyle(.roundedBorder)
                    .autocorrectionDisabled()
                    .textInputAutocapitalization(.never)
                    .disabled(location.isTracking)
            }

            HStack {
                Label("Interval", systemImage: "clock")
                    .foregroundStyle(.secondary)
                Spacer()
                Picker("Interval", selection: $location.interval) {
                    ForEach(intervalOptions, id: \.seconds) { option in
                        Text(option.label).tag(option.seconds)
                    }
                }
                .disabled(location.isTracking)
            }

            Text(location.status)
                .font(.headline)

            if let sent = location.lastSent {
                Text("Last sent \(sent.formatted(date: .omitted, time: .standard))")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
            }

            HStack(spacing: 12) {
                if location.isTracking {
                    Button("Force Update") { location.forceUpdate() }
                        .buttonStyle(.bordered)
                }
                Button(location.isTracking ? "Stop" : "Start Tracking") {
                    location.isTracking ? location.stop() : location.start()
                }
                .buttonStyle(.borderedProminent)
                .tint(location.isTracking ? .red : .green)
            }
        }
        .padding()
    }
}

#Preview {
    ContentView()
}
