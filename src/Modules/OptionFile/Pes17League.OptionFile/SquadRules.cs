namespace Pes17League.OptionFile;

public sealed record SquadRulesOptions
{
    /// <summary>Số người tối thiểu còn lại sau khi bán (PO chốt 01/11: 11 đá chính + 5 dự bị).</summary>
    public int MinSquadSize { get; init; } = 16;
    public int MinGoalkeepers { get; init; } = 2;
}

/// <summary>Kết quả một thao tác squad. Lỗi nghiệp vụ trả về ở đây, không ném exception.</summary>
public sealed record SquadResult(ClubSquad Squad, SquadError? Error = null, bool NeedsLineupReview = false)
{
    public bool Ok => Error is null;
}

public sealed record ShirtNumberChange(int Club, int PlayerId, int From, int To);

/// <summary>
/// Adapter của <see cref="SquadPolicy{TId}"/> (luật chung với domain) trên option file: vị trí lấy từ OF.
/// Các hàm nhận và trả <see cref="ClubSquad"/> mới, không sửa OF; ghi bằng <see cref="OptionFileImage.SetSquad"/>.
/// </summary>
public sealed class SquadRules(OptionFileImage image, SquadRulesOptions? options = null)
{
    private readonly SquadPolicy<int> _policy = new(new SquadPolicyOptions
    {
        MinSquadSize = (options ?? new()).MinSquadSize,
        MinGoalkeepers = (options ?? new()).MinGoalkeepers,
        MaxSquadSize = OptionFileImage.SquadSize,
        Starters = ClubSquad.Starters,
    });

    /// <summary>Bán/thanh lý; cách lấp đội hình xem <see cref="SquadPolicy{TId}.Release"/>.</summary>
    public SquadResult Release(ClubSquad squad, int playerId) => Wrap(squad, _policy.Release(Entries(squad), playerId));

    /// <summary>Mua cầu thủ tự do: vào slot trống đầu tiên, số áo nhỏ nhất còn trống. Đủ 32 người thì chặn.</summary>
    public SquadResult Sign(ClubSquad squad, int playerId)
    {
        if (FindClub(playerId) is { } other && other != squad.Club) return new(squad, SquadError.PlayerInOtherClub);
        return Add(squad, playerId);
    }

    private SquadResult Add(ClubSquad squad, int playerId) =>
        Wrap(squad, _policy.Add(Entries(squad), playerId, image.GetPosition(playerId)));

    /// <summary>Chuyển nhượng giữa 2 CLB: kiểm cả hai bên trước, chỉ ghi OF khi cả hai hợp lệ.</summary>
    public (SquadResult From, SquadResult To) Transfer(int fromClub, int toClub, int playerId, bool apply = true)
    {
        var r1 = Release(image.GetSquad(fromClub), playerId);
        var to = image.GetSquad(toClub);
        if (!r1.Ok) return (r1, new(to));
        var r2 = Add(to, playerId);
        if (apply && r2.Ok)
        {
            image.SetSquad(r1.Squad);
            image.SetSquad(r2.Squad);
        }
        return (r1, r2);
    }

    public SquadResult SetShirtNumber(ClubSquad squad, int playerId, int number) =>
        Wrap(squad, _policy.SetShirtNumber(Entries(squad), playerId, number));

    /// <summary>Chuẩn hoá số áo trùng/sai (chạy 1 lần khi sinh OF mùa 1): người đứng sau đổi sang số trống nhỏ nhất.</summary>
    public static (ClubSquad Squad, IReadOnlyList<ShirtNumberChange> Changes) NormalizeShirtNumbers(ClubSquad squad)
    {
        // Vị trí không ảnh hưởng chuẩn hoá số áo → không cần đọc OF.
        var (list, changes) = SquadPolicy<int>.NormalizeShirtNumbers(
            squad.Players.Select(p => new SquadEntry<int>(p.PlayerId, p.ShirtNumber, Position.GK)).ToList());
        return (ToSquad(squad, list), changes.Select(c => new ShirtNumberChange(squad.Club, c.PlayerId, c.From, c.To)).ToList());
    }

    public int Goalkeepers(ClubSquad squad) => squad.Players.Count(p => image.GetPosition(p.PlayerId) == Position.GK);

    /// <summary>CLB (0–139) đang có cầu thủ này, null nếu tự do.</summary>
    public int? FindClub(int playerId)
    {
        for (var c = 0; c < OptionFileImage.ClubCount; c++)
            if (image.GetSquad(c).Contains(playerId)) return c;
        return null;
    }

    private List<SquadEntry<int>> Entries(ClubSquad squad) =>
        squad.Players.Select(p => new SquadEntry<int>(p.PlayerId, p.ShirtNumber, image.GetPosition(p.PlayerId))).ToList();

    private static ClubSquad ToSquad(ClubSquad squad, IReadOnlyList<SquadEntry<int>> list) =>
        squad with { Players = list.Select(e => new SquadSlot(e.PlayerId, e.ShirtNumber)).ToList() };

    private static SquadResult Wrap(ClubSquad squad, SquadChange<int> r) =>
        r.Ok ? new(ToSquad(squad, r.Squad), NeedsLineupReview: r.NeedsLineupReview) : new(squad, r.Error);
}
