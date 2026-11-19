namespace Pes17League.Clubs.Domain;

/// <summary>
/// Sang mùa career (PO 19/11, đề xuất mặc định — chỉnh được): mỗi cầu thủ +1 tuổi, các chỉ số hợp vai trò (<see cref="AttributeEvolution.RoleFields"/>)
/// đổi theo tuổi mới: ≤ 20 +2…+4, 21–23 +1…+3, 24–26 0…+1, 27–29 giữ, 30–31 0…−1, 32–33 −1…−2, ≥ 34 −2…−3 (cùng mức cho mọi chỉ số của 1 người).
/// Giải nghệ theo tuổi mới: 34 → 10%, 35 → 25%, 36 → 45%, 37 → 70%, ≥ 38 chắc chắn.
/// </summary>
public static class AgingRules
{
    public const int MinAttribute = 1, MaxAttribute = 99;

    /// <summary><paramref name="roll"/> 0–999.</summary>
    public static int Development(int age, int roll) => age switch
    {
        <= 20 => 2 + roll % 3,
        <= 23 => 1 + roll % 3,
        <= 26 => roll % 2,
        <= 29 => 0,
        <= 31 => -(roll % 2),
        <= 33 => -1 - roll % 2,
        _ => -2 - roll % 2,
    };

    public static int RetirePermille(int age) => age switch { < 34 => 0, 34 => 100, 35 => 250, 36 => 450, 37 => 700, _ => 1000 };

    public static bool Retires(int age, int roll) => roll < RetirePermille(age);

    /// <summary>Chỉ số mùa mới: AGE +1, chỉ số vai trò + <see cref="Development"/>.</summary>
    public static Dictionary<string, int> NextSeason(IReadOnlyDictionary<string, int> values, IEnumerable<string> roleFields, int roll)
    {
        var next = values.ToDictionary(kv => kv.Key, kv => kv.Value);
        if (!next.TryGetValue("AGE", out var age)) return next;
        next["AGE"] = ++age;
        var d = Development(age, roll);
        foreach (var f in roleFields)
            if (next.TryGetValue(f, out var v)) next[f] = Math.Clamp(v + d, MinAttribute, MaxAttribute);
        return next;
    }

    /// <summary>Cầu thủ tự do thay người giải nghệ ở CLB BOT: tối đa tuổi này (tuổi mùa mới).</summary>
    public const int ReplacementMaxAge = 30;
}
