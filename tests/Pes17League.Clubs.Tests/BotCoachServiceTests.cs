using Microsoft.EntityFrameworkCore;
using Pes17League.Clubs.Application;

namespace Pes17League.Clubs.Tests;

/// <summary>HLV BOT v1 gắn đóng kỳ CN — CLB BOT nhận Tactics + XI; CLB Human không đổi.</summary>
public class BotCoachServiceTests
{
    [DbFact]
    public async Task Close_window_writes_tactics_for_bot_not_human()
    {
        await using var t = await TestDb.CreateAsync();
        await using var db = t.Context();
        var svc = new CompetitionService(db, t.Store, t.Clock);
        var season = await svc.CreateSeasonAsync("Mùa HLV", 1);
        var import = await new ImportPatchService(db, t.Store, t.Clock).ImportAsync("PES6VN 25-26", Env.OrigOptionFile, season.Id,
            [new LeagueClub(0, "Arsenal"), new LeagueClub(1)]);
        await new EconomyService(db, t.Clock).SetParametersAsync(season.Id, new(InitialBudgetFund: 100_000_000));
        var clubs = await db.ClubPesMappings.Where(m => m.PatchVersionId == import.PatchVersionId)
            .OrderBy(m => m.PesClubIndex).Select(m => m.ClubId).ToListAsync();
        var human = clubs[0];
        var bot = clubs[1];

        Assert.True((await svc.StartSeasonAsync(season.Id, import.PatchVersionId, human)).Ok);

        // Ghi dấu đội hình Human trước đóng CN.
        var humanBefore = await db.SquadMemberships.AsNoTracking()
            .Where(m => m.ClubId == human && m.LeftAt == null)
            .OrderBy(m => m.SlotOrder).Select(m => new { m.PlayerId, m.SlotOrder }).ToListAsync();
        var humanSlot5 = humanBefore[5];

        // Đổi chỗ BOT trước để chắc Apply ghi đè.
        var botSquad = await db.SquadMemberships.Where(m => m.ClubId == bot && m.LeftAt == null)
            .OrderBy(m => m.SlotOrder).ToListAsync();
        Assert.Null(await new LineupService(db).SwapAsync(bot, botSquad[1].PlayerId, botSquad[11].PlayerId));

        var md1 = await db.Matchdays.SingleAsync(m => m.SeasonId == season.Id && m.Number == 1);
        var close = await svc.CloseTransferWindowAsync(md1.Id);
        Assert.True(close.Ok, string.Join("; ", close.Issues?.Select(i => i.Message) ?? []));

        var botTactics = await db.Tactics.SingleAsync(x => x.ClubId == bot);
        Assert.NotNull(botTactics.Formation);
        Assert.Equal(0, botTactics.BackLine);
        Assert.Equal(2, botTactics.OffsideTrap);
        Assert.Equal(2, botTactics.Pressure);
        Assert.Equal(2, botTactics.CounterAttack);
        Assert.NotNull(botTactics.Strategies);
        Assert.Equal(4, botTactics.Strategies!.Length);

        var botXi = await db.SquadMemberships.AsNoTracking()
            .Where(m => m.ClubId == bot && m.LeftAt == null && m.SlotOrder <= 10)
            .OrderBy(m => m.SlotOrder).Select(m => m.PlayerId).ToListAsync();
        Assert.Equal(11, botXi.Count);
        Assert.Equal(11, botXi.Distinct().Count());
        var botGk = await (from m in db.SquadMemberships.AsNoTracking()
                           join p in db.Players on m.PlayerId equals p.Id
                           where m.ClubId == bot && m.LeftAt == null && m.SlotOrder == 0
                           select p.Position).SingleAsync();
        Assert.Equal(Position.GK, botGk);

        // Human: không có dòng Tactics mới bắt buộc (có thể null) và SlotOrder giữ nguyên.
        Assert.False(await db.Tactics.AnyAsync(x => x.ClubId == human));
        var humanAfter = await db.SquadMemberships.AsNoTracking()
            .Where(m => m.ClubId == human && m.LeftAt == null)
            .OrderBy(m => m.SlotOrder).Select(m => new { m.PlayerId, m.SlotOrder }).ToListAsync();
        Assert.Equal(humanBefore.Select(x => (x.PlayerId, x.SlotOrder)), humanAfter.Select(x => (x.PlayerId, x.SlotOrder)));
        Assert.Equal(humanSlot5.PlayerId, humanAfter[5].PlayerId);
    }
}
