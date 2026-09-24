import SafariServices
import SwiftUI

/// A page presented in an in-app browser (`SFSafariViewController`) — a sheet with Safari's own
/// controls and a Done button, so the user never leaves the app. The app can't see into the page.
struct SafariView: UIViewControllerRepresentable {
    let url: URL

    func makeUIViewController(context: Context) -> SFSafariViewController {
        SFSafariViewController(url: url)
    }

    func updateUIViewController(_ controller: SFSafariViewController, context: Context) {}
}

/// Lets a `URL` drive `.fullScreenCover(item:)`.
struct IdentifiableURL: Identifiable {
    let url: URL
    var id: URL { url }
}
