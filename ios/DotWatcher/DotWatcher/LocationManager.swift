import CoreLocation
import Foundation

@Observable
@MainActor
final class LocationManager {
    private(set) var status = "Idle"
    private(set) var lastSent: Date?
    private(set) var isTracking = false
    var participants: [String] = []
    private var lastParticipantCount = 0
    fileprivate var latestLocation: CLLocation?

    private let clManager = CLLocationManager()
    private let locationDelegate = LocationDelegate()
    private var trackingTask: Task<Void, Never>?

    let serverURL = URL(string: "http://dot-watcher.skelstar.io/api/location")!
    let bearerToken = "dev-token"
    var sessionCode = "" {
        didSet {
            participants = []
            lastParticipantCount = 0
            if isTracking { captureAndPost() }
        }
    }

    var dateSuffix: String {
        let cal = Calendar.current
        let now = Date()
        let day = cal.component(.day, from: now)
        let month = cal.component(.month, from: now)
        return String(format: "-%02d%02d", day, month)
    }

    var fullSessionName: String { sessionCode + dateSuffix }
    var runnerName: String = UserDefaults.standard.string(forKey: "runnerName") ?? "" {
        didSet { UserDefaults.standard.set(runnerName, forKey: "runnerName") }
    }
    let interval: TimeInterval = 15

    init() {
        locationDelegate.owner = self
        clManager.delegate = locationDelegate
        clManager.desiredAccuracy = kCLLocationAccuracyNearestTenMeters
        clManager.distanceFilter = 25.0
        clManager.activityType = .fitness
        clManager.pausesLocationUpdatesAutomatically = true
        clManager.allowsBackgroundLocationUpdates = true
        clManager.showsBackgroundLocationIndicator = true
    }

    func start() {
        guard !isTracking else { return }
        clManager.requestAlwaysAuthorization()
        clManager.startUpdatingLocation()
        isTracking = true
        status = "Tracking..."
        trackingTask = Task { [weak self] in await self?.trackingLoop() }
        captureAndPost()
    }

    func stop() {
        trackingTask?.cancel()
        trackingTask = nil
        clManager.stopUpdatingLocation()
        isTracking = false
        status = "Stopped"
    }

    private func trackingLoop() async {
        while !Task.isCancelled {
            let now = Date().timeIntervalSince1970
            let delay = ((floor(now / interval) + 1) * interval) - now
            try? await Task.sleep(for: .seconds(delay))
            guard !Task.isCancelled else { break }
            captureAndPost()
        }
    }

func previewSession(_ sessionName: String) async -> [String] {
        guard let url = URL(string: "http://dot-watcher.skelstar.io/api/locations/\(sessionName)") else { return [] }
        var req = URLRequest(url: url)
        req.setValue("Bearer \(bearerToken)", forHTTPHeaderField: "Authorization")
        guard let (data, response) = try? await URLSession.shared.data(for: req),
              (response as? HTTPURLResponse)?.statusCode == 200,
              let groups = try? JSONSerialization.jsonObject(with: data) as? [[[String: Any]]] else { return [] }
        return groups.compactMap { $0.first?["runnerName"] as? String }
    }

    private func captureAndPost() {
        if let loc = latestLocation {
            let heading: Double? = loc.course >= 0 ? loc.course : nil
            Task { await post(lat: loc.coordinate.latitude, lon: loc.coordinate.longitude, heading: heading, timestamp: loc.timestamp) }
        } else {
            Task { await post(lat: nil, lon: nil, heading: nil, timestamp: nil) }
        }
    }

    private func post(lat: Double?, lon: Double?, heading: Double?, timestamp: Date?) async {
        var req = URLRequest(url: serverURL)
        req.httpMethod = "POST"
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.setValue("Bearer \(bearerToken)", forHTTPHeaderField: "Authorization")
        var body: [String: Any] = [
            "runnerName": runnerName,
            "sessionCode": fullSessionName,
            "timestamp": ISO8601DateFormatter().string(from: timestamp ?? Date())
        ]
        if let lat, let lon {
            body["latitude"] = lat
            body["longitude"] = lon
        }
        if let h = heading { body["heading"] = h }
        do {
            req.httpBody = try JSONSerialization.data(withJSONObject: body)
            let (data, response) = try await URLSession.shared.data(for: req)
            let code = (response as? HTTPURLResponse)?.statusCode ?? 0
            if code == 200 {
                status = "Sent ✓"
                lastSent = Date()
                if let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                   let names = json["participants"] as? [String],
                   names.count != lastParticipantCount {
                    lastParticipantCount = names.count
                    participants = names
                }
            } else {
                status = "HTTP \(code)"
            }
        } catch {
            status = "Error: \(error.localizedDescription)"
        }
    }
}

private final class LocationDelegate: NSObject, CLLocationManagerDelegate {
    weak var owner: LocationManager?

    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let loc = locations.last else { return }
        Task { @MainActor [weak self] in self?.owner?.latestLocation = loc }
    }

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {}
}
