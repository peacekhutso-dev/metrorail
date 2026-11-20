namespace MetroRail.Network;

/// <summary>Pure timetable logic (no I/O) so it is easy to test and reuse.</summary>
public sealed record Stop(int Seq, string StationId, string StationName, int MinutesFromOrigin);

public static class Timetable
{
    /// <summary>Stops in travel order, with minutes from that direction's origin.</summary>
    public static List<Stop> Ordered(IReadOnlyList<Stop> stops, string direction)
    {
        if (direction == "southbound") return stops.OrderBy(s => s.Seq).ToList();
        var rev = stops.OrderByDescending(s => s.Seq).ToList();
        var total = rev[0].MinutesFromOrigin;
        return rev.Select(s => s with { MinutesFromOrigin = total - s.MinutesFromOrigin }).ToList();
    }

    public static string? DirectionBetween(IReadOnlyList<Stop> stops, string fromId, string toId)
    {
        var seq = stops.ToDictionary(s => s.StationId, s => s.Seq);
        if (!seq.ContainsKey(fromId) || !seq.ContainsKey(toId) || fromId == toId) return null;
        return seq[toId] > seq[fromId] ? "southbound" : "northbound";
    }
}
