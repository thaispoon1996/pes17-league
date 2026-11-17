using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

public class LineupServiceTests
{
    /// <summary>Đổi đá chính ↔ dự bị; ô 0 chỉ cho GK; cầu thủ ngoài squad bị chặn.</summary>
    [DbFact]
    public async Task Swap_moves_players_between_slots_and_keeps_goalkeeper_slot()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var season = (await new CompetitionService(db, t.Store, t.Clock).CreateSeasonAsync("Mùa 1", 1)).Id;
        var import = await new ImportPatchService(db, t.Store, t.Clock).ImportAsync("PES6VN 25-26", Env.OrigOptionFile, season, [new(0), new(1)]);
        var clubs = await db.ClubPesMappings.Where(m => m.PatchVersionId == import.PatchVersionId).OrderBy(m => m.PesClubIndex).Select(m => m.ClubId).ToListAsync();
        async Task<List<(Guid Player, int Slot, Position Pos)>> Squad(Guid club)
        {
            await using var q = t.Context();
            return (await (from m in q.SquadMemberships join p in q.Players on m.PlayerId equals p.Id
                           where m.ClubId == club && m.LeftAt == null orderby m.SlotOrder select new { m.PlayerId, m.SlotOrder, p.Position }).ToListAsync())
                .Select(x => (x.PlayerId, x.SlotOrder, x.Position)).ToList();
        }
        var before = await Squad(clubs[0]);
        var svc = new LineupService(t.Context());

        Assert.Null(await svc.SwapAsync(clubs[0], before[1].Player, before[11].Player));
        var after = await Squad(clubs[0]);
        Assert.Equal((before[11].Player, before[1].Player), (after[1].Player, after[11].Player));
        Assert.Equal(before.Count, after.Count);

        var outfield = before.First(x => x.Pos != Position.GK).Player;
        var subGk = before.Skip(1).First(x => x.Pos == Position.GK).Player;
        Assert.Equal(LineupError.GoalkeeperSlot, await new LineupService(t.Context()).SwapAsync(clubs[0], before[0].Player, outfield));
        Assert.Null(await new LineupService(t.Context()).SwapAsync(clubs[0], before[0].Player, subGk));
        Assert.Equal(subGk, (await Squad(clubs[0]))[0].Player);

        var other = (await Squad(clubs[1]))[5].Player;
        Assert.Equal(LineupError.PlayerNotInSquad, await new LineupService(t.Context()).SwapAsync(clubs[0], before[2].Player, other));
    }
}
