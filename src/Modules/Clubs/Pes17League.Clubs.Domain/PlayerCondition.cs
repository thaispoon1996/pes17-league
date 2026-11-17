namespace Pes17League.Clubs.Domain;

/// <summary>Thể lực / chấn thương / tinh thần của cầu thủ (FM đợt 5). Chưa đọc được từ game → mô phỏng trên web khi chốt vòng.</summary>
public class PlayerCondition
{
    public Guid PlayerId { get; set; }
    /// <summary>Mệt mỏi 0–100 (0 = sung sức).</summary>
    public int Fatigue { get; set; }
    /// <summary>Tinh thần 0–100.</summary>
    public int Morale { get; set; } = ConditionRules.DefaultMorale;
    /// <summary>Số vòng còn nghỉ vì chấn thương (0 = khoẻ).</summary>
    public int InjuredRounds { get; set; }
    public string? Injury { get; set; }
    /// <summary>Vòng đã áp gần nhất (chống ghi 2 lần khi chốt lại).</summary>
    public Guid? LastMatchdayId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Luật mô phỏng (PO duyệt mặc định 12/11: chưa đọc được chấn thương từ game thì mô phỏng). Đá trận: mệt +25 (tập Nặng +5),
/// không đá: −30 (y tế tốt hồi nhanh hơn). Rủi ro chấn thương mỗi trận 2% + 4% nếu đã mệt > 70, nghỉ 1–4 vòng.
/// Tinh thần: điểm ≥ 7 +5, ≤ 5 −5, thắng +3, thua −3, không được đá −2.
/// </summary>
public static class ConditionRules
{
    public const int DefaultMorale = 60;
    public const int MatchFatigue = 25, HardTrainingFatigue = 5, RestRecovery = 30, TiredAt = 70;
    public const int InjuryPermille = 20, TiredInjuryPermille = 40;
    public static readonly string[] Injuries = ["Căng cơ đùi", "Bong gân cổ chân", "Đau gối", "Va chạm mạnh", "Rách cơ bắp chân"];

    /// <summary>Ngẫu nhiên ổn định theo (cầu thủ, vòng): 0–999.</summary>
    public static int Roll(Guid playerId, Guid matchdayId) => (int)((uint)HashCode.Combine(playerId, matchdayId, 17) % 1000);

    public static bool Injured(int fatigueBefore, int roll, int cut = 0) => roll < Math.Max(5, InjuryPermille - cut) + (fatigueBefore > TiredAt ? TiredInjuryPermille : 0);
    public static int InjuryRounds(int roll) => 1 + roll % 4;

    public static int MoraleDelta(bool played, decimal? rating, int goalDiff)
    {
        if (!played) return -2;
        var d = rating >= 7 ? 5 : rating <= 5 ? -5 : 0;
        return d + (goalDiff > 0 ? 3 : goalDiff < 0 ? -3 : 0);
    }

    /// <summary>Chỉ số tổng hiệu dụng cho HLV BOT chọn đội: chấn thương −40, mệt trừ dần.</summary>
    public static int EffectiveOverall(int overall, PlayerCondition? c) =>
        c is null ? overall : overall - (c.InjuredRounds > 0 ? 40 : 0) - Math.Max(0, c.Fatigue - 50) / 10;
}
