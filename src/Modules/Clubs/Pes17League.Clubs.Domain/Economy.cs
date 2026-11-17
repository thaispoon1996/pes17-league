namespace Pes17League.Clubs.Domain;

// Luật kinh tế decisions.md 01/11 (C12/C14/C16/C19/C20) + lựa chọn PO 01/11:
// giá = mô hình A (mũ theo chỉ số tổng × tuổi × phong độ), lương và thuế xa xỉ trừ mỗi vòng khi chốt vòng.
// Tiền là số nguyên (long), đơn vị tiền ảo. Lương = tiền mỗi vòng (mỗi vòng = 1 trận).

/// <summary>Tham số kinh tế mỗi mùa, admin đặt (C14). Chung cho mọi CLB.</summary>
public class SeasonEconomy
{
    public Guid SeasonId { get; set; }
    /// <summary>Trần lương mềm: quỹ lương mỗi vòng (tổng Wage hợp đồng Active).</summary>
    public long SoftWageCap { get; set; }
    /// <summary>Lương mỗi vòng = giá × WagePermille / 1000 cho hợp đồng nhập từ patch, gia hạn, về CLB gốc. Mặc định 10‰ = 1% (tạm, PO chưa chốt).</summary>
    public int WagePermille { get; set; } = 10;
    /// <summary>Tổng quỹ ngân sách đầu mùa chia cho các CLB.</summary>
    public long InitialBudgetFund { get; set; }
    /// <summary>Hệ số k chia ngân sách: trọng số (1/giá trị squad)^k. 0 = chia đều, 1 = tỷ lệ nghịch.</summary>
    public double BudgetExponent { get; set; } = 0.5;
    /// <summary>Số trận hợp đồng nhập từ patch / tự gia hạn tại CLB gốc / về CLB gốc (PO chọn 10).</summary>
    public int DefaultContractMatches { get; set; } = ContractRules.MinMatches;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Đánh dấu đã trừ lương/thuế cho 1 vòng — chống trừ 2 lần.</summary>
public class RoundSettlement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeasonId { get; set; }
    public Guid MatchdayId { get; set; }
    public DateTimeOffset SettledAt { get; set; }
}

/// <summary>Giá hiện tại của cầu thủ trong mùa (C19: giá động từ mùa 1).</summary>
public class PlayerPrice
{
    public Guid PlayerId { get; set; }
    public Guid SeasonId { get; set; }
    public int Overall { get; set; }
    public long BasePrice { get; set; }
    /// <summary>Hệ số phong độ tích luỹ (1 = trung tính).</summary>
    public double FormMultiplier { get; set; } = 1;
    public long Price { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class ContractRules
{
    /// <summary>C16: hợp đồng 10–60 trận.</summary>
    public const int MinMatches = 10, MaxMatches = 60;
    public static bool IsValidLength(int matches) => matches is >= MinMatches and <= MaxMatches;
    /// <summary>Nhắc gia hạn khi còn 3 trận.</summary>
    public const int RenewalReminderMatches = 3;
    /// <summary>Hết hạn mà rời đi làm CLB vi phạm luật squad → tự gia hạn 10 trận, giữ lương.</summary>
    public const int AutoRenewMatches = 10;

    public static long Wage(long price, int permille) => price * permille / 1000;
}

/// <summary>Fee (Kinh tế đợt 2): tự do / CLB BOT = đúng giá; giữa 2 CLB có chủ 80–200% giá; phí gia hạn trả CLB gốc 0–100% giá.</summary>
public static class FeeRules
{
    public const int MinNegotiatedPercent = 80, MaxNegotiatedPercent = 200, MaxRenewalPercent = 100;
    public static bool IsValidNegotiated(long fee, long price) => fee * 100 >= price * MinNegotiatedPercent && fee * 100 <= price * MaxNegotiatedPercent;
    public static bool IsValidRenewal(long fee, long price) => fee >= 0 && fee * 100 <= price * MaxRenewalPercent;
}

/// <summary>Ngân sách đầu mùa bù ngược theo sức mạnh: CLB i nhận T × w_i / Σw, w_i = (1 / V_i)^k, V = tổng giá squad.</summary>
public static class BudgetAllocation
{
    /// <summary>Làm tròn xuống 1.000; phần dư dồn cho CLB yếu nhất để tổng đúng bằng quỹ.</summary>
    public static IReadOnlyDictionary<TId, long> Allocate<TId>(IReadOnlyList<(TId Club, long SquadValue)> clubs, long fund, double exponent) where TId : notnull
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fund);
        ArgumentOutOfRangeException.ThrowIfNegative(exponent);
        if (clubs.Count == 0) return new Dictionary<TId, long>();
        var weights = clubs.Select(c => Math.Pow(Math.Max(1, c.SquadValue), -exponent)).ToList();
        var total = weights.Sum();
        var result = new Dictionary<TId, long>();
        for (var i = 0; i < clubs.Count; i++) result[clubs[i].Club] = (long)Math.Floor(fund * (weights[i] / total) / 1_000 + 1e-9) * 1_000;
        var weakest = clubs.MinBy(c => c.SquadValue).Club;
        result[weakest] += fund - result.Values.Sum();
        return result;
    }
}

/// <summary>C14: trần mềm + thuế xa xỉ luỹ tiến trên phần vượt, trần cứng +40%.</summary>
public static class LuxuryTax
{
    /// <summary>(ngưỡng vượt tính theo % trần, thuế suất %) — biên luỹ tiến: phần vượt 0–10% chịu 50%, 10–25% chịu 100%, trên 25% chịu 150%.</summary>
    public static readonly IReadOnlyList<(int UpToPercent, int RatePercent)> Brackets = [(10, 50), (25, 100), (40, 150)];
    public const int HardCapPercent = 40;

    public static long HardCap(long softCap) => softCap + softCap * HardCapPercent / 100;

    /// <summary>Thuế mỗi vòng cho quỹ lương <paramref name="payroll"/>. Không vượt trần → 0.</summary>
    public static long Compute(long payroll, long softCap)
    {
        if (softCap <= 0 || payroll <= softCap) return 0;
        long tax = 0, prev = 0, excess = payroll - softCap;
        foreach (var (upTo, rate) in Brackets)
        {
            var top = upTo == HardCapPercent ? long.MaxValue : softCap * upTo / 100;   // bậc cuối nhận cả phần (bất thường) vượt trần cứng
            var part = Math.Min(excess, top) - prev;
            if (part <= 0) break;
            tax += part * rate / 100;
            prev += part;
        }
        return tax;
    }
}

/// <summary>Tham số mô hình giá A (C20). Mặc định là điểm khởi đầu, admin chỉnh được; chưa hiệu chỉnh với dữ liệu thật.</summary>
public sealed record PriceModelOptions
{
    /// <summary>Giá khi chỉ số tổng = Pivot, tuổi 24–27, phong độ trung tính.</summary>
    public long BasePrice { get; init; } = 1_000_000;
    public int Pivot { get; init; } = 70;
    /// <summary>Mỗi +1 chỉ số tổng nhân giá e^Growth (0.12 ≈ +12,7%).</summary>
    public double Growth { get; init; } = 0.12;
    /// <summary>Hệ số theo tuổi: (tuổi tối đa, hệ số), xét theo thứ tự.</summary>
    public IReadOnlyList<(int MaxAge, double Factor)> AgeFactors { get; init; } =
        [(20, 1.3), (23, 1.15), (27, 1.0), (30, 0.85), (33, 0.6), (int.MaxValue, 0.4)];
    /// <summary>Phong độ chỉnh giá tối đa mỗi vòng (±5%).</summary>
    public double MaxFormChangePerRound { get; init; } = 0.05;
    public double MinFormMultiplier { get; init; } = 0.5;
    public double MaxFormMultiplier { get; init; } = 2.0;
    /// <summary>Trọng số chỉ số tổng theo nhóm vị trí; khoá = tên trường PlayerField (OF).</summary>
    public IReadOnlyDictionary<PositionGroup, IReadOnlyDictionary<string, double>> Weights { get; init; } = DefaultWeights;

    public static readonly IReadOnlyDictionary<PositionGroup, IReadOnlyDictionary<string, double>> DefaultWeights =
        new Dictionary<PositionGroup, IReadOnlyDictionary<string, double>>
        {
            [PositionGroup.Goalkeeper] = new Dictionary<string, double>
            { ["GOAL KEEPING"] = 5, ["RESPONSE"] = 2, ["JUMP"] = 1, ["BALANCE"] = 1, ["AGILITY"] = 1 },
            [PositionGroup.Defender] = new Dictionary<string, double>
            { ["DEFENSE"] = 4, ["BALANCE"] = 2, ["HEADING"] = 1, ["JUMP"] = 1, ["TOP SPEED"] = 1, ["RESPONSE"] = 1, ["SHORT PASS ACCURACY"] = 1 },
            [PositionGroup.Midfielder] = new Dictionary<string, double>
            {
                ["SHORT PASS ACCURACY"] = 3, ["LONG PASS ACCURACY"] = 1, ["TEAM WORK"] = 1, ["TECHNIQUE"] = 2, ["DRIBBLE ACCURACY"] = 1,
                ["STAMINA"] = 1, ["ATTACK"] = 1, ["DEFENSE"] = 1, ["RESPONSE"] = 1,
            },
            [PositionGroup.Forward] = new Dictionary<string, double>
            {
                ["ATTACK"] = 3, ["SHOT ACCURACY"] = 2, ["SHOT POWER"] = 1, ["TOP SPEED"] = 1, ["ACCELERATION"] = 1,
                ["DRIBBLE ACCURACY"] = 1, ["TECHNIQUE"] = 1, ["RESPONSE"] = 1,
            },
        };
}

/// <summary>Mô hình giá A: Price = BasePrice × e^(Growth × (Overall − Pivot)) × hệ số tuổi × hệ số phong độ.</summary>
public sealed class PriceModel(PriceModelOptions? options = null)
{
    public PriceModelOptions Options { get; } = options ?? new();

    /// <summary>Chỉ số tổng (0–99) = trung bình có trọng số các chỉ số của nhóm vị trí.</summary>
    public int Overall(Position position, IReadOnlyDictionary<string, int> attributes)
    {
        var w = Options.Weights[position.Group()];
        double sum = 0, total = 0;
        foreach (var (field, weight) in w)
            if (attributes.TryGetValue(field, out var v)) { sum += v * weight; total += weight; }
        return total == 0 ? 0 : (int)Math.Round(sum / total, MidpointRounding.AwayFromZero);
    }

    public double AgeFactor(int age) => Options.AgeFactors.First(a => age <= a.MaxAge).Factor;

    /// <summary>Giá gốc (chưa tính phong độ), làm tròn 1.000.</summary>
    public long BasePrice(int overall, int age) =>
        RoundPrice(Options.BasePrice * Math.Exp(Options.Growth * (overall - Options.Pivot)) * AgeFactor(age));

    public long Price(long basePrice, double formMultiplier) => RoundPrice(basePrice * formMultiplier);

    /// <summary>
    /// Cập nhật hệ số phong độ sau 1 vòng. <paramref name="form"/> trong [-1, 1] (−1 rất tệ, 0 trung tính, 1 rất hay);
    /// cách tính form từ kết quả trận chưa có (chờ module trận). Mỗi vòng đổi tối đa ±MaxFormChangePerRound.
    /// </summary>
    public double NextFormMultiplier(double current, double form) =>
        Math.Clamp(current * (1 + Math.Clamp(form, -1, 1) * Options.MaxFormChangePerRound), Options.MinFormMultiplier, Options.MaxFormMultiplier);

    private static long RoundPrice(double v) => Math.Max(1_000, (long)Math.Round(v / 1_000, MidpointRounding.AwayFromZero) * 1_000);
}

/// <summary>
/// C19 (PO chọn 04/11, thay "từ mùa 2"): chỉ số tiến hoá theo từng trận từ mùa 1. Điểm trận ≥ 7,5 → +1, ≤ 5,0 → −1 mỗi chỉ số hợp vai trò
/// (= các chỉ số trong trọng số giá của nhóm vị trí); tối đa ±3/mùa mỗi chỉ số so với snapshot đầu mùa.
/// </summary>
public static class AttributeEvolution
{
    public const int MaxChangePerSeason = 3;
    public const int FirstSeasonNumber = 1;
    public const decimal UpRating = 7.5m, DownRating = 5.0m;
    public static int Clamp(int previous, int proposed) => Math.Clamp(proposed, previous - MaxChangePerSeason, previous + MaxChangePerSeason);
    public static int MatchDelta(decimal rating) => rating >= UpRating ? 1 : rating <= DownRating ? -1 : 0;
    public static IEnumerable<string> RoleFields(Position position, PriceModelOptions? options = null) =>
        (options ?? new()).Weights[position.Group()].Keys;
}
