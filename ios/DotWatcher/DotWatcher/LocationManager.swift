import CoreLocation
import Foundation

@Observable
@MainActor
final class LocationManager {
    private(set) var status = "Idle"
    private(set) var lastSent: Date?
    private(set) var isTracking = false
    var participants: [String] = []
    fileprivate var latestLocation: CLLocation?
    private var lastTransmittedLocation: CLLocation?

    private let clManager = CLLocationManager()
    private let locationDelegate = LocationDelegate()
    private var trackingTask: Task<Void, Never>?

    let serverURL = URL(string: "http://dot-watcher.skelstar.io/api/location")!
    let bearerToken = "dev-token"
    var sessionCode = ""
    var runnerName: String = UserDefaults.standard.string(forKey: "runnerName") ?? "" {
        didSet { UserDefaults.standard.set(runnerName, forKey: "runnerName") }
    }
    let interval: TimeInterval = 15

    init() {
        locationDelegate.owner = self
        clManager.delegate = locationDelegate
        clManager.desiredAccuracy = kCLLocationAccuracyNearestTenMeters
        clManager.distanceFilter = 10.0
        clManager.activityType = .fitness
        clManager.pausesLocationUpdatesAutomatically = false
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

    func forceUpdate() {
        captureAndPost(forced: true)
    }

    private func captureAndPost(forced: Bool = false) {
        guard let loc = latestLocation else {
            status = "Waiting for GPS..."
            return
        }
        if !forced, let last = lastTransmittedLocation, loc.distance(from: last) < 10 {
            status = "Stationary 💤"
            return
        }
        lastTransmittedLocation = loc
        let captureTime = loc.timestamp
        let lat = loc.coordinate.latitude
        let lon = loc.coordinate.longitude
        let heading: Double? = loc.course >= 0 ? loc.course : nil
        Task { await post(lat: lat, lon: lon, heading: heading, timestamp: captureTime) }
    }

    private func post(lat: Double, lon: Double, heading: Double?, timestamp: Date) async {
        var req = URLRequest(url: serverURL)
        req.httpMethod = "POST"
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.setValue("Bearer \(bearerToken)", forHTTPHeaderField: "Authorization")
        var body: [String: Any] = [
            "runnerName": runnerName,
            "sessionCode": sessionCode,
            "latitude": lat,
            "longitude": lon,
            "timestamp": ISO8601DateFormatter().string(from: timestamp)
        ]
        if let h = heading { body["heading"] = h }
        do {
            req.httpBody = try JSONSerialization.data(withJSONObject: body)
            let (data, response) = try await URLSession.shared.data(for: req)
            let code = (response as? HTTPURLResponse)?.statusCode ?? 0
            if code == 200 {
                status = "Sent ✓"
                lastSent = Date()
                if let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                   let names = json["participants"] as? [String] {
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
