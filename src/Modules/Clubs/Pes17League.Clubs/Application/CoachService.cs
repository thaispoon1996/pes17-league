using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Application;

public sealed record CoachInfo(string Name, string? Nickname, int Age, string Nationality, string Formation, string Style);

public sealed record CoachLook(string Hair, string HairColor, string Outfit, string Skin);

/// <summary>Hồ sơ HLV + CLB đang dẫn + ví (trang /hlv, thẻ HLV).</summary>
public sealed record CoachProfile(Coach Coach, long Wallet, CoachContract? Contract, string? ClubName, string? ClubLogo, int Unread, int Pending,
    IReadOnlyList<CoachAsset> Assets, IReadOnlyList<CoachLedgerEntry> Ledger, IReadOnlyList<(Guid Id, string Caption, DateTimeOffset At)> Photos);

/// <summary>
/// Hồ sơ HLV career (PO 19/11): tạo (bốc xuất thân, phân điểm, ngoại hình, mặt 3D), ví riêng, mức sống, tài sản, khoá bằng, ảnh lưu niệm.
/// Luật ở <see cref="CoachRules"/>. Mọi hàm đổi dữ liệu trả lỗi dạng chuỗi (null = thành công).
/// </summary>
public sealed class CoachService(ClubsDbContext db, TimeProvider clock)
{
    public const int MaxFaceBytes = 400_000, MaxPhotoBytes = 1_500_000, MaxMeshChars = 60_000;

    public Task<Coach?> GetAsync(Guid managerId, CancellationToken ct = default) =>
        db.Coaches.SingleOrDefaultAsync(c => c.ManagerId == managerId, ct);

    /// <summary>Mốc hiện tại: mùa chưa xong sớm nhất (hoặc mùa cuối) × 100 + số khung đã chốt.</summary>
    public static async Task<int> NowTickAsync(ClubsDbContext db, CancellationToken ct = default)
    {
        var s = await db.Seasons.AsNoTracking().Where(x => x.Status != SeasonStatus.Finished).OrderBy(x => x.Number).FirstOrDefaultAsync(ct)
            ?? await db.Seasons.AsNoTracking().OrderByDescending(x => x.Number).FirstOrDefaultAsync(ct);
        if (s is null) return 0;
        var settled = await db.Matchdays.CountAsync(m => m.SeasonId == s.Id && m.Status == MatchdayStatus.Settled, ct);
        return CoachRules.Tick(s.Number, settled);
    }

    public static Task<long> BalanceAsync(ClubsDbContext db, Guid coachId, CancellationToken ct = default) =>
        db.CoachLedger.Where(e => e.CoachId == coachId).SumAsync(e => e.Amount, ct);

    internal static void Money(ClubsDbContext db, Coach c, long amount, CoachMoney kind, string note, int tick, DateTimeOffset at)
    {
        if (amount != 0) db.CoachLedger.Add(new CoachLedgerEntry { CoachId = c.Id, Amount = amount, Kind = kind, Note = note, Tick = tick, At = at });
    }

    // ---------- tạo ----------

    /// <summary>Bốc xuất thân lần đầu (tạo hồ sơ nháp) hoặc bốc lại (tối đa <see cref="CoachRules.Rerolls"/> lần, chỉ khi đang tạo).</summary>
    public async Task<(Coach? Coach, string? Error)> DrawAsync(Guid managerId, string? defaultName = null, CancellationToken ct = default)
    {
        var c = await GetAsync(managerId, ct);
        var now = clock.GetUtcNow();
        if (c is null)
        {
            c = new Coach { ManagerId = managerId, Name = (defaultName ?? "").Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 60)] : "HLV mới", Status = CoachStatus.Creating, CreatedAt = now };
            db.Coaches.Add(c);
        }
        else if (c.Status != CoachStatus.Creating) return (c, "Hồ sơ đã hoàn tất, không bốc lại được.");
        else if (c.Draws >= CoachRules.Rerolls) return (c, "Đã hết lượt bốc lại.");
        else c.Draws++;
        c.Seed = Random.Shared.Next(1, int.MaxValue);
        var (origin, skills, rep, trait) = CoachRules.Draw(c.Seed);
        var def = CoachRules.Origin(origin);
        c.Origin = origin;
        c.Trait = trait;
        c.BaseSkills = skills;
        c.Skills = [.. skills];
        c.FreePoints = def.FreePoints;
        c.Reputation = rep;
        c.License = def.License;
        c.Tick = await NowTickAsync(db, ct);
        c.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return (c, null);
    }

    public async Task<string?> SaveInfoAsync(Guid managerId, CoachInfo info, CancellationToken ct = default)
    {
        if (await GetAsync(managerId, ct) is not { } c) return "Chưa có hồ sơ HLV.";
        var name = info.Name.Trim();
        if (name.Length is < 2 or > 60) return "Tên 2–60 ký tự.";
        if (info.Age is < 28 or > 70) return "Tuổi 28–70.";
        if (!CoachRules.Formations.Contains(info.Formation)) return "Sơ đồ không hợp lệ.";
        if (!CoachRules.Styles.Contains(info.Style)) return "Lối chơi không hợp lệ.";
        c.Name = name;
        c.Nickname = string.IsNullOrWhiteSpace(info.Nickname) ? null : info.Nickname.Trim()[..Math.Min(info.Nickname.Trim().Length, 40)];
        c.Age = info.Age;
        c.Nationality = string.IsNullOrWhiteSpace(info.Nationality) ? "Việt Nam" : info.Nationality.Trim()[..Math.Min(info.Nationality.Trim().Length, 40)];
        c.Formation = info.Formation;
        c.Style = info.Style;
        c.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Phân điểm lúc tạo: mỗi năng lực ≥ gốc, ≤ <see cref="CoachRules.CreateCap"/>, tổng cộng thêm ≤ điểm tự do của xuất thân.</summary>
    public async Task<string?> SetSkillsAsync(Guid managerId, int[] skills, CancellationToken ct = default)
    {
        if (await GetAsync(managerId, ct) is not { Status: CoachStatus.Creating } c) return "Chỉ phân điểm khi đang tạo hồ sơ.";
        if (skills.Length != CoachRules.SkillCount) return "Thiếu năng lực.";
        var pool = CoachRules.Origin(c.Origin).FreePoints;
        var used = 0;
        for (var i = 0; i < skills.Length; i++)
        {
            if (skills[i] < c.BaseSkills[i] || skills[i] > Math.Max(CoachRules.CreateCap, c.BaseSkills[i])) return $"{CoachRules.SkillDefs[i].Name} ngoài giới hạn.";
            used += skills[i] - c.BaseSkills[i];
        }
        if (used > pool) return $"Vượt {pool} điểm tự do.";
        c.Skills = [.. skills];
        c.FreePoints = pool - used;
        c.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Dùng 1 điểm tự do (từ kinh nghiệm, khoá học, mục tiêu) sau khi đã tạo: +1 năng lực, trần 20.</summary>
    public async Task<string?> SpendPointAsync(Guid managerId, CoachSkill skill, CancellationToken ct = default)
    {
        if (await GetAsync(managerId, ct) is not { } c || c.Status == CoachStatus.Creating) return "Chưa có hồ sơ HLV.";
        if (c.FreePoints <= 0) return "Không còn điểm tự do.";
        var s = c.Skills.ToArray();
        if (s[(int)skill] >= CoachRules.SkillMax) return "Năng lực đã tối đa.";
        s[(int)skill]++;
        c.Skills = s;
        c.FreePoints--;
        c.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> SetLookAsync(Guid managerId, CoachLook look, CancellationToken ct = default)
    {
        if (await GetAsync(managerId, ct) is not { } c) return "Chưa có hồ sơ HLV.";
        static bool Hex(string s) => s.Length == 7 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);
        if (!CoachRules.Hairs.Contains(look.Hair) || !CoachRules.Outfits.Contains(look.Outfit) || !Hex(look.HairColor) || !Hex(look.Skin)) return "Ngoại hình không hợp lệ.";
        (c.Hair, c.HairColor, c.Outfit, c.Skin) = (look.Hair, look.HairColor, look.Outfit, look.Skin);
        c.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Ảnh mặt đã căn (JPEG) + lưới mặt (tuỳ chọn). <paramref name="jpeg"/> rỗng = xoá mặt.</summary>
    public async Task<string?> SetFaceAsync(Guid managerId, byte[]? jpeg, string? mesh, CancellationToken ct = default)
    {
        if (await GetAsync(managerId, ct) is not { } c) return "Chưa có hồ sơ HLV.";
        if (jpeg is { Length: > MaxFaceBytes }) return "Ảnh mặt quá lớn.";
        if (jpeg is { Length: > 0 } && !IsImage(jpeg)) return "Không phải ảnh JPEG/PNG.";
        if (mesh is { Length: > MaxMeshChars }) return "Dữ liệu quét mặt quá lớn.";
        c.Face = jpeg is { Length: > 0 } ? jpeg : null;
        c.FaceMesh = c.Face is null ? null : mesh;
        c.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return null;
    }

    internal static bool IsImage(byte[] b) =>
        b.Length > 4 && ((b[0] == 0xFF && b[1] == 0xD8) || (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47));

    /// <summary>
    /// Hoàn tất hồ sơ. Người chơi đang cầm CLB (career dở) → hợp đồng mặc định với CLB đó (<see cref="CoachJobService.DefaultContractAsync"/>);
    /// chưa có CLB → thất nghiệp, đi ứng tuyển.
    /// </summary>
    public async Task<string?> FinishAsync(Guid managerId, CancellationToken ct = default)
    {
        if (await GetAsync(managerId, ct) is not { Status: CoachStatus.Creating } c) return "Hồ sơ không ở bước tạo.";
        if (c.Name.Trim().Length < 2) return "Chưa đặt tên HLV.";
        var now = clock.GetUtcNow();
        c.Status = CoachStatus.Unemployed;
        c.Tick = await NowTickAsync(db, ct);
        c.UpdatedAt = now;
        CoachLetterService.Write(db, c, LetterKind.Official, LetterFrom.Federation, "Liên đoàn bóng đá", "Chào mừng tới nghề huấn luyện",
            $"Hồ sơ HLV {c.Name} đã được ghi nhận. Xuất thân: {CoachRules.Origin(c.Origin).Name}. Tính cách: {CoachRules.Trait(c.Trait).Name}. " +
            $"Bằng hiện có: {CoachRules.Licenses[c.License]}. Danh tiếng {c.Reputation} ({CoachRules.RepTier(c.Reputation)}).", c.Tick, now);
        await db.SaveChangesAsync(ct);
        var club = await db.TeamAssignments.Where(a => a.To == null && a.ControllerType == ControllerType.Human && a.ParticipantId == managerId)
            .Select(a => (Guid?)a.ClubId).FirstOrDefaultAsync(ct);
        if (club is { } id) await new CoachJobService(db, clock).DefaultContractAsync(c, id, ct);
        return null;
    }

    // ---------- hồ sơ ----------

    public async Task<CoachProfile?> ProfileAsync(Guid managerId, CancellationToken ct = default)
    {
        if (await db.Coaches.AsNoTracking().SingleOrDefaultAsync(c => c.ManagerId == managerId, ct) is not { } c) return null;
        var contract = await db.CoachContracts.AsNoTracking().Where(k => k.CoachId == c.Id && k.Status == CoachContractStatus.Active).OrderByDescending(k => k.SignedAt).FirstOrDefaultAsync(ct);
        var club = contract is null ? null : await db.Clubs.AsNoTracking().Where(x => x.Id == contract.ClubId).Select(x => new { x.Name, x.LogoPath }).SingleOrDefaultAsync(ct);
        var unread = await db.CoachLetters.CountAsync(l => l.CoachId == c.Id && !l.Outgoing && !l.Read && !l.Archived, ct);
        var pending = await db.CoachLetters.CountAsync(l => l.CoachId == c.Id && l.Kind == LetterKind.Decision && l.Chosen == null, ct);
        var assets = await db.CoachAssets.AsNoTracking().Where(a => a.CoachId == c.Id).OrderBy(a => a.At).ToListAsync(ct);
        var ledger = await db.CoachLedger.AsNoTracking().Where(e => e.CoachId == c.Id).OrderByDescending(e => e.At).ThenByDescending(e => e.Tick).Take(30).ToListAsync(ct);
        var photos = (await db.CoachPhotos.AsNoTracking().Where(p => p.CoachId == c.Id).OrderByDescending(p => p.At).Select(p => new { p.Id, p.Caption, p.At }).ToListAsync(ct))
            .Select(p => (p.Id, p.Caption, p.At)).ToList();
        return new(c, await BalanceAsync(db, c.Id, ct), contract, club?.Name, club?.LogoPath, unread, pending, assets, ledger, photos);
    }

    // ---------- cuộc sống ----------

    public async Task<string?> SetLifestyleAsync(Guid managerId, int level, CancellationToken ct = default)
    {
        if (await GetAsync(managerId, ct) is not { } c || c.Status == CoachStatus.Creating) return "Chưa có hồ sơ HLV.";
        if (level < 0 || level >= CoachRules.Living.Length) return "Mức sống không hợp lệ.";
        c.Lifestyle = level;
        c.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> BuyAssetAsync(Guid managerId, string key, CancellationToken ct = default)
    {
        if (await GetAsync(managerId, ct) is not { } c || c.Status == CoachStatus.Creating) return "Chưa có hồ sơ HLV.";
        if (CoachRules.Assets.FirstOrDefault(a => a.Key == key) is not { } def) return "Không có tài sản này.";
        if (await db.CoachAssets.AnyAsync(a => a.CoachId == c.Id && a.Key == key, ct)) return "Đã sở hữu.";
        if (await BalanceAsync(db, c.Id, ct) < def.Price) return "Ví HLV không đủ tiền.";
        var now = clock.GetUtcNow();
        var tick = await NowTickAsync(db, ct);
        db.CoachAssets.Add(new CoachAsset { CoachId = c.Id, Key = key, Price = def.Price, Tick = tick, At = now });
        Money(db, c, -def.Price, CoachMoney.Asset, $"Mua {def.Name}", tick, now);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> SellAssetAsync(Guid managerId, Guid assetId, CancellationToken ct = default)
    {
        if (await GetAsync(managerId, ct) is not { } c) return "Chưa có hồ sơ HLV.";
        if (await db.CoachAssets.SingleOrDefaultAsync(a => a.Id == assetId && a.CoachId == c.Id && a.MaturesTick == null, ct) is not { } a) return "Không tìm thấy tài sản.";
        var def = CoachRules.Assets.FirstOrDefault(x => x.Key == a.Key);
        var now = clock.GetUtcNow();
        Money(db, c, (long)(a.Price * CoachRules.AssetResale), CoachMoney.AssetSale, $"Bán {def?.Name ?? a.Key}", await NowTickAsync(db, ct), now);
        db.CoachAssets.Remove(a);
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Đăng ký khoá bằng kế tiếp: học phí trừ ví ngay, học <see cref="CoachCourse.Rounds"/> vòng.</summary>
    public async Task<string?> StartCourseAsync(Guid managerId, CancellationToken ct = default)
    {
        if (await GetAsync(managerId, ct) is not { } c || c.Status == CoachStatus.Creating) return "Chưa có hồ sơ HLV.";
        if (c.CourseLicense > 0) return "Đang học một khoá.";
        if (c.License >= CoachRules.Courses.Length) return "Đã có bằng cao nhất.";
        var course = CoachRules.Courses[c.License];
        var fee = CoachRules.CourseFee(course, c.Trait);
        if (await BalanceAsync(db, c.Id, ct) < fee) return "Ví HLV không đủ học phí.";
        var now = clock.GetUtcNow();
        var tick = await NowTickAsync(db, ct);
        c.CourseLicense = course.License;
        c.CourseRoundsLeft = course.Rounds;
        c.UpdatedAt = now;
        Money(db, c, -fee, CoachMoney.Course, $"Học phí bằng {CoachRules.Licenses[course.License]}", tick, now);
        CoachLetterService.Write(db, c, LetterKind.Official, LetterFrom.Federation, "Liên đoàn bóng đá", $"Xác nhận ghi danh khoá bằng {CoachRules.Licenses[course.License]}",
            $"Ông đã ghi danh khoá bằng {CoachRules.Licenses[course.License]}. Thời lượng {course.Rounds} vòng. Học phí {fee:N0} đã thu.", tick, now);
        await db.SaveChangesAsync(ct);
        return null;
    }

    // ---------- ảnh ----------

    public async Task<(Guid? Id, string? Error)> AddPhotoAsync(Guid managerId, string caption, byte[] image, CancellationToken ct = default)
    {
        if (await GetAsync(managerId, ct) is not { } c) return (null, "Chưa có hồ sơ HLV.");
        if (image.Length is 0 or > MaxPhotoBytes || !IsImage(image)) return (null, "Ảnh không hợp lệ.");
        var p = new CoachPhoto { CoachId = c.Id, Caption = caption.Length > 160 ? caption[..160] : caption, Image = image, Tick = await NowTickAsync(db, ct), At = clock.GetUtcNow() };
        db.CoachPhotos.Add(p);
        await db.SaveChangesAsync(ct);
        return (p.Id, null);
    }

    /// <summary>Ảnh (lưu niệm hoặc mặt) — chỉ chủ hồ sơ xem được (<paramref name="managerId"/> null = admin).</summary>
    public async Task<byte[]?> PhotoAsync(Guid? managerId, Guid photoId, CancellationToken ct = default) =>
        await (from p in db.CoachPhotos join c in db.Coaches on p.CoachId equals c.Id
               where p.Id == photoId && (managerId == null || c.ManagerId == managerId) select p.Image).SingleOrDefaultAsync(ct);

    public async Task<byte[]?> FaceAsync(Guid? managerId, Guid coachId, CancellationToken ct = default) =>
        await db.Coaches.Where(c => c.Id == coachId && (managerId == null || c.ManagerId == managerId)).Select(c => c.Face).SingleOrDefaultAsync(ct);
}
