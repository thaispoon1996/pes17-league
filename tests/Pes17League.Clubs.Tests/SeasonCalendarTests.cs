namespace Pes17League.Clubs.Tests;

/// <summary>Lịch khung ngày của mùa nhiều giải (PO chọn 06/11, bước 6).</summary>
public class SeasonCalendarTests
{
    private static string Label(FixtureStage? stage, int? leg, int? round) => stage switch
    {
        null => $"QN{round}", FixtureStage.LeaguePhase => $"LP{round}", FixtureStage.KnockoutPlayoff => $"PO-{leg}",
        FixtureStage.RoundOf32 => leg is { } l ? $"1/16-{l}" : "1/16", FixtureStage.RoundOf16 => leg is { } l8 ? $"1/8-{l8}" : "1/8",
        FixtureStage.QuarterFinal => leg is { } lq ? $"TK-{lq}" : "TK", FixtureStage.SemiFinal => leg is { } ls ? $"BK-{ls}" : "BK",
        FixtureStage.Final => "CK", _ => "?",
    };

    private static string Labels(IEnumerable<CalendarSlot> slots) => string.Join(" ", slots.Select(s => Label(s.Stage, s.Leg, s.Round)));

    [Fact]
    public void Calendar_follows_po_template()
    {
        var ko = CupDraw.KnockoutStages(24, 4, 12);
        var slots = SeasonCalendar.Build(18, 8, ko);
        Assert.Equal("QN1 QN2 LP1 QN3 LP2 QN4 LP3 QN5 LP4 QN6 LP5 QN7 LP6 QN8 LP7 QN9 LP8 QN10 QN11 PO-1 QN12 PO-2 QN13 TK-1 QN14 TK-2 QN15 BK-1 QN16 BK-2 QN17 QN18 CK",
            Labels(slots));
        Assert.Equal(slots, SeasonCalendar.Number(slots.Select(s => (s.Stage, s.Leg))));      // đọc lại từ Matchday.Stage/Leg
        // Không có cúp → chỉ quốc nội; hết khung quốc nội → bỏ chỗ QN.
        Assert.Equal("QN1 QN2 QN3", Labels(SeasonCalendar.Build(3, 0, [])));
        Assert.Equal("QN1 QN2 LP1 QN3 LP2 LP3 PO-1 PO-2 TK-1 TK-2 BK-1 BK-2 CK", Labels(SeasonCalendar.Build(3, 3, ko)));
        // Cúp quốc gia có khung riêng trước KO châu lục (kể cả trùng tên giai đoạn → 2 CK).
        var domestic = CupDraw.SingleLegStages(20);
        Assert.Equal("QN1 QN2 LP1 QN3 LP2 LP3 1/16 1/8 TK BK CK PO-1 PO-2 TK-1 TK-2 BK-1 BK-2 CK",
            Labels(SeasonCalendar.Build(3, 3, ko, domestic)));
    }

    [Fact]
    public void Knockout_stages_follow_format_and_cups_share_slots()
    {
        Assert.Equal("PO-1 PO-2 1/8-1 1/8-2 TK-1 TK-2 BK-1 BK-2 CK", string.Join(" ", CupDraw.KnockoutStages(32, 8, 24).Select(s => Label(s.Stage, s.Leg, null))));
        Assert.Throws<ArgumentException>(() => CupDraw.KnockoutStages(24, 4, 10));
        var c1 = CupDraw.KnockoutStages(24, 4, 12);
        Assert.Equal(c1, SeasonCalendar.Union([CupDraw.KnockoutStages(12, 2, 6), c1]));        // cúp ít vòng hơn đá chung khung với cúp kia
    }

    [Fact]
    public void Shorter_league_rests_evenly()
    {
        var used = Enumerable.Range(1, 14).Select(r => SeasonCalendar.DomesticSlot(r, 14, 18)).ToList();
        Assert.Equal(used.Distinct().Order(), used);
        Assert.Equal([5, 9, 14, 18], Enumerable.Range(1, 18).Except(used));
        Assert.Equal(Enumerable.Range(1, 18), Enumerable.Range(1, 18).Select(r => SeasonCalendar.DomesticSlot(r, 18, 18)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SeasonCalendar.DomesticSlot(1, 19, 18));
    }
}
