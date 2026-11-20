namespace Pes17League.Clubs.Domain;

/// <summary>Trạng thái hồ sơ HLV career (PO 19/11): đang tạo → có việc / thất nghiệp (bị sa thải, hết hợp đồng, từ chức).</summary>
public enum CoachStatus { Creating, Employed, Unemployed }

/// <summary>Xuất thân bốc ngẫu nhiên khi tạo HLV (PO 19/11: bốc lại tối đa 2 lần).</summary>
public enum CoachOrigin { Rookie, Academy, Tactician, ExPro }

/// <summary>Tính cách ngẫu nhiên (1 cái) bốc cùng xuất thân.</summary>
public enum CoachTrait { PressDarling, HotHead, Frugal, Studious, IronDiscipline, Charismatic }

/// <summary>7 năng lực HLV, thứ tự cố định = chỉ số mảng <see cref="Coach.Skills"/>.</summary>
public enum CoachSkill { Training, Youth, People, Fitness, Negotiation, Media, Scouting }

/// <summary>
/// Hồ sơ HLV của 1 tài khoản (career). Năng lực có tác dụng thật vào tập luyện, thể lực, tinh thần, họp báo, mua cầu thủ, trinh sát
/// (<see cref="CoachRules"/>). Ví HLV tách khỏi tiền CLB (<see cref="CoachLedgerEntry"/>). Mốc thời gian = "tick" (mùa×100 + khung).
/// </summary>
public class Coach
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ManagerId { get; set; }
    public string Name { get; set; } = "";
    public string? Nickname { get; set; }
    public int Age { get; set; } = 38;
    public string Nationality { get; set; } = "Việt Nam";
    public string Formation { get; set; } = "4-3-3";
    public string Style { get; set; } = "Cân bằng";
    public CoachOrigin Origin { get; set; }
    public CoachTrait Trait { get; set; }
    /// <summary>Số lần đã bốc lại xuất thân (tối đa <see cref="CoachRules.Rerolls"/>).</summary>
    public int Draws { get; set; }
    public int Seed { get; set; }
    /// <summary>Năng lực hiện tại (7 số, 1–20).</summary>
    public int[] Skills { get; set; } = new int[CoachRules.SkillCount];
    /// <summary>Năng lực gốc theo xuất thân (sàn khi phân điểm lúc tạo).</summary>
    public int[] BaseSkills { get; set; } = new int[CoachRules.SkillCount];
    public int FreePoints { get; set; }
    public int Xp { get; set; }
    /// <summary>Bằng: 0 chưa có, 1 C, 2 B, 3 A, 4 Pro.</summary>
    public int License { get; set; }
    /// <summary>Khoá học đang theo (bằng sẽ đạt); 0 = không học.</summary>
    public int CourseLicense { get; set; }
    public int CourseRoundsLeft { get; set; }
    public int Reputation { get; set; }
    public int Stress { get; set; } = CoachRules.StartStress;
    /// <summary>Mức sống 0–3 (<see cref="CoachRules.Living"/>).</summary>
    public int Lifestyle { get; set; } = 1;
    public string Hair { get; set; } = "short";
    public string HairColor { get; set; } = "#2a1d14";
    public string Outfit { get; set; } = "suit";
    public string Skin { get; set; } = "#c99a78";
    /// <summary>Ảnh mặt đã căn (JPEG) dán lên tượng 3D; null = mặt mặc định.</summary>
    public byte[]? Face { get; set; }
    /// <summary>Lưới mặt quét được (JSON điểm mốc MediaPipe, toạ độ ảnh 0–1); null = dán ảnh phẳng.</summary>
    public string? FaceMesh { get; set; }
    public CoachStatus Status { get; set; }
    /// <summary>Mốc đã xử lý gần nhất (mùa×100 + khung).</summary>
    public int Tick { get; set; }
    /// <summary>Cảnh cáo sa thải đã gửi ở tick này (mỗi mùa 1 lần).</summary>
    public int WarnedTick { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum CoachContractStatus { Active, Expired, Sacked, Resigned }

/// <summary>Hợp đồng HLV trưởng. Lương trả mỗi vòng vào ví HLV (tiền tự sinh, CLB không bị trừ).</summary>
public class CoachContract
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CoachId { get; set; }
    public Guid ClubId { get; set; }
    public Guid SeasonId { get; set; }
    /// <summary>Số mùa (1–3); hết hạn khi xong mùa <see cref="EndSeasonNumber"/>.</summary>
    public int Seasons { get; set; }
    public int EndSeasonNumber { get; set; }
    public long Wage { get; set; }
    public long Bonus { get; set; }
    public int SeverancePct { get; set; }
    public int ExpectedPosition { get; set; }
    public string Objective { get; set; } = "";
    public CoachContractStatus Status { get; set; }
    public int RoundsPaid { get; set; }
    /// <summary>Tổng số vòng dự kiến của hợp đồng (tính bồi thường).</summary>
    public int TotalRounds { get; set; }
    public int SignedTick { get; set; }
    public int? EndedTick { get; set; }
    /// <summary>Đã họp báo ra mắt (1 lần mỗi hợp đồng).</summary>
    public bool Launched { get; set; }
    /// <summary>Đã đồng ý gia hạn ở thư "gia-han" (mùa hiện tại).</summary>
    public bool RenewAgreed { get; set; }
    public DateTimeOffset SignedAt { get; set; }
}

public enum CoachApplicationStatus { Open, Agreed, Signed, Refused, Withdrawn }

/// <summary>Ứng tuyển / được mời + thương lượng với chủ tịch (3 lần kiên nhẫn; hết → CLB từ chối hết mùa).</summary>
public class CoachApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CoachId { get; set; }
    public Guid ClubId { get; set; }
    public Guid SeasonId { get; set; }
    public CoachApplicationStatus Status { get; set; }
    public bool Invited { get; set; }
    public int Patience { get; set; } = CoachRules.Patience;
    public long BaseWage { get; set; }
    public long Wage { get; set; }
    public long Bonus { get; set; }
    public int Seasons { get; set; } = 2;
    public int SeverancePct { get; set; } = 50;
    /// <summary>Nhật ký thương lượng, mỗi dòng 1 câu (mới nhất trước).</summary>
    public string Log { get; set; } = "";
    public int Tick { get; set; }
    public DateTimeOffset At { get; set; }
}

public enum CoachMoney { Wage, Bonus, Severance, Living, Asset, AssetSale, Course, Event, Investment, Upkeep, Sponsor }

/// <summary>Sổ ví HLV. Số dư = tổng Amount.</summary>
public class CoachLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CoachId { get; set; }
    public long Amount { get; set; }
    public CoachMoney Kind { get; set; }
    public string Note { get; set; } = "";
    public int Tick { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Loại thư: chính thức (thông báo), quyết định (phải chọn), tin, yêu cầu HLV gửi đi, hồi âm.</summary>
public enum LetterKind { Official, Decision, Info, Request, Reply }

public enum LetterFrom { Board, Player, Federation, Press, Agent, Family, Fans, Sponsor, Staff, Club, Coach }

/// <summary>
/// Thư trong hòm thư HLV career (PO 19/11: mọi sự kiện, quyết định chính thức đi qua hòm thư; HLV gửi yêu cầu cho BLĐ/cầu thủ,
/// hồi âm vòng sau). Thư quyết định có <see cref="Options"/>; quá <see cref="DeadlineTick"/> chưa chọn → áp phương án <see cref="DefaultOption"/>.
/// </summary>
public class CoachLetter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CoachId { get; set; }
    public LetterKind Kind { get; set; }
    public LetterFrom From { get; set; }
    public string Sender { get; set; } = "";
    public Guid? ClubId { get; set; }
    public Guid? PlayerId { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>Mã sự kiện / yêu cầu (<see cref="CoachEvents"/>).</summary>
    public string? Key { get; set; }
    /// <summary>Nhãn các phương án, ngăn bởi '\n' (thư quyết định).</summary>
    public string? Options { get; set; }
    public int DefaultOption { get; set; }
    public int? Chosen { get; set; }
    public int? DeadlineTick { get; set; }
    /// <summary>Thư yêu cầu: đã có hồi âm ở tick này (null = chờ vòng sau).</summary>
    public int? AnsweredTick { get; set; }
    public bool Outgoing { get; set; }
    public Guid? ReplyToId { get; set; }
    /// <summary>Tham số kèm (vd. vai trò nhân viên, mã việc làm), dạng "k=v;k=v".</summary>
    public string? Data { get; set; }
    /// <summary>Đường dẫn trong app (vd. "hlv/viec-lam").</summary>
    public string? Link { get; set; }
    public bool Read { get; set; }
    public bool Archived { get; set; }
    public int Tick { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Ảnh lưu niệm (chụp từ tượng 3D: họp báo ra mắt, ký hợp đồng…).</summary>
public class CoachPhoto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CoachId { get; set; }
    public string Caption { get; set; } = "";
    public byte[] Image { get; set; } = [];
    public int Tick { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Tài sản (nhà, xe) hoặc khoản đầu tư (sự kiện) của HLV.</summary>
public class CoachAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CoachId { get; set; }
    /// <summary>Mã trong <see cref="CoachRules.Assets"/>, hoặc "dau-tu".</summary>
    public string Key { get; set; } = "";
    public long Price { get; set; }
    /// <summary>Đầu tư: đáo hạn ở tick này, lãi/lỗ theo phần nghìn (đã bốc khi góp).</summary>
    public int? MaturesTick { get; set; }
    public int ReturnPermille { get; set; }
    public int Tick { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Bài tập riêng HLV giao cầu thủ: buổi tập kế tiếp cầu thủ được +1 chỉ số (nếu còn dưới trần mùa).</summary>
public class CoachDrill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CoachId { get; set; }
    public Guid ClubId { get; set; }
    public Guid PlayerId { get; set; }
    public bool Applied { get; set; }
    public int Tick { get; set; }
    public DateTimeOffset At { get; set; }
}
