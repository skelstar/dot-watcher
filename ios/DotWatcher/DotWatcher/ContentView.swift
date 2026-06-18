import SwiftUI
import UIKit

struct ContentView: View {
    @State private var location = LocationManager()
    @State private var batteryLevel: Float = UIDevice.current.batteryLevel
    @State private var showNameEntry: Bool = false
    @State private var nameInput: String = ""
    @State private var showHelp: Bool = false
    @State private var showSessionEntry: Bool = false

    var body: some View {
        ScrollView {
            VStack(spacing: 16) {
                headerSection
                runnerRow
                sessionNameCard
                statusCard
                participantsCard
            }
            .padding()
        }
        .safeAreaInset(edge: .bottom) {
            bottomButton
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                .frame(maxWidth: .infinity)
                .background(.regularMaterial)
        }
        .onAppear {
            UIDevice.current.isBatteryMonitoringEnabled = true
            batteryLevel = UIDevice.current.batteryLevel
            if location.runnerName.trimmingCharacters(in: .whitespaces).isEmpty {
                showNameEntry = true
            }
        }
        .onReceive(NotificationCenter.default.publisher(for: UIDevice.batteryLevelDidChangeNotification)) { _ in
            batteryLevel = UIDevice.current.batteryLevel
        }
        .sheet(isPresented: $showHelp) {
            HelpView()
        }
        .sheet(isPresented: $showNameEntry) {
            NameEntryView(
                name: $nameInput,
                isFirstLaunch: location.runnerName.trimmingCharacters(in: .whitespaces).isEmpty
            ) {
                location.runnerName = nameInput.trimmingCharacters(in: .whitespaces)
                showNameEntry = false
            }
            .interactiveDismissDisabled(location.runnerName.trimmingCharacters(in: .whitespaces).isEmpty)
        }
        .sheet(isPresented: $showSessionEntry) {
            SessionEntrySheet(location: location)
        }
    }

    // MARK: - Header

    private var headerSection: some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 4) {
                Text("DotWatcher")
                    .font(.largeTitle.bold())
                let build = Bundle.main.infoDictionary?["CFBundleVersion"] as? String
                let sha = Bundle.main.infoDictionary?["GitCommitSHA"] as? String
                if build != nil || sha != nil {
                    Text([build.map { "build \($0)" }, sha].compactMap { $0 }.joined(separator: " · "))
                        .font(.caption2)
                        .foregroundStyle(.tertiary)
                }
            }
            Spacer()
            Button { showHelp = true } label: {
                Image(systemName: "questionmark.circle")
                    .font(.title2)
                    .foregroundStyle(.secondary)
            }
        }
    }

    // MARK: - Runner Row

    private var runnerRow: some View {
        RunnerCircle(name: location.runnerName, size: 56, isHighlighted: true)
            .frame(maxWidth: .infinity)
            .contentShape(Rectangle())
            .onTapGesture {
                guard !location.isTracking else { return }
                nameInput = location.runnerName
                showNameEntry = true
            }
    }

    // MARK: - Session Name Card

    private var sessionNameCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("SESSION NAME")
                .font(.caption)
                .fontWeight(.semibold)
                .foregroundStyle(.secondary)

            HStack(spacing: 0) {
                if location.sessionCode.isEmpty {
                    Text("Tap to set")
                        .font(.title3.monospaced())
                        .foregroundStyle(.tertiary)
                } else {
                    Text(location.sessionCode)
                        .font(.title3.bold().monospaced())
                        .foregroundStyle(.primary)
                    Text(location.dateSuffix)
                        .font(.title3.bold().monospaced())
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Image(systemName: "lock")
                    .font(.subheadline)
                    .foregroundStyle(.tertiary)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 14)
            .background(Color(.tertiarySystemBackground))
            .clipShape(RoundedRectangle(cornerRadius: 10))
            .contentShape(RoundedRectangle(cornerRadius: 10))
            .onTapGesture { showSessionEntry = true }

            if location.sessionCode.count == 6,
               let url = URL(string: "http://dot-watcher.skelstar.io/\(location.fullSessionName)") {
                Link("Open map in browser →", destination: url)
                    .font(.subheadline)
                    .foregroundStyle(Color.accentColor)
            }
        }
        .padding(16)
        .background(Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 14))
    }

    // MARK: - Status Card

    private var statusCard: some View {
        VStack(spacing: 10) {
            HStack {
                HStack(spacing: 8) {
                    Circle()
                        .fill(statusDotColor)
                        .frame(width: 10, height: 10)
                    Text(location.status)
                        .font(.headline)
                }
                Spacer()
                Text("every 15s")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            HStack {
                if batteryLevel >= 0 {
                    Label("\(Int(batteryLevel * 100))%", systemImage: batteryIcon)
                        .font(.caption)
                        .foregroundStyle(batteryLevel < 0.2 ? .red : .secondary)
                }
                Spacer()
                if let sent = location.lastSent {
                    Text("Last sent \(sent.formatted(date: .omitted, time: .standard))")
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                }
            }
        }
        .padding(16)
        .background(Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 14))
    }

    // MARK: - Participants Card

    private var participantsCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("PARTICIPANTS")
                .font(.caption)
                .fontWeight(.semibold)
                .foregroundStyle(.secondary)
            if location.participants.isEmpty {
                Text("No participants yet")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
            } else {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 56))], spacing: 10) {
                    ForEach(location.participants, id: \.self) { participant in
                        RunnerCircle(
                            name: participant,
                            size: 56,
                            isHighlighted: participant == location.runnerName
                        )
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .padding(16)
        .background(Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 14))
    }

    // MARK: - Bottom Button

    @ViewBuilder
    private var bottomButton: some View {
        if location.isTracking {
            Button { location.stop() } label: {
                Text("Stop").frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .tint(.red)
            .controlSize(.large)
        } else {
            Button {
                if location.sessionCode.count == 6 {
                    location.start()
                } else {
                    showSessionEntry = true
                }
            } label: {
                Text("Start tracking").frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .tint(.green)
            .controlSize(.large)
        }
    }

    // MARK: - Helpers

    private var statusDotColor: Color {
        let s = location.status
        if s == "Idle" { return .gray }
        if s == "Stopped" { return .red }
        if s.hasPrefix("Sent") || s.hasPrefix("Tracking") { return .green }
        return .orange
    }

    private var batteryIcon: String {
        switch batteryLevel {
        case ..<0.25: return "battery.25"
        case ..<0.50: return "battery.50"
        case ..<0.75: return "battery.75"
        default:      return "battery.100"
        }
    }
}

// MARK: - Session Entry Sheet

struct SessionEntrySheet: View {
    var location: LocationManager

    @State private var localCode: String = ""
    @State private var participantCount: Int? = nil
    @State private var isChecking: Bool = false
    @FocusState private var focused: Bool
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 24) {
            Text("Edit session name")
                .font(.title2.bold())
                .padding(.top, 8)

            Text("Friends search for this name to find and follow you. Starting will share your live location with them.")
                .multilineTextAlignment(.center)
                .foregroundStyle(.secondary)

            tileInput

            participantStatusView

            Spacer()

            Button(location.isTracking ? "Done" : "Start") {
                location.sessionCode = localCode
                if !location.isTracking {
                    location.start()
                }
                dismiss()
            }
            .buttonStyle(.borderedProminent)
            .tint(.green)
            .controlSize(.large)
            .frame(maxWidth: .infinity)
            .disabled(localCode.count < 6)

            Button("Cancel", role: .cancel) {
                dismiss()
            }
            .foregroundStyle(Color.accentColor)
        }
        .padding(24)
        .onAppear {
            localCode = location.sessionCode
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
                focused = true
            }
        }
        .task(id: localCode) {
            guard localCode.count == 6 else {
                participantCount = nil
                isChecking = false
                return
            }
            isChecking = true
            participantCount = nil
            try? await Task.sleep(for: .milliseconds(500))
            guard !Task.isCancelled else { return }
            let count = await location.fetchParticipantCount(for: localCode + location.dateSuffix)
            guard !Task.isCancelled else { return }
            participantCount = count
            isChecking = false
        }
        .presentationDetents([.height(480), .large])
        .presentationDragIndicator(.visible)
    }

    private var tileInput: some View {
        HStack(spacing: 6) {
            HStack(spacing: 6) {
                ForEach(0..<6, id: \.self) { i in
                    CodeBox(
                        character: character(at: i),
                        isActive: focused && localCode.count == i
                    )
                }
            }
            .onTapGesture { focused = true }

            Text(location.dateSuffix)
                .font(.title3.bold().monospaced())
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .padding(.leading, 2)
        }
        .overlay {
            TextField("", text: $localCode)
                .focused($focused)
                .keyboardType(.alphabet)
                .autocorrectionDisabled()
                .textInputAutocapitalization(.characters)
                .opacity(0)
                .frame(width: 1, height: 1)
                .onChange(of: localCode) { _, new in
                    let filtered = String(new.uppercased().filter { $0.isLetter || $0.isNumber }.prefix(6))
                    if filtered != new { localCode = filtered }
                }
        }
    }

    @ViewBuilder
    private var participantStatusView: some View {
        if isChecking {
            HStack(spacing: 6) {
                ProgressView()
                    .scaleEffect(0.8)
                Text("Checking...")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        } else if let count = participantCount {
            HStack(spacing: 6) {
                Image(systemName: "figure.run")
                    .font(.caption)
                    .foregroundStyle(.green)
                Text(count == 0
                     ? "No one else here yet"
                     : "\(count) runner\(count == 1 ? "" : "s") already here")
                    .font(.caption)
                    .foregroundStyle(.green)
            }
        }
    }

    private func character(at index: Int) -> Character? {
        guard index < localCode.count else { return nil }
        return localCode[localCode.index(localCode.startIndex, offsetBy: index)]
    }
}

// MARK: - Name Entry Sheet

struct NameEntryView: View {
    @Binding var name: String
    let isFirstLaunch: Bool
    let onConfirm: () -> Void
    @Environment(\.dismiss) private var dismiss
    @FocusState private var focused: Bool

    private var trimmedName: String { name.trimmingCharacters(in: .whitespaces) }

    var body: some View {
        VStack(spacing: 24) {
            RunnerCircle(name: name, size: 72)

            Text(isFirstLaunch ? "Welcome to DotWatcher" : "Enter Initials")
                .font(.title2.bold())

            Text("Enter your initials so others can find you on the map.")
                .multilineTextAlignment(.center)
                .foregroundStyle(.secondary)

            HStack(spacing: 6) {
                ForEach(0..<3, id: \.self) { i in
                    CodeBox(
                        character: character(at: i),
                        isActive: focused && name.count == i
                    )
                }
            }
            .onTapGesture { focused = true }
            .overlay {
                TextField("", text: $name)
                    .focused($focused)
                    .keyboardType(.alphabet)
                    .autocorrectionDisabled()
                    .textInputAutocapitalization(.characters)
                    .opacity(0)
                    .frame(width: 1, height: 1)
                    .onChange(of: name) { _, new in
                        let filtered = String(new.uppercased().filter { $0.isLetter && $0.isASCII }.prefix(3))
                        if filtered != new { name = filtered }
                    }
            }

            Button("Save") {
                onConfirm()
            }
            .buttonStyle(.borderedProminent)
            .disabled(trimmedName.isEmpty)

            if !isFirstLaunch {
                Button("Cancel", role: .cancel) {
                    dismiss()
                }
                .foregroundStyle(.secondary)
            }
        }
        .padding(32)
        .onAppear { focused = true }
    }

    private func character(at index: Int) -> Character? {
        guard index < name.count else { return nil }
        return name[name.index(name.startIndex, offsetBy: index)]
    }
}

// MARK: - Code Box

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
        .frame(width: 40, height: 54)
    }
}

// MARK: - Help View

struct HelpView: View {
    @Environment(\.dismiss) private var dismiss

    private var rawMarkdown: String {
        guard let url = Bundle.main.url(forResource: "Help", withExtension: "md"),
              let content = try? String(contentsOf: url, encoding: .utf8)
        else { return "Help unavailable." }
        return content
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 4) {
                ForEach(Array(rawMarkdown.components(separatedBy: "\n").enumerated()), id: \.offset) { _, line in
                    if line.hasPrefix("# ") {
                        Text(String(line.dropFirst(2)))
                            .font(.title2.bold())
                            .padding(.top, 8)
                            .padding(.bottom, 4)
                    } else if line.hasPrefix("## ") {
                        Text(String(line.dropFirst(3)))
                            .font(.headline)
                            .underline()
                            .padding(.top, 16)
                            .padding(.bottom, 2)
                    } else if line.isEmpty {
                        Color.clear.frame(height: 4)
                    } else {
                        Text((try? AttributedString(markdown: line)) ?? AttributedString(line))
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(24)
        }
        .safeAreaInset(edge: .bottom) {
            Button("Done") { dismiss() }
                .buttonStyle(.borderedProminent)
                .padding()
                .frame(maxWidth: .infinity)
                .background(.regularMaterial)
        }
    }
}

// MARK: - Runner Circle

struct RunnerCircle: View {
    let name: String
    var size: CGFloat = 40
    var isHighlighted: Bool = false

    private static let highlightColor = Color.accentColor

    var body: some View {
        ZStack {
            Circle()
                .fill(isHighlighted ? Self.highlightColor : Color(.systemGray4))
                .frame(width: size, height: size)
            Text(name.trimmingCharacters(in: .whitespaces).isEmpty ? "?" : name)
                .font(.system(size: size * 0.3, weight: .bold))
                .foregroundStyle(isHighlighted ? .white : .primary)
        }
    }
}

#Preview {
    ContentView()
}

#Preview("First Launch") {
    let _ = UserDefaults.standard.removeObject(forKey: "runnerName")
    ContentView()
}

#Preview("Session Entry") {
    SessionEntrySheet(location: LocationManager())
}

#Preview("Help") {
    HelpView()
}

#Preview("Participants Dots") {
    let names = ["SKE", "JOH", "CHQ", "ALI", "ROS", "TOM", "BEA", "WIL", "ZOE", "MAX"]
    VStack(alignment: .leading, spacing: 10) {
        Text("PARTICIPANTS")
            .font(.caption)
            .fontWeight(.semibold)
            .foregroundStyle(.secondary)
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 56))], spacing: 10) {
            ForEach(names, id: \.self) { name in
                RunnerCircle(name: name, size: 56, isHighlighted: name == "SKE")
            }
        }
    }
    .frame(maxWidth: .infinity, alignment: .topLeading)
    .padding(16)
    .background(Color(.secondarySystemBackground))
    .clipShape(RoundedRectangle(cornerRadius: 14))
    .padding()
}
