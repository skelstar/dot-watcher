import SwiftUI

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
