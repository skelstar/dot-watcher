namespace BotDots.Service;

/// <summary>
/// One bot-dot out on its trip. Pure movement: it holds a path (built hop by hop by <see cref="Wanderer"/>),
/// walks along it at the persona's pace with a slow drift, and reports a position with a little GPS wobble.
/// It knows nothing about the network.
/// </summary>
public sealed class BotRunner
{
    private const double VisitedCellDegrees = 0.0002; // about 22 m

    private readonly Random _rng;
    private readonly double _baseSpeedMs;
    private readonly List<LatLon> _path = [];
    private int _segment;
    private double _intoSegment;
    private double _drift;
    private double _noiseEast;
    private double _noiseNorth;

    public BotRunner(
        int accountIndex,
        Persona persona,
        string cellId,
        LatLon centre,
        LatLon start,
        DateTimeOffset tripEndsAt,
        Random rng)
    {
        AccountIndex = accountIndex;
        Persona = persona;
        CellId = cellId;
        Centre = centre;
        Position = start;
        TripEndsAt = tripEndsAt;
        _rng = rng;
        _baseSpeedMs = persona.PickSpeed(rng);
        Heading = rng.NextDouble() * 360;
    }

    public int AccountIndex { get; }

    /// <summary>The reserved account this bot-dot signs in as. Matches the server's <c>botdot-</c> prefix.</summary>
    public string Username => $"botdot-{AccountIndex:000}";

    public Persona Persona { get; }

    /// <summary>The geohash cell it was spawned for, and that cell's middle (all it ever knew about a person).</summary>
    public string CellId { get; }

    public LatLon Centre { get; }

    public DateTimeOffset TripEndsAt { get; }

    public LatLon Position { get; private set; }

    public double Heading { get; private set; }

    /// <summary>Finishing its current path and then stopping, instead of heading off again.</summary>
    public bool Retiring { get; set; }

    // Set by the worker once it has signed in and started a casual run.
    public string? Token { get; set; }

    public string? SessionId { get; set; }

    public HashSet<(int, int)> Visited { get; } = [];

    public double RemainingMeters
    {
        get
        {
            if (_segment >= _path.Count - 1)
                return 0;

            var total = Geo.DistanceMeters(_path[_segment], _path[_segment + 1]) - _intoSegment;
            for (var i = _segment + 1; i < _path.Count - 1; i++)
                total += Geo.DistanceMeters(_path[i], _path[i + 1]);
            return Math.Max(0, total);
        }
    }

    /// <summary>Adds a hop. The first hop also snaps the bot-dot onto the route's start.</summary>
    public void AppendRoute(IReadOnlyList<LatLon> shape)
    {
        if (shape.Count == 0)
            return;

        if (_path.Count == 0)
        {
            _path.AddRange(shape);
            Position = shape[0];
            _segment = 0;
            _intoSegment = 0;
            return;
        }

        // Drop what has been walked, then join the new hop on (skipping its first point when it is where we are).
        if (_segment > 0)
        {
            _path.RemoveRange(0, _segment);
            _segment = 0;
        }

        var skip = Geo.DistanceMeters(_path[^1], shape[0]) < 50 ? 1 : 0;
        for (var i = skip; i < shape.Count; i++)
            _path.Add(shape[i]);
    }

    /// <summary>Walks for <paramref name="seconds"/> and records where it has been.</summary>
    public void Advance(double seconds)
    {
        if (seconds <= 0 || _path.Count < 2)
            return;

        _drift = Math.Clamp(_drift * 0.95 + (_rng.NextDouble() - 0.5) * 0.04, -0.1, 0.1);
        var remaining = _baseSpeedMs * (1 + _drift) * seconds;

        while (remaining > 0 && _segment < _path.Count - 1)
        {
            var a = _path[_segment];
            var b = _path[_segment + 1];
            var length = Geo.DistanceMeters(a, b);
            if (length < 0.01)
            {
                _segment++;
                _intoSegment = 0;
                continue;
            }

            Heading = Geo.BearingDegrees(a, b);
            var left = length - _intoSegment;
            if (remaining < left)
            {
                _intoSegment += remaining;
                remaining = 0;
            }
            else
            {
                remaining -= left;
                _segment++;
                _intoSegment = 0;
            }
        }

        Position = CurrentPoint();
        Visited.Add(Cell(Position));
    }

    /// <summary>The current position with a small, slowly wandering GPS error (a few metres), as a phone has.</summary>
    public LatLon NoisyPosition()
    {
        _noiseEast = _noiseEast * 0.85 + (_rng.NextDouble() - 0.5) * 4;
        _noiseNorth = _noiseNorth * 0.85 + (_rng.NextDouble() - 0.5) * 4;
        var dLat = _noiseNorth / 111_320.0;
        var dLon = _noiseEast / (111_320.0 * Math.Max(0.01, Math.Cos(Position.Lat * Math.PI / 180)));
        return new LatLon(Position.Lat + dLat, Position.Lon + dLon);
    }

    /// <summary>The share of a candidate hop's points (after the first few) that are on ground already covered.</summary>
    public double OverlapFraction(IReadOnlyList<LatLon> shape)
    {
        var considered = shape.Skip(3).ToList();
        if (considered.Count == 0)
            return 0;

        return considered.Count(p => Visited.Contains(Cell(p))) / (double)considered.Count;
    }

    private LatLon CurrentPoint()
    {
        if (_segment >= _path.Count - 1)
            return _path[^1];

        var a = _path[_segment];
        var b = _path[_segment + 1];
        var length = Geo.DistanceMeters(a, b);
        var fraction = length < 0.01 ? 0 : Math.Min(1, _intoSegment / length);
        return new LatLon(a.Lat + (b.Lat - a.Lat) * fraction, a.Lon + (b.Lon - a.Lon) * fraction);
    }

    private static (int, int) Cell(LatLon p) =>
        ((int)Math.Round(p.Lat / VisitedCellDegrees), (int)Math.Round(p.Lon / VisitedCellDegrees));
}
