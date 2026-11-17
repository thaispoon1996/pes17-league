namespace Pes17League.OptionFile;

public sealed record SquadSlot(int PlayerId, int ShirtNumber);

/// <summary>Squad CLB theo thứ tự slot (không chứa slot trống). Slot 0–10 là đá chính.</summary>
public sealed record ClubSquad(int Club, IReadOnlyList<SquadSlot> Players)
{
    public const int Starters = 11;

    public int IndexOf(int playerId)
    {
        for (var i = 0; i < Players.Count; i++)
            if (Players[i].PlayerId == playerId) return i;
        return -1;
    }

    public bool Contains(int playerId) => IndexOf(playerId) >= 0;

    /// <summary>Số áo nhỏ nhất (1–99) chưa ai dùng; null nếu hết số.</summary>
    public int? LowestFreeNumber()
    {
        var used = Players.Select(p => p.ShirtNumber).ToHashSet();
        for (var n = 1; n <= 99; n++)
            if (!used.Contains(n)) return n;
        return null;
    }
}
