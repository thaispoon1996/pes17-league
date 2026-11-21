using System.Text;

namespace Pes17League.OptionFile;

/// <summary>Option file đã giải mã. Mọi thao tác đọc/ghi trên bộ đệm này; xuất file bằng <see cref="OptionFileCodec.Encode"/>.</summary>
public sealed class OptionFileImage
{
    public const int PlayerCount = 5000;        // ID 1..4999 (0 = "không có cầu thủ")
    public const int FirstEditId = 32768;
    public const int EditCount = 184;           // ID 32768..32951
    public const int ClubCount = 140;
    public const int SquadSize = 32;
    public const int MaxNameLength = 15;

    private const int PlayerStart = 37116, EditStart = 14288, PlayerLen = 124;
    private const int ClubStart = 751472, ClubLen = 88, ClubNameLen = 49, ClubAbbrLen = 3;
    private const int ClubNumbers = 659635, ClubSlots = 667730;
    private const int NationalSlots = 664372, NationalTeamCount = 73, NationalSquadSize = 23;
    internal const byte EmptyNumber = 0xFF;
    // Khối đội hình: 64 đội tuyển rồi tới CLB (đội = CLB + 64), 364 byte/đội; 3 bộ Normal/Plan A/Plan B mỗi bộ 82 byte.
    private const int FormationStart = 677202, FormationLen = 364, FormationNationals = 64, FormationSetLen = 82;
    private const int FormationPreset = 118, TeamSettings = 194;
    // Ngoài 3 bộ sơ đồ, dùng chung (spike 05/11): 102..105 chiến thuật L2, 111..116 người đá phạt/đội trưởng (chỉ số ô squad).
    private const int StrategyOffset = 102, JobOffset = 111;
    private static readonly int JobCount = Enum.GetValues<TeamJob>().Length;

    internal byte[] Data { get; }

    internal OptionFileImage(byte[] data) => Data = data;

    public static OptionFileImage Load(string path) => OptionFileCodec.Decode(File.ReadAllBytes(path));

    /// <summary>Ghi file mới (atomic: file tạm rồi đổi tên). Không bao giờ sửa tại chỗ.</summary>
    public void Save(string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, OptionFileCodec.Encode(this));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Dải ID cầu thủ ẩn/hư cấu của PES6 (bố cục ID gốc của game): Classic 1312–1472 (đội huyền thoại), ML mặc định 4414–4436,
    /// Shop 4437–4596, ML trẻ 4597–4773, ML già 4774–4783. Patch có thể ghi đè cầu thủ thật vào các dải này — vẫn coi là ẩn (PO 02/11:
    /// thị trường chỉ bán cầu thủ tự do thật, lọc theo dải ID).
    /// </summary>
    public static bool IsHiddenPlayerId(int id) => id is >= 1312 and <= 1472 or >= 4414 and <= 4783;

    public OptionFileImage Clone() => new((byte[])Data.Clone());

    public static bool IsValidPlayerId(int id) =>
        id is > 0 and < PlayerCount || id is >= FirstEditId and < FirstEditId + EditCount;

    // ---- cầu thủ ----

    private static int RecordOffset(int id)
    {
        if (!IsValidPlayerId(id)) throw new ArgumentOutOfRangeException(nameof(id), id, "ID cầu thủ không hợp lệ");
        return id >= FirstEditId ? EditStart + (id - FirstEditId) * PlayerLen : PlayerStart + id * PlayerLen;
    }

    public string GetPlayerName(int id)
    {
        var s = Data.AsSpan(RecordOffset(id), 32);
        var n = 0;
        while (n < 32 && (s[n] | s[n + 1]) != 0) n += 2;   // cắt ở ký tự NUL đầu tiên
        return Encoding.Unicode.GetString(s[..n]);
    }

    /// <summary>Tên được chuyển qua <see cref="GameText.ToDisplayable"/> trước khi ghi.</summary>
    public void SetPlayerName(int id, string name)
    {
        name = GameText.ToDisplayable(name);
        if (name.Length > MaxNameLength) throw new ArgumentException($"Tên tối đa {MaxNameLength} ký tự", nameof(name));
        var s = Data.AsSpan(RecordOffset(id), 32);
        s.Clear();
        Encoding.Unicode.GetBytes(name, s);
    }

    /// <summary>Tên in áo: byte 32–47, ASCII chữ hoa, tối đa 15 ký tự + NUL (OF gốc: 4783/4783 cầu thủ đúng dạng này).</summary>
    public string GetShirtName(int id) => Encoding.ASCII.GetString(Data.AsSpan(RecordOffset(id) + 32, 16)).TrimEnd('\0');

    public void SetShirtName(int id, string name)
    {
        if (name.Length > MaxNameLength || name.Any(ch => ch is < ' ' or > '~'))
            throw new ArgumentException($"Tên in áo tối đa {MaxNameLength} ký tự ASCII", nameof(name));
        var s = Data.AsSpan(RecordOffset(id) + 32, 16);
        s.Clear();
        Encoding.ASCII.GetBytes(name.ToUpperInvariant(), s);
    }

    /// <summary>Giá trị hiển thị (đã cộng DisplayAdd).</summary>
    public int Get(int id, PlayerField f)
    {
        var a = RecordOffset(id) + 48 + f.Offset;
        var word = (Data[a] << 8) | Data[a - 1];
        return ((word >> f.Shift) & f.Mask) + f.DisplayAdd;
    }

    public void Set(int id, PlayerField f, int value)
    {
        if (value < f.Min || value > f.Max)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"{f.Name}: {f.Min}..{f.Max}");
        var a = RecordOffset(id) + 48 + f.Offset;
        var word = (Data[a] << 8) | Data[a - 1];
        word = (word & ~(f.Mask << f.Shift)) | ((value - f.DisplayAdd) << f.Shift);
        Data[a] = (byte)(word >> 8);
        Data[a - 1] = (byte)word;
    }

    public Position GetPosition(int id) => (Position)Get(id, PlayerField.RegisteredPosition);

    // ---- CLB ----

    private static int ClubOffset(int club)
    {
        if (club is < 0 or >= ClubCount) throw new ArgumentOutOfRangeException(nameof(club), club, "CLB 0..139");
        return ClubStart + club * ClubLen;
    }

    public string GetClubName(int club) => ReadUtf8(Data.AsSpan(ClubOffset(club), ClubNameLen));
    public string GetClubAbbreviation(int club) => ReadUtf8(Data.AsSpan(ClubOffset(club) + ClubNameLen, ClubAbbrLen));

    /// <summary>
    /// Chỉ đổi trong OF. Tên còn ở exe và kitserver 49.bin (C15) — đồng bộ ngoài module.
    /// Viết tắt game hiển thị không lấy từ OF (kiểm trong game 01/11). Tên/viết tắt được bỏ dấu như <see cref="GameText"/>.
    /// </summary>
    public void SetClubName(int club, string name, string abbreviation)
    {
        name = GameText.ToDisplayable(name);
        abbreviation = GameText.ToDisplayable(abbreviation);
        WriteUtf8(Data.AsSpan(ClubOffset(club), ClubNameLen - 1), name, nameof(name));       // chừa 1 byte NUL
        WriteUtf8(Data.AsSpan(ClubOffset(club) + ClubNameLen, ClubAbbrLen), abbreviation, nameof(abbreviation));
    }

    private static string ReadUtf8(ReadOnlySpan<byte> s)
    {
        var n = s.IndexOf((byte)0);
        return Encoding.UTF8.GetString(n < 0 ? s : s[..n]);
    }

    private static void WriteUtf8(Span<byte> dest, string value, string param)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > dest.Length) throw new ArgumentException($"Tối đa {dest.Length} byte UTF-8", param);
        dest.Clear();
        bytes.CopyTo(dest);
    }

    // ---- squad CLB ----

    public ClubSquad GetSquad(int club)
    {
        ClubOffset(club);
        var slots = new List<SquadSlot>();
        for (var p = 0; p < SquadSize; p++)
        {
            var a = ClubSlots + (club * SquadSize + p) * 2;
            var id = Data[a] | Data[a + 1] << 8;
            var num = Data[ClubNumbers + club * SquadSize + p];
            if (id != 0) slots.Add(new SquadSlot(id, num + 1));
        }
        return new ClubSquad(club, slots);
    }

    /// <summary>Mọi ID cầu thủ trong squad đội tuyển quốc gia (73 đội × 23 slot, chỉ đọc).</summary>
    public IReadOnlySet<int> GetNationalTeamPlayerIds()
    {
        var ids = new HashSet<int>();
        for (var p = 0; p < NationalTeamCount * NationalSquadSize; p++)
        {
            var a = NationalSlots + p * 2;
            var id = Data[a] | Data[a + 1] << 8;
            if (id != 0) ids.Add(id);
        }
        return ids;
    }

    /// <summary>Có slot trống nằm giữa các slot có người (GetSquad bỏ qua slot trống nên không thấy).</summary>
    public bool SquadHasGaps(int club)
    {
        ClubOffset(club);
        var empty = false;
        for (var p = 0; p < SquadSize; p++)
        {
            var a = ClubSlots + (club * SquadSize + p) * 2;
            var isEmpty = (Data[a] | Data[a + 1]) == 0;
            if (!isEmpty && empty) return true;
            empty |= isEmpty;
        }
        return false;
    }

    /// <summary>Ghi lại squad: cầu thủ theo thứ tự, phần còn lại điền slot trống. Không kiểm luật — xem <see cref="SquadRules"/>.</summary>
    public void SetSquad(ClubSquad squad)
    {
        ClubOffset(squad.Club);
        if (squad.Players.Count > SquadSize) throw new ArgumentException("Squad quá 32 người");
        for (var p = 0; p < SquadSize; p++)
        {
            var (id, num) = p < squad.Players.Count ? (squad.Players[p].PlayerId, (byte)(squad.Players[p].ShirtNumber - 1)) : (0, EmptyNumber);
            var a = ClubSlots + (squad.Club * SquadSize + p) * 2;
            Data[a] = (byte)id;
            Data[a + 1] = (byte)(id >> 8);
            Data[ClubNumbers + squad.Club * SquadSize + p] = num;
        }
    }

    // ---- sơ đồ / thiết lập đội CLB ----

    private static int FormationOffset(int club)
    {
        if (club is < 0 or >= ClubCount) throw new ArgumentOutOfRangeException(nameof(club), club, "CLB 0..139");
        return FormationStart + (club + FormationNationals) * FormationLen;
    }

    /// <summary>Sơ đồ (tên mẫu nếu khớp 1 trong 21 mẫu, không thì null) + thiết lập đội của bộ Normal.</summary>
    public TeamTactics GetTactics(int club)
    {
        var a = FormationOffset(club);
        var cur = Data.AsSpan(a + FormationPreset, Formations.Length);
        string? name = null;
        foreach (var n in Formations.Names)
            if (cur.SequenceEqual(Formations.Preset(n))) { name = n; break; }
        var t = Data.AsSpan(a + TeamSettings, 4);
        return new(name, t[0], t[1], t[2], t[3], Strategies: [.. Data.AsSpan(a + StrategyOffset, 4).ToArray().Select(v => (int?)v)],
            Jobs: [.. Data.AsSpan(a + JobOffset, JobCount).ToArray().Select(v => (int?)v)]);
    }

    /// <summary>Bố cục đội hình bộ Normal (kể cả sơ đồ tự chỉnh của patch, không khớp mẫu).</summary>
    public FormationLayout GetFormationLayout(int club) => FormationLayout.From(Data.AsSpan(FormationOffset(club) + FormationPreset, Formations.Length));

    /// <summary>Ghi sơ đồ mẫu + thiết lập đội vào cả 3 bộ (Normal/Plan A/Plan B); trường null giữ nguyên.</summary>
    public void SetTactics(int club, TeamTactics tactics)
    {
        var a = FormationOffset(club);
        int?[] settings = [tactics.BackLine, tactics.Pressure, tactics.OffsideTrap, tactics.CounterAttack];
        foreach (var v in settings)
            if (v is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(tactics), v, "Thiết lập đội 0..2 (A/B/C)");
        if (tactics.Positions is { } pos && (pos.Count != 10 || pos.Any(p => p.X is < FormationBounds.MinX or > FormationBounds.MaxX || p.Y is < FormationBounds.MinY or > FormationBounds.MaxY)))
            throw new ArgumentOutOfRangeException(nameof(tactics), "Vị trí tự do: đúng 10 ô, X/Y trong giới hạn");
        if (tactics.Roles is { } rl && (rl.Count != 10 || rl.Any(v => v is < 1 or > 40)))
            throw new ArgumentOutOfRangeException(nameof(tactics), "Vai trò: đúng 10 mã ô 1–10, 1..40");
        if (tactics.Strategies is { } st && (st.Count != 4 || st.Any(v => v is < 0 || v >= TeamStrategies.Names.Count)))
            throw new ArgumentOutOfRangeException(nameof(tactics), "Chiến thuật L2: 4 giá trị 0..11");
        if (tactics.Jobs is { } jobs && (jobs.Count != JobCount || jobs.Any(v => v is < 0 or >= SquadSize)))
            throw new ArgumentOutOfRangeException(nameof(tactics), "Người đá phạt: 6 chỉ số ô 0..31");
        for (var set = 0; set < 3; set++)
        {
            var b = a + set * FormationSetLen;
            if (tactics.Formation is { } f) Formations.Preset(f).CopyTo(Data.AsSpan(b + FormationPreset));
            if (tactics.Positions is { } p)
                for (var i = 0; i < 10; i++) (Data[b + FormationPreset + 2 * i], Data[b + FormationPreset + 2 * i + 1]) = ((byte)p[i].X, (byte)p[i].Y);
            if (tactics.Roles is { } r)
                for (var i = 0; i < 10; i++) Data[b + FormationPreset + 21 + i] = (byte)r[i];
            for (var i = 0; i < 4; i++)
                if (settings[i] is { } v) Data[b + TeamSettings + i] = (byte)v;
        }
        for (var i = 0; i < 4; i++)
            if (tactics.Strategies?[i] is { } v) Data[a + StrategyOffset + i] = (byte)v;
        for (var i = 0; i < JobCount; i++)
            if (tactics.Jobs?[i] is { } v) Data[a + JobOffset + i] = (byte)v;
    }
}
