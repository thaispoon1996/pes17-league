namespace Pes17League.OptionFile;

/// <summary>
/// Trường bit trong bản ghi cầu thủ: word 16-bit tại (48 + Offset − 1, 48 + Offset), dịch Shift, mặt nạ Mask.
/// Giá trị hiển thị = giá trị lưu + DisplayAdd. Đã đối chiếu 5175 cầu thủ với stats.csv của PES Editor (0 lệch).
/// </summary>
public sealed record PlayerField(string Name, int Offset, int Shift, int Mask, int DisplayAdd = 0)
{
    public int Min => DisplayAdd;
    public int Max => Mask + DisplayAdd;

    public static readonly PlayerField RegisteredPosition = new("REGISTERED POSITION", 6, 4, 0xF);
    public static readonly PlayerField Attack = new("ATTACK", 7, 0, 0x7F);
    public static readonly PlayerField Defense = new("DEFENSE", 8, 0, 0x7F);
    public static readonly PlayerField Balance = new("BALANCE", 9, 0, 0x7F);
    public static readonly PlayerField Stamina = new("STAMINA", 10, 0, 0x7F);
    public static readonly PlayerField TopSpeed = new("TOP SPEED", 11, 0, 0x7F);
    public static readonly PlayerField Acceleration = new("ACCELERATION", 12, 0, 0x7F);
    public static readonly PlayerField Response = new("RESPONSE", 13, 0, 0x7F);
    public static readonly PlayerField Agility = new("AGILITY", 14, 0, 0x7F);
    public static readonly PlayerField DribbleAccuracy = new("DRIBBLE ACCURACY", 15, 0, 0x7F);
    public static readonly PlayerField DribbleSpeed = new("DRIBBLE SPEED", 16, 0, 0x7F);
    public static readonly PlayerField ShortPassAccuracy = new("SHORT PASS ACCURACY", 17, 0, 0x7F);
    public static readonly PlayerField ShortPassSpeed = new("SHORT PASS SPEED", 18, 0, 0x7F);
    public static readonly PlayerField LongPassAccuracy = new("LONG PASS ACCURACY", 19, 0, 0x7F);
    public static readonly PlayerField LongPassSpeed = new("LONG PASS SPEED", 20, 0, 0x7F);
    public static readonly PlayerField ShotAccuracy = new("SHOT ACCURACY", 21, 0, 0x7F);
    public static readonly PlayerField ShotPower = new("SHOT POWER", 22, 0, 0x7F);
    public static readonly PlayerField ShotTechnique = new("SHOT TECHNIQUE", 23, 0, 0x7F);
    public static readonly PlayerField FreeKickAccuracy = new("FREE KICK ACCURACY", 24, 0, 0x7F);
    public static readonly PlayerField Swerve = new("SWERVE", 25, 0, 0x7F);
    public static readonly PlayerField Heading = new("HEADING", 26, 0, 0x7F);
    public static readonly PlayerField Jump = new("JUMP", 27, 0, 0x7F);
    public static readonly PlayerField TeamWork = new("TEAM WORK", 28, 0, 0x7F);
    public static readonly PlayerField Technique = new("TECHNIQUE", 29, 0, 0x7F);
    public static readonly PlayerField Aggression = new("AGGRESSION", 30, 0, 0x7F);
    public static readonly PlayerField Mentality = new("MENTALITY", 31, 0, 0x7F);
    public static readonly PlayerField GoalKeeping = new("GOAL KEEPING", 32, 0, 0x7F);
    public static readonly PlayerField Consistency = new("CONSISTENCY", 33, 0, 0x07, 1);
    public static readonly PlayerField WeakFootFrequency = new("WEAK FOOT FREQUENCY", 33, 3, 0x07, 1);
    public static readonly PlayerField Condition = new("CONDITION / FITNESS", 33, 8, 0x07, 1);
    public static readonly PlayerField WeakFootAccuracy = new("WEAK FOOT ACCURACY", 33, 11, 0x07, 1);
    public static readonly PlayerField Height = new("HEIGHT", 41, 0, 0x3F, 148);
    public static readonly PlayerField Weight = new("WEIGHT", 41, 8, 0x7F);
    public static readonly PlayerField Age = new("AGE", 65, 9, 0x1F, 15);

    /// <summary>Chỉ số trong <see cref="GameText.Nations"/>. Đối chiếu cột NATIONALITY của stats.csv (06/11): 97 giá trị, mỗi giá trị 1 nước, 0 lệch.</summary>
    public static readonly PlayerField Nationality = new("NATIONALITY", 65, 0, 0x7F);

    /// <summary>
    /// Ô "vị trí đá được" (0/1): bit 7 của offset 7..18 theo thứ tự GK, SW, CB, SB, DMF, WB, CMF, SMF, AMF, WF, SS, CF.
    /// Đối chiếu OF gốc PES6VN 25-26 (06/11): cả 4783 cầu thủ có tên đều bật ô của vị trí đăng ký; ô mẫu 32768+ chỉ bật CF.
    /// </summary>
    public static PlayerField Playable(Position p) => new($"PLAYABLE {p}", 7 + (p == Position.GK ? 0 : (int)p - 1), 7, 1);

    /// <summary>33 trường đã đối chiếu với stats.csv (tên = tên cột CSV của editor).</summary>
    public static readonly IReadOnlyList<PlayerField> Verified =
    [
        Attack, Defense, Balance, Stamina, TopSpeed, Acceleration, Response, Agility, DribbleAccuracy, DribbleSpeed,
        ShortPassAccuracy, ShortPassSpeed, LongPassAccuracy, LongPassSpeed, ShotAccuracy, ShotPower, ShotTechnique,
        FreeKickAccuracy, Swerve, Heading, Jump, TeamWork, Technique, Aggression, Mentality, GoalKeeping,
        Consistency, WeakFootFrequency, Condition, WeakFootAccuracy, Height, Weight, Age,
    ];
}
