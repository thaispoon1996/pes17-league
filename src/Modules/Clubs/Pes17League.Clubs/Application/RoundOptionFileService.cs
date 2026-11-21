using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Application;

public sealed record RoundOptionFileResult(Guid BuildId, bool Ok, string? StoragePath, string? Sha256, IReadOnlyList<ValidationIssue> Issues);

/// <summary>
/// Chốt vòng (luồng 4): squad (+ sơ đồ/thiết lập đội nếu CLB đã chọn) hiện tại trong DB → ánh xạ PES của patch → OptionFileBuilder trên OF gốc → lưu file + OptionFileBuild.
/// Lỗi kiểm hợp lệ → không xuất file, ghi bản Failed kèm lỗi để báo admin.
/// Chỉ số đã tiến hoá theo trận của mùa (C19, PO chọn 04/11) ghi đè lên OF (giá trị tuyệt đối → ghi lại trên OF vòng vẫn đúng).
/// Chưa ghi tên CLB vào OF (C15: tên chỉ đổi đầu mùa, luồng riêng).
/// Admin sửa cầu thủ (bước 3c, A7): tên + vị trí + chỉ số ghi ở OF vòng sau; OF trận không lấy phần admin sửa sau lúc khoá vòng.
/// Cầu thủ admin tạo (ô 32768+, bước 3c phần 2): ghi đủ tên, tên in áo, vị trí, 33 chỉ số; tạo sau lúc khoá vòng thì OF trận ghi luôn (OF vòng chưa có).
/// Trận có người (PO duyệt 03/11): đội hình chốt lúc bấm vào trận → <see cref="BuildForMatchAsync"/> ghi lại squad + sơ đồ CLB người chơi lên OF của vòng.
/// </summary>
public sealed class RoundOptionFileService(ClubsDbContext db, IOptionFileStore store, TimeProvider clock, SquadRulesOptions? options = null)
{
    public async Task<RoundOptionFileResult> BuildAsync(Guid patchVersionId, Guid? matchdayId, CancellationToken ct = default)
    {
        var patch = await db.PatchVersions.SingleAsync(p => p.Id == patchVersionId, ct);
        var baseFile = await store.ReadAsync(patch.BaseOptionFilePath, ct);
        if (Hash.Sha256(baseFile) != patch.BaseOptionFileSha256)
            throw new InvalidOperationException("OF gốc trong kho không khớp sha256 đã lưu");
        return await BuildAsync(baseFile, patchVersionId, matchdayId, null, null, ct);
    }

    /// <summary>OF của vòng + squad hiện tại trong DB của <paramref name="clubIds"/> (đổi chỗ/số áo sau khi khoá vòng) → bản build mới cho 1 trận.</summary>
    public async Task<RoundOptionFileResult> BuildForMatchAsync(Guid matchdayId, IReadOnlyCollection<Guid> clubIds, CancellationToken ct = default)
    {
        var md = await db.Matchdays.SingleAsync(m => m.Id == matchdayId, ct);
        var round = await db.OptionFileBuilds.SingleAsync(b => b.Id == md.OptionFileBuildId, ct);
        return await BuildAsync(await store.ReadAsync(round.StoragePath!, ct), round.PatchVersionId, matchdayId, clubIds, round.BuiltAt, ct);
    }

    /// <summary>OF 1 trận giải (PO 17/11 bỏ khoá vòng): squad/sơ đồ/chỉ số hiện tại của <paramref name="clubIds"/> ghi lên OF gốc patch.
    /// Vòng cũ đã khoá (có OF vòng) → như <see cref="BuildForMatchAsync"/>.</summary>
    public async Task<RoundOptionFileResult> BuildForFixtureAsync(Guid matchdayId, IReadOnlyCollection<Guid> clubIds, CancellationToken ct = default)
    {
        var md = await db.Matchdays.SingleAsync(m => m.Id == matchdayId, ct);
        if (md.OptionFileBuildId is not null) return await BuildForMatchAsync(matchdayId, clubIds, ct);
        var patchId = await db.Seasons.Where(s => s.Id == md.SeasonId).Select(s => s.PatchVersionId).SingleAsync(ct)
            ?? throw new InvalidOperationException("Mùa chưa gắn patch");
        var patch = await db.PatchVersions.SingleAsync(p => p.Id == patchId, ct);
        var baseFile = await store.ReadAsync(patch.BaseOptionFilePath, ct);
        if (Hash.Sha256(baseFile) != patch.BaseOptionFileSha256)
            throw new InvalidOperationException("OF gốc trong kho không khớp sha256 đã lưu");
        return await BuildAsync(baseFile, patchId, matchdayId, clubIds, null, ct);
    }

    /// <summary>Giao hữu xếp hạng (PO 16/11): squad + sơ đồ hiện tại của 2 CLB ghi lên OF vòng mới nhất của mùa (chưa khoá vòng nào → OF gốc patch).</summary>
    public async Task<RoundOptionFileResult> BuildForFriendlyAsync(Guid seasonId, IReadOnlyCollection<Guid> clubIds, CancellationToken ct = default)
    {
        var md = await db.Matchdays.Where(m => m.SeasonId == seasonId && m.OptionFileBuildId != null).OrderByDescending(m => m.Number)
            .Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
        if (md is { } id) return await BuildForMatchAsync(id, clubIds, ct);
        var patchId = await db.Seasons.Where(s => s.Id == seasonId).Select(s => s.PatchVersionId).SingleAsync(ct)
            ?? throw new InvalidOperationException("Mùa chưa gắn patch");
        var patch = await db.PatchVersions.SingleAsync(p => p.Id == patchId, ct);
        return await BuildAsync(await store.ReadAsync(patch.BaseOptionFilePath, ct), patchId, null, clubIds, null, ct);
    }

    /// <param name="adminCutoff">OF trận: lúc khoá vòng — phần admin sửa sau đó để vòng sau. Null = OF vòng.</param>
    private async Task<RoundOptionFileResult> BuildAsync(byte[] baseFile, Guid patchVersionId, Guid? matchdayId, IReadOnlyCollection<Guid>? clubIds,
        DateTimeOffset? adminCutoff, CancellationToken ct)
    {
        // CLB đã lưu trữ (cầu thủ đã thành tự do, PO chọn 05/11) không ghi: ô của nó giữ squad như OF gốc.
        var mappings = db.ClubPesMappings.Where(c => c.PatchVersionId == patchVersionId && (clubIds == null || clubIds.Contains(c.ClubId))
            && db.Clubs.Any(x => x.Id == c.ClubId && x.Status == ClubStatus.Active));
        var rows = await (from cm in mappings
                          join m in db.SquadMemberships on cm.ClubId equals m.ClubId
                          where m.LeftAt == null
                          join pm in db.PlayerPesMappings on new { m.PlayerId, PatchVersionId = patchVersionId } equals new { pm.PlayerId, pm.PatchVersionId }
                          select new { cm.PesClubIndex, m.PlayerId, pm.PesPlayerId, m.ShirtNumber, m.SlotOrder }).ToListAsync(ct);
        var clubIndexes = await mappings.Select(c => c.PesClubIndex).ToListAsync(ct);
        var squads = clubIndexes.Select(ci => new ClubSquad(ci, rows.Where(r => r.PesClubIndex == ci).OrderBy(r => r.SlotOrder)
            .Select(r => new SquadSlot(r.PesPlayerId, r.ShirtNumber)).ToList())).ToList();
        var snapshot = JsonSerializer.Serialize(squads.ToDictionary(s => s.Club.ToString(), s => s.Players.Select(p => new[] { p.PlayerId, p.ShirtNumber })));
        var tactics = await TacticsAsync(mappings, rows.Select(r => (r.PesClubIndex, r.PlayerId, r.SlotOrder)).ToList(), matchdayId, ct);

        var attributes = matchdayId is null ? [] : await EvolvedAttributesAsync(matchdayId.Value, patchVersionId, adminCutoff, ct);
        var (names, shirtNames) = await EditedInfoAsync(baseFile, patchVersionId, matchdayId, attributes, adminCutoff, ct);

        var build = new OptionFileBuild { PatchVersionId = patchVersionId, MatchdayId = matchdayId, BuiltAt = clock.GetUtcNow(), SquadSnapshot = snapshot };
        IReadOnlyList<ValidationIssue> issues = [];
        try
        {
            var data = OptionFileBuilder.Build(baseFile, squads, options, tactics, attributes, names, shirtNames);
            build.Sha256 = Hash.Sha256(data);
            build.StoragePath = await store.SaveAsync($"rounds/{build.Id}.OPT", data, ct);
            build.Status = OptionFileBuildStatus.Built;
        }
        catch (OptionFileValidationException ex)
        {
            issues = ex.Issues;
            build.Status = OptionFileBuildStatus.Failed;
            build.Errors = JsonSerializer.Serialize(ex.Issues);
        }
        db.OptionFileBuilds.Add(build);
        await db.SaveChangesAsync(ct);
        return new(build.Id, build.Status == OptionFileBuildStatus.Built, build.StoragePath, build.Sha256, issues);
    }

    /// <summary>
    /// Chiến thuật ghi vào OF: sơ đồ/thiết lập/vị trí tự do/chiến thuật L2 CLB đã chọn + đội trưởng/người đá phạt cho MỌI CLB (PO duyệt 05/11):
    /// người CLB chọn nếu đang đá chính, không thì tự chọn (<see cref="TeamJobPicker"/>) → chỉ số ô trong squad vừa ghi.
    /// Không có vòng (không biết mùa → không có OVR/chỉ số) → giữ vai như OF gốc.
    /// </summary>
    private async Task<Dictionary<int, TeamTactics>> TacticsAsync(IQueryable<ClubPesMapping> mappings, List<(int PesClubIndex, Guid PlayerId, int SlotOrder)> rows,
        Guid? matchdayId, CancellationToken ct)
    {
        var clubs = await mappings.Select(c => new { c.ClubId, c.PesClubIndex }).ToListAsync(ct);
        var ids = clubs.Select(c => c.ClubId).ToList();
        var chosen = await db.Tactics.Where(t => ids.Contains(t.ClubId)).ToDictionaryAsync(t => t.ClubId, ct);
        var seasonId = matchdayId is null ? (Guid?)null : await db.Matchdays.Where(m => m.Id == matchdayId).Select(m => m.SeasonId).SingleAsync(ct);
        var starters = seasonId is { } s ? await new TacticsService(db, clock).StartersAsync(s, ids, ct) : [];
        var result = new Dictionary<int, TeamTactics>();
        foreach (var c in clubs)
        {
            var t = chosen.GetValueOrDefault(c.ClubId);
            var order = rows.Where(r => r.PesClubIndex == c.PesClubIndex).OrderBy(r => r.SlotOrder).Select(r => r.PlayerId).ToList();
            int?[]? jobs = null;
            if (starters.TryGetValue(c.ClubId, out var xi) && xi.Count > 0)
                jobs = [.. Enum.GetValues<TeamJob>().Select(j => TeamJobPicker.Resolve(j, TacticsService.GetJob(t, j), xi) is { } p
                    && order.IndexOf(p.PlayerId) is var i and >= 0 ? i : (int?)null)];
            if (t is null && jobs is null) continue;
            result[c.PesClubIndex] = new TeamTactics(t?.Formation, t?.BackLine, t?.Pressure, t?.OffsideTrap, t?.CounterAttack,
                TacticsService.PositionsOf(t), TacticsService.StrategiesOf(t), jobs, TacticsService.RolesOf(t));
        }
        return result;
    }

    /// <summary>
    /// PES ID → chỉ số hiện tại (snapshot + tổng thay đổi) của các trường đã đổi trong mùa của vòng (mọi cầu thủ có thay đổi, kể cả đã thành tự do).
    /// Cầu thủ tạo mới (ô ≥ <see cref="OptionFileImage.FirstEditId"/>, OF gốc chỉ có bản mẫu mọi chỉ số 50): đủ 33 trường.
    /// <paramref name="adminCutoff"/> có giá trị → bỏ phần admin sửa sau mốc đó.
    /// </summary>
    private async Task<Dictionary<int, IReadOnlyDictionary<PlayerField, int>>> EvolvedAttributesAsync(Guid matchdayId, Guid patchVersionId,
        DateTimeOffset? adminCutoff, CancellationToken ct)
    {
        var seasonId = await db.Matchdays.Where(m => m.Id == matchdayId).Select(m => m.SeasonId).SingleAsync(ct);
        var changed = await db.AttributeChanges
            .Where(c => c.SeasonId == seasonId && (adminCutoff == null || c.Source != AttributeChangeSource.Admin || c.At <= adminCutoff))
            .GroupBy(c => new { c.PlayerId, c.Field }).Select(g => new { g.Key.PlayerId, g.Key.Field, Delta = g.Sum(c => c.Delta) }).ToListAsync(ct);
        var created = await db.PlayerPesMappings.Where(m => m.PatchVersionId == patchVersionId && m.PesPlayerId >= OptionFileImage.FirstEditId)
            .Select(m => m.PlayerId).ToListAsync(ct);
        if (changed.Count == 0 && created.Count == 0) return [];
        var ids = changed.Select(c => c.PlayerId).Concat(created).Distinct().ToList();
        var start = await db.PlayerAttributes.Where(a => a.SeasonId == seasonId && ids.Contains(a.PlayerId)).ToDictionaryAsync(a => a.PlayerId, a => a.Values, ct);
        var pes = await db.PlayerPesMappings.Where(m => m.PatchVersionId == patchVersionId && ids.Contains(m.PlayerId)).ToDictionaryAsync(m => m.PlayerId, m => m.PesPlayerId, ct);
        var fields = PlayerField.Verified.ToDictionary(f => f.Name);
        var result = changed.Where(c => pes.ContainsKey(c.PlayerId) && start.ContainsKey(c.PlayerId)).GroupBy(c => c.PlayerId).ToDictionary(g => pes[g.Key],
            g => (IReadOnlyDictionary<PlayerField, int>)g.ToDictionary(c => fields[c.Field], c => start[g.Key][c.Field] + c.Delta));
        foreach (var id in created.Where(start.ContainsKey))
        {
            var delta = changed.Where(c => c.PlayerId == id).ToDictionary(c => c.Field, c => c.Delta);
            result[pes[id]] = PlayerField.Verified.ToDictionary(f => f, f => start[id][f.Name] + delta.GetValueOrDefault(f.Name));
        }
        return result;
    }

    /// <summary>
    /// Tên + vị trí đăng ký admin đã sửa (bước 3c) = khác OF gốc của patch. So tên thô để tên patch ngoài Latin-1 không bị ghi lại (bỏ dấu).
    /// Vị trí ghi thêm vào <paramref name="attributes"/> (REGISTERED POSITION) và bật thêm ô vị trí đá được của vị trí mới, giữ các ô cũ (PO chọn 06/11).
    /// Cầu thủ tạo mới: luôn ghi vị trí đăng ký, ô vị trí đá được = chỉ vị trí đăng ký (bỏ CF của bản mẫu), tên in áo tự sinh (<see cref="GameText.ShirtName"/>),
    /// quốc tịch theo mã (không có trong game → Tự do), có năm sinh thì tuổi = năm build − năm sinh thay chỉ số AGE (PO chọn 06/11).
    /// <paramref name="adminCutoff"/> (OF trận, gốc = OF vòng): chỉ cầu thủ tạo sau lúc khoá vòng (chưa có trong OF vòng) — thay đổi khác để vòng sau (A7).
    /// </summary>
    private async Task<(Dictionary<int, string> Names, Dictionary<int, string> ShirtNames)> EditedInfoAsync(byte[] baseFile, Guid patchVersionId,
        Guid? matchdayId, Dictionary<int, IReadOnlyDictionary<PlayerField, int>> attributes, DateTimeOffset? adminCutoff, CancellationToken ct)
    {
        var image = OptionFileCodec.Decode(baseFile);
        var seasonId = matchdayId is null ? (Guid?)null : await db.Matchdays.Where(m => m.Id == matchdayId).Select(m => m.SeasonId).SingleAsync(ct);
        var query = from pm in db.PlayerPesMappings
                    where pm.PatchVersionId == patchVersionId
                    join p in db.Players on pm.PlayerId equals p.Id
                    select new { pm.PlayerId, pm.PesPlayerId, p.Name, p.Position, p.BirthYear, p.Nationality };
        if (adminCutoff is not null)
            query = query.Where(p => p.PesPlayerId >= OptionFileImage.FirstEditId
                && db.PlayerAttributes.Any(a => a.PlayerId == p.PlayerId && a.SeasonId == seasonId && a.CapturedAt > adminCutoff));
        var players = await query.ToListAsync(ct);
        foreach (var p in players.Where(p => p.PesPlayerId >= OptionFileImage.FirstEditId || p.Position != image.GetPosition(p.PesPlayerId)))
        {
            var values = attributes.TryGetValue(p.PesPlayerId, out var v) ? new Dictionary<PlayerField, int>(v) : [];
            values[PlayerField.RegisteredPosition] = (int)p.Position;
            if (p.PesPlayerId >= OptionFileImage.FirstEditId)
            {
                foreach (var q in Enum.GetValues<Position>()) values[PlayerField.Playable(q)] = q == p.Position ? 1 : 0;
                values[PlayerField.Nationality] = GameText.NationIndex(p.Nationality);
                if (p.BirthYear is { } y) values[PlayerField.Age] = Math.Clamp(clock.GetUtcNow().Year - y, PlayerField.Age.Min, PlayerField.Age.Max);
            }
            else values[PlayerField.Playable(p.Position)] = 1;
            attributes[p.PesPlayerId] = values;
        }
        return (players.Where(p => p.Name != image.GetPlayerName(p.PesPlayerId)).ToDictionary(p => p.PesPlayerId, p => p.Name),
            players.Where(p => p.PesPlayerId >= OptionFileImage.FirstEditId).ToDictionary(p => p.PesPlayerId, p => GameText.ShirtName(p.Name)));
    }
}
