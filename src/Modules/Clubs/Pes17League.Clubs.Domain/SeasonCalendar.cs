namespace Pes17League.Clubs.Domain;

/// <summary>Một khung ngày của lịch mùa. <see cref="Stage"/> null = quốc nội; <see cref="Round"/> = thứ tự khung quốc nội (QN1…) hoặc vòng league phase (LP1…),
/// null với vòng loại trực tiếp.</summary>
public sealed record CalendarSlot(FixtureStage? Stage, int? Leg, int? Round);

/// <summary>
/// Lịch khung ngày của mùa (PO chọn 06/11, bước 6): [QN, QN] + (LP, QN)×K + [QN] + (cúp quốc gia KO, QN)×D + (cúp châu lục KO, QN)×M + QN còn lại + chung kết châu lục.
/// Hết khung quốc nội thì bỏ chỗ QN. Cúp cùng loại đá chung khung (Distinct); cúp quốc gia và châu lục có khung riêng kể cả trùng tên giai đoạn (tránh CLB đá 2 trận/khung).
/// </summary>
public static class SeasonCalendar
{
    /// <summary>Khung ngày có kỳ chuyển nhượng (PO chọn 06/11, bước 8): khung 1–5 và 2 khung nghỉ cúp giữa mùa (18–19).
    /// Khung khác vẫn qua bước "khoá khung" (sinh OF) nhưng không mua/bán được.</summary>
    public static bool HasTransferWindow(int number) => number <= 5 || number is 18 or 19;

    /// <param name="domesticRounds">Số khung quốc nội = số vòng của giải nhiều vòng nhất.</param>
    /// <param name="knockout">Khung loại trực tiếp cúp châu lục (<see cref="CupDraw.KnockoutStages"/>), chung kết (nếu có) ở cuối.</param>
    /// <param name="domesticCupKnockout">Khung knockout 1 lượt cúp quốc gia (<see cref="CupDraw.SingleLegStages"/>); null/rỗng = không có.</param>
    public static IReadOnlyList<CalendarSlot> Build(int domesticRounds, int leaguePhaseRounds, IReadOnlyList<(FixtureStage Stage, int? Leg)> knockout,
        IReadOnlyList<(FixtureStage Stage, int? Leg)>? domesticCupKnockout = null)
    {
        var slots = new List<CalendarSlot>();
        var domestic = 0;
        void Domestic() { if (domestic < domesticRounds) slots.Add(new(null, null, ++domestic)); }
        Domestic();
        Domestic();
        for (var i = 1; i <= leaguePhaseRounds; i++) { slots.Add(new(FixtureStage.LeaguePhase, null, i)); Domestic(); }
        Domestic();
        foreach (var (stage, leg) in domesticCupKnockout ?? []) { slots.Add(new(stage, leg, null)); Domestic(); }
        var final = knockout.Count > 0 && knockout[^1].Stage == FixtureStage.Final;
        foreach (var (stage, leg) in final ? knockout.Take(knockout.Count - 1) : knockout) { slots.Add(new(stage, leg, null)); Domestic(); }
        while (domestic < domesticRounds) Domestic();
        if (final) slots.Add(new(FixtureStage.Final, null, null));
        return slots;
    }

    /// <summary>Hợp các khung loại trực tiếp của nhiều cúp, theo thứ tự giai đoạn rồi lượt.</summary>
    public static IReadOnlyList<(FixtureStage Stage, int? Leg)> Union(IEnumerable<IReadOnlyList<(FixtureStage Stage, int? Leg)>> cups) =>
        [.. cups.SelectMany(c => c).Distinct().OrderBy(s => s.Stage).ThenBy(s => s.Leg)];

    /// <summary>Khung quốc nội (1..<paramref name="slots"/>) của vòng <paramref name="round"/> (1..<paramref name="rounds"/>):
    /// giải ít vòng hơn nghỉ rải đều (Đức hạng 2: 14 vòng / 18 khung → nghỉ khung 5, 9, 14, 18).</summary>
    public static int DomesticSlot(int round, int rounds, int slots)
    {
        if (rounds > slots || round < 1 || round > rounds) throw new ArgumentOutOfRangeException(nameof(round));
        return (round - 1) * slots / rounds + 1;
    }

    /// <summary>Đánh số lại khung đã lưu (Matchday.Stage/Leg theo thứ tự Number): QN và LP đếm riêng.</summary>
    public static IReadOnlyList<CalendarSlot> Number(IEnumerable<(FixtureStage? Stage, int? Leg)> ordered)
    {
        var (domestic, lp) = (0, 0);
        return [.. ordered.Select(s => s.Stage switch
        {
            null => new CalendarSlot(null, null, ++domestic),
            FixtureStage.LeaguePhase => new CalendarSlot(s.Stage, null, ++lp),
            _ => new CalendarSlot(s.Stage, s.Leg, null),
        })];
    }
}
