using System.Collections.Concurrent;

namespace Pes17League.Web;

/// <summary>Giao hữu (PO 16/11): ai đang ở sảnh/phòng (online) + báo đổi để các trang mở cùng lúc cập nhật ngay
/// (ghép trận, mời, chọn đội, chat) thay vì chỉ chờ vòng làm mới.</summary>
public sealed class FriendlyHub
{
    public static readonly TimeSpan OnlineFor = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<Guid, (string Name, DateTimeOffset At)> _seen = new();

    /// <summary>Có thay đổi giao hữu (bất kỳ) — trang tự tải lại.</summary>
    public event Action? Changed;

    public void Notify() => Changed?.Invoke();

    /// <summary>Trang sảnh/phòng gọi mỗi vòng làm mới (≤ 5 s).</summary>
    public void Touch(Guid managerId, string name) => _seen[managerId] = (name, DateTimeOffset.UtcNow);

    public void Leave(Guid managerId) => _seen.TryRemove(managerId, out _);

    public List<(Guid Id, string Name)> Online()
    {
        var cut = DateTimeOffset.UtcNow - OnlineFor;
        return _seen.Where(p => p.Value.At > cut).Select(p => (p.Key, p.Value.Name)).OrderBy(p => p.Name).ToList();
    }

    private readonly ConcurrentDictionary<Guid, string> _stage = new();

    /// <summary>Bước máy giao hữu của trận (pesmatch: boot/menu/teams/loading/kickoff); null = xoá. true = đổi.</summary>
    public bool SetStage(Guid matchId, string? stage)
    {
        if (stage is null) return _stage.TryRemove(matchId, out _);
        var old = _stage.GetValueOrDefault(matchId);
        _stage[matchId] = stage;
        return old != stage;
    }

    public string? Stage(Guid matchId) => _stage.GetValueOrDefault(matchId);

    /// <summary>Trận giải người vs người đang chạy trên máy 2 người (PO 17/11); null = không có.</summary>
    public Guid? LeagueFixtureId { get; set; }

    private readonly ConcurrentDictionary<Guid, string> _duelError = new();

    /// <summary>Lỗi lần đá gần nhất của trận giải người vs người (hiện trên trang trận; bấm Sẵn sàng lại để đá lại).</summary>
    public string? DuelError(Guid fixtureId) => _duelError.GetValueOrDefault(fixtureId);

    public void SetDuelError(Guid fixtureId, string? error)
    {
        if (error is null) _duelError.TryRemove(fixtureId, out _); else _duelError[fixtureId] = error;
    }

    public bool IsOnline(Guid managerId) => _seen.TryGetValue(managerId, out var v) && v.At > DateTimeOffset.UtcNow - OnlineFor;
}
