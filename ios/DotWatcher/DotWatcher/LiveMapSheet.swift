import SwiftUI

struct LiveMapSheet: View {
    static let peekHeight: CGFloat = 80

    var location: LocationManager

    var body: some View {
        if let url = mapURL {
            LiveMapWebView(url: url)
        } else {
            Color.clear
        }
    }

    private var mapURL: URL? {
        guard let inviteCode = location.activeMembership?.inviteCode else { return nil }
        return location.liveMapURL(inviteCode: inviteCode)
    }
}
