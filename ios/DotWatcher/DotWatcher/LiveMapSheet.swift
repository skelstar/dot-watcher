import SwiftUI

struct LiveMapSheet: View {
    static let peekHeight: CGFloat = 64

    var location: LocationManager

    var body: some View {
        GeometryReader { proxy in
            let isPeeking = proxy.size.height <= Self.peekHeight
            VStack(spacing: 0) {
                if isPeeking {
                    header
                        .transition(.opacity)
                }
                if !isPeeking, let url = mapURL {
                    LiveMapWebView(url: url)
                } else {
                    Color.clear
                }
            }
            .animation(.easeInOut(duration: 0.15), value: isPeeking)
        }
    }

    private var header: some View {
        HStack(spacing: 10) {
            Image("MapIcon")
                .resizable()
                .scaledToFit()
                .frame(width: 28, height: 28)
            Text("Map")
                .font(.headline)
            LivePill()
            Spacer()
            if let url = mapURL {
                Button {
                    UIApplication.shared.open(url)
                } label: {
                    Text("Open in browser")
                        .font(.subheadline)
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
    }

    private var mapURL: URL? {
        guard let inviteCode = location.activeMembership?.inviteCode else { return nil }
        return location.liveMapURL(inviteCode: inviteCode)
    }
}

private struct LivePill: View {
    var body: some View {
        HStack(spacing: 4) {
            Circle()
                .fill(Color.white)
                .frame(width: 6, height: 6)
            Text("LIVE")
                .font(.caption2.weight(.bold))
        }
        .foregroundStyle(.white)
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
        .background(Color.red, in: Capsule())
    }
}
