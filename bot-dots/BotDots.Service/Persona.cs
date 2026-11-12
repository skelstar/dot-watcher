namespace BotDots.Service;

/// <summary>How a bot-dot moves: a pace range in seconds per kilometre, and how common the type is.</summary>
public sealed record Persona(string Name, double PaceMinSecPerKm, double PaceMaxSecPerKm, double Weight)
{
    public static readonly IReadOnlyList<Persona> All =
    [
        new Persona("jogger", 330, 405, 0.5),   // 5:30 - 6:45 /km
        new Persona("steady", 285, 330, 0.3),   // 4:45 - 5:30 /km
        new Persona("walker", 660, 780, 0.2),   // 11 - 13 min/km
    ];

    public static Persona Pick(Random rng, IReadOnlyList<Persona>? from = null)
    {
        var list = from ?? All;
        var roll = rng.NextDouble() * list.Sum(p => p.Weight);
        foreach (var persona in list)
        {
            roll -= persona.Weight;
            if (roll <= 0)
                return persona;
        }

        return list[^1];
    }

    /// <summary>A speed in metres per second for one bot-dot, somewhere in the pace range.</summary>
    public double PickSpeed(Random rng)
    {
        var pace = PaceMinSecPerKm + rng.NextDouble() * (PaceMaxSecPerKm - PaceMinSecPerKm);
        return 1000.0 / pace;
    }
}
