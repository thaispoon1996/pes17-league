using System.Text;

namespace Pes17League.OptionFile.Tests;

/// <summary>Đối chiếu với PES Editor (oracle đọc + ghi) và với file đã kiểm trong game.</summary>
public class OracleTests
{
    private const int Raya = 1598, EditZero = 32768, Arsenal = 0, AstonVilla = 1;
    private const int Dowman = 1753, Mosquera = 1257, Gyokeres = 965;

    [FixtureFact]
    public void RoundTrip_is_byte_identical()
    {
        foreach (var f in new[] { "orig.OPT", "atk36.OPT", "work5.OPT" })
        {
            var raw = Fixtures.Bytes(f);
            Assert.Equal(raw, OptionFileCodec.Encode(OptionFileCodec.Decode(raw)));
        }
    }

    [FixtureFact]
    public void Read_matches_editor_csv_export()
    {
        var img = Fixtures.Image("atk36.OPT");
        var lines = File.ReadAllLines(Fixtures.Path("stats.csv"), Encoding.Latin1);
        var header = ParseCsv(lines[0]);
        var col = header.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i);
        int players = 0, mismatches = 0;
        var bad = new List<string>();
        foreach (var line in lines.Skip(1))
        {
            var r = ParseCsv(line);
            if (r.Count != header.Count || !int.TryParse(r[col["ID"]], out var id)) continue;   // dòng editor làm vỡ
            players++;
            // editor xuất CSV chỉ giữ byte thấp của mỗi ký tự UTF-16 (ć U+0107 → 0x07) → so theo đúng quy tắc đó
            var lossy = new string(img.GetPlayerName(id).Select(ch => (char)(ch & 0xFF)).ToArray());
            var csvName = r[col["NAME"]];
            var placeholder = lossy == "" && (csvName.StartsWith("<Unused ") || csvName.StartsWith("<Edited "));   // editor hiện tên rỗng như vậy
            if (csvName != lossy && !placeholder) { mismatches++; bad.Add($"{id} NAME"); }
            foreach (var f in PlayerField.Verified)
                if (img.Get(id, f).ToString() != r[col[f.Name]].Trim()) { mismatches++; bad.Add($"{id} {f.Name}"); }
        }
        Assert.True(players > 5100, $"chỉ đọc được {players} dòng");
        Assert.True(mismatches == 0, string.Join(", ", bad.Take(10)));
    }

    [FixtureFact]
    public void Write_stat_matches_editor()
    {
        var img = Fixtures.Image("orig.OPT");
        Assert.Equal(35, img.Get(Raya, PlayerField.Attack));
        img.Set(Raya, PlayerField.Attack, 36);
        Assert.Equal(Fixtures.Bytes("atk36.OPT"), OptionFileCodec.Encode(img));
    }

    [FixtureFact]
    public void Write_name_height_weight_and_edit_slot_matches_editor()
    {
        var img = Fixtures.Image("atk36.OPT");
        img.SetPlayerName(Raya, "Raya Test");
        img.Set(Raya, PlayerField.Height, 190);
        img.Set(Raya, PlayerField.Weight, 85);
        img.SetPlayerName(EditZero, "Edit Zero");
        img.Set(EditZero, PlayerField.Height, 180);
        Assert.Equal(Fixtures.Bytes("work3.OPT"), OptionFileCodec.Encode(img));
    }

    [FixtureFact]
    public void Transfer_sub_matches_editor()
    {
        var img = Fixtures.Image("orig.OPT");
        var (from, to) = new SquadRules(img).Transfer(Arsenal, AstonVilla, Dowman);
        Assert.True(from.Ok && to.Ok);
        Assert.Equal(Fixtures.Bytes("work4.OPT"), OptionFileCodec.Encode(img));
    }

    [FixtureFact]
    public void Transfer_starter_matches_editor_and_ingame_file()
    {
        var img = Fixtures.Image("orig.OPT");
        var rules = new SquadRules(img);
        Assert.True(rules.Transfer(Arsenal, AstonVilla, Mosquera).To.Ok);
        var (from, to) = rules.Transfer(Arsenal, AstonVilla, Gyokeres);
        Assert.True(to.Ok);
        Assert.False(from.NeedsLineupReview);   // có dự bị cùng vị trí (Havertz, CF)
        var bytes = OptionFileCodec.Encode(img);
        Assert.Equal(Fixtures.Bytes("work5.OPT"), bytes);
        Assert.Equal(Fixtures.Bytes("transfer-ingame.OPT"), bytes);
    }

    [FixtureFact]
    public void Player_edit_matches_file_verified_in_game()
    {
        var img = Fixtures.Image("orig.OPT");
        img.SetPlayerName(Raya, "Raya Spike");
        img.Set(Raya, PlayerField.Height, 190);
        img.Set(Raya, PlayerField.Weight, 85);
        img.Set(Raya, PlayerField.Attack, 40);
        img.SetPlayerName(EditZero, "Edit Zero");
        Assert.Equal(Fixtures.Bytes("spike-ingame.OPT"), OptionFileCodec.Encode(img));
    }

    private static List<string> ParseCsv(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else sb.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        cells.Add(sb.ToString());
        return cells;
    }
}
