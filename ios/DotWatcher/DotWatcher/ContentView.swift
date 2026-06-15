import SwiftUI
import UIKit

struct ContentView: View {
    @State private var location = LocationManager()
    @FocusState private var codeFieldFocused: Bool
    @State private var batteryLevel: Float = UIDevice.current.batteryLevel

    var body: some View {
        VStack(spacing: 24) {
            Text("DotWatcher")
                .font(.largeTitle.bold())

            HStack {
                Image(systemName: "figure.run")
                    .foregroundStyle(.secondary)
                TextField("Runner name", text: $location.runnerName)
                    .multilineTextAlignment(.center)
                    .foregroundStyle(.secondary)
                    .disabled(location.isTracking)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
            .background(Color(.systemBackground))
            .clipShape(RoundedRectangle(cornerRadius: 8))
            .overlay(
                RoundedRectangle(cornerRadius: 8)
                    .stroke(location.runnerName.trimmingCharacters(in: .whitespaces).isEmpty ? Color.orange : Color.secondary.opacity(0.3), lineWidth: location.runnerName.trimmingCharacters(in: .whitespaces).isEmpty ? 2 : 1)
            )

            sessionCodeEntry

            Text("Location is sent every 15s")
                .font(.caption)
                .foregroundStyle(.secondary)

            Text(location.status)
                .font(.headline)

            if batteryLevel >= 0 {
                Label("\(Int(batteryLevel * 100))%", systemImage: batteryIcon)
                    .font(.caption)
                    .foregroundStyle(batteryLevel < 0.2 ? .red : .secondary)
            }

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
                .disabled(!location.isTracking && (location.sessionCode.count < 6 || location.runnerName.trimmingCharacters(in: .whitespaces).isEmpty))
            }

            VStack(alignment: .leading, spacing: 8) {
                Text("Participants")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                ForEach(location.participants, id: \.self) { name in
                    Label(name, systemImage: "figure.run")
                        .font(.caption)
                }
            }
            .frame(maxWidth: .infinity, minHeight: 80, alignment: .topLeading)
            .padding(12)
            .background(Color(.secondarySystemBackground))
            .clipShape(RoundedRectangle(cornerRadius: 10))
        }
        .padding()
        .onAppear {
            codeFieldFocused = true
            UIDevice.current.isBatteryMonitoringEnabled = true
            batteryLevel = UIDevice.current.batteryLevel
        }
        .onReceive(NotificationCenter.default.publisher(for: UIDevice.batteryLevelDidChangeNotification)) { _ in
            batteryLevel = UIDevice.current.batteryLevel
        }
    }

    private var batteryIcon: String {
        switch batteryLevel {
        case ..<0.25: return "battery.25"
        case ..<0.50: return "battery.50"
        case ..<0.75: return "battery.75"
        default:      return "battery.100"
        }
    }

    private var sessionCodeEntry: some View {
        VStack(spacing: 10) {
            Label("Session Name:", systemImage: "tag")
                .foregroundStyle(.secondary)

            ZStack {
                TextField("", text: $location.sessionCode)
                    .focused($codeFieldFocused)
                    .keyboardType(.alphabet)
                    .autocorrectionDisabled()
                    .textInputAutocapitalization(.characters)
                    .opacity(0)
                    .frame(width: 1, height: 1)
                    .disabled(location.isTracking)
                    .onChange(of: location.sessionCode) { _, new in
                        let filtered = String(new.uppercased().filter { $0.isLetter || $0.isNumber }.prefix(6))
                        if filtered != new { location.sessionCode = filtered }
                    }

                HStack(spacing: 10) {
                    ForEach(0..<6, id: \.self) { i in
                        CodeBox(
                            character: character(at: i),
                            isActive: codeFieldFocused && !location.isTracking && location.sessionCode.count == i
                        )
                    }
                }
                .onTapGesture {
                    if !location.isTracking { codeFieldFocused = true }
                }
            }

            if !location.sessionCode.isEmpty,
               let url = URL(string: "http://dot-watcher.skelstar.io/\(location.sessionCode)") {
                Link("Open map in browser →", destination: url)
                    .font(.caption)
                    .foregroundStyle(Color.accentColor)
            }
        }
    }

    private func character(at index: Int) -> Character? {
        guard index < location.sessionCode.count else { return nil }
        return location.sessionCode[location.sessionCode.index(location.sessionCode.startIndex, offsetBy: index)]
    }
}

struct CodeBox: View {
    let character: Character?
    let isActive: Bool

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 8)
                .fill(Color(.systemBackground))
            RoundedRectangle(cornerRadius: 8)
                .stroke(isActive ? Color.accentColor : Color.secondary.opacity(0.4), lineWidth: isActive ? 2 : 1.5)

            if let char = character {
                Text(String(char))
                    .font(.title.bold())
            } else if isActive {
                Rectangle()
                    .frame(width: 2, height: 22)
                    .foregroundStyle(Color.accentColor)
            }
        }
        .frame(width: 44, height: 54)
    }
}

#Preview {
    ContentView()
}
