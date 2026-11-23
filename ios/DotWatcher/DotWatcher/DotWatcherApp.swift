//
//  DotWatcherApp.swift
//  DotWatcher
//
//  Created by skelstar on 13/6/2026.
//

import SwiftUI

@main
struct DotWatcherApp: App {
    init() {
        SatelliteAlerts.shared.install()
        // Red Hat Text in UIKit-drawn places too (navigation bar titles and buttons). See AppFont.
        AppFont.installAppearance()
        // Read the saved start-screen palette now, so the first screen already has it.
        _ = AppliedPalette.shared
    }

    var body: some Scene {
        WindowGroup {
            ContentView()
                // The app's text font: anything without its own `.font` (buttons, toggles, form rows) is Red Hat Text.
                // Sheets and covers inherit it from here.
                .font(.app(.body))
        }
    }
}
