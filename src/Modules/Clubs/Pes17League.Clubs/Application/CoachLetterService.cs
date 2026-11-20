using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public enum CoachFolder { Inbox, Decisions, Sent, Archived }

public sealed record CoachLetterRow(CoachLetter Letter, IReadOnlyList<(string Label, string Hint)> Choices, string? PlayerName);

/// <summary>
/// Hòm thư HLV career (PO 19/11): mọi sự kiện, quyết định chính thức đi qua đây; HLV gửi yêu cầu cho BLĐ / cầu thủ, hồi âm vòng sau
/// (<see cref="CoachRoundService"/> gọi <see cref="AnswerAsync"/>). Mỗi loại yêu cầu 1 lần / <see cref="CoachEvents.RequestCooldown"/> vòng.
/// </summary>
public sealed class CoachLetterService(ClubsDbContext db, TimeProvider clock)
{
    internal static CoachLetter Write(ClubsDbContext db, Coach c, LetterKind kind, LetterFrom from, string sender, string subject, string body, int tick,
        DateTimeOffset at, Guid? clubId = null, Guid? playerId = null, string? key = null, string? link = null, string? data = null)
    {
        var l = new CoachLetter
        {
            CoachId = c.Id, Kind = kind, From = from, Sender = Cut(sender, 120), Subject = Cut(subject, 160), Body = Cut(body, 4000), Tick = tick, At = at,
            ClubId = clubId, PlayerId = playerId, Key = key, Link = link, Data = data,
        };
        db.CoachLetters.Add(l);
        return l;
    }

    static string Cut(string s, int n) => s.Length > n ? s[..n] : s;

    /// <summary>Thư quyết định từ 1 sự kiện trong <see cref="CoachEvents.All"/>.</summary>
    internal static CoachLetter WriteEvent(ClubsDbContext db, Coach c, CoachEventDef e, int tick, DateTimeOffset at, Guid? clubId, string? clubName,
        Guid? playerId, string? playerName)
    {
        string F(string s) => CoachEvents.Fill(s, clubName, playerName, c.Name);
        var l = Write(db, c, LetterKind.Decision, e.From, F(e.Sender), e.Subject, F(e.Body), tick, at, clubId, playerId, e.Key);
        l.Options = string.Join('\n', e.Options.Select(o => F(o.Label)));
        l.DefaultOption = e.Default;
        l.DeadlineTick = tick + CoachEvents.DeadlineRounds;
        return l;
    }

    public async Task<IReadOnlyList<CoachLetterRow>> ListAsync(Guid coachId, CoachFolder folder, CancellationToken ct = default)
    {
        var q = db.CoachLetters.AsNoTracking().Where(l => l.CoachId == coachId);
        q = folder switch
        {
            CoachFolder.Decisions => q.Where(l => l.Kind == LetterKind.Decision && l.Chosen == null && !l.Archived),
            CoachFolder.Sent => q.Where(l => l.Outgoing),
            CoachFolder.Archived => q.Where(l => l.Archived),
            _ => q.Where(l => !l.Outgoing && !l.Archived),
        };
        var letters = await q.OrderByDescending(l => l.Tick).ThenByDescending(l => l.At).Take(150).ToListAsync(ct);
        var pids = letters.Where(l => l.PlayerId != null).Select(l => l.PlayerId!.Value).Distinct().ToList();
        var names = await db.Players.Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return letters.Select(l => new CoachLetterRow(l, Choices(l), l.PlayerId is { } p ? names.GetValueOrDefault(p) : null)).ToList();
    }

    /// <summary>Phương án + gợi ý chiều (sự kiện) của thư quyết định.</summary>
    public static IReadOnlyList<(string Label, string Hint)> Choices(CoachLetter l)
    {
        if (l.Options is null) return [];
        var labels = l.Options.Split('\n');
        var def = CoachEvents.Find(l.Key);
        return labels.Select((x, i) => (x, def is not null && i < def.Options.Length ? CoachEvents.Hint(def.Options[i].Fx) : JobHint(l.Key, i))).ToList();
    }

    static string JobHint(string? key, int i) => (key, i) switch
    {
        ("moi-viec", 0) => "Mở thương lượng hợp đồng",
        ("moi-viec", _) => "Bỏ qua lời mời",
        ("gia-han", 0) => "Ở lại thêm 1 mùa",
        ("gia-han", _) => "Rời CLB cuối mùa",
        _ => "",
    };

    public async Task<int> UnreadAsync(Guid managerId, CancellationToken ct = default) =>
        await (from l in db.CoachLetters join c in db.Coaches on l.CoachId equals c.Id
               where c.ManagerId == managerId && !l.Outgoing && !l.Read && !l.Archived select l).CountAsync(ct);

    public async Task MarkReadAsync(Guid coachId, Guid letterId, CancellationToken ct = default)
    {
        if (await db.CoachLetters.SingleOrDefaultAsync(l => l.Id == letterId && l.CoachId == coachId, ct) is { Read: false } l)
        {
            l.Read = true;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task ArchiveAsync(Guid coachId, Guid letterId, bool archive = true, CancellationToken ct = default)
    {
        if (await db.CoachLetters.SingleOrDefaultAsync(l => l.Id == letterId && l.CoachId == coachId, ct) is { } l)
        {
            if (archive && l.Kind == LetterKind.Decision && l.Chosen is null) return;   // thư chờ quyết định không cất được
            l.Archived = archive;
            l.Read = true;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Chọn phương án cho thư quyết định. Trả về (lỗi, mô tả kết quả).</summary>
    public async Task<(string? Error, string? Result)> ChooseAsync(Guid coachId, Guid letterId, int option, CancellationToken ct = default)
    {
        var l = await db.CoachLetters.SingleOrDefaultAsync(x => x.Id == letterId && x.CoachId == coachId, ct);
        if (l is not { Kind: LetterKind.Decision }) return ("Không tìm thấy thư.", null);
        if (l.Chosen is not null) return ("Đã quyết định thư này.", null);
        var c = await db.Coaches.SingleAsync(x => x.Id == coachId, ct);
        var count = l.Options?.Split('\n').Length ?? 0;
        if (option < 0 || option >= count) return ("Phương án không hợp lệ.", null);
        if (CoachEvents.Find(l.Key) is { } e && e.Options[option].Fx is { } fx && fx.Invest + Math.Max(0, -fx.Money) > 0
            && await CoachService.BalanceAsync(db, c.Id, ct) < fx.Invest + Math.Max(0, -fx.Money))
            return ("Ví HLV không đủ tiền cho phương án này.", null);
        var result = await ResolveAsync(c, l, option, false, ct);
        await db.SaveChangesAsync(ct);
        return (null, result);
    }

    /// <summary>Áp phương án (người chọn hoặc mặc định khi quá hạn). Không lưu.</summary>
    internal async Task<string> ResolveAsync(Coach c, CoachLetter l, int option, bool byDefault, CancellationToken ct)
    {
        l.Chosen = option;
        l.Read = true;
        var tick = await CoachService.NowTickAsync(db, ct);
        var label = l.Options?.Split('\n').ElementAtOrDefault(option) ?? "";
        if (l.Key == "moi-viec")
        {
            if (option == 0 && CoachEvents.Data(l.Data).TryGetValue("club", out var cs) && Guid.TryParse(cs, out var clubId))
            {
                var (app, err) = await new CoachJobService(db, clock).OpenAsync(c, clubId, invited: true, ct);
                return err ?? "Đã mở thương lượng. Vào mục Việc làm để trao đổi với chủ tịch.";
            }
            return byDefault ? "Lời mời đã hết hạn." : "Đã từ chối lời mời.";
        }
        if (l.Key == "gia-han")
        {
            var k = await db.CoachContracts.SingleOrDefaultAsync(x => x.CoachId == c.Id && x.Status == CoachContractStatus.Active, ct);
            if (k is not null && option == 0) k.RenewAgreed = true;
            return option == 0 ? "Đã đồng ý gia hạn thêm 1 mùa." : "Ông sẽ rời CLB khi mùa giải kết thúc.";
        }
        if (CoachEvents.Find(l.Key) is not { } e || option >= e.Options.Length) return label;
        var fx = e.Options[option].Fx;
        var summary = await new CoachEffects(db, clock).ApplyAsync(c, fx, l.ClubId, l.PlayerId, tick, $"{e.Subject}: {label}", ct);
        return (byDefault ? "Hết hạn trả lời, áp phương án: " + label + ". " : "") + summary;
    }

    // ---------- yêu cầu gửi đi ----------

    /// <summary>
    /// Gửi yêu cầu cho BLĐ (cần đang có việc) hoặc cầu thủ (thuộc CLB). <paramref name="data"/> vd "role=Coach" cho thuê nhân viên.
    /// Hồi âm ở vòng sau. Mỗi loại (cầu thủ: mỗi loại × mỗi người) 1 lần / 6 vòng.
    /// </summary>
    public async Task<string?> SendAsync(Guid coachId, string key, Guid? playerId, string? note, string? data = null, CancellationToken ct = default)
    {
        if (CoachEvents.Request(key) is not { } r) return "Loại yêu cầu không hợp lệ.";
        var c = await db.Coaches.SingleAsync(x => x.Id == coachId, ct);
        var k = await db.CoachContracts.SingleOrDefaultAsync(x => x.CoachId == c.Id && x.Status == CoachContractStatus.Active, ct);
        if (c.Status != CoachStatus.Employed || k is null) return "Ông đang không dẫn dắt CLB nào.";
        var tick = await CoachService.NowTickAsync(db, ct);
        if (r.ToPlayer)
        {
            if (playerId is not { } pid || !await db.SquadMemberships.AnyAsync(m => m.ClubId == k.ClubId && m.PlayerId == pid && m.LeftAt == null, ct))
                return "Cầu thủ không thuộc CLB.";
        }
        else playerId = null;
        if (key == "nhan-su" && !(CoachEvents.Data(data).TryGetValue("role", out var role) && Enum.TryParse<StaffRole>(role, out _))) return "Chọn vị trí nhân viên.";
        if (key == "tai-tro-khoa-hoc" && c.CourseLicense == 0) return "Ông chưa ghi danh khoá học nào.";
        if (key == "tai-tro-khoa-hoc") data = $"course={c.CourseLicense}";
        var last = await db.CoachLetters.Where(l => l.CoachId == c.Id && l.Outgoing && l.Key == key && (playerId == null || l.PlayerId == playerId))
            .OrderByDescending(l => l.Tick).Select(l => (int?)l.Tick).FirstOrDefaultAsync(ct);
        if (last is { } t && SameWindow(t, tick)) return $"Loại yêu cầu này chỉ gửi 1 lần mỗi {CoachEvents.RequestCooldown} vòng.";
        var club = await db.Clubs.Where(x => x.Id == k.ClubId).Select(x => x.Name).SingleAsync(ct);
        var player = playerId is { } p ? await db.Players.Where(x => x.Id == p).Select(x => x.Name).SingleAsync(ct) : null;
        var to = r.ToPlayer ? player! : $"Ban lãnh đạo {club}";
        var body = string.IsNullOrWhiteSpace(note) ? RequestBody(key, c, player, data) : note.Trim();
        var l = Write(db, c, LetterKind.Request, r.ToPlayer ? LetterFrom.Player : LetterFrom.Board, to, r.Name, body, tick, clock.GetUtcNow(), k.ClubId, playerId, key, data: data);
        l.Outgoing = true;
        l.Read = true;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Hai tick cách nhau &lt; 6 vòng (cùng mùa) — khác mùa coi như đã qua.</summary>
    internal static bool SameWindow(int a, int b) => a / 100 == b / 100 && Math.Abs(b - a) < CoachEvents.RequestCooldown;

    static string RequestBody(string key, Coach c, string? player, string? data) => key switch
    {
        "tang-ngan-sach" => "Kính gửi ban lãnh đạo, đội cần bổ sung lực lượng để bám mục tiêu. Tôi đề nghị CLB cân nhắc tăng ngân sách chuyển nhượng.",
        "nhan-su" => $"Tôi đề nghị CLB nâng cấp vị trí {StaffName(CoachEvents.Data(data).GetValueOrDefault("role"))} để ban huấn luyện làm việc hiệu quả hơn.",
        "tang-luong" => "Tôi mong được gia hạn hợp đồng và điều chỉnh đãi ngộ tương xứng với kết quả của đội.",
        "giam-muc-tieu" => "Với tình hình lực lượng hiện tại, tôi đề nghị BLĐ điều chỉnh mục tiêu mùa giải cho thực tế hơn.",
        "tai-tro-khoa-hoc" => "Tôi đang theo khoá nâng bằng huấn luyện, mong CLB hỗ trợ một phần học phí.",
        "tu-chuc" => "Sau thời gian cân nhắc, tôi xin được chấm dứt hợp đồng theo thoả thuận.",
        "khen" => $"{player}, thầy đánh giá cao những gì em thể hiện. Cứ giữ phong độ này.",
        "nhac-nho" => $"{player}, thầy cần em tập trung hơn. Phong độ vừa qua chưa đạt yêu cầu.",
        "bai-tap" => $"{player}, thầy giao cho em giáo án tập riêng trong tuần này.",
        "danh-sach-ban" => $"{player}, CLB đang cân nhắc chào bán em nếu có đề nghị phù hợp.",
        _ => c.Name,
    };

    public static string StaffName(string? role) => role switch
    {
        nameof(StaffRole.Assistant) => "trợ lý HLV",
        nameof(StaffRole.Coach) => "HLV tập luyện",
        nameof(StaffRole.Physio) => "bác sĩ / chuyên gia thể lực",
        nameof(StaffRole.Scout) => "trinh sát",
        _ => "nhân viên",
    };

    /// <summary>Hồi âm 1 yêu cầu (gọi ở vòng sau khi gửi). Không lưu.</summary>
    internal async Task AnswerAsync(Coach c, CoachLetter req, Guid seasonId, int tick, CancellationToken ct)
    {
        req.AnsweredTick = tick;
        var now = clock.GetUtcNow();
        var k = await db.CoachContracts.SingleOrDefaultAsync(x => x.CoachId == c.Id && x.Status == CoachContractStatus.Active, ct);
        if (k is null || k.ClubId != req.ClubId)
        {
            Reply(c, req, "Không còn hiệu lực", "Yêu cầu không được xử lý vì ông không còn dẫn dắt CLB.", tick, now);
            return;
        }
        var fx = new CoachEffects(db, clock);
        var board = await fx.BoardAsync(seasonId, k.ClubId, ct);
        var chairman = CoachRules.Chairman(k.ClubId);
        string sign = $"\n\n{chairman}\nChủ tịch CLB";
        if (!CoachEvents.Request(req.Key)!.ToPlayer)
        {
            var recent = await db.CoachLetters.CountAsync(l => l.CoachId == c.Id && l.Outgoing && l.PlayerId == null && l.Id != req.Id
                && l.Tick / 100 == tick / 100 && l.Tick > tick - CoachEvents.RequestCooldown, ct);
            var spam = "";
            if (recent >= 3 && board is not null)
            {
                board.Confidence = Math.Max(0, board.Confidence - 2);
                spam = "\n\nBan lãnh đạo nhắc ông hạn chế gửi quá nhiều yêu cầu trong thời gian ngắn.";
            }
            var conf = board?.Confidence ?? BoardRules.StartConfidence;
            var (title, text) = req.Key switch
            {
                "tang-ngan-sach" => await BudgetAsync(k, conf, seasonId, now, ct),
                "nhan-su" => await StaffAsync(k, conf, CoachEvents.Data(req.Data).GetValueOrDefault("role"), now, ct),
                "tang-luong" => await RaiseAsync(k, conf, seasonId, ct),
                "giam-muc-tieu" => await ObjectiveAsync(k, board, seasonId, ct),
                "tai-tro-khoa-hoc" => await CourseAsync(c, k, conf, tick, now, ct),
                "tu-chuc" => await new CoachJobService(db, clock).ResignAsync(c, k, tick, ct),
                _ => ("Đã nhận", "Ban lãnh đạo đã nhận thư."),
            };
            if (req.Key is not "tu-chuc" && title.StartsWith("Từ chối") && board is not null && conf < 50) board.Confidence = Math.Max(0, board.Confidence - 1);
            Reply(c, req, title, text + spam + sign, tick, now);
            return;
        }

        var pid = req.PlayerId!.Value;
        var name = await db.Players.Where(p => p.Id == pid).Select(p => p.Name).SingleOrDefaultAsync(ct) ?? "Cầu thủ";
        if (!await db.SquadMemberships.AnyAsync(m => m.ClubId == k.ClubId && m.PlayerId == pid && m.LeftAt == null, ct))
        {
            Reply(c, req, "Không còn hiệu lực", $"{name} không còn thuộc CLB.", tick, now);
            return;
        }
        var cond = await db.PlayerConditions.FindAsync([pid], ct);
        if (cond is null) db.PlayerConditions.Add(cond = new PlayerCondition { PlayerId = pid });
        var people = CoachRules.PeopleFactor(CoachRules.Effective(c, CoachSkill.People));
        int Scale(int d) => d > 0 ? (int)Math.Round(d * people, MidpointRounding.AwayFromZero) : d;
        int delta = 0;
        string reply;
        switch (req.Key)
        {
            case "khen":
            case "nhac-nho":
                var rating = await (from r in db.MatchRatings join f in db.Fixtures on r.FixtureId equals f.Id
                                    where r.PlayerId == pid orderby f.PlayedAt descending select r.Rating).FirstOrDefaultAsync(ct);
                delta = MediaRules.Talk(req.Key == "khen" ? TalkKind.Praise : TalkKind.Criticise, rating, cond.Morale);
                if (req.Key == "nhac-nho" && c.Trait == CoachTrait.HotHead) delta = delta * 3 / 2;
                delta = Scale(delta);
                reply = delta > 0
                    ? (req.Key == "khen" ? "Em cảm ơn thầy. Em sẽ cố gắng giữ phong độ." : "Em hiểu rồi thầy. Em sẽ tập trung hơn ở trận tới.")
                    : (req.Key == "khen" ? "Em cảm ơn thầy, nhưng em biết mình chưa chơi tốt." : "Em thấy thầy hơi khắt khe với em. Em không đồng ý lắm.");
                break;
            case "bai-tap":
                db.CoachDrills.Add(new CoachDrill { CoachId = c.Id, ClubId = k.ClubId, PlayerId = pid, Tick = tick, At = now });
                delta = Scale(1);
                reply = "Em nhận giáo án rồi thầy. Em sẽ tập thêm sau giờ tập chung.";
                break;
            default:
                delta = -5;
                reply = "Em đã nhận thông báo. Em mong được ở lại chứng tỏ bản thân.";
                break;
        }
        cond.Morale = Math.Clamp(cond.Morale + delta, 0, 100);
        cond.UpdatedAt = now;
        Reply(c, req, delta >= 0 ? "Hồi âm" : "Hồi âm (không vui)", $"{reply}\n\n{name}", tick, now, LetterFrom.Player, name);
    }

    void Reply(Coach c, CoachLetter req, string title, string body, int tick, DateTimeOffset at, LetterFrom from = LetterFrom.Board, string? sender = null)
    {
        var l = Write(db, c, LetterKind.Reply, from, sender ?? "Ban lãnh đạo", $"Re: {req.Subject} — {title}", body, tick, at, req.ClubId, req.PlayerId, req.Key);
        l.ReplyToId = req.Id;
        l.Data = req.Data;
    }

    async Task<bool> AcceptedThisSeasonAsync(Guid coachId, string key, int season, CancellationToken ct) =>
        await db.CoachLetters.AnyAsync(l => l.CoachId == coachId && l.Kind == LetterKind.Reply && l.Key == key && l.Tick / 100 == season
            && l.Subject.Contains("Chấp thuận"), ct);

    async Task<(string, string)> BudgetAsync(CoachContract k, int conf, Guid seasonId, DateTimeOffset now, CancellationToken ct)
    {
        var season = await db.Seasons.Where(s => s.Id == seasonId).Select(s => s.Number).SingleAsync(ct);
        if (await AcceptedThisSeasonAsync(k.CoachId, "tang-ngan-sach", season, ct)) return ("Từ chối", "CLB đã bơm thêm ngân sách mùa này. Ông hãy dùng hiệu quả số tiền hiện có.");
        if (conf < 50) return ("Từ chối", $"Kết quả hiện tại chưa thuyết phục ban lãnh đạo chi thêm (độ tin cậy {conf}/100).");
        var initial = await db.Ledger.Where(e => e.ClubId == k.ClubId && e.Reason == LedgerReason.InitialBudget).OrderByDescending(e => e.At).Select(e => e.Amount).FirstOrDefaultAsync(ct);
        var share = conf >= 65 ? 10 : 5;
        var amount = Math.Max(conf >= 65 ? 200_000 : 100_000, Math.Abs(initial) * share / 100 / 100_000 * 100_000);
        db.Ledger.Add(new ClubLedgerEntry { ClubId = k.ClubId, Amount = amount, Reason = LedgerReason.Adjustment, RefType = "CoachRequest", RefId = k.Id, At = now });
        return ("Chấp thuận", $"Ban lãnh đạo đồng ý bổ sung {MoneyText.Format(amount)} vào ngân sách chuyển nhượng. Mong ông dùng đúng chỗ.");
    }

    async Task<(string, string)> StaffAsync(CoachContract k, int conf, string? role, DateTimeOffset now, CancellationToken ct)
    {
        if (!Enum.TryParse<StaffRole>(role, out var r)) return ("Từ chối", "Yêu cầu thiếu vị trí cần nâng cấp.");
        var level = (await db.ClubStaff.FindAsync([k.ClubId, r], ct))?.Level ?? 1;
        if (level >= StaffRules.MaxLevel) return ("Từ chối", $"Vị trí {StaffName(role)} đã ở cấp cao nhất.");
        if (conf < 55) return ("Từ chối", $"Ban lãnh đạo chưa sẵn sàng đầu tư thêm cho ban huấn luyện (độ tin cậy {conf}/100).");
        var fee = StaffRules.SigningFee(level + 1);
        db.Ledger.Add(new ClubLedgerEntry { ClubId = k.ClubId, Amount = fee, Reason = LedgerReason.Adjustment, RefType = "CoachRequest", RefId = k.Id, At = now });
        await db.SaveChangesAsync(ct);
        var err = await new BoardService(db, clock).SetLevelAsync(k.ClubId, r, level + 1, ct);
        return err is null
            ? ("Chấp thuận", $"CLB đã chi {MoneyText.Format(fee)} nâng {StaffName(role)} lên cấp {level + 1}. Lương nhân viên tính vào quỹ CLB.")
            : ("Từ chối", err);
    }

    async Task<(string, string)> RaiseAsync(CoachContract k, int conf, Guid seasonId, CancellationToken ct)
    {
        var season = await db.Seasons.Where(s => s.Id == seasonId).Select(s => s.Number).SingleAsync(ct);
        if (await AcceptedThisSeasonAsync(k.CoachId, "tang-luong", season, ct)) return ("Từ chối", "Hợp đồng của ông vừa được điều chỉnh mùa này.");
        if (conf < 55) return ("Từ chối", $"Chưa phải lúc bàn chuyện hợp đồng (độ tin cậy {conf}/100). Hãy cải thiện kết quả trước.");
        var rounds = await db.Matchdays.CountAsync(m => m.SeasonId == seasonId, ct);
        k.Seasons++;
        k.EndSeasonNumber++;
        k.TotalRounds += rounds;
        if (conf >= 70)
        {
            k.Wage = k.Wage * 110 / 100 / 100 * 100;
            k.Bonus = CoachRules.OfferBonus(k.Wage);
            return ("Chấp thuận", $"Ban lãnh đạo gia hạn thêm 1 mùa (tới hết mùa {k.EndSeasonNumber}) và tăng lương lên {MoneyText.Format(k.Wage)}/vòng.");
        }
        return ("Chấp thuận", $"Ban lãnh đạo gia hạn thêm 1 mùa (tới hết mùa {k.EndSeasonNumber}), giữ nguyên lương.");
    }

    async Task<(string, string)> ObjectiveAsync(CoachContract k, BoardConfidence? board, Guid seasonId, CancellationToken ct)
    {
        var season = await db.Seasons.Where(s => s.Id == seasonId).Select(s => s.Number).SingleAsync(ct);
        if (await AcceptedThisSeasonAsync(k.CoachId, "giam-muc-tieu", season, ct)) return ("Từ chối", "Mục tiêu đã được điều chỉnh một lần mùa này.");
        var status = await new BoardService(db, clock).StatusAsync(seasonId, k.ClubId, ct);
        if (board is null || status is null) return ("Từ chối", "Chưa có bảng xếp hạng để đánh giá.");
        if (board.Confidence < 40) return ("Từ chối", "Ban lãnh đạo không chấp nhận hạ mục tiêu lúc này.");
        if (status.Position is not { } pos || pos <= board.ExpectedPosition) return ("Từ chối", "Đội đang bám đúng mục tiêu, không có lý do điều chỉnh.");
        board.ExpectedPosition = Math.Min(status.Teams, board.ExpectedPosition + 2);
        board.Confidence = Math.Max(0, board.Confidence - 5);
        k.ExpectedPosition = board.ExpectedPosition;
        k.Objective = BoardRules.Objective(board.ExpectedPosition, status.Teams);
        return ("Chấp thuận", $"Mục tiêu mới: {k.Objective.ToLowerInvariant()} (hạng {board.ExpectedPosition}). Ban lãnh đạo không vui nhưng chấp nhận.");
    }

    async Task<(string, string)> CourseAsync(Coach c, CoachContract k, int conf, int tick, DateTimeOffset now, CancellationToken ct)
    {
        if (c.CourseLicense == 0) return ("Từ chối", "Ông không theo khoá học nào.");
        var key = $"course={c.CourseLicense}";
        if (await db.CoachLetters.AnyAsync(l => l.CoachId == c.Id && l.Kind == LetterKind.Reply && l.Key == "tai-tro-khoa-hoc" && l.Subject.Contains("Chấp thuận")
                && l.Data == key, ct))
            return ("Từ chối", "CLB đã tài trợ khoá học này.");
        if (conf < 60) return ("Từ chối", $"Ban lãnh đạo chưa sẵn sàng tài trợ (độ tin cậy {conf}/100).");
        var fee = CoachRules.CourseFee(CoachRules.Courses[c.CourseLicense - 1], c.Trait) / 2;
        CoachService.Money(db, c, fee, CoachMoney.Course, "CLB tài trợ học phí", tick, now);
        db.Ledger.Add(new ClubLedgerEntry { ClubId = k.ClubId, Amount = -fee, Reason = LedgerReason.Staff, RefType = "CoachCourse", RefId = c.Id, At = now });
        return ("Chấp thuận", $"CLB trả một nửa học phí ({MoneyText.Format(fee)}), đã chuyển vào ví của ông.");
    }
}
