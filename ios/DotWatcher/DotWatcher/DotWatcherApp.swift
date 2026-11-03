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
        // Read the saved start-screen palette now, so the first screen already has it.
        _ = AppliedPalette.shared
    }

    var body: some Scene {
        WindowGroup {
            ContentView()
        }
    }
}
