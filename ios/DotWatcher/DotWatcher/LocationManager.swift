import CoreLocation
import Foundation

@Observable
@MainActor
final class LocationManager {
    private(set) var status = "Idle"
    private(set) var lastSent: Date?
    private(set) var isTracking = false
    fileprivate var latestLocation: CLLocation?

    private let clManager = CLLocationManager()
    private let locationDelegate = LocationDelegate()
    private var trackingTask: Task<Void, Never>?

    let serverURL = URL(string: "http://192.168.1.199:8080/location")!
    let bearerToken = "dev-token"
    let sessionCode = "test"
    let runnerName = "Gerald"
    let interval: TimeInterval = 3

    init() {
        locationDelegate.owner = self
        clManager.delegate = locationDelegate
        clManager.desiredAccuracy = kCLLocationAccuracyBest
    }

    func start() {
        guard !isTracking else { return }
        clManager.requestWhenInUseAuthorization()
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

    private func captureAndPost() {
        guard let loc = latestLocation else {
            status = "Waiting for GPS..."
            return
        }
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
            let (_, response) = try await URLSession.shared.data(for: req)
            let code = (response as? HTTPURLResponse)?.statusCode ?? 0
            status = code == 200 ? "Sent ✓" : "HTTP \(code)"
            lastSent = Date()
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
