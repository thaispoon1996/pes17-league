namespace Pes17League.Clubs.Domain;

public sealed record CoachOriginDef(CoachOrigin Origin, string Name, string Icon, int Weight, int Reputation, int License, int FreePoints, int[] Skills, string Description);

public sealed record CoachTraitDef(CoachTrait Trait, string Name, string Effect);

public sealed record CoachSkillDef(CoachSkill Skill, string Name, string Short, string Icon);

public enum ClubTier { Small, Mid, Big, Elite }

public sealed record ClubTierDef(ClubTier Tier, string Name, int Reputation, int License, long BaseWage);

public sealed record CoachCourse(int License, long Fee, int Rounds);

public sealed record CoachAssetDef(string Key, string Name, string Icon, long Price, long Upkeep, int StressRelief);

public sealed record CoachLivingDef(string Name, long Cost, int StressRelief);

public sealed record LaunchAnswer(string Text, string Tone, int FansPermille, int Board, int Reputation);

public sealed record LaunchQuestion(string Who, string Question, LaunchAnswer[] Answers);

public enum NegotiationOutcome { Accept, Counter, Reject, WalkAway }

/// <summary>
/// Luật HLV career (PO duyệt "làm tất cả theo đề xuất" 19/11). Năng lực so với mốc 10: mỗi điểm trên/dưới 10 đổi tác dụng
/// theo các hàm dưới. Căng thẳng ≥ 70: Con người, Truyền thông −1; ≥ 85: Huấn luyện thêm −1.
/// </summary>
public static class CoachRules
{
    public const int SkillCount = 7, CreateCap = 16, SkillMax = 20, SkillMin = 1, Rerolls = 2, Patience = 3, XpPerPoint = 25;
    public const int StartStress = 20, StressMax = 100, BurnoutAt = 80;
    public const int WarnBelow = 30, SackBelow = 20, SackAfterRound = 8, RenewFrom = 50, VacancyBelow = 30;
    public const int BonusRounds = 15, DefaultSeasons = 2, DefaultSeverance = 50;
    public const double AssetResale = 0.8;

    public static readonly CoachSkillDef[] SkillDefs =
    [
        new(CoachSkill.Training, "Huấn luyện", "Huấn luyện", "fitness_center"),
        new(CoachSkill.Youth, "Phát triển trẻ", "Trẻ", "child_care"),
        new(CoachSkill.People, "Quản lý con người", "Con người", "groups"),
        new(CoachSkill.Fitness, "Thể lực – y tế", "Thể lực", "monitor_heart"),
        new(CoachSkill.Negotiation, "Đàm phán", "Đàm phán", "handshake"),
        new(CoachSkill.Media, "Truyền thông", "Truyền thông", "mic"),
        new(CoachSkill.Scouting, "Trinh sát", "Trinh sát", "travel_explore"),
    ];

    public static readonly CoachOriginDef[] Origins =
    [
        new(CoachOrigin.Rookie, "Người mới", "trending_up", 35, 10, 0, 14, [8, 8, 8, 8, 8, 8, 8],
            "Bắt đầu từ con số không. Danh tiếng thấp nhất nhưng nhiều điểm tự do nhất."),
        new(CoachOrigin.Academy, "HLV học viện", "school", 30, 20, 1, 8, [11, 14, 10, 10, 7, 7, 10],
            "Lớn lên cùng lò đào tạo. Mạnh nhất ở phát triển cầu thủ trẻ."),
        new(CoachOrigin.Tactician, "Nhà chiến thuật", "psychology", 25, 30, 2, 8, [13, 9, 9, 9, 8, 8, 12],
            "Mê phân tích, có bằng B sớm. Giỏi huấn luyện và trinh sát, ít tiếng tăm."),
        new(CoachOrigin.ExPro, "Cựu danh thủ", "military_tech", 10, 45, 1, 6, [9, 7, 13, 10, 8, 13, 8],
            "Từng khoác áo đội tuyển. Báo chí và phòng thay đồ nể ngay từ đầu, nhưng chưa học nhiều về huấn luyện."),
    ];

    public static readonly CoachTraitDef[] Traits =
    [
        new(CoachTrait.PressDarling, "Được lòng báo chí", "Họp báo hiệu quả hơn 10%, ít dính scandal"),
        new(CoachTrait.HotHead, "Nóng tính", "Thua trận căng thẳng thêm, lời nhắc nhở cầu thủ mạnh hơn"),
        new(CoachTrait.Frugal, "Tiết kiệm", "Chi tiêu cá nhân giảm 20%"),
        new(CoachTrait.Studious, "Ham học", "Học phí khoá bằng giảm 25%"),
        new(CoachTrait.IronDiscipline, "Kỷ luật thép", "Cầu thủ ít bất mãn hơn"),
        new(CoachTrait.Charismatic, "Hào hoa", "Nhãn hàng hay mời quảng cáo hơn"),
    ];

    public static readonly string[] Licenses = ["Chưa có", "C", "B", "A", "Pro"];

    /// <summary>Khoá học lên bằng kế tiếp (chỉ số = bằng hiện tại).</summary>
    public static readonly CoachCourse[] Courses = [new(1, 500_000, 3), new(2, 1_500_000, 6), new(3, 4_000_000, 10), new(4, 10_000_000, 16)];

    public static readonly ClubTierDef[] Tiers =
    [
        new(ClubTier.Small, "Nhỏ", 0, 0, 20_000),
        new(ClubTier.Mid, "Vừa", 30, 1, 35_000),
        new(ClubTier.Big, "Lớn", 55, 2, 60_000),
        new(ClubTier.Elite, "Ông lớn", 75, 3, 100_000),
    ];

    public static readonly CoachLivingDef[] Living =
    [
        new("Giản dị", 3_000, 0), new("Thoải mái", 8_000, 1), new("Sung túc", 20_000, 2), new("Xa hoa", 45_000, 3),
    ];

    public static readonly CoachAssetDef[] Assets =
    [
        new("can-ho", "Căn hộ trung tâm", "apartment", 1_500_000, 2_000, 1),
        new("biet-thu", "Biệt thự ven sông", "villa", 6_000_000, 8_000, 2),
        new("xe", "Xe gia đình", "directions_car", 600_000, 1_000, 1),
        new("xe-the-thao", "Xe thể thao", "sports_motorsports", 2_500_000, 5_000, 1),
    ];

    public static readonly string[] Chairmen = ["Ông Harold Pike", "Bà Margaret Lowe", "Ông Graham Doyle", "Ông Victor Hale", "Bà Elena Sorel", "Ông Arturo Benz"];

    public static readonly string[] Formations = ["4-3-3", "4-4-2", "4-2-3-1", "3-5-2", "4-1-4-1", "5-3-2"];
    public static readonly string[] Styles = ["Kiểm soát bóng", "Pressing tầm cao", "Phản công", "Phòng ngự chắc", "Cân bằng"];
    public static readonly string[] Hairs = ["short", "quiff", "buzz", "bald"];
    public static readonly string[] Outfits = ["suit", "track", "polo"];

    // ---------- tạo HLV ----------

    /// <summary>Bốc xuất thân + năng lực gốc (±2) + danh tiếng (±5) + tính cách, tất định theo seed.</summary>
    public static (CoachOrigin Origin, int[] Skills, int Reputation, CoachTrait Trait) Draw(int seed)
    {
        var r = new Random(seed);
        var roll = r.Next(100);
        var def = Origins[^1];
        foreach (var o in Origins)
        {
            if (roll < o.Weight) { def = o; break; }
            roll -= o.Weight;
        }
        var skills = def.Skills.Select(s => Math.Clamp(s + r.Next(-2, 3), SkillMin, CreateCap)).ToArray();
        var rep = Math.Max(0, def.Reputation + r.Next(-5, 6));
        return (def.Origin, skills, rep, (CoachTrait)r.Next(Traits.Length));
    }

    public static CoachOriginDef Origin(CoachOrigin o) => Origins.Single(x => x.Origin == o);
    public static CoachTraitDef Trait(CoachTrait t) => Traits.Single(x => x.Trait == t);

    // ---------- năng lực hiệu dụng ----------

    public static int Effective(Coach c, CoachSkill s) => Effective(c.Skills, c.Stress, s);

    public static int Effective(int[] skills, int stress, CoachSkill s)
    {
        var v = skills.Length > (int)s ? skills[(int)s] : 10;
        if (stress >= 70 && s is CoachSkill.People or CoachSkill.Media) v--;
        if (stress >= 85 && s == CoachSkill.Training) v--;
        return Math.Clamp(v, SkillMin, SkillMax);
    }

    /// <summary>Huấn luyện: cơ hội (‰) thêm 1 chỉ số tăng ở buổi tập (âm = cơ hội bớt 1).</summary>
    public static int TrainingExtraPermille(int v) => (v - 10) * 60;

    /// <summary>Phát triển trẻ: cầu thủ ≤ <see cref="YouthAge"/> tuổi thêm cơ hội (‰) tăng 1 chỉ số mỗi buổi tập.</summary>
    public static int YouthExtraPermille(int v) => (v - 10) * 80;
    public const int YouthAge = 21;

    /// <summary>Quản lý con người: hệ số tác động tinh thần (nói chuyện, họp báo).</summary>
    public static double PeopleFactor(int v) => 1 + (v - 10) * 0.05;

    /// <summary>Thể lực – y tế: thể lực hồi thêm mỗi vòng nghỉ.</summary>
    public static int FitnessRecovery(int v) => (int)Math.Round((v - 10) / 2.0, MidpointRounding.AwayFromZero);

    /// <summary>Thể lực – y tế: giảm rủi ro chấn thương (‰).</summary>
    public static int FitnessInjuryCut(int v) => (int)Math.Round((v - 10) * 0.6, MidpointRounding.AwayFromZero);

    /// <summary>Đàm phán: giảm phí mua cầu thủ (%).</summary>
    public static int NegotiationDiscountPct(int v) => v - 10;

    /// <summary>Phí mua sau giảm (làm tròn nghìn).</summary>
    public static long Discounted(long price, int pct) => price * (100 - pct) / 100 / 1000 * 1000;

    /// <summary>Đàm phán: trần chủ tịch chịu chi khi thương lượng hợp đồng HLV (tỉ lệ so với đề nghị gốc).</summary>
    public static double ChairmanMax(int v) => 1.10 + (v - 10) * 0.02;

    /// <summary>Truyền thông: hệ số tác động tích cực của họp báo.</summary>
    public static double MediaFactor(int v, CoachTrait trait) => 1 + (v - 10) * 0.05 + (trait == CoachTrait.PressDarling ? 0.10 : 0);

    /// <summary>Trinh sát: báo cáo về sớm hơn (số vòng).</summary>
    public static int ScoutHaste(int v) => v >= 18 ? 2 : v >= 14 ? 1 : 0;

    /// <summary>Hiệu ứng người dùng đọc được cho năng lực mức <paramref name="v"/>.</summary>
    public static string Describe(CoachSkill s, int v)
    {
        static string Sg(int n) => n > 0 ? "+" + n : n < 0 ? "−" + -n : "0";
        return s switch
        {
            CoachSkill.Training => v == 10 ? "Chỉ số tăng khi tập: bình thường" : $"{Sg((v - 10) * 6)}% cơ hội thêm 1 chỉ số mỗi buổi tập",
            CoachSkill.Youth => v == 10 ? "Cầu thủ ≤21 tuổi lên như thường" : $"{Sg((v - 10) * 8)}% cơ hội cầu thủ ≤21 tuổi lên thêm",
            CoachSkill.People => v == 10 ? "Tinh thần sau nói chuyện: bình thường" : $"{Sg((v - 10) * 5)}% tác động nói chuyện, họp báo lên tinh thần",
            CoachSkill.Fitness => v == 10 ? "Hồi thể lực, rủi ro chấn thương bình thường" : $"{Sg(FitnessRecovery(v))} thể lực hồi mỗi vòng, {Sg(-FitnessInjuryCut(v))}‰ rủi ro chấn thương",
            CoachSkill.Negotiation => v == 10 ? "Giá mua cầu thủ bình thường" : $"{Sg(-(v - 10))}% phí mua cầu thủ, chủ tịch nhượng thêm {Sg((v - 10) * 2)}%",
            CoachSkill.Media => v == 10 ? "Họp báo hiệu quả bình thường" : $"{Sg((v - 10) * 5)}% hiệu quả họp báo",
            _ => v >= 18 ? "Báo cáo trinh sát sớm 2 vòng" : v >= 14 ? "Báo cáo trinh sát sớm 1 vòng (18: sớm 2)" : "Báo cáo đúng hạn (14: sớm 1 vòng)",
        };
    }

    // ---------- danh tiếng, bằng, CLB ----------

    public static string RepTier(int rep) => rep < 25 ? "Địa phương" : rep < 50 ? "Quốc gia" : rep < 75 ? "Châu lục" : "Thế giới";

    /// <summary>Bậc thẻ theo danh tiếng (dùng lớp thẻ cầu thủ: bronze/silver/gold/legend).</summary>
    public static string RepCard(int rep) => rep < 25 ? "bronze" : rep < 50 ? "silver" : rep < 75 ? "gold" : "legend";

    /// <summary>Bậc CLB theo thứ hạng giá trị đội trong giải (0 = cao nhất): top 10% ông lớn, 10–30% lớn, 30–60% vừa, còn lại nhỏ.</summary>
    public static ClubTier TierOf(int rank, int count)
    {
        if (count <= 0) return ClubTier.Small;
        var p = (rank + 0.5) / count;
        return p < 0.10 ? ClubTier.Elite : p < 0.30 ? ClubTier.Big : p < 0.60 ? ClubTier.Mid : ClubTier.Small;
    }

    public static ClubTierDef Tier(ClubTier t) => Tiers[(int)t];

    /// <summary>Lý do bị khoá (null = mở): thiếu danh tiếng hoặc bằng.</summary>
    public static string? LockReason(ClubTier tier, int rep, int license)
    {
        var t = Tier(tier);
        if (rep < t.Reputation && license < t.License) return $"Cần danh tiếng {t.Reputation} và bằng {Licenses[t.License]}";
        if (rep < t.Reputation) return $"Cần danh tiếng {t.Reputation}";
        if (license < t.License) return $"Cần bằng {Licenses[t.License]}";
        return null;
    }

    /// <summary>Lương/vòng chủ tịch đề nghị: lương gốc bậc × (0,8 + danh tiếng × 0,006), làm tròn 100.</summary>
    public static long OfferWage(ClubTier tier, int rep) => (long)Math.Round(Tier(tier).BaseWage * (0.8 + rep * 0.006) / 100) * 100;

    public static long OfferBonus(long wage) => wage * BonusRounds;

    public static string Chairman(Guid clubId) => Chairmen[(int)((uint)clubId.GetHashCode() % Chairmen.Length)];

    // ---------- thương lượng ----------

    /// <summary>Tỉ lệ đòi hỏi so với đề nghị gốc (w0, thưởng 15×w0, 2 mùa, bồi thường 50%).</summary>
    public static double Ratio(long wage, long bonus, int seasons, int severance, long baseWage) =>
        0.6 * wage / baseWage + 0.25 * bonus / (double)OfferBonus(baseWage) + 0.15 + (seasons - DefaultSeasons) * 0.03 + (severance - DefaultSeverance) / 100.0 * 0.1;

    /// <summary>Phán quyết của chủ tịch cho đề nghị có tỉ lệ <paramref name="ratio"/>: ≤ trần đồng ý; ≤ trần + 0,15 trả giá ở giữa (−1 kiên nhẫn);
    /// cao hơn → bác (−1 kiên nhẫn); hết kiên nhẫn → bỏ đi.</summary>
    public static NegotiationOutcome Judge(double ratio, double max, double current, int patience)
    {
        if (ratio <= max + 1e-9 || ratio <= current + 1e-9) return NegotiationOutcome.Accept;
        if (patience - 1 <= 0) return NegotiationOutcome.WalkAway;
        return ratio <= max + 0.15 ? NegotiationOutcome.Counter : NegotiationOutcome.Reject;
    }

    /// <summary>Đề nghị ở giữa (lương, thưởng) giữa đòi hỏi và đề nghị hiện tại, hạ dần tới khi ≤ trần.</summary>
    public static (long Wage, long Bonus) Middle(long askWage, long askBonus, long offWage, long offBonus, int seasons, int severance, long baseWage, double max)
    {
        long w = (askWage + offWage) / 2 / 100 * 100, b = (askBonus + offBonus) / 2 / 100 * 100;
        while (Ratio(w, b, seasons, severance, baseWage) > max && w > offWage)
        {
            w -= 100;
            b = Math.Max(offBonus, b - 1500);
        }
        return (Math.Max(w, offWage), Math.Max(b, offBonus));
    }

    // ---------- họp báo ra mắt ----------

    public static readonly LaunchQuestion[] Launch =
    [
        new("Phóng viên báo Thể Thao", "Mục tiêu của ông ở {club} mùa này là gì?",
        [
            new("Vượt mục tiêu ban lãnh đạo đặt ra.", "Tham vọng", 6, 2, 1),
            new("Hoàn thành mục tiêu {objective} trước đã.", "Thận trọng", 2, 4, 0),
            new("Tôi chỉ trả lời trên sân cỏ.", "Kiệm lời", 1, 0, 1),
        ]),
        new("Phóng viên truyền hình", "Ông có định chi mạnh trong kỳ chuyển nhượng hè?",
        [
            new("Đội cần thêm 2–3 bản hợp đồng chất lượng.", "Đòi hỏi", 4, -2, 0),
            new("Tôi tin vào đội hình hiện có.", "Tiết kiệm", -1, 4, 0),
            new("Tôi sẽ đôn cầu thủ trẻ từ học viện.", "Xây dựng", 2, 2, 1),
        ]),
        new("Phóng viên đài phát thanh", "Ông muốn nhắn gì tới cổ động viên {club}?",
        [
            new("Hãy lấp đầy khán đài, chúng tôi chiến đấu vì các bạn.", "Truyền lửa", 6, 0, 1),
            new("Hãy kiên nhẫn, thành công cần thời gian.", "Thực tế", -2, 3, 0),
            new("Tôi đến đây để giành danh hiệu.", "Tự tin", 5, -1, 2),
        ]),
    ];

    /// <summary>Tác động thật của 1 câu trả lời (phần dương nhân hệ số truyền thông).</summary>
    public static (int Fans, int Board, int Rep) LaunchEffect(LaunchAnswer a, double media)
    {
        int F(int v) => v > 0 ? (int)Math.Round(v * media, MidpointRounding.AwayFromZero) : v;
        return (F(a.FansPermille), F(a.Board), F(a.Reputation));
    }

    // ---------- tiến triển ----------

    /// <summary>Danh tiếng sau 1 trận: thắng đội mạnh hơn +1, thua đội yếu hơn −1.</summary>
    public static int MatchRep(int goalDiff, double myOvr, double oppOvr) =>
        goalDiff > 0 && oppOvr > myOvr ? 1 : goalDiff < 0 && oppOvr < myOvr ? -1 : 0;

    public static int MatchXp(int goalDiff) => goalDiff > 0 ? 3 : goalDiff == 0 ? 1 : 0;

    /// <summary>Căng thẳng sau trận: thua +3 (nóng tính +4), thắng −2.</summary>
    public static int MatchStress(int goalDiff, CoachTrait trait) => goalDiff < 0 ? (trait == CoachTrait.HotHead ? 4 : 3) : goalDiff > 0 ? -2 : 0;

    /// <summary>Danh tiếng cuối mùa: vô địch +12, đạt mục tiêu +6, vượt ≥ 3 bậc +4, trượt −4, có cúp +8.</summary>
    public static int SeasonRep(bool champion, int position, int expected, int cups)
    {
        var r = champion ? 12 : 0;
        r += position <= expected ? 6 : -4;
        if (position <= expected - 3) r += 4;
        return r + cups * 8;
    }

    public const int SackedRep = -8, ResignRep = -3;

    public static long CourseFee(CoachCourse c, CoachTrait trait) => trait == CoachTrait.Studious ? c.Fee * 3 / 4 : c.Fee;

    public static long LivingCost(int lifestyle, CoachTrait trait)
    {
        var cost = Living[Math.Clamp(lifestyle, 0, Living.Length - 1)].Cost;
        return trait == CoachTrait.Frugal ? cost * 4 / 5 : cost;
    }

    /// <summary>Số tiền bồi thường khi bị sa thải: % lương × số vòng còn lại của hợp đồng.</summary>
    public static long Severance(CoachContract k) => k.Wage * Math.Max(0, k.TotalRounds - k.RoundsPaid) * k.SeverancePct / 100;

    // ---------- tick, ngẫu nhiên tất định ----------

    public static int Tick(int seasonNumber, int matchday) => seasonNumber * 100 + matchday;

    /// <summary>Số 0–999 tất định theo (id, muối) — FNV-1a trên byte Guid.</summary>
    public static int Roll(Guid id, int salt)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var b in id.ToByteArray()) h = (h ^ b) * 16777619u;
            for (var i = 0; i < 4; i++) h = (h ^ (byte)(salt >> (i * 8))) * 16777619u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            return (int)(h % 1000);
        }
    }
}
