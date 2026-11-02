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
        // Read the saved start-screen palette now, so this run uses one palette throughout.
        _ = PaletteStore.applied
    }

    var body: some Scene {
        WindowGroup {
            ContentView()
        }
    }
}
