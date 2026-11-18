using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

/// <summary>1 dòng bảng điểm giao hữu xếp hạng (thắng 3 / hoà 1 / thua 0).</summary>
public sealed record FriendlyStandingRow(Guid ManagerId, string Name, int Elo, int Played, int Won, int Drawn, int Lost, int GoalsFor, int GoalsAgainst, int Points);

/// <summary>Thống kê 1 người chơi theo chế độ (hồ sơ).</summary>
public sealed record FriendlyStats(int Played, int Won, int Drawn, int Lost, int GoalsFor, int GoalsAgainst, int Abandoned);

/// <summary>Giao hữu người vs người (PO duyệt 16/11, docs/domain-friendly-design.md): ghép trận, thách đấu, phòng chọn đội, chat,
/// kết quả + Elo + tiền (Ranked). Phần máy/pesmatch nằm ở host web; đây chỉ là nghiệp vụ trên DB.</summary>
public sealed class FriendlyService(ClubsDbContext db, TimeProvider clock)
{
    /// <summary>Ghép trận phải tuần tự (1 web) để 2 vé không cùng ghép với 1 người.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public const string RefType = nameof(FriendlyMatch);

    // ---------- ghép trận ----------

    /// <summary>"Tìm trận": tạo vé (hoặc giữ vé đang chờ) rồi thử ghép. Trả trận nếu ghép được ngay.</summary>
    public async Task<FriendlyMatch?> FindAsync(Guid me, FriendlyMode mode, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (await OpenMatchAsync(me, ct) is { } open) return open;
            if (mode == FriendlyMode.Ranked && await ClubOfAsync(me, ct) is null) throw new AdminRuleException("Giao hữu xếp hạng cần có CLB");
            var mine = await db.MatchmakingTickets.Where(t => t.ManagerId == me && t.MatchId == null && !t.Cancelled && t.ExpiresAt > now).ToListAsync(ct);
            foreach (var t in mine.Where(t => t.Mode != mode)) t.Cancelled = true;
            var ticket = mine.FirstOrDefault(t => t.Mode == mode);
            if (ticket is null)
            {
                var elo = await db.Managers.Where(m => m.Id == me).Select(m => m.Elo).SingleAsync(ct);
                ticket = new MatchmakingTicket { ManagerId = me, Mode = mode, Elo = elo, CreatedAt = now, ExpiresAt = now + FriendlyRules.TicketTtl };
                db.MatchmakingTickets.Add(ticket);
            }
            var match = await TryPairAsync(ticket, now, ct);
            await db.SaveChangesAsync(ct);
            return match;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Ghép vé với vé khác cùng chế độ chờ lâu nhất; Ranked: chênh Elo trong cửa sổ của CẢ 2 vé.</summary>
    private async Task<FriendlyMatch?> TryPairAsync(MatchmakingTicket ticket, DateTimeOffset now, CancellationToken ct)
    {
        var others = await db.MatchmakingTickets
            .Where(t => t.Mode == ticket.Mode && t.ManagerId != ticket.ManagerId && t.MatchId == null && !t.Cancelled && t.ExpiresAt > now)
            .OrderBy(t => t.CreatedAt).ToListAsync(ct);
        foreach (var o in others)
        {
            if (ticket.Mode == FriendlyMode.Ranked)
            {
                var gap = Math.Abs(o.Elo - ticket.Elo);
                if (gap > FriendlyRules.Window(now - o.CreatedAt) || gap > FriendlyRules.Window(now - ticket.CreatedAt)) continue;
            }
            if (await OpenMatchAsync(o.ManagerId, ct) is not null) { o.Cancelled = true; continue; }
            var m = await NewMatchAsync(ticket.Mode, o.ManagerId, ticket.ManagerId, FriendlyStatus.Picking, now, ct);   // người chờ trước làm chủ nhà
            if (m is null) continue;
            o.MatchId = ticket.MatchId = m.Id;
            return m;
        }
        return null;
    }

    /// <summary>Huỷ vé tìm trận đang chờ.</summary>
    public async Task CancelFindAsync(Guid me, CancellationToken ct = default)
    {
        await db.MatchmakingTickets.Where(t => t.ManagerId == me && t.MatchId == null && !t.Cancelled)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Cancelled, true), ct);
    }

    /// <summary>Vé đang chờ của tôi (null = không tìm).</summary>
    public Task<MatchmakingTicket?> TicketAsync(Guid me, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return db.MatchmakingTickets.AsNoTracking().Where(t => t.ManagerId == me && t.MatchId == null && !t.Cancelled && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);
    }

    // ---------- thách đấu ----------

    public async Task<FriendlyMatch> ChallengeAsync(Guid me, Guid opponent, FriendlyMode mode, CancellationToken ct = default)
    {
        if (me == opponent) throw new AdminRuleException("Không tự thách đấu chính mình");
        await Gate.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (await OpenMatchAsync(me, ct) is not null) throw new AdminRuleException("Bạn đang có trận giao hữu chưa xong");
            if (await OpenMatchAsync(opponent, ct) is not null) throw new AdminRuleException("Đối thủ đang bận trận khác");
            if (await db.Managers.AnyAsync(m => m.Id == opponent && m.Disabled, ct)) throw new AdminRuleException("Tài khoản đối thủ đang bị khoá");
            await CancelFindAsync(me, ct);
            var m = await NewMatchAsync(mode, me, opponent, FriendlyStatus.Invited, now, ct)
                ?? throw new AdminRuleException("Giao hữu xếp hạng cần cả 2 người có CLB");
            await db.SaveChangesAsync(ct);
            return m;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Người được mời (đội khách) nhận / từ chối.</summary>
    public async Task<FriendlyMatch> RespondAsync(Guid matchId, Guid me, bool accept, CancellationToken ct = default)
    {
        var m = await GetAsync(matchId, ct);
        if (m.Status != FriendlyStatus.Invited || m.AwayManagerId != me) throw new AdminRuleException("Lời mời không còn hiệu lực");
        var now = clock.GetUtcNow();
        if (now - m.CreatedAt > FriendlyRules.InviteTtl) { m.Status = FriendlyStatus.Expired; await db.SaveChangesAsync(ct); throw new AdminRuleException("Lời mời đã hết hạn"); }
        if (accept)
        {
            await CancelFindAsync(me, ct);
            m.Status = FriendlyStatus.Picking;
            m.PickDeadline = now + FriendlyRules.PickTime;
        }
        else { m.Status = FriendlyStatus.Declined; m.EndedAt = now; }
        await db.SaveChangesAsync(ct);
        return m;
    }

    // ---------- phòng chọn đội ----------

    /// <summary>Tự do: chọn đội của patch (chỉ số menu game, kể cả đội tuyển; trùng đội được). Đổi đội thì bỏ Sẵn sàng.</summary>
    public async Task PickTeamAsync(Guid matchId, Guid me, int pesTeam, string name, CancellationToken ct = default)
    {
        var m = await GetAsync(matchId, ct);
        if (m.Status != FriendlyStatus.Picking || !m.Involves(me)) throw new AdminRuleException("Phòng không còn chọn đội");
        if (m.Mode != FriendlyMode.Free) throw new AdminRuleException("Giao hữu xếp hạng dùng CLB của bạn");
        if (pesTeam < 0) throw new AdminRuleException("Đội không hợp lệ");
        if (m.HomeManagerId == me) { m.HomePesTeam = pesTeam; m.HomeTeamName = name; m.HomeReady = false; }
        else { m.AwayPesTeam = pesTeam; m.AwayTeamName = name; m.AwayReady = false; }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sẵn sàng / bỏ sẵn sàng. Cả 2 sẵn sàng → Queued (máy nhận trận). Trả trận sau cập nhật.</summary>
    public async Task<FriendlyMatch> ReadyAsync(Guid matchId, Guid me, bool ready = true, CancellationToken ct = default)
    {
        var m = await GetAsync(matchId, ct);
        if (m.Status != FriendlyStatus.Picking || !m.Involves(me)) throw new AdminRuleException("Phòng không còn chọn đội");
        if (ready && m.Mode == FriendlyMode.Free && (m.HomeManagerId == me ? m.HomePesTeam : m.AwayPesTeam) is null)
            throw new AdminRuleException("Chọn đội trước đã");
        if (m.HomeManagerId == me) m.HomeReady = ready; else m.AwayReady = ready;
        if (m.HomeReady && m.AwayReady) m.Status = FriendlyStatus.Queued;
        await db.SaveChangesAsync(ct);
        return m;
    }

    /// <summary>Người trong phòng bỏ trận trước khi đá (Invited/Picking/Queued).</summary>
    public async Task CancelAsync(Guid matchId, Guid me, CancellationToken ct = default)
    {
        var m = await GetAsync(matchId, ct);
        if (!m.Involves(me) || m.Status is not (FriendlyStatus.Invited or FriendlyStatus.Picking or FriendlyStatus.Queued))
            throw new AdminRuleException("Không huỷ được trận này");
        m.Status = FriendlyStatus.Cancelled;
        m.EndedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>"Đá lại": phòng mới, cùng 2 người + cùng đội, vào thẳng Picking. Trả trận mới (hoặc trận "đá lại" đối thủ đã tạo).</summary>
    public async Task<FriendlyMatch> RematchAsync(Guid matchId, Guid me, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var old = await GetAsync(matchId, ct);
            if (!old.Involves(me) || old.IsOpen) throw new AdminRuleException("Chưa đá lại được");
            if (await db.FriendlyMatches.FirstOrDefaultAsync(x => x.RematchOf == matchId && x.Status == FriendlyStatus.Picking, ct) is { } already) return already;
            var other = old.HomeManagerId == me ? old.AwayManagerId : old.HomeManagerId;
            if (await OpenMatchAsync(me, ct) is not null || await OpenMatchAsync(other, ct) is not null) throw new AdminRuleException("Một bên đang có trận khác");
            var now = clock.GetUtcNow();
            var m = await NewMatchAsync(old.Mode, old.HomeManagerId, old.AwayManagerId, FriendlyStatus.Picking, now, ct)
                ?? throw new AdminRuleException("Giao hữu xếp hạng cần cả 2 người có CLB");
            m.RematchOf = old.Id;
            if (m.Mode == FriendlyMode.Free)
                (m.HomePesTeam, m.HomeTeamName, m.AwayPesTeam, m.AwayTeamName) = (old.HomePesTeam, old.HomeTeamName, old.AwayPesTeam, old.AwayTeamName);
            await db.SaveChangesAsync(ct);
            return m;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Dọn định kỳ: lời mời quá 2 phút → Expired; phòng quá hạn chót → tự Sẵn sàng (Tự do thiếu đội → huỷ).
    /// Trả số trận vừa vào hàng chờ máy.</summary>
    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var inviteCut = now - FriendlyRules.InviteTtl;
        foreach (var m in await db.FriendlyMatches.Where(x => x.Status == FriendlyStatus.Invited && x.CreatedAt < inviteCut).ToListAsync(ct))
        { m.Status = FriendlyStatus.Expired; m.EndedAt = now; }
        var queued = 0;
        foreach (var m in await db.FriendlyMatches.Where(x => x.Status == FriendlyStatus.Picking && x.PickDeadline < now).ToListAsync(ct))
        {
            if (m.Mode == FriendlyMode.Free && (m.HomePesTeam is null || m.AwayPesTeam is null))
            { m.Status = FriendlyStatus.Cancelled; m.EndedAt = now; m.LastError = "Hết giờ chọn đội"; continue; }
            m.HomeReady = m.AwayReady = true;
            m.Status = FriendlyStatus.Queued;
            queued++;
        }
        await db.SaveChangesAsync(ct);
        return queued;
    }

    // ---------- chat ----------

    public async Task<FriendlyChatMessage> ChatAsync(Guid? matchId, Guid me, string text, CancellationToken ct = default)
    {
        text = text.Trim();
        if (text.Length == 0) throw new AdminRuleException("Tin nhắn trống");
        if (text.Length > FriendlyRules.ChatMax) text = text[..FriendlyRules.ChatMax];
        if (matchId is { } id && !(await GetAsync(id, ct)).Involves(me)) throw new AdminRuleException("Bạn không ở phòng này");
        var msg = new FriendlyChatMessage { MatchId = matchId, ManagerId = me, Text = text, At = clock.GetUtcNow() };
        db.FriendlyChat.Add(msg);
        await db.SaveChangesAsync(ct);
        return msg;
    }

    public Task<List<FriendlyChatMessage>> ChatLogAsync(Guid? matchId, int take = 50, CancellationToken ct = default) =>
        db.FriendlyChat.AsNoTracking().Where(c => c.MatchId == matchId).OrderByDescending(c => c.At).Take(take)
            .OrderBy(c => c.At).ToListAsync(ct);

    // ---------- máy ----------

    /// <summary>Máy nhận trận Queued cũ nhất → Playing. null = không có.</summary>
    public async Task<FriendlyMatch?> StartNextAsync(CancellationToken ct = default)
    {
        var m = await db.FriendlyMatches.Where(x => x.Status == FriendlyStatus.Queued).OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (m is null) return null;
        m.Status = FriendlyStatus.Playing;
        m.StartedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return m;
    }

    /// <summary>Yêu cầu chạy pesmatch "both" cho trận đang Playing. Tự do: OF gốc patch mới nhất + đội đã chọn;
    /// Xếp hạng: OF có squad/sơ đồ hiện tại của 2 CLB (<see cref="RoundOptionFileService.BuildForFriendlyAsync"/>) + chỉ số PES của CLB.</summary>
    public async Task<MatchRunRequest> MachineRequestAsync(FriendlyMatch m, string container, IOptionFileStore store, CancellationToken ct = default)
    {
        if (m.Mode == FriendlyMode.Free)
        {
            if (m.HomePesTeam is not { } h || m.AwayPesTeam is not { } a) throw new InvalidOperationException("Chưa chọn đủ 2 đội");
            var patch = await db.PatchVersions.AsNoTracking().OrderByDescending(p => p.ImportedAt).FirstAsync(ct);
            if (Pes17RoundService.IsPes17(patch.Name))   // PES17 (18/11): máy cần ảnh chụp đội hình (squads JSON), không phải option file
                return new MatchRunRequest(m.Id, container, await Pes17SquadsAsync(store, patch.Id, ct), h, a, null, BothHuman: true);
            return new MatchRunRequest(m.Id, container, await store.ReadAsync(patch.BaseOptionFilePath, ct), h, a, null, BothHuman: true);
        }
        if (m.SeasonId is not { } sid || m.HomeClubId is not { } hc || m.AwayClubId is not { } ac) throw new InvalidOperationException("Trận xếp hạng thiếu CLB/mùa");
        var patchId = await db.Seasons.Where(s => s.Id == sid).Select(s => s.PatchVersionId).SingleAsync(ct) ?? throw new InvalidOperationException("Mùa chưa gắn patch");
        var index = await db.ClubPesMappings.Where(x => x.PatchVersionId == patchId && (x.ClubId == hc || x.ClubId == ac)).ToDictionaryAsync(x => x.ClubId, x => x.PesClubIndex, ct);
        if (await new Pes17RoundService(db, store, clock).IsPes17SeasonAsync(sid, ct))
            return new MatchRunRequest(m.Id, container, await Pes17SquadsAsync(store, patchId, ct), index[hc], index[ac], null, BothHuman: true);
        var build = await new RoundOptionFileService(db, store, clock).BuildForFriendlyAsync(sid, [hc, ac], ct);
        if (!build.Ok) throw new InvalidOperationException($"Đội hình không hợp lệ: {string.Join("; ", build.Issues.Select(i => i.Message))}");
        return new MatchRunRequest(m.Id, container, await store.ReadAsync(build.StoragePath!, ct), index[hc], index[ac], null, BothHuman: true);
    }

    /// <summary>PES17: đội hình hiện tại của mọi CLB (giống ảnh chụp vòng) → bộ chạy tự cài nếu khác bản trong game.</summary>
    private async Task<byte[]> Pes17SquadsAsync(IOptionFileStore store, Guid patchId, CancellationToken ct) =>
        await store.ReadAsync((await new Pes17RoundService(db, store, clock).SnapshotAsync(patchId, null, ct)).StoragePath!, ct);

    /// <summary>Máy lỗi trước khi đá xong → huỷ (không ai bị xử thua).</summary>
    public async Task FailAsync(Guid matchId, string error, CancellationToken ct = default)
    {
        var m = await GetAsync(matchId, ct);
        if (!m.IsOpen) return;
        m.Status = FriendlyStatus.Voided;
        m.LastError = error.Length > 200 ? error[..200] : error;
        m.EndedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Trận đá xong: ghi tỉ số + bàn thắng; Ranked đổi Elo + thưởng vào sổ CLB.</summary>
    public async Task RecordResultAsync(Guid matchId, int homeGoals, int awayGoals, IEnumerable<FriendlyGoal> goals, CancellationToken ct = default)
    {
        var m = await GetAsync(matchId, ct);
        if (m.Status != FriendlyStatus.Playing) throw new AdminRuleException("Trận không ở trạng thái đang đá");
        foreach (var g in goals) { g.MatchId = m.Id; db.FriendlyGoals.Add(g); }
        m.Status = FriendlyStatus.Played;
        await SettleAsync(m, homeGoals, awayGoals, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Người rời trận (PO 16/11): trước hết hiệp 1 → huỷ; sau đó Ranked xử thua 0-3, Tự do ghi "bỏ dở".</summary>
    /// <summary>Dấu trận bị xử thua vì AFK (ghi vào LastError, không cần cột mới).</summary>
    public const string AfkNote = "Xử thua vì đứng yên 5 phút (AFK)";

    public async Task AbandonAsync(Guid matchId, Guid leaver, bool afterFirstHalf, CancellationToken ct = default, bool afk = false)
    {
        var m = await GetAsync(matchId, ct);
        if (m.Status != FriendlyStatus.Playing || !m.Involves(leaver)) throw new AdminRuleException("Trận không ở trạng thái đang đá");
        m.AbandonedBy = leaver;
        if (afk) m.LastError = AfkNote;                  // PO 18/11: hiện "bỏ trận (AFK)" thay "rời trận"
        if (!afterFirstHalf) { m.Status = FriendlyStatus.Voided; m.EndedAt = clock.GetUtcNow(); }
        else
        {
            m.Status = FriendlyStatus.Abandoned;
            // PO 17/11: giao hữu cá nhân (Tự do) cũng tính Elo xếp hạng → bỏ dở sau hiệp 1 xử thua 0-3 như Ranked
            var homeLeft = m.HomeManagerId == leaver;
            await SettleAsync(m, homeLeft ? 0 : 3, homeLeft ? 3 : 0, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SettleAsync(FriendlyMatch m, int hg, int ag, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        m.HomeGoals = hg; m.AwayGoals = ag; m.EndedAt = now;
        // PO 17/11: người có tài khoản (chưa cần CLB) đá giao hữu để xếp hạng → Elo đổi ở cả 2 chế độ; tiền thưởng chỉ khi đá bằng CLB (Ranked)
        var home = await db.Managers.SingleAsync(x => x.Id == m.HomeManagerId, ct);
        var away = await db.Managers.SingleAsync(x => x.Id == m.AwayManagerId, ct);
        var (dh, da) = FriendlyRules.EloDelta(home.Elo, away.Elo, hg, ag);
        home.Elo += dh; away.Elo += da;
        m.EloHomeDelta = dh; m.EloAwayDelta = da;
        if (m.Mode != FriendlyMode.Ranked || !m.Counts) return;
        var eco = m.SeasonId is { } sid ? await db.SeasonEconomies.AsNoTracking().SingleOrDefaultAsync(e => e.SeasonId == sid, ct) : null;
        long win = eco?.FriendlyWinPrize ?? FriendlyRules.DefaultWinPrize, draw = eco?.FriendlyDrawPrize ?? FriendlyRules.DefaultDrawPrize;
        void Pay(Guid? club, long amount)
        {
            if (club is { } c && amount != 0) db.Ledger.Add(new ClubLedgerEntry { ClubId = c, Amount = amount, Reason = LedgerReason.FriendlyPrize, RefType = RefType, RefId = m.Id, At = now });
        }
        if (hg == ag) { Pay(m.HomeClubId, draw); Pay(m.AwayClubId, draw); }
        else Pay(hg > ag ? m.HomeClubId : m.AwayClubId, win);
    }

    // ---------- đọc ----------

    public async Task<FriendlyMatch> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.FriendlyMatches.SingleOrDefaultAsync(m => m.Id == id, ct) ?? throw new AdminRuleException("Không có trận giao hữu này");

    /// <summary>Trận giao hữu đang mở của 1 người (mời/chọn đội/chờ máy/đang đá).</summary>
    public Task<FriendlyMatch?> OpenMatchAsync(Guid managerId, CancellationToken ct = default) =>
        db.FriendlyMatches.Where(m => (m.HomeManagerId == managerId || m.AwayManagerId == managerId)
                && (m.Status == FriendlyStatus.Invited || m.Status == FriendlyStatus.Picking || m.Status == FriendlyStatus.Queued || m.Status == FriendlyStatus.Playing))
            .OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync(ct);

    /// <summary>Bảng điểm theo chế độ (mặc định Ranked = thách đấu bằng CLB): theo mùa (null = mọi thời gian; Tự do không gắn mùa).
    /// Tính cả trận bỏ dở có tỉ số (xử thua 0-3).</summary>
    public async Task<List<FriendlyStandingRow>> StandingsAsync(Guid? seasonId, CancellationToken ct = default, FriendlyMode mode = FriendlyMode.Ranked)
    {
        var q = db.FriendlyMatches.AsNoTracking().Where(m => m.Mode == mode && m.Counts && m.HomeGoals != null
            && (m.Status == FriendlyStatus.Played || m.Status == FriendlyStatus.Abandoned));
        if (seasonId is { } s) q = q.Where(m => m.SeasonId == s);
        var ms = await q.Select(m => new { m.HomeManagerId, m.AwayManagerId, m.HomeGoals, m.AwayGoals }).ToListAsync(ct);
        var sides = ms.SelectMany(m => new[] { (Id: m.HomeManagerId, F: m.HomeGoals!.Value, A: m.AwayGoals!.Value), (Id: m.AwayManagerId, F: m.AwayGoals!.Value, A: m.HomeGoals!.Value) }).ToList();
        var ids = sides.Select(x => x.Id).Distinct().ToList();
        var people = await db.Managers.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return sides.GroupBy(x => x.Id).Select(g => new FriendlyStandingRow(g.Key, people[g.Key].DisplayName, people[g.Key].Elo, g.Count(),
                g.Count(x => x.F > x.A), g.Count(x => x.F == x.A), g.Count(x => x.F < x.A), g.Sum(x => x.F), g.Sum(x => x.A), g.Sum(x => FriendlyRules.Points(x.F, x.A))))
            .OrderByDescending(r => r.Points).ThenByDescending(r => r.GoalsFor - r.GoalsAgainst).ThenByDescending(r => r.GoalsFor).ThenByDescending(r => r.Elo).ToList();
    }

    /// <summary>Thống kê thắng/hoà/thua của 1 người theo chế độ (hồ sơ).</summary>
    public async Task<FriendlyStats> StatsAsync(Guid managerId, FriendlyMode mode, CancellationToken ct = default)
    {
        var ms = await db.FriendlyMatches.AsNoTracking()
            .Where(m => m.Mode == mode && (m.HomeManagerId == managerId || m.AwayManagerId == managerId)
                && (m.Status == FriendlyStatus.Played || m.Status == FriendlyStatus.Abandoned))
            .Select(m => new { Home = m.HomeManagerId == managerId, m.HomeGoals, m.AwayGoals, m.Status }).ToListAsync(ct);
        var scored = ms.Where(m => m.HomeGoals != null).Select(m => (F: m.Home ? m.HomeGoals!.Value : m.AwayGoals!.Value, A: m.Home ? m.AwayGoals!.Value : m.HomeGoals!.Value)).ToList();
        return new FriendlyStats(scored.Count, scored.Count(x => x.F > x.A), scored.Count(x => x.F == x.A), scored.Count(x => x.F < x.A),
            scored.Sum(x => x.F), scored.Sum(x => x.A), ms.Count(m => m.Status == FriendlyStatus.Abandoned && m.HomeGoals == null));
    }

    /// <summary>10 trận gần nhất đã kết thúc của 1 người.</summary>
    public Task<List<FriendlyMatch>> RecentAsync(Guid managerId, int take = 10, CancellationToken ct = default) =>
        db.FriendlyMatches.AsNoTracking()
            .Where(m => (m.HomeManagerId == managerId || m.AwayManagerId == managerId)
                && (m.Status == FriendlyStatus.Played || m.Status == FriendlyStatus.Abandoned || m.Status == FriendlyStatus.Voided))
            .OrderByDescending(m => m.EndedAt).Take(take).ToListAsync(ct);

    /// <summary>Nhóm CLB trong menu Chọn đội của game (khớp LEAGUES trong tools/match/pesmatch.py). Đội tuyển chưa hỗ trợ
    /// (pesmatch chưa chọn được) — mở sau.</summary>
    public static readonly (int First, int Count, string Group)[] FreeGroups =
        [(0, 20, "Anh"), (20, 20, "Tây Ban Nha"), (40, 20, "Ý"), (60, 18, "Đức"), (78, 20, "Uefa League"), (98, 20, "Europa Clubs"), (118, 7, "CLB khác")];
    private static List<(int Index, string Name, string Group)>? _freeTeams;

    /// <summary>Đội chọn được ở Giao hữu tự do: CLB của OF gốc patch mới nhất (tên theo OF gốc).</summary>
    public async Task<List<(int Index, string Name, string Group)>> FreeTeamsAsync(IOptionFileStore store, CancellationToken ct = default)
    {
        if (_freeTeams is not null) return _freeTeams;
        var patch = await db.PatchVersions.AsNoTracking().OrderByDescending(p => p.ImportedAt).FirstOrDefaultAsync(ct);
        if (patch is null) return [];
        if (Pes17RoundService.IsPes17(patch.Name))   // PES17 (18/11): CLB có mã đội PES17 của patch (96 CLB 5 giải, máy có mẫu ảnh tên), nhóm theo nước
            return _freeTeams = (await (from x in db.ClubPesMappings join c in db.Clubs on x.ClubId equals c.Id
                                        where x.PatchVersionId == patch.Id
                                        select new { x.PesClubIndex, c.Name, c.Country }).ToListAsync(ct))
                .OrderBy(x => x.Country).ThenBy(x => x.Name).Select(x => (x.PesClubIndex, x.Name, x.Country ?? "CLB khác")).ToList();
        var of = Pes17League.OptionFile.OptionFileCodec.Decode(await store.ReadAsync(patch.BaseOptionFilePath, ct));
        return _freeTeams = FreeGroups.SelectMany(g => Enumerable.Range(g.First, g.Count).Select(i => (i, of.GetClubName(i), g.Group))).ToList();
    }

    // ---------- nội bộ ----------

    /// <summary>CLB người chơi đang cầm (phân công Human đang mở).</summary>
    public Task<Guid?> ClubOfAsync(Guid managerId, CancellationToken ct = default) =>
        db.TeamAssignments.Where(a => a.ParticipantId == managerId && a.To == null && a.ControllerType == ControllerType.Human)
            .Select(a => (Guid?)a.ClubId).FirstOrDefaultAsync(ct);

    /// <summary>Tạo trận; Ranked gắn CLB + mùa đang đá (null nếu 1 bên không có CLB).</summary>
    private async Task<FriendlyMatch?> NewMatchAsync(FriendlyMode mode, Guid home, Guid away, FriendlyStatus status, DateTimeOffset now, CancellationToken ct)
    {
        var m = new FriendlyMatch { Mode = mode, Status = status, HomeManagerId = home, AwayManagerId = away, CreatedAt = now };
        if (status == FriendlyStatus.Picking) m.PickDeadline = now + FriendlyRules.PickTime;
        if (mode == FriendlyMode.Ranked)
        {
            m.HomeClubId = await ClubOfAsync(home, ct);
            m.AwayClubId = await ClubOfAsync(away, ct);
            if (m.HomeClubId is null || m.AwayClubId is null) return null;
            m.SeasonId = await db.Seasons.Where(s => s.Status == SeasonStatus.Active).OrderByDescending(s => s.Number).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            var names = await db.Clubs.Where(c => c.Id == m.HomeClubId || c.Id == m.AwayClubId).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            m.HomeTeamName = names[m.HomeClubId.Value];
            m.AwayTeamName = names[m.AwayClubId.Value];
        }
        db.FriendlyMatches.Add(m);
        return m;
    }
}
