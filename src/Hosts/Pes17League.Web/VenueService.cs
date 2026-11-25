using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Pes17League.Web;

/// <summary>Sân + thời tiết trận (PO 25/11): HLV chọn sân cho trận sân nhà (/san-van-dong), mặc định = sân thật của CLB nếu game có;
/// thời tiết, giờ, mùa lấy theo thời tiết hiện tại ngoài đời tại sân nhà CLB (Open-Meteo, không cần khoá).
/// Dữ liệu ở $DATA/venues: clubs-geo.json (tools/pes17db/clubgeo.py, Wikidata) + choice.json (lựa chọn của HLV).
/// Bộ chạy nhận JSON qua PESMATCH_CONDITIONS, đặt ở tab STADIUM màn Kickoff (tools/match17/pesmatch17.py match_conditions).
/// Biển quảng cáo (PO 25/11): $GRAPHICS/ads/{clb}/ (spec.json, ảnh tải lên, preview.png) → gói $GRAPHICS/ads/{clb}.cpk (tools/pes17db/ads.py);
/// trận sân nhà của CLB có gói → bộ chạy cài trước trận (ensure_ads, mở lại game nếu khác gói đang cài).</summary>
public sealed class VenueService(string dir, string graphicsDir, string adsScript, string dataDir, IHttpClientFactory http, TimeProvider clock, ILogger<VenueService> log)
{
    /// <summary>Sân có trong game, đúng thứ tự menu (chỉ số 1–40; 0 = "Home Field" của game). Keys: chữ thường không dấu, có trong tên sân Wikidata ("=…" = trùng hẳn).</summary>
    public sealed record Stadium(int Index, string Name, string City, params string[] Keys);

    public static readonly Stadium[] Stadiums =
    [
        new(1, "Giuseppe Meazza", "Milano", "giuseppe meazza"),
        new(2, "Estadi Olímpic Lluís Companys", "Barcelona", "lluis companys", "montjuic"),
        new(3, "Ramón Sánchez-Pizjuán", "Sevilla", "sanchez pizjuan", "sanchez-pizjuan"),
        new(4, "Anfield", "Liverpool", "anfield"),
        new(5, "Wembley", "London", "wembley"),
        new(6, "Stadio Olimpico", "Roma", "=stadio olimpico"),
        new(7, "Old Trafford", "Manchester", "old trafford"),
        new(8, "Estádio da Luz", "Lisboa", "estadio da luz", "sport lisboa e benfica"),
        new(9, "Stamford Bridge", "London", "stamford bridge"),
        new(10, "Parc des Princes", "Paris", "parc des princes"),
        new(11, "Allianz Arena", "München", "allianz arena"),
        new(12, "Etihad Stadium", "Manchester", "etihad", "city of manchester stadium"),
        new(13, "Stadion Patriot Bekasi", "Bekasi", "patriot"),
        new(14, "Estádio do Morumbi", "São Paulo", "morumbi", "cicero pompeu"),
        new(15, "Emirates Stadium", "London", "emirates stadium"),
        new(16, "Estádio Urbano Caldeira", "Santos", "urbano caldeira", "vila belmiro"),
        new(17, "Deutsche Bank Park", "Frankfurt", "deutsche bank park", "waldstadion", "commerzbank-arena"),
        new(18, "Stade Vélodrome", "Marseille", "velodrome"),
        new(19, "Vitality Stadium", "Bournemouth", "vitality stadium", "dean court"),
        new(20, "Santiago Bernabéu", "Madrid", "bernabeu"),
        new(21, "Juventus Stadium", "Torino", "juventus stadium", "allianz stadium"),
        new(22, "Saitama Stadium", "Saitama", "saitama stadium"),
        new(23, "El Cilindro", "Avellaneda", "cilindro", "presidente peron"),
        new(24, "Turf Moor", "Burnley", "turf moor"),
        new(25, "La Bombonera", "Buenos Aires", "bombonera", "alberto j. armando"),
        new(26, "Konami Stadium", "Sân chung", "konami"),
        new(27, "San Siro", "Milano", "san siro"),
        new(28, "Türk Telekom Arena", "İstanbul", "turk telekom", "rams park", "nef stadyumu", "ali sami yen"),
        new(29, "Craven Cottage", "London", "craven cottage"),
        new(30, "Jakarta International Stadium", "Jakarta", "jakarta international"),
        new(31, "Wanda Metropolitano", "Madrid", "metropolitano"),
        new(32, "Mestalla", "Valencia", "mestalla"),
        new(33, "The Den", "London", "the den"),
        new(34, "London Stadium", "London", "london stadium", "olympic stadium (london)"),
        new(35, "Estádio Alfredo Jaconi", "Caxias do Sul", "alfredo jaconi"),
        new(36, "Gelora Bung Karno", "Jakarta", "gelora bung karno"),
        new(37, "Red Bull Arena", "Leipzig", "red bull arena"),
        new(38, "Mercedes-Benz Stadium", "Atlanta", "mercedes-benz stadium"),
        new(39, "Signal Iduna Park", "Dortmund", "signal iduna", "westfalenstadion"),
        new(40, "Kassam Stadium", "Oxford", "kassam"),
    ];

    public const int HomeField = 0;
    public static Stadium? ById(int i) => i is >= 1 and <= 40 ? Stadiums[i - 1] : null;
    public static string Thumb(int i) => $"img/stadium/s{Math.Clamp(i, 0, 40):00}.jpg";

    /// <summary>Toạ độ sân nhà (Wikidata): venue = tên sân thật, city = trụ sở/nơi đặt khi không có toạ độ sân.</summary>
    public sealed record Geo(string? Qid, string? Venue, string? City, double Lat, double Lon);

    /// <summary>Thời tiết lúc này tại sân. Weather/Time/Season = giá trị menu game (fine|rainy, day|night, summer|winter).</summary>
    public sealed record Now(int Code, double TempC, bool IsDay, string Weather, string Time, string Season, string Label, string Icon, bool Snow, DateTimeOffset At);

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    readonly object gate = new();
    Dictionary<string, Geo>? geo;
    DateTime geoStamp;
    readonly Dictionary<(double, double), Now> weather = [];

    string ChoicePath => Path.Combine(dir, "choice.json");
    string GeoPath => Path.Combine(dir, "clubs-geo.json");

    Dictionary<string, int> Choices()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(ChoicePath), Json) ?? []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    public Geo? GeoOf(Guid club)
    {
        lock (gate)
        {
            try
            {
                var stamp = File.GetLastWriteTimeUtc(GeoPath);   // file chưa có → năm 1601, không lỗi
                if (geo is null || stamp != geoStamp)
                    (geo, geoStamp) = (JsonSerializer.Deserialize<Dictionary<string, Geo>>(File.ReadAllText(GeoPath), Json) ?? [], stamp);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { geo ??= []; }
            return geo.GetValueOrDefault(club.ToString());
        }
    }

    /// <summary>Sân thật của CLB có trong game (khớp tên sân Wikidata) → chỉ số; không có → Home Field.</summary>
    public int DefaultFor(Guid club)
    {
        if (GeoOf(club)?.Venue is not { Length: > 0 } v) return HomeField;
        var n = Plain(v);
        return Stadiums.FirstOrDefault(s => s.Keys.Any(k => k[0] == '=' ? n == k[1..] : n.Contains(k)))?.Index ?? HomeField;
    }

    /// <summary>HLV đã chọn → lựa chọn đó; chưa → <see cref="DefaultFor"/>.</summary>
    public int StadiumFor(Guid club)
    {
        lock (gate) return Choices().TryGetValue(club.ToString(), out var i) && i is >= 0 and <= 40 ? i : DefaultFor(club);
    }

    public bool HasChoice(Guid club) { lock (gate) return Choices().ContainsKey(club.ToString()); }

    /// <summary>Lưu sân HLV chọn; null = về mặc định.</summary>
    public void Save(Guid club, int? index)
    {
        lock (gate)
        {
            var all = Choices();
            if (index is int i && i is >= 0 and <= 40) all[club.ToString()] = i; else all.Remove(club.ToString());
            Directory.CreateDirectory(dir);
            var tmp = ChoicePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(all, Json));
            File.Move(tmp, ChoicePath, true);
        }
    }

    /// <summary>Thời tiết hiện tại ở sân nhà CLB (giữ 15 phút). Không có toạ độ / gọi lỗi → null.</summary>
    public async Task<Now?> WeatherAsync(Guid club, CancellationToken ct = default)
    {
        if (GeoOf(club) is not { } g) return null;
        var key = (Math.Round(g.Lat, 2), Math.Round(g.Lon, 2));
        lock (gate)
            if (weather.TryGetValue(key, out var hit) && clock.GetUtcNow() - hit.At < TimeSpan.FromMinutes(15)) return hit;
        try
        {
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={key.Item1}&longitude={key.Item2}&current=weather_code,temperature_2m,is_day&timezone=auto");
            using var doc = JsonDocument.Parse(await http.CreateClient("weather").GetStringAsync(url, ct));
            var c = doc.RootElement.GetProperty("current");
            var now = Describe(c.GetProperty("weather_code").GetInt32(), c.GetProperty("temperature_2m").GetDouble(), c.GetProperty("is_day").GetInt32() == 1, clock.GetUtcNow());
            lock (gate) weather[key] = now;
            return now;
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning("Thời tiết {Club}: {Error}", club, e.Message);
            return null;
        }
    }

    /// <summary>Mã WMO → giá trị menu game + nhãn. Game không có tuyết: tuyết/mưa đá → "rainy" (cờ Snow để web ghi rõ).
    /// Mùa theo nhiệt độ (≤ 12 °C = Winter: khán giả mặc áo ấm, cầu thủ áo dài tay).</summary>
    public static Now Describe(int code, double temp, bool isDay, DateTimeOffset at)
    {
        var snow = code is >= 71 and <= 77 or 85 or 86;
        var rain = snow || code is >= 51 and <= 67 or >= 80 and <= 82 or >= 95;
        var (label, icon) = code switch
        {
            0 => ("Trời quang", isDay ? "clear_day" : "clear_night"),
            1 or 2 => ("Ít mây", isDay ? "partly_cloudy_day" : "partly_cloudy_night"),
            3 => ("Nhiều mây", "cloud"),
            45 or 48 => ("Sương mù", "foggy"),
            >= 51 and <= 57 => ("Mưa phùn", "rainy"),
            >= 61 and <= 67 or >= 80 and <= 82 => ("Mưa", "rainy"),
            _ when snow => ("Tuyết rơi", "weather_snowy"),
            >= 95 => ("Dông", "thunderstorm"),
            _ => ("Không rõ", "cloud"),
        };
        return new(code, temp, isDay, rain ? "rainy" : "fine", isDay ? "day" : "night", temp <= 12 ? "winter" : "summer", label, icon, snow, at);
    }

    /// <summary>JSON cho bộ chạy (PESMATCH_CONDITIONS) theo CLB chủ nhà: sân luôn có; giờ/mùa/thời tiết chỉ khi lấy được thời tiết thật.</summary>
    public async Task<string?> ConditionsAsync(Guid homeClub, CancellationToken ct)
    {
        var d = new Dictionary<string, object> { ["stadium"] = StadiumFor(homeClub) };
        if (File.Exists(AdsPack(homeClub))) d["ads"] = homeClub.ToString("N");
        if (await WeatherAsync(homeClub, ct) is { } w)
            (d["time"], d["season"], d["weather"]) = (w.Time, w.Season, w.Weather);
        return JsonSerializer.Serialize(d);
    }

    // ───────── Biển quảng cáo ─────────

    /// <summary>Một tấm biển 512x64 (8:1): chữ (Text + màu nền/chữ) hoặc ảnh tải lên (Image = tên file trong thư mục CLB).</summary>
    public sealed record Board(string? Text = null, string Bg = "#0f131d", string Fg = "#00ff87", string? Image = null);

    public const int AdSlots = 6;
    public const long AdMaxBytes = 8 << 20;
    /// <summary>Cặp màu gợi ý (nền, chữ) theo bảng màu trang.</summary>
    public static readonly (string Bg, string Fg)[] AdColors =
        [("#0f131d", "#00ff87"), ("#00ff87", "#0f131d"), ("#ffb95f", "#0f131d"), ("#0f131d", "#ffffff"), ("#ffffff", "#0f131d"), ("#ff4d5e", "#ffffff"), ("#1d4ed8", "#ffffff"), ("#facc15", "#0f131d")];
    static readonly string[] ImageExts = [".png", ".jpg", ".webp"];
    static readonly SemaphoreSlim BuildGate = new(1, 1);

    string AdsDir(Guid club) => Path.Combine(graphicsDir, "ads", club.ToString("N"));
    string AdsPack(Guid club) => Path.Combine(graphicsDir, "ads", club.ToString("N") + ".cpk");
    /// <summary>Đường web của file trong thư mục biển của CLB (GraphicsStore phục vụ tĩnh $GRAPHICS).</summary>
    public string AdsWeb(Guid club, string file) =>
        $"{GraphicsStore.RequestPath}/ads/{club:N}/{file}?v={File.GetLastWriteTimeUtc(Path.Combine(AdsDir(club), file)).Ticks}";

    /// <summary>Bộ biển đã lưu (đã dựng gói); chưa có → null (game dùng biển gốc).</summary>
    public IReadOnlyList<Board>? Ads(Guid club)
    {
        if (!File.Exists(AdsPack(club))) return null;
        try { return JsonSerializer.Deserialize<List<Board>>(File.ReadAllText(Path.Combine(AdsDir(club), "boards.json")), Json); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Lưu ảnh tải lên cho một tấm → tên file (giữ đuôi), chưa dựng gói.</summary>
    public async Task<string> SaveAdImageAsync(Guid club, Stream data, string ext, CancellationToken ct = default)
    {
        ext = ext.ToLowerInvariant() is ".jpeg" ? ".jpg" : ext.ToLowerInvariant();
        if (!ImageExts.Contains(ext)) throw new InvalidOperationException("Chỉ nhận ảnh PNG, JPG hoặc WEBP");
        var d = AdsDir(club);
        Directory.CreateDirectory(d);
        var name = $"up-{Guid.NewGuid():N}"[..15] + ext;
        await using (var f = File.Create(Path.Combine(d, name))) await data.CopyToAsync(f, ct);
        return name;
    }

    /// <summary>Dựng gói biển (ads.py build, ~2–5 s) + ảnh xem trước; xoá ảnh tải lên không còn dùng.</summary>
    public async Task BuildAdsAsync(Guid club, IReadOnlyList<Board> boards, CancellationToken ct = default)
    {
        var used = boards.Where(b => !string.IsNullOrWhiteSpace(b.Text) || b.Image is not null).Take(AdSlots)
            .Select(b => b with { Text = b.Text?.Trim() is { Length: > 0 } t ? t[..Math.Min(t.Length, 40)] : null,
                Image = b.Image is { } i && File.Exists(Path.Combine(AdsDir(club), Path.GetFileName(i))) ? Path.GetFileName(i) : null })
            .Where(b => b.Text is not null || b.Image is not null).ToList();
        if (used.Count == 0) throw new InvalidOperationException("Chưa có tấm biển nào: nhập chữ hoặc tải ảnh");
        var d = AdsDir(club);
        Directory.CreateDirectory(d);
        var spec = new { boards = used.Select(b => b.Image is { } i ? (object)new { image = Path.Combine(d, i) } : new { text = b.Text, bg = b.Bg, fg = b.Fg }) };
        var specPath = Path.Combine(d, "spec.json");
        await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(spec), ct);
        await BuildGate.WaitAsync(ct);
        try
        {
            var psi = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in new[] { adsScript, "build", specPath, AdsPack(club), Path.Combine(d, "preview.png") }) psi.ArgumentList.Add(a);
            psi.Environment["PES17_DATA"] = dataDir;
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEndAsync(ct);
            _ = p.StandardOutput.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(1));
            try { await p.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { p.Kill(true); throw new InvalidOperationException("Dựng biển quá 1 phút, thử lại sau"); }
            if (p.ExitCode != 0)
            {
                var e = (await err).Trim();
                log.LogWarning("Dựng biển {Club}: {Error}", club, e);
                throw new InvalidOperationException(e.StartsWith("LỖI ") ? e[4..] : "Không dựng được biển, thử ảnh khác");
            }
        }
        finally { BuildGate.Release(); }
        lock (gate)
        {
            File.WriteAllText(Path.Combine(d, "boards.json"), JsonSerializer.Serialize(used, Json));
            var keep = used.Select(b => b.Image).OfType<string>().ToHashSet();
            foreach (var f in Directory.GetFiles(d, "up-*")) if (!keep.Contains(Path.GetFileName(f))) File.Delete(f);
        }
    }

    /// <summary>Bỏ biển riêng → trận sân nhà dùng biển gốc của game.</summary>
    public void ClearAds(Guid club)
    {
        lock (gate)
        {
            File.Delete(AdsPack(club));
            if (Directory.Exists(AdsDir(club))) Directory.Delete(AdsDir(club), true);
        }
    }

    static string Plain(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString().Replace('ı', 'i');
    }
}
