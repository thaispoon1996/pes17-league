namespace Pes17League.Clubs.Domain;

/// <summary>Tác động của 1 phương án / hồi âm. Fans theo phần nghìn khán giả; Board = độ tin cậy BLĐ; Invest = tiền góp đầu tư.</summary>
public sealed record CoachFx(long Money = 0, int Rep = 0, int Stress = 0, int Board = 0, int Fans = 0, int TeamMorale = 0, int PlayerMorale = 0,
    int Xp = 0, long Invest = 0);

public sealed record CoachEventOption(string Label, CoachFx Fx);

/// <summary>Sự kiện ngẫu nhiên (thư quyết định). Chữ có chỗ thay: {club} {player} {coach} {chairman}.</summary>
public sealed record CoachEventDef(string Key, bool Life, LetterFrom From, string Sender, string Subject, string Body, int Weight, int Default,
    CoachEventOption[] Options);

public sealed record CoachRequestDef(string Key, bool ToPlayer, string Name, string Icon, string Help);

/// <summary>
/// Danh mục sự kiện ngẫu nhiên + yêu cầu HLV gửi đi (PO 19/11). Sự kiện: 12%/vòng, tối đa 1/vòng, tối đa 3 thư quyết định đang chờ;
/// điều kiện thoả → trọng số ×3; căng thẳng ≥ 80 → buộc "kiệt sức". Thư chờ 3 vòng, quá hạn áp phương án mặc định (kém nhất).
/// Phương án chỉ hiện chiều tăng/giảm (<see cref="Hint"/>), không hiện số.
/// </summary>
public static class CoachEvents
{
    public const int ChancePermille = 120, MaxPending = 3, DeadlineRounds = 3, RequestCooldown = 6;

    public static readonly CoachEventDef[] All =
    [
        // ---- sự nghiệp ----
        new("ghe-nong", false, LetterFrom.Press, "Báo Bóng Đá", "Tin đồn ghế nóng",
            "Báo chí đưa tin ban lãnh đạo {club} đang tìm người thay {coach} sau chuỗi kết quả kém. Phóng viên chờ ông phản hồi.", 6, 1,
        [
            new("Lên tiếng khẳng định sẽ trụ lại", new(Board: 3, Stress: 4, Rep: 1)),
            new("Im lặng, tập trung chuyên môn", new(Stress: 3, Board: -1)),
            new("Hẹn gặp chủ tịch nói thẳng", new(Board: 5, Stress: 6)),
        ]),
        new("clb-lon-dom-ngo", false, LetterFrom.Agent, "Người đại diện", "Một CLB lớn dòm ngó",
            "Người đại diện báo một CLB lớn hỏi thăm về ông. Báo chí đã đánh hơi, {club} và CĐV đang theo dõi phản ứng của ông.", 6, 1,
        [
            new("Công khai trung thành với {club}", new(Board: 6, Fans: 4, Rep: -1)),
            new("Để ngỏ khả năng", new(Rep: 3, Board: -6)),
            new("Không bình luận", new(Stress: 2)),
        ]),
        new("cau-thu-bat-man", false, LetterFrom.Player, "{player}", "Cầu thủ bất mãn",
            "{player} gửi thư phàn nàn về thời gian thi đấu và muốn gặp ông nói chuyện thẳng thắn.", 6, 1,
        [
            new("Hứa trao thêm cơ hội ra sân", new(PlayerMorale: 12, Stress: 1)),
            new("Giữ kỷ luật, phải tự cố gắng", new(PlayerMorale: -6, TeamMorale: 1)),
            new("Đưa vào danh sách bán", new(PlayerMorale: -10, TeamMorale: -1)),
        ]),
        new("binh-luan-tv", false, LetterFrom.Press, "Đài truyền hình thể thao", "Mời bình luận trên TV",
            "Đài truyền hình mời ông làm khách mời bình luận trận cầu tâm điểm cuối tuần, có thù lao.", 8, 1,
        [
            new("Nhận lời", new(Money: 40_000, Rep: 2, Stress: 3)),
            new("Từ chối, tập trung đội bóng", new(Board: 2)),
        ]),
        new("hoi-thao", false, LetterFrom.Federation, "Liên đoàn bóng đá", "Hội thảo chiến thuật",
            "Liên đoàn mở hội thảo chiến thuật 3 ngày với các HLV hàng đầu. Phí tham dự 50.000.", 8, 1,
        [
            new("Đăng ký tham dự", new(Money: -50_000, Xp: 15, Stress: 2)),
            new("Bỏ qua lần này", new()),
        ]),
        new("qua-cdv", false, LetterFrom.Fans, "Hội cổ động viên {club}", "Cổ động viên tặng quà",
            "Hội CĐV {club} gửi tặng ông một chiếc khăn thêu tay cùng lời chúc. Họ mong được gặp ông.", 8, 2,
        [
            new("Gửi thư cảm ơn kèm chữ ký", new(Fans: 5, Stress: -3)),
            new("Mời đại diện CĐV xem tập", new(Fans: 8, TeamMorale: 1, Stress: 1)),
            new("Cảm ơn qua thư ký", new(Fans: -2)),
        ]),
        // ---- cuộc sống ----
        new("quang-cao", true, LetterFrom.Sponsor, "Nhãn hàng đồ thể thao", "Lời mời đóng quảng cáo",
            "Một nhãn hàng muốn ông làm gương mặt đại diện cho chiến dịch mới. Thù lao hấp dẫn, nhưng sẽ mất vài buổi tập.", 7, 1,
        [
            new("Ký hợp đồng quảng cáo", new(Money: 90_000, Board: -2, Stress: 2, Rep: 1)),
            new("Từ chối", new(Board: 1)),
        ]),
        new("dau-tu", true, LetterFrom.Agent, "Bạn cũ thời đi học", "Cơ hội đầu tư",
            "Một người bạn cũ rủ ông góp vốn vào chuỗi sân bóng mini. Sau 10 vòng sẽ chia lãi, cũng có thể lỗ.", 7, 2,
        [
            new("Góp 300.000", new(Invest: 300_000)),
            new("Góp 100.000", new(Invest: 100_000)),
            new("Từ chối", new()),
        ]),
        new("kiet-suc", true, LetterFrom.Staff, "Bác sĩ riêng", "Dấu hiệu kiệt sức",
            "Bác sĩ cảnh báo ông ngủ ít, căng thẳng kéo dài. Nếu không nghỉ, khả năng làm việc sẽ giảm.", 4, 2,
        [
            new("Nghỉ một tuần, giao trợ lý", new(Stress: -25, Board: -3)),
            new("Đi nghỉ dưỡng cuối tuần", new(Money: -80_000, Stress: -15)),
            new("Cố gắng tiếp", new(Stress: 5)),
        ]),
        new("gia-dinh", true, LetterFrom.Family, "Gia đình", "Chuyện gia đình",
            "Gia đình nhắn ông về quê dự đám cưới người em. Lịch thi đấu đang dày.", 7, 1,
        [
            new("Về quê vài ngày", new(Money: -30_000, Stress: -8, Board: -1)),
            new("Gọi video, hẹn dịp khác", new(Stress: 4)),
        ]),
        new("scandal", true, LetterFrom.Club, "Phòng truyền thông {club}", "Scandal mạng xã hội",
            "Một đoạn clip cắt ghép ông nổi nóng trong buổi tập lan truyền trên mạng xã hội. Phòng truyền thông xin ý kiến.", 5, 2,
        [
            new("Xin lỗi công khai", new(Rep: -1, Stress: 3, Board: 1)),
            new("Phản bác mạnh mẽ", new(Fans: 2, Board: -4, Stress: 5)),
            new("Im lặng chờ lắng xuống", new(Rep: -3, Stress: 2)),
        ]),
        new("tu-thien", true, LetterFrom.Family, "Quỹ khuyến học quê nhà", "Lời mời làm từ thiện",
            "Quỹ khuyến học ở quê nhà mời ông ủng hộ xây sân bóng cho trường làng.", 7, 2,
        [
            new("Ủng hộ 100.000", new(Money: -100_000, Rep: 3, Fans: 3, Stress: -2)),
            new("Ủng hộ 20.000", new(Money: -20_000, Rep: 1)),
            new("Từ chối", new(Rep: -1)),
        ]),
    ];

    public static CoachEventDef? Find(string? key) => All.FirstOrDefault(e => e.Key == key);

    /// <summary>Gợi ý chỉ chiều (không số): "Tiền ↓, Danh tiếng ↑".</summary>
    public static string Hint(CoachFx fx)
    {
        var parts = new List<string>();
        void Add(string name, long v) { if (v != 0) parts.Add($"{name} {(v > 0 ? "↑" : "↓")}"); }
        Add("Tiền", fx.Money - fx.Invest);
        Add("Danh tiếng", fx.Rep);
        Add("Căng thẳng", fx.Stress);
        Add("BLĐ tin cậy", fx.Board);
        Add("CĐV", fx.Fans);
        Add("Tinh thần đội", fx.TeamMorale);
        Add("Tinh thần cầu thủ", fx.PlayerMorale);
        Add("Kinh nghiệm", fx.Xp);
        if (fx.Invest > 0) parts.Add("Lãi/lỗ sau 10 vòng");
        return parts.Count == 0 ? "Không ảnh hưởng" : string.Join(", ", parts);
    }

    public const int InvestRounds = 10;

    /// <summary>Lãi/lỗ đầu tư (‰): −600…+800, bốc tất định.</summary>
    public static int InvestReturn(int roll) => roll * 1400 / 1000 - 600;

    // ---------- yêu cầu HLV gửi đi ----------

    public static readonly CoachRequestDef[] Requests =
    [
        new("tang-ngan-sach", false, "Xin thêm ngân sách", "payments", "BLĐ bơm thêm tiền chuyển nhượng nếu đang tin tưởng ông."),
        new("nhan-su", false, "Thuê / nâng cấp nhân viên", "badge", "BLĐ trả phí nâng 1 cấp nhân viên bạn chọn."),
        new("tang-luong", false, "Gia hạn / tăng lương", "contract_edit", "Đòi thêm 1 mùa hợp đồng, có thể kèm tăng lương."),
        new("giam-muc-tieu", false, "Điều chỉnh mục tiêu", "flag", "Xin hạ mục tiêu mùa khi đội đang chơi dưới kỳ vọng."),
        new("tai-tro-khoa-hoc", false, "Tài trợ khoá học", "school", "Xin CLB trả một nửa học phí khoá bằng đang học."),
        new("tu-chuc", false, "Đơn từ chức", "logout", "Chấm dứt hợp đồng theo thoả thuận: nhận nửa khoản bồi thường, danh tiếng giảm."),
        new("khen", true, "Khen ngợi", "thumb_up", "Động viên cầu thủ chơi tốt."),
        new("nhac-nho", true, "Nhắc nhở", "campaign", "Chấn chỉnh cầu thủ chơi kém."),
        new("bai-tap", true, "Giao bài tập riêng", "exercise", "Buổi tập kế tiếp cầu thủ được thêm 1 chỉ số (nếu chưa chạm trần)."),
        new("danh-sach-ban", true, "Báo đưa vào danh sách bán", "sell", "Thông báo cầu thủ có thể bị bán. Tinh thần giảm."),
    ];

    public static CoachRequestDef? Request(string? key) => Requests.FirstOrDefault(r => r.Key == key);

    public static string Fill(string text, string? club = null, string? player = null, string? coach = null, string? chairman = null) => text
        .Replace("{club}", club ?? "CLB").Replace("{player}", player ?? "Cầu thủ").Replace("{coach}", coach ?? "HLV").Replace("{chairman}", chairman ?? "Chủ tịch");

    /// <summary>Dữ liệu kèm thư "k=v;k=v" → từ điển.</summary>
    public static Dictionary<string, string> Data(string? data) =>
        (data ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.First()[1]);
}
