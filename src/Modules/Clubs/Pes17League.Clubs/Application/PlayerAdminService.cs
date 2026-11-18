using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Application;

/// <summary>Thông tin cầu thủ admin sửa. Name = tên web và tên trong game (≤ 15 ký tự sau khi bỏ dấu); năm sinh, quốc tịch chỉ trên web (tuổi trong game = chỉ số AGE).</summary>
public sealed record PlayerInput(string Name, Position Position, int? BirthYear, string? Nationality);

/// <summary>Dòng danh sách admin: cầu thủ thuộc patch của mùa đang mở. Price = giá mùa nếu đã tính, chưa thì giá gốc theo chỉ số hiện tại.</summary>
public sealed record PlayerListRow(Guid Id, int PesId, string Name, Position Position, int? Age, int Overall, long Price, Guid? ClubId, string? ClubName, bool Visible);

/// <param name="PesId">Ô PES trong patch của mùa đang mở; null = cầu thủ của patch khác.</param>
/// <param name="Start">Chỉ số đầu mùa (snapshot); null = chưa có snapshot mùa đang mở (không sửa chỉ số được).</param>
/// <param name="Current">Chỉ số hiện tại = đầu mùa + tiến hoá theo trận + admin sửa.</param>
public sealed record PlayerDetail(FootballPlayer Player, Season Season, int? PesId, IReadOnlyDictionary<string, int>? Start, IReadOnlyDictionary<string, int>? Current,
    Guid? ClubId, string? ClubName, int? ShirtNumber, bool Starter, PlayerContract? Contract, PlayerPrice? Price, bool Visible);

/// <param name="NeedsLineupReview">Cầu thủ đá chính đổi vị trí / rời đội mà không có dự bị cùng vị trí → CLB nên xếp lại đội hình.</param>
/// <param name="NewStartingGoalkeeper">Thủ môn bắt chính đổi sang vị trí khác → GK dự bị được đưa lên ô 0 (tên).</param>
public sealed record PlayerChangeResult(bool NeedsLineupReview, string? NewStartingGoalkeeper = null);

/// <summary>
/// Admin cầu thủ (bước 3c, docs/domain-admin-design.md mục 5, PO duyệt 06/11). Ghi AdminAuditEntry.
/// Tên, vị trí, chỉ số có hiệu lực trong game ở OF vòng sau (A7, <see cref="RoundOptionFileService"/>); năm sinh, quốc tịch chỉ trên web.
/// Chỉ số sửa = AttributeChange nguồn Admin của mùa đang mở, không tính vào trần ±3 của C19.
/// Phần 2 (A8): tạo cầu thủ ở ô 32768–32951 của patch, xoá hẳn khi chưa từng đá/thuộc CLB.
/// </summary>
public sealed class PlayerAdminService(ClubsDbContext db, TimeProvider clock)
{
    /// <summary>26 chỉ số chính = các trường đầu của PlayerField.Verified (ATTACK … GOAL KEEPING).</summary>
    private static readonly HashSet<PlayerField> Main = [.. PlayerField.Verified.Take(26)];

    private readonly SquadPolicy<Guid> _policy = new();
    private readonly PriceModel _price = new();

    public static PlayerInput Input(FootballPlayer p) => new(p.Name, p.Position, p.BirthYear, p.Nationality);

    /// <summary>Khoảng admin được nhập: 26 chỉ số chính 1–99 (như C19), trường khác theo OF, tối thiểu 1.</summary>
    public static (int Min, int Max) Range(PlayerField f) => Main.Contains(f) ? (1, 99) : (Math.Max(f.Min, 1), f.Max);

    /// <summary>Cầu thủ thuộc patch mùa đang mở: OVR theo chỉ số hiện tại, giá mùa nếu đã tính (chưa thì giá gốc); sắp OVR giảm dần.</summary>
    public async Task<(Season Season, IReadOnlyList<PlayerListRow> Rows)> ListAsync(CancellationToken ct = default)
    {
        var season = await OpenSeasonAsync(ct);
        var players = await (from pm in db.PlayerPesMappings
                             where pm.PatchVersionId == season.PatchVersionId
                             join p in db.Players on pm.PlayerId equals p.Id
                             select new { p.Id, pm.PesPlayerId, p.Name, p.Position, p.MarketVisibility }).ToListAsync(ct);
        var values = await new EconomyService(db, clock).CurrentAttributesAsync(season.Id, null, ct);
        var prices = await db.PlayerPrices.Where(x => x.SeasonId == season.Id).ToDictionaryAsync(x => x.PlayerId, x => x.Price, ct);
        var clubs = await (from m in db.SquadMemberships
                           where m.LeftAt == null
                           join c in db.Clubs on m.ClubId equals c.Id
                           select new { m.PlayerId, m.ClubId, c.Name }).ToDictionaryAsync(x => x.PlayerId, ct);
        var rows = players.Select(p =>
        {
            var v = values.GetValueOrDefault(p.Id) ?? [];
            var overall = _price.Overall(p.Position, v);
            int? age = v.TryGetValue(PlayerField.Age.Name, out var a) ? a : null;
            var club = clubs.GetValueOrDefault(p.Id);
            return new PlayerListRow(p.Id, p.PesPlayerId, p.Name, p.Position, age, overall,
                prices.TryGetValue(p.Id, out var price) ? price : _price.BasePrice(overall, age ?? 27), club?.ClubId, club?.Name,
                MarketVisibilityService.IsVisible(p.MarketVisibility, p.PesPlayerId, club is not null));
        });
        return (season, [.. rows.OrderByDescending(r => r.Overall).ThenBy(r => r.Name)]);
    }

    public async Task<PlayerDetail?> DetailAsync(Guid playerId, CancellationToken ct = default)
    {
        var p = await db.Players.AsNoTracking().SingleOrDefaultAsync(x => x.Id == playerId, ct);
        if (p is null) return null;
        var season = await OpenSeasonAsync(ct);
        var pesId = await db.PlayerPesMappings.Where(m => m.PlayerId == playerId && m.PatchVersionId == season.PatchVersionId)
            .Select(m => (int?)m.PesPlayerId).FirstOrDefaultAsync(ct);
        var start = (await db.PlayerAttributes.AsNoTracking().FirstOrDefaultAsync(a => a.SeasonId == season.Id && a.PlayerId == playerId, ct))?.Values;
        var current = start is null ? null : (await new EconomyService(db, clock).CurrentAttributesAsync(season.Id, [playerId], ct))[playerId];
        var member = await (from m in db.SquadMemberships
                            where m.PlayerId == playerId && m.LeftAt == null
                            join c in db.Clubs on m.ClubId equals c.Id
                            select new { m.ClubId, c.Name, m.ShirtNumber, m.SlotOrder }).FirstOrDefaultAsync(ct);
        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(k => k.PlayerId == playerId && k.Status == ContractStatus.Active, ct);
        var price = await db.PlayerPrices.AsNoTracking().FirstOrDefaultAsync(x => x.SeasonId == season.Id && x.PlayerId == playerId, ct);
        return new(p, season, pesId, start, current, member?.ClubId, member?.Name, member?.ShirtNumber, member?.SlotOrder < _policy.Options.Starters,
            contract, price, MarketVisibilityService.IsVisible(p.MarketVisibility, pesId ?? 0, member is not null));
    }

    /// <summary>
    /// Sửa tên/vị trí/năm sinh/quốc tịch. Thủ môn đổi vị trí: chặn nếu CLB còn dưới 2 GK; đang bắt chính → GK dự bị đầu tiên lên ô 0
    /// (ô 0 chỉ GK được đứng, như <see cref="LineupService"/>). Đổi vị trí và mùa đang mở đã có giá → tính lại OVR + giá.
    /// </summary>
    public async Task<PlayerChangeResult> UpdateInfoAsync(Guid playerId, PlayerInput input, string actor, CancellationToken ct = default)
    {
        var p = await PlayerAsync(playerId, ct);
        input = Normalize(input);
        if (input == Input(p)) return new(false);
        var e = Errors(input, clock.GetUtcNow().Year);
        if (e.Count > 0) throw new AdminRuleException(string.Join("; ", e));

        var result = new PlayerChangeResult(false);
        if (input.Position != p.Position && await db.SquadMemberships.FirstOrDefaultAsync(m => m.PlayerId == playerId && m.LeftAt == null, ct) is { } member)
        {
            var club = await db.Clubs.SingleAsync(c => c.Id == member.ClubId, ct);
            var squad = await SquadStore.LoadAsync(db, club.Id, ct);
            var list = squad.Entries.Select(x => x.PlayerId == playerId ? x with { Position = input.Position } : x).ToList();
            var goalkeepers = list.Count(x => x.Position == Position.GK);
            if (p.Position == Position.GK && goalkeepers < _policy.Options.MinGoalkeepers)
                throw new AdminRuleException($"{club.Name} sẽ chỉ còn {goalkeepers} thủ môn (tối thiểu {_policy.Options.MinGoalkeepers})");
            var i = list.FindIndex(x => x.PlayerId == playerId);
            string? keeper = null;
            if (i == 0 && p.Position == Position.GK && input.Position != Position.GK)
            {
                var j = list.FindIndex(1, x => x.Position == Position.GK);
                (list[0], list[j]) = (list[j], list[0]);
                SquadStore.Apply(db, club, squad, new SquadChange<Guid>(list), clock.GetUtcNow());
                keeper = await db.Players.Where(x => x.Id == list[0].PlayerId).Select(x => x.Name).SingleAsync(ct);
            }
            result = new(i < _policy.Options.Starters, keeper);
        }
        var before = Snapshot(p);
        var moved = input.Position != p.Position;
        (p.Name, p.Position, p.BirthYear, p.Nationality) = (input.Name, input.Position, input.BirthYear, input.Nationality);
        Audit(playerId, "Update", before, Snapshot(p), actor);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        if (moved) await RecalculatePriceAsync(playerId, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>
    /// Sửa chỉ số (giá trị mới theo tên trường PlayerField). Ghi AttributeChange nguồn Admin của mùa đang mở = phần chênh với chỉ số hiện tại;
    /// mùa đã có giá → tính lại OVR + giá gốc (giữ hệ số phong độ; lương hợp đồng giữ nguyên). Trả số trường đã đổi.
    /// </summary>
    public async Task<int> SetAttributesAsync(Guid playerId, IReadOnlyDictionary<string, int> values, string actor, CancellationToken ct = default)
    {
        var p = await PlayerAsync(playerId, ct);
        var season = await OpenSeasonAsync(ct);
        var current = (await new EconomyService(db, clock).CurrentAttributesAsync(season.Id, [playerId], ct)).GetValueOrDefault(playerId)
            ?? throw new AdminRuleException($"{p.Name} chưa có chỉ số {season.Name}");
        var fields = PlayerField.Verified.ToDictionary(f => f.Name);
        var e = new List<string>();
        foreach (var (name, v) in values)
            if (!fields.TryGetValue(name, out var f)) e.Add($"Không có chỉ số {name}");
            else if (Range(f) is var (min, max) && (v < min || v > max)) e.Add($"{name} {min}–{max}");
        if (e.Count > 0) throw new AdminRuleException(string.Join("; ", e));
        var changed = values.Where(kv => current[kv.Key] != kv.Value).ToDictionary();
        if (changed.Count == 0) return 0;

        var now = clock.GetUtcNow();
        foreach (var (name, v) in changed)
            db.AttributeChanges.Add(new AttributeChange
            {
                SeasonId = season.Id, PlayerId = playerId, Source = AttributeChangeSource.Admin, Field = name, Delta = v - current[name], At = now,
            });
        Audit(playerId, "SetAttributes", changed.ToDictionary(c => c.Key, c => current[c.Key]), changed, actor);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await RecalculatePriceAsync(playerId, ct);
        await tx.CommitAsync(ct);
        return changed.Count;
    }

    /// <summary>
    /// Giải nghệ cầu thủ patch (PO chọn 06/11): rời CLB theo luật thanh lý (chặn nếu CLB còn dưới 16 người hoặc dưới 2 GK; đá chính → dự bị cùng vị trí lên),
    /// hợp đồng kết thúc ngay, không tiền; đề nghị gia hạn đang chờ bị huỷ; ẩn khỏi thị trường. Hiện lại: <see cref="SetVisibilityAsync"/> (true).
    /// </summary>
    public async Task<PlayerChangeResult> RetireAsync(Guid playerId, string actor, CancellationToken ct = default)
    {
        var p = await PlayerAsync(playerId, ct);
        var now = clock.GetUtcNow();
        var member = await db.SquadMemberships.FirstOrDefaultAsync(m => m.PlayerId == playerId && m.LeftAt == null, ct);
        if (member is null && p.MarketVisibility == false) throw new AdminRuleException($"{p.Name} đã giải nghệ (ẩn khỏi thị trường)");
        var review = false;
        string? clubName = null;
        if (member is not null)
        {
            var club = await db.Clubs.SingleAsync(c => c.Id == member.ClubId, ct);
            var squad = await SquadStore.LoadAsync(db, club.Id, ct);
            var change = _policy.Release(squad.Entries, playerId);
            if (change.Error is { } error)
                throw new AdminRuleException(error switch
                {
                    SquadError.BelowMinSquadSize => $"{club.Name} chỉ còn {squad.Entries.Count} cầu thủ (tối thiểu {_policy.Options.MinSquadSize})",
                    SquadError.BelowMinGoalkeepers => $"{club.Name} sẽ dưới {_policy.Options.MinGoalkeepers} thủ môn",
                    _ => error.ToString(),
                });
            SquadStore.Apply(db, club, squad, change, now);
            (review, clubName) = (change.NeedsLineupReview, club.Name);
        }
        foreach (var k in await db.Contracts.Where(x => x.PlayerId == playerId && x.Status == ContractStatus.Active).ToListAsync(ct))
            (k.Status, k.EndedAt, k.EndReason) = (ContractStatus.Ended, now, ContractEndReason.Retired);
        foreach (var r in await db.ContractRenewals.Where(r => r.PlayerId == playerId && r.Status == RenewalStatus.Pending).ToListAsync(ct))
            (r.Status, r.RejectReason, r.DecidedAt) = (RenewalStatus.Cancelled, "PlayerRetired", now);
        Audit(playerId, "Retire", new { Club = clubName, p.MarketVisibility }, new { MarketVisibility = false, NeedsLineupReview = review }, actor);
        p.MarketVisibility = false;
        await db.SaveChangesAsync(ct);
        return new(review);
    }

    /// <summary>Ẩn/hiện trên thị trường như trang /an-hien nhưng có nhật ký và không cần mùa đang đá; true = "còn đá" (sau giải nghệ), null = theo luật.</summary>
    public async Task SetVisibilityAsync(Guid playerId, bool? visible, string actor, CancellationToken ct = default)
    {
        var p = await PlayerAsync(playerId, ct);
        if (p.MarketVisibility == visible) return;
        Audit(playerId, "SetVisibility", new { p.MarketVisibility }, new { MarketVisibility = visible }, actor);
        p.MarketVisibility = visible;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Số ô tạo cầu thủ còn trống (trên <see cref="OptionFileImage.EditCount"/>) trong patch của mùa đang mở.</summary>
    public async Task<int> FreeSlotsAsync(CancellationToken ct = default)
    {
        var season = await OpenSeasonAsync(ct);
        // PES17 (UI 18/11): ID cầu thủ thật cũng ≥ FirstEditId → phép trừ ra số âm; không còn ô an toàn để tạo
        return Math.Max(0, OptionFileImage.EditCount - await db.PlayerPesMappings
            .CountAsync(m => m.PatchVersionId == season.PatchVersionId && m.PesPlayerId >= OptionFileImage.FirstEditId, ct));
    }

    /// <summary>
    /// Chỉ số gợi ý khi tạo cầu thủ (PO chọn 06/11): trung vị (dưới) từng trường trong snapshot mùa đang mở của cầu thủ patch cùng vị trí đăng ký,
    /// bỏ dải ẩn/ML; vị trí không có ai → mọi vị trí. Admin sửa được hết trước khi lưu.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> TemplateAsync(Position position, CancellationToken ct = default)
    {
        var season = await OpenSeasonAsync(ct);
        var rows = (await (from pm in db.PlayerPesMappings
                           where pm.PatchVersionId == season.PatchVersionId && pm.PesPlayerId < OptionFileImage.FirstEditId
                           join p in db.Players on pm.PlayerId equals p.Id
                           join a in db.PlayerAttributes on new { PlayerId = p.Id, SeasonId = season.Id } equals new { a.PlayerId, a.SeasonId }
                           select new { pm.PesPlayerId, p.Position, a.Values }).ToListAsync(ct))
            .Where(r => !OptionFileImage.IsHiddenPlayerId(r.PesPlayerId)).ToList();
        if (rows.Count == 0) throw new AdminRuleException($"{season.Name} chưa có chỉ số cầu thủ patch");
        if (rows.Any(r => r.Position == position)) rows = rows.Where(r => r.Position == position).ToList();
        return PlayerField.Verified.ToDictionary(f => f.Name, f =>
        {
            var sorted = rows.Where(r => r.Values.ContainsKey(f.Name)).Select(r => r.Values[f.Name]).Order().ToList();
            var (min, max) = Range(f);
            return Math.Clamp(sorted.Count == 0 ? min : sorted[(sorted.Count - 1) / 2], min, max);
        });
    }

    /// <summary>
    /// Tạo cầu thủ (A8, PO chọn 06/11): ô trống thấp nhất 32768–32951 của patch mùa đang mở, thành cầu thủ tự do (hiện trên thị trường theo luật, CLB mua theo giá),
    /// snapshot chỉ số mùa đang mở = <paramref name="values"/> (đủ 33 trường). Mùa đã có giá → tính giá luôn. Vào game từ OF vòng sau, không có ảnh mặt.
    /// </summary>
    public async Task<Guid> CreateAsync(PlayerInput input, IReadOnlyDictionary<string, int> values, string actor, CancellationToken ct = default)
    {
        var season = await OpenSeasonAsync(ct);
        input = Normalize(input);
        var e = Errors(input, clock.GetUtcNow().Year);
        foreach (var f in PlayerField.Verified)
            if (!values.TryGetValue(f.Name, out var v)) e.Add($"Thiếu chỉ số {f.Name}");
            else if (Range(f) is var (min, max) && (v < min || v > max)) e.Add($"{f.Name} {min}–{max}");
        e.AddRange(values.Keys.Where(k => PlayerField.Verified.All(f => f.Name != k)).Select(k => $"Không có chỉ số {k}"));
        if (e.Count > 0) throw new AdminRuleException(string.Join("; ", e));
        if (await FreeSlotsAsync(ct) == 0) throw new AdminRuleException("Hết ô tạo cầu thủ (patch PES 17: ô PES trùng cầu thủ thật, chưa hỗ trợ tạo mới)");
        var used = (await db.PlayerPesMappings.Where(m => m.PatchVersionId == season.PatchVersionId && m.PesPlayerId >= OptionFileImage.FirstEditId)
            .Select(m => m.PesPlayerId).ToListAsync(ct)).ToHashSet();
        var slot = Enumerable.Range(OptionFileImage.FirstEditId, OptionFileImage.EditCount).FirstOrDefault(i => !used.Contains(i));
        if (slot == 0) throw new AdminRuleException($"Hết ô tạo cầu thủ ({OptionFileImage.EditCount}/{OptionFileImage.EditCount})");

        var p = new FootballPlayer { Name = input.Name, Position = input.Position, BirthYear = input.BirthYear, Nationality = input.Nationality };
        var snapshot = PlayerField.Verified.ToDictionary(f => f.Name, f => values[f.Name]);
        db.Players.Add(p);
        db.PlayerPesMappings.Add(new PlayerPesMapping { PlayerId = p.Id, PatchVersionId = season.PatchVersionId!.Value, PesPlayerId = slot });
        db.PlayerAttributes.Add(new PlayerAttributes { PlayerId = p.Id, SeasonId = season.Id, Values = snapshot, CapturedAt = clock.GetUtcNow() });
        // Name để cấp ngoài: trang nhật ký lấy tên cầu thủ đã xoá hẳn từ ảnh chụp.
        Audit(p.Id, "Create", null, new { p.Name, Position = p.Position.ToString(), p.BirthYear, p.Nationality, PesId = slot, Values = snapshot }, actor);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await RecalculatePriceAsync(p.Id, ct);
        await tx.CommitAsync(ct);
        return p.Id;
    }

    /// <summary>
    /// Lý do không xoá hẳn được; null = xoá được (PO chọn 06/11): chỉ cầu thủ admin tạo (ô 32768+) chưa từng ra sân (điểm trận, bàn thắng, tiến hoá theo trận)
    /// và chưa từng thuộc CLB (squad, hợp đồng, chuyển nhượng, đề nghị gia hạn). Còn lại dùng giải nghệ.
    /// </summary>
    public async Task<string?> DeleteBlockAsync(Guid playerId, CancellationToken ct = default)
    {
        var season = await OpenSeasonAsync(ct);
        var pesId = await db.PlayerPesMappings.Where(m => m.PlayerId == playerId && m.PatchVersionId == season.PatchVersionId)
            .Select(m => (int?)m.PesPlayerId).FirstOrDefaultAsync(ct);
        if (pesId is not >= OptionFileImage.FirstEditId) return "Cầu thủ của patch chỉ giải nghệ được, không xoá";
        if (await db.MatchRatings.AnyAsync(r => r.PlayerId == playerId, ct) || await db.Goals.AnyAsync(g => g.ScorerPlayerId == playerId, ct)
            || await db.AttributeChanges.AnyAsync(c => c.PlayerId == playerId && c.Source == AttributeChangeSource.Match, ct))
            return "Đã ra sân: chỉ giải nghệ được";
        if (await db.SquadMemberships.AnyAsync(m => m.PlayerId == playerId, ct) || await db.Contracts.AnyAsync(k => k.PlayerId == playerId, ct)
            || await db.Transfers.AnyAsync(t => t.PlayerId == playerId, ct) || await db.ContractRenewals.AnyAsync(r => r.PlayerId == playerId, ct))
            return "Đã từng thuộc CLB: chỉ giải nghệ được";
        return null;
    }

    /// <summary>Xoá hẳn cầu thủ admin tạo (<see cref="DeleteBlockAsync"/> = null): ô PES trống lại cho lần tạo sau; nhật ký giữ thông tin + chỉ số lúc xoá.</summary>
    public async Task DeleteAsync(Guid playerId, string actor, CancellationToken ct = default)
    {
        var p = await PlayerAsync(playerId, ct);
        if (await DeleteBlockAsync(playerId, ct) is { } reason) throw new AdminRuleException(reason);
        var mappings = await db.PlayerPesMappings.Where(m => m.PlayerId == playerId).ToListAsync(ct);
        var attributes = await db.PlayerAttributes.Where(a => a.PlayerId == playerId).ToListAsync(ct);
        Audit(playerId, "Delete", new { p.Name, Position = p.Position.ToString(), p.BirthYear, p.Nationality,
            PesIds = mappings.Select(m => m.PesPlayerId), Values = attributes.Select(a => a.Values) }, null, actor);
        db.AttributeChanges.RemoveRange(await db.AttributeChanges.Where(c => c.PlayerId == playerId).ToListAsync(ct));
        db.PlayerAttributes.RemoveRange(attributes);
        db.PlayerPrices.RemoveRange(await db.PlayerPrices.Where(x => x.PlayerId == playerId).ToListAsync(ct));
        db.PlayerPesMappings.RemoveRange(mappings);
        db.Players.Remove(p);
        await db.SaveChangesAsync(ct);
    }

    private async Task<FootballPlayer> PlayerAsync(Guid id, CancellationToken ct) =>
        await db.Players.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new AdminRuleException("Không tìm thấy cầu thủ");

    /// <summary>Mùa chưa kết thúc sớm nhất (như ClubAdminService): chỉ số sửa thuộc mùa này, danh sách theo patch của nó.</summary>
    private Task<Season?> FindOpenSeasonAsync(CancellationToken ct) =>
        db.Seasons.Where(s => s.Status != SeasonStatus.Finished).OrderBy(s => s.Number).FirstOrDefaultAsync(ct);

    private async Task<Season> OpenSeasonAsync(CancellationToken ct)
    {
        var season = await FindOpenSeasonAsync(ct) ?? throw new AdminRuleException("Chưa có mùa đang mở");
        return season.PatchVersionId is null ? throw new AdminRuleException($"{season.Name} chưa nhập patch") : season;
    }

    /// <summary>Mùa đang mở đã có giá (đã khởi động) → tính lại OVR + giá gốc cầu thủ này (như ClubAdminService.CreateAsync).</summary>
    private async Task RecalculatePriceAsync(Guid playerId, CancellationToken ct)
    {
        if (await FindOpenSeasonAsync(ct) is { } season && await db.PlayerPrices.AnyAsync(x => x.SeasonId == season.Id, ct))
            await new EconomyService(db, clock).RecalculatePricesAsync(season.Id, ct, [playerId]);
    }

    private static List<string> Errors(PlayerInput i, int year)
    {
        var e = new List<string>();
        var game = GameText.ToDisplayable(i.Name);
        if (game.Length is 0 or > OptionFileImage.MaxNameLength) e.Add($"Tên 1–{OptionFileImage.MaxNameLength} ký tự (tên trong game: {game})");
        else if (game.Count(ch => ch == '?') > i.Name.Count(ch => ch == '?')) e.Add($"Tên có ký tự game không hiện được: {game}");
        if (!Enum.IsDefined(i.Position)) e.Add("Vị trí không hợp lệ");
        if (i.BirthYear is < 1900 || i.BirthYear > year) e.Add($"Năm sinh 1900–{year}");
        if (i.Nationality?.Length > 3) e.Add("Mã quốc tịch tối đa 3 ký tự");
        return e;
    }

    private static PlayerInput Normalize(PlayerInput i) => i with
    {
        Name = i.Name.Trim().Normalize(NormalizationForm.FormC),
        Nationality = string.IsNullOrWhiteSpace(i.Nationality) ? null : i.Nationality.Trim().ToUpperInvariant(),
    };

    private static object Snapshot(FootballPlayer p) => new { p.Name, Position = p.Position.ToString(), p.BirthYear, p.Nationality };

    private void Audit(Guid id, string action, object? before, object? after, string actor) =>
        db.AdminAudit.Add(new AdminAuditEntry { At = clock.GetUtcNow(), Actor = actor, EntityType = "Player", EntityId = id, Action = action,
            Before = before is null ? null : JsonSerializer.Serialize(before), After = after is null ? null : JsonSerializer.Serialize(after) });
}
