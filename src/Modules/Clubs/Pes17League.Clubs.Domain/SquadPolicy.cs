namespace Pes17League.Clubs.Domain;

public sealed record SquadPolicyOptions
{
    /// <summary>Số người tối thiểu còn lại sau khi bán (PO chốt 01/11: 11 đá chính + 5 dự bị).</summary>
    public int MinSquadSize { get; init; } = 16;
    public int MinGoalkeepers { get; init; } = 2;
    public int MaxSquadSize { get; init; } = 32;
    public int Starters { get; init; } = 11;
}

public enum SquadError
{
    PlayerNotInSquad,
    PlayerInOtherClub,
    AlreadyInSquad,
    SquadFull,
    BelowMinSquadSize,
    BelowMinGoalkeepers,
    InvalidShirtNumber,
    ShirtNumberTaken,
    NoFreeShirtNumber,
}

/// <summary>Một dòng squad theo thứ tự slot (0..Starters-1 là đá chính).</summary>
public sealed record SquadEntry<TId>(TId PlayerId, int ShirtNumber, Position Position);

public sealed record SquadChange<TId>(IReadOnlyList<SquadEntry<TId>> Squad, SquadError? Error = null, bool NeedsLineupReview = false)
{
    public bool Ok => Error is null;
}

public sealed record ShirtNumberChange<TId>(TId PlayerId, int From, int To);

/// <summary>
/// Luật squad thuần (decisions.md 2026-11-01 "Luật squad/chuyển nhượng"), không phụ thuộc game hay DB.
/// Domain dùng với Guid, module option file dùng với ID cầu thủ PES — một bộ luật cho cả hai.
/// Lỗi nghiệp vụ trả trong kết quả, không ném exception.
/// </summary>
public sealed class SquadPolicy<TId>(SquadPolicyOptions? options = null) where TId : notnull
{
    public SquadPolicyOptions Options { get; } = options ?? new();

    /// <summary>
    /// Bán/thanh lý. Dự bị: các dự bị sau dồn lên. Đá chính: lấp bằng dự bị cùng vị trí đầu tiên (giống PES Editor),
    /// không có thì dự bị cùng nhóm vị trí, không có nữa thì dự bị đầu tiên — hai trường hợp sau bật NeedsLineupReview.
    /// </summary>
    public SquadChange<TId> Release(IReadOnlyList<SquadEntry<TId>> squad, TId playerId)
    {
        var i = IndexOf(squad, playerId);
        if (i < 0) return new(squad, SquadError.PlayerNotInSquad);
        if (squad.Count - 1 < Options.MinSquadSize) return new(squad, SquadError.BelowMinSquadSize);
        var pos = squad[i].Position;
        if (pos == Position.GK && squad.Count(e => e.Position == Position.GK) - 1 < Options.MinGoalkeepers)
            return new(squad, SquadError.BelowMinGoalkeepers);

        var list = squad.ToList();
        var review = false;
        if (i < Options.Starters)
        {
            var subs = Enumerable.Range(Options.Starters, Math.Max(0, list.Count - Options.Starters)).ToList();
            var j = subs.FirstOrDefault(k => list[k].Position == pos, -1);
            if (j < 0)
            {
                review = true;
                j = subs.FirstOrDefault(k => list[k].Position.Group() == pos.Group(), -1);
                if (j < 0) j = subs.FirstOrDefault(-1);
            }
            if (j >= 0)
            {
                list[i] = list[j];
                list.RemoveAt(j);
            }
            else list.RemoveAt(i);
        }
        else list.RemoveAt(i);
        return new(list, NeedsLineupReview: review);
    }

    /// <summary>Thêm cầu thủ (đã kiểm không thuộc CLB khác): vào cuối squad, số áo nhỏ nhất còn trống. Đủ người thì chặn.</summary>
    public SquadChange<TId> Add(IReadOnlyList<SquadEntry<TId>> squad, TId playerId, Position position)
    {
        if (IndexOf(squad, playerId) >= 0) return new(squad, SquadError.AlreadyInSquad);
        if (squad.Count >= Options.MaxSquadSize) return new(squad, SquadError.SquadFull);
        if (LowestFreeNumber(squad) is not { } n) return new(squad, SquadError.NoFreeShirtNumber);
        return new([.. squad, new SquadEntry<TId>(playerId, n, position)]);
    }

    public SquadChange<TId> SetShirtNumber(IReadOnlyList<SquadEntry<TId>> squad, TId playerId, int number)
    {
        var i = IndexOf(squad, playerId);
        if (i < 0) return new(squad, SquadError.PlayerNotInSquad);
        if (number is < 1 or > 99) return new(squad, SquadError.InvalidShirtNumber);
        if (squad.Where((_, k) => k != i).Any(e => e.ShirtNumber == number)) return new(squad, SquadError.ShirtNumberTaken);
        var list = squad.ToList();
        list[i] = list[i] with { ShirtNumber = number };
        return new(list);
    }

    /// <summary>Chuẩn hoá số áo trùng/sai (1 lần khi nhập patch mùa 1): người đứng sau đổi sang số trống nhỏ nhất.</summary>
    public static (IReadOnlyList<SquadEntry<TId>> Squad, IReadOnlyList<ShirtNumberChange<TId>> Changes) NormalizeShirtNumbers(IReadOnlyList<SquadEntry<TId>> squad)
    {
        var list = squad.ToList();
        var changes = new List<ShirtNumberChange<TId>>();
        var seen = new HashSet<int>();
        var bad = new List<int>();
        for (var i = 0; i < list.Count; i++)
            if (list[i].ShirtNumber is < 1 or > 99 || !seen.Add(list[i].ShirtNumber)) bad.Add(i);
        foreach (var i in bad)
        {
            var n = Enumerable.Range(1, 99).First(x => !seen.Contains(x));   // ≤ 32 người nên luôn còn số
            seen.Add(n);
            changes.Add(new(list[i].PlayerId, list[i].ShirtNumber, n));
            list[i] = list[i] with { ShirtNumber = n };
        }
        return (list, changes);
    }

    /// <summary>Số áo nhỏ nhất (1–99) chưa ai dùng; null nếu hết số.</summary>
    public static int? LowestFreeNumber(IReadOnlyList<SquadEntry<TId>> squad)
    {
        var used = squad.Select(e => e.ShirtNumber).ToHashSet();
        for (var n = 1; n <= 99; n++)
            if (!used.Contains(n)) return n;
        return null;
    }

    private static int IndexOf(IReadOnlyList<SquadEntry<TId>> squad, TId playerId)
    {
        for (var i = 0; i < squad.Count; i++)
            if (EqualityComparer<TId>.Default.Equals(squad[i].PlayerId, playerId)) return i;
        return -1;
    }
}
