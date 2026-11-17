using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Application;

/// <summary>Thông tin CLB admin sửa trên web. Tên/viết tắt trong game theo patch, chỉ đổi đầu mùa (C15, chưa làm); logo trong game nằm ở AFS.</summary>
public sealed record ClubInput(string Name, string? ShortName, string? Country, string? LogoPath);

/// <summary>Ô PES chưa gán CLB trong patch của mùa đang mở: tên/viết tắt và squad trong OF gốc.</summary>
public sealed record SpareSlot(int Slot, string GameName, string GameAbbr, int Players, int Goalkeepers);

/// <param name="AddedGoalkeepers">GK tự do bổ sung vì ô thiếu thủ môn.</param>
/// <param name="Skipped">Cầu thủ của ô đang thuộc CLB khác → không nhận.</param>
public sealed record CreateClubResult(Guid ClubId, int Players, IReadOnlyList<string> AddedGoalkeepers, IReadOnlyList<string> Skipped);

/// <summary>
/// Admin CLB (bước 3b, docs/domain-admin-design.md mục 5): sửa tên, tên ngắn, nước, logo; thêm CLB vào ô PES dư; xoá/lưu trữ. Ghi AdminAuditEntry.
/// Thêm/xem ô dư cần <paramref name="store"/> (đọc OF gốc của patch).
/// </summary>
public sealed class ClubAdminService(ClubsDbContext db, TimeProvider clock, IOptionFileStore? store = null)
{
    /// <summary>Ô 125 (Slovan Bratislava) không có trong menu chọn đội của game (mục 9) → không dùng.</summary>
    public static readonly IReadOnlySet<int> NotInGameMenu = new HashSet<int> { 125 };

    private readonly SquadPolicy<int> _policy = new();

    public static ClubInput Input(Club c) => new(c.Name, c.ShortName, c.Country, c.LogoPath);

    public async Task UpdateAsync(Guid id, ClubInput input, string actor, CancellationToken ct = default)
    {
        var c = await db.Clubs.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new AdminRuleException("Không tìm thấy CLB");
        input = Normalize(input);
        if (input == Input(c)) return;
        var e = Errors(input);
        // CLB alpha đã lưu trữ trùng tên CLB liên đoàn → chỉ cấm trùng giữa CLB đang hoạt động.
        if (c.Status == ClubStatus.Active && await db.Clubs.AnyAsync(x => x.Id != id && x.Status == ClubStatus.Active && x.Name == input.Name, ct))
            e.Add($"Đã có CLB tên {input.Name}");
        if (e.Count > 0) throw new AdminRuleException(string.Join("; ", e));
        var before = Snapshot(c);
        (c.Name, c.ShortName, c.Country, c.LogoPath) = (input.Name, input.ShortName, input.Country, input.LogoPath);
        Audit(id, "Update", before, Snapshot(c), actor);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Ô dư của patch mùa đang mở (trừ ô không có trong menu game), theo số ô.</summary>
    public async Task<IReadOnlyList<SpareSlot>> SpareSlotsAsync(CancellationToken ct = default)
    {
        var (_, patch) = await OpenSeasonAsync(ct);
        var image = await BaseImageAsync(patch, ct);
        var taken = (await db.ClubPesMappings.Where(m => m.PatchVersionId == patch.Id).Select(m => m.PesClubIndex).ToListAsync(ct)).ToHashSet();
        return Enumerable.Range(0, OptionFileImage.ClubCount).Where(i => !taken.Contains(i) && !NotInGameMenu.Contains(i)).Select(i =>
        {
            var squad = image.GetSquad(i).Players;
            return new SpareSlot(i, image.GetClubName(i), image.GetClubAbbreviation(i), squad.Count, squad.Count(s => image.GetPosition(s.PlayerId) == Position.GK));
        }).ToList();
    }

    /// <summary>
    /// Thêm CLB vào ô PES dư của patch mùa đang mở (PO chọn 05/11): đội hình = cầu thủ của ô trong OF gốc (tạo mới, CLB gốc = CLB này,
    /// snapshot chỉ số mùa); người của ô đã có trong DB thì đang thuộc CLB khác → bỏ qua, tự do → nhận. Thiếu thủ môn → bổ sung GK tự do
    /// như nhập patch (GOAL KEEPING thấp nhất mà ≥ 70, hoà: trẻ hơn); vẫn thiếu thì chặn (OF vòng kiểm mọi CLB đã gán ô).
    /// Hợp đồng = số trận mặc định của mùa (mùa 1: 36); mùa đã có giá → tính giá + lương cho người mới, chưa có thì đầu mùa tính chung.
    /// Không tự chia ngân sách.
    /// </summary>
    public async Task<CreateClubResult> CreateAsync(int slot, ClubInput input, string actor, CancellationToken ct = default)
    {
        input = Normalize(input);
        var e = Errors(input);
        if (await db.Clubs.AnyAsync(x => x.Status == ClubStatus.Active && x.Name == input.Name, ct)) e.Add($"Đã có CLB tên {input.Name}");
        if (e.Count > 0) throw new AdminRuleException(string.Join("; ", e));
        if (slot is < 0 or >= OptionFileImage.ClubCount || NotInGameMenu.Contains(slot)) throw new AdminRuleException($"Ô PES {slot} không dùng được");
        var (season, patch) = await OpenSeasonAsync(ct);
        if (await (from m in db.ClubPesMappings
                   join c in db.Clubs on m.ClubId equals c.Id
                   where m.PatchVersionId == patch.Id && m.PesClubIndex == slot
                   select c.Name).FirstOrDefaultAsync(ct) is { } owner)
            throw new AdminRuleException($"Ô {slot} đã có CLB {owner}");
        var image = await BaseImageAsync(patch, ct);

        // Người của ô đã có trong DB (PES ID → cầu thủ); đang có squad → bỏ qua.
        var squad = image.GetSquad(slot).Players;
        var pesIds = squad.Select(s => s.PlayerId).ToList();
        var known = await db.PlayerPesMappings.Where(m => m.PatchVersionId == patch.Id && pesIds.Contains(m.PesPlayerId))
            .ToDictionaryAsync(m => m.PesPlayerId, m => m.PlayerId, ct);
        var knownIds = known.Values.ToList();
        var owned = (await db.SquadMemberships.Where(m => knownIds.Contains(m.PlayerId) && m.LeftAt == null).Select(m => m.PlayerId).ToListAsync(ct)).ToHashSet();
        var skipped = squad.Where(s => known.TryGetValue(s.PlayerId, out var p) && owned.Contains(p)).Select(s => image.GetPlayerName(s.PlayerId)).ToList();
        var (entries, _) = SquadPolicy<int>.NormalizeShirtNumbers(squad
            .Where(s => !(known.TryGetValue(s.PlayerId, out var p) && owned.Contains(p)))
            .Select(s => new SquadEntry<int>(s.PlayerId, s.ShirtNumber, image.GetPosition(s.PlayerId))).ToList());
        if (entries.Count < _policy.Options.MinSquadSize)
            throw new AdminRuleException($"Ô {slot} chỉ có {entries.Count} cầu thủ dùng được (tối thiểu {_policy.Options.MinSquadSize})");

        var added = new List<string>();
        int Goalkeepers() => entries.Count(x => x.Position == Position.GK);
        if (Goalkeepers() < _policy.Options.MinGoalkeepers)
        {
            var free = (await (from p in db.Players
                               join m in db.PlayerPesMappings on p.Id equals m.PlayerId
                               where m.PatchVersionId == patch.Id && p.Position == Position.GK
                                     && !db.SquadMemberships.Any(s => s.PlayerId == p.Id && s.LeftAt == null)
                               select new { p.Id, p.Name, p.MarketVisibility, m.PesPlayerId }).ToListAsync(ct))
                .Where(p => MarketVisibilityService.IsVisible(p.MarketVisibility, p.PesPlayerId, false) && !pesIds.Contains(p.PesPlayerId)).ToList();
            var values = await new EconomyService(db, clock).CurrentAttributesAsync(season.Id, free.Select(p => p.Id).ToList(), ct);
            int Value(Guid id, PlayerField f) => values.GetValueOrDefault(id)?.GetValueOrDefault(f.Name) ?? 0;
            var floor = new ImportOptions().GoalkeeperFloor;
            foreach (var gk in free.Where(p => Value(p.Id, PlayerField.GoalKeeping) >= floor)
                         .OrderBy(p => Value(p.Id, PlayerField.GoalKeeping)).ThenBy(p => Value(p.Id, PlayerField.Age)).ThenBy(p => p.PesPlayerId))
            {
                if (Goalkeepers() >= _policy.Options.MinGoalkeepers) break;
                var r = _policy.Add(entries, gk.PesPlayerId, Position.GK);
                if (!r.Ok) break;
                entries = r.Squad;
                known[gk.PesPlayerId] = gk.Id;
                added.Add(gk.Name);
            }
            if (Goalkeepers() < _policy.Options.MinGoalkeepers)
                throw new AdminRuleException($"Ô {slot} chỉ có {Goalkeepers()} thủ môn và không còn GK tự do đủ chuẩn (GOAL KEEPING ≥ {floor}) để bổ sung");
        }

        var now = clock.GetUtcNow();
        var club = new Club { Name = input.Name, ShortName = input.ShortName, Country = input.Country, LogoPath = input.LogoPath };
        db.Clubs.Add(club);
        db.ClubPesMappings.Add(new ClubPesMapping { ClubId = club.Id, PatchVersionId = patch.Id, PesClubIndex = slot });
        db.TeamAssignments.Add(new TeamAssignment { ClubId = club.Id, ControllerType = ControllerType.Bot, From = now });
        var originIndex = new Dictionary<int, int>();   // như nhập patch: ô đầu tiên có cầu thủ
        for (var c = 0; c < OptionFileImage.ClubCount; c++)
            foreach (var s in image.GetSquad(c).Players) originIndex.TryAdd(s.PlayerId, c);
        var matches = (await db.SeasonEconomies.FindAsync([season.Id], ct))?.DefaultContractMatches ?? FederationSetupService.FirstSeasonContractMatches;
        var created = new List<Guid>();
        for (var i = 0; i < entries.Count; i++)
        {
            var pesId = entries[i].PlayerId;
            if (!known.TryGetValue(pesId, out var playerId))
            {
                var p = new FootballPlayer { Name = image.GetPlayerName(pesId), Position = image.GetPosition(pesId), OriginClubId = club.Id };
                db.Players.Add(p);
                db.PlayerPesMappings.Add(new PlayerPesMapping
                {
                    PlayerId = p.Id, PatchVersionId = patch.Id, PesPlayerId = pesId,
                    OriginPesClubIndex = originIndex.TryGetValue(pesId, out var o) ? o : null,
                });
                db.PlayerAttributes.Add(new PlayerAttributes
                {
                    PlayerId = p.Id, SeasonId = season.Id, CapturedAt = now,
                    Values = PlayerField.Verified.ToDictionary(f => f.Name, f => image.Get(pesId, f)),
                });
                created.Add(playerId = p.Id);
            }
            db.SquadMemberships.Add(new SquadMembership { ClubId = club.Id, PlayerId = playerId, ShirtNumber = entries[i].ShirtNumber, SlotOrder = i, JoinedAt = now });
            db.Contracts.Add(new PlayerContract { ClubId = club.Id, PlayerId = playerId, MatchesTotal = matches, StartedAt = now });
        }
        Audit(club.Id, "Create", null, new
        {
            club.Name, club.ShortName, club.Country, club.LogoPath, Slot = slot, Patch = patch.Name, Players = entries.Count,
            AddedGoalkeepers = added, Skipped = skipped,
        }, actor);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        if (await db.PlayerPrices.AnyAsync(x => x.SeasonId == season.Id, ct))
        {
            var economy = new EconomyService(db, clock);
            await economy.RecalculatePricesAsync(season.Id, ct, created);
            await economy.SetImportedWagesAsync(season.Id, ct);
        }
        await tx.CommitAsync(ct);
        return new(club.Id, entries.Count, added, skipped);
    }

    /// <summary>
    /// CLB đã có lịch sử → chỉ lưu trữ được: trận, mùa giải, chuyển nhượng, sổ quỹ, gia hạn, từng có người chơi điều khiển,
    /// cầu thủ đã rời/hợp đồng đã kết thúc, hoặc cầu thủ tạo cùng CLB (CLB gốc) đã có dữ liệu ở nơi khác.
    /// </summary>
    public async Task<bool> HasHistoryAsync(Guid id, CancellationToken ct = default)
    {
        var origin = db.Players.Where(p => p.OriginClubId == id).Select(p => p.Id);
        return await db.Fixtures.AnyAsync(f => f.HomeClubId == id || f.AwayClubId == id, ct)
            || await db.EditionClubs.AnyAsync(x => x.ClubId == id, ct)
            || await db.SeasonClubs.AnyAsync(x => x.ClubId == id, ct)
            || await db.Transfers.AnyAsync(t => t.FromClubId == id || t.ToClubId == id || origin.Contains(t.PlayerId), ct)
            || await db.Ledger.AnyAsync(x => x.ClubId == id, ct)
            || await db.ContractRenewals.AnyAsync(r => r.ClubId == id || r.OriginClubId == id || origin.Contains(r.PlayerId), ct)
            || await db.TeamAssignments.AnyAsync(a => a.ClubId == id && a.ControllerType == ControllerType.Human, ct)
            || await db.SquadMemberships.AnyAsync(m => m.ClubId == id ? m.LeftAt != null : origin.Contains(m.PlayerId), ct)
            || await db.Contracts.AnyAsync(c => c.ClubId == id ? c.Status != ContractStatus.Active : origin.Contains(c.PlayerId), ct)
            || await db.MatchRatings.AnyAsync(r => origin.Contains(r.PlayerId), ct)
            || await db.Goals.AnyAsync(g => g.ScorerPlayerId != null && origin.Contains(g.ScorerPlayerId.Value), ct)
            || await db.AttributeChanges.AnyAsync(a => origin.Contains(a.PlayerId), ct);
    }

    /// <summary>
    /// Xoá/lưu trữ CLB (mục 5, PO chọn 05/11). Còn trong mùa giải/mùa chưa kết thúc hoặc còn trận chưa đá → chặn.
    /// Chưa có lịch sử → xoá hẳn (trả true): xoá luôn cầu thủ có CLB gốc là CLB này (như CLB không tham gia giải), người khác (GK bổ sung) thành tự do,
    /// ô PES thành ô dư. Có lịch sử → lưu trữ (trả false): mọi cầu thủ thành tự do (hợp đồng kết thúc, đề nghị gia hạn huỷ), hết điều khiển,
    /// OF vòng bỏ qua CLB; ô PES vẫn giữ trong patch này (giải phóng từ mùa sau). Không có khôi phục.
    /// </summary>
    public async Task<bool> DeleteOrArchiveAsync(Guid id, string actor, CancellationToken ct = default)
    {
        var c = await db.Clubs.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new AdminRuleException("Không tìm thấy CLB");
        if (c.Status != ClubStatus.Active) return false;
        if (await (from ec in db.EditionClubs
                   join e in db.Editions on ec.EditionId equals e.Id
                   join s in db.Seasons on e.SeasonId equals s.Id
                   join comp in db.Competitions on e.CompetitionId equals comp.Id
                   where ec.ClubId == id && (e.Status != EditionStatus.Finished || s.Status != SeasonStatus.Finished)
                   select comp.Name + " " + s.Name).FirstOrDefaultAsync(ct) is { } edition)
            throw new AdminRuleException($"{c.Name} đang dự {edition}: bỏ khỏi mùa giải trước");
        if (await (from sc in db.SeasonClubs
                   join s in db.Seasons on sc.SeasonId equals s.Id
                   where sc.ClubId == id && s.Status != SeasonStatus.Finished
                   select s.Name).FirstOrDefaultAsync(ct) is { } season)
            throw new AdminRuleException($"{c.Name} đang trong {season}");
        if (await db.Fixtures.AnyAsync(f => (f.HomeClubId == id || f.AwayClubId == id) && f.Status != FixtureStatus.Played, ct))
            throw new AdminRuleException($"{c.Name} còn trận chưa đá");

        var now = clock.GetUtcNow();
        var before = Snapshot(c);
        var slots = await db.ClubPesMappings.Where(m => m.ClubId == id).ToListAsync(ct);
        if (await HasHistoryAsync(id, ct))
        {
            var members = await db.SquadMemberships.Where(m => m.ClubId == id && m.LeftAt == null).ToListAsync(ct);
            foreach (var m in members) m.LeftAt = now;
            foreach (var k in await db.Contracts.Where(x => x.ClubId == id && x.Status == ContractStatus.Active).ToListAsync(ct))
                (k.Status, k.EndedAt, k.EndReason) = (ContractStatus.Ended, now, ContractEndReason.Released);
            foreach (var r in await db.ContractRenewals.Where(r => r.ClubId == id && r.Status == RenewalStatus.Pending).ToListAsync(ct))
                (r.Status, r.RejectReason, r.DecidedAt) = (RenewalStatus.Cancelled, "ClubArchived", now);
            foreach (var a in await db.TeamAssignments.Where(a => a.ClubId == id && a.To == null).ToListAsync(ct)) a.To = now;
            c.Status = ClubStatus.Inactive;
            c.SquadVersion++;
            Audit(id, "Archive", before, new { Status = c.Status.ToString(), Released = members.Count }, actor);
            await db.SaveChangesAsync(ct);
            return false;
        }

        var origin = await db.Players.Where(p => p.OriginClubId == id).Select(p => p.Id).ToListAsync(ct);
        db.Contracts.RemoveRange(await db.Contracts.Where(x => x.ClubId == id).ToListAsync(ct));
        db.SquadMemberships.RemoveRange(await db.SquadMemberships.Where(m => m.ClubId == id).ToListAsync(ct));
        db.PlayerPrices.RemoveRange(await db.PlayerPrices.Where(x => origin.Contains(x.PlayerId)).ToListAsync(ct));
        db.PlayerAttributes.RemoveRange(await db.PlayerAttributes.Where(x => origin.Contains(x.PlayerId)).ToListAsync(ct));
        db.PlayerPesMappings.RemoveRange(await db.PlayerPesMappings.Where(x => origin.Contains(x.PlayerId)).ToListAsync(ct));
        db.Players.RemoveRange(await db.Players.Where(p => origin.Contains(p.Id)).ToListAsync(ct));
        db.ClubPesMappings.RemoveRange(slots);
        db.TeamAssignments.RemoveRange(await db.TeamAssignments.Where(a => a.ClubId == id).ToListAsync(ct));
        db.Tactics.RemoveRange(await db.Tactics.Where(t => t.ClubId == id).ToListAsync(ct));
        db.Clubs.Remove(c);
        Audit(id, "Delete", new { before, Slots = slots.Select(m => m.PesClubIndex), DeletedPlayers = origin.Count }, null, actor);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public const string SetControllerAction = "SetController";

    /// <summary>
    /// CLB của tôi (PO uỷ quyền 06/11; tầm nhìn: Human nhận/rời CLB giữa mùa, không đổi lịch, giữ lịch sử TeamAssignment). 1 Human tối đa 1 CLB:
    /// CLB cũ về BOT, <paramref name="clubId"/> null = không cầm CLB nào (BOT đá mọi trận). CLB phải đang hoạt động và dự mùa đang mở;
    /// chặn khi CLB cũ/mới có trận đang đá. Không đổi → không ghi gì.
    /// </summary>
    /// <param name="participantId">HLV (<see cref="Manager"/>) nhận CLB; null = admin (PO). Nhiều HLV (đợt 2, 12/11): mỗi HLV tối đa 1 CLB, 1 CLB tối đa 1 HLV.</param>
    public async Task SetHumanClubAsync(Guid? clubId, string actor, Guid? participantId = null, CancellationToken ct = default)
    {
        var current = await db.TeamAssignments.Where(a => a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId == participantId).ToListAsync(ct);
        if (current.Count == (clubId is null ? 0 : 1) && current.All(a => a.ClubId == clubId)) return;
        if (clubId is { } id)
        {
            var c = await db.Clubs.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new AdminRuleException("Không tìm thấy CLB");
            if (c.Status != ClubStatus.Active) throw new AdminRuleException($"{c.Name} đã lưu trữ");
            var season = await db.Seasons.Where(s => s.Status != SeasonStatus.Finished).OrderBy(s => s.Number).FirstOrDefaultAsync(ct)
                ?? throw new AdminRuleException("Chưa có mùa đang mở");
            if (!await db.SeasonClubs.AnyAsync(x => x.SeasonId == season.Id && x.ClubId == id, ct)
                && !await (from ec in db.EditionClubs join e in db.Editions on ec.EditionId equals e.Id where e.SeasonId == season.Id && ec.ClubId == id select ec).AnyAsync(ct))
                throw new AdminRuleException($"{c.Name} không dự {season.Name}");
            if (await db.TeamAssignments.AnyAsync(a => a.ClubId == id && a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId != participantId, ct))
                throw new AdminRuleException($"{c.Name} đang có HLV khác");
        }
        var touched = current.Select(a => a.ClubId).ToList();
        if (clubId is { } target) touched.Add(target);
        if (await db.Fixtures.AnyAsync(f => f.Status == FixtureStatus.Playing && (touched.Contains(f.HomeClubId) || touched.Contains(f.AwayClubId)), ct))
            throw new AdminRuleException("CLB đang có trận đang đá: chờ đá xong rồi đổi");

        var now = clock.GetUtcNow();
        foreach (var a in current.Where(a => a.ClubId != clubId))
        {
            a.To = now;
            db.TeamAssignments.Add(new TeamAssignment { ClubId = a.ClubId, ControllerType = ControllerType.Bot, From = now });
            Audit(a.ClubId, SetControllerAction, new { ControllerType = nameof(ControllerType.Human) }, new { ControllerType = nameof(ControllerType.Bot) }, actor);
        }
        if (clubId is { } human && current.All(a => a.ClubId != human))
        {
            foreach (var a in await db.TeamAssignments.Where(a => a.ClubId == human && a.To == null).ToListAsync(ct)) a.To = now;
            db.TeamAssignments.Add(new TeamAssignment { ClubId = human, ParticipantId = participantId, ControllerType = ControllerType.Human, From = now });
            Audit(human, SetControllerAction, new { ControllerType = nameof(ControllerType.Bot) }, new { ControllerType = nameof(ControllerType.Human), ParticipantId = participantId }, actor);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Mùa chưa kết thúc sớm nhất và patch của nó (ô PES tính theo patch này).</summary>
    private async Task<(Season Season, PatchVersion Patch)> OpenSeasonAsync(CancellationToken ct)
    {
        var season = await db.Seasons.Where(s => s.Status != SeasonStatus.Finished).OrderBy(s => s.Number).FirstOrDefaultAsync(ct)
            ?? throw new AdminRuleException("Chưa có mùa đang mở");
        var patch = await db.PatchVersions.SingleOrDefaultAsync(p => p.Id == season.PatchVersionId, ct)
            ?? throw new AdminRuleException($"{season.Name} chưa nhập patch");
        return (season, patch);
    }

    private async Task<OptionFileImage> BaseImageAsync(PatchVersion patch, CancellationToken ct)
    {
        var file = await (store ?? throw new InvalidOperationException("Cần IOptionFileStore để đọc OF gốc")).ReadAsync(patch.BaseOptionFilePath, ct);
        if (Hash.Sha256(file) != patch.BaseOptionFileSha256) throw new InvalidOperationException("OF gốc trong kho không khớp sha256 đã lưu");
        return OptionFileCodec.Decode(file);
    }

    private static List<string> Errors(ClubInput input)
    {
        var e = new List<string>();
        if (input.Name.Length is 0 or > 100) e.Add("Tên CLB 1–100 ký tự");
        if (input.ShortName?.Length > 10) e.Add("Tên ngắn tối đa 10 ký tự");
        if (input.Country?.Length > 3) e.Add("Mã nước tối đa 3 ký tự");
        if (input.LogoPath?.Length > 300) e.Add("Đường dẫn logo tối đa 300 ký tự");
        return e;
    }

    private static ClubInput Normalize(ClubInput i) => new(i.Name.Trim(), string.IsNullOrWhiteSpace(i.ShortName) ? null : i.ShortName.Trim(),
        string.IsNullOrWhiteSpace(i.Country) ? null : i.Country.Trim().ToUpperInvariant(), string.IsNullOrWhiteSpace(i.LogoPath) ? null : i.LogoPath.Trim());

    private static object Snapshot(Club c) => new { c.Name, c.ShortName, c.Country, c.LogoPath, Status = c.Status.ToString() };

    private void Audit(Guid id, string action, object? before, object? after, string actor) =>
        db.AdminAudit.Add(new AdminAuditEntry { At = clock.GetUtcNow(), Actor = actor, EntityType = nameof(Club), EntityId = id, Action = action,
            Before = before is null ? null : JsonSerializer.Serialize(before), After = after is null ? null : JsonSerializer.Serialize(after) });
}
