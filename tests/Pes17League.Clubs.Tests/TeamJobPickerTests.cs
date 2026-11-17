using Pes17League.Clubs.Application;
using Pes17League.Clubs.Domain;
using Pes17League.OptionFile;

namespace Pes17League.Clubs.Tests;

/// <summary>Tự chọn đội trưởng/người đá phạt (PO duyệt 05/11).</summary>
public class TeamJobPickerTests
{
    private static StarterInfo P(int i, Position pos, int ovr, params (string, int)[] a) =>
        new(Guid.NewGuid(), i, pos, ovr, a.ToDictionary(x => x.Item1, x => x.Item2));

    private readonly List<StarterInfo> xi =
    [
        P(0, Position.GK, 90, ("SHOT ACCURACY", 99), ("FREE KICK ACCURACY", 99), ("LONG PASS ACCURACY", 99)),
        P(1, Position.CB, 80, ("SHOT ACCURACY", 50), ("FREE KICK ACCURACY", 50), ("LONG PASS ACCURACY", 60)),
        P(2, Position.CMF, 85, ("SHOT ACCURACY", 70), ("FREE KICK ACCURACY", 90), ("LONG PASS ACCURACY", 88), ("SWERVE", 85)),
        P(3, Position.CF, 84, ("SHOT ACCURACY", 92), ("SHOT TECHNIQUE", 85), ("FREE KICK ACCURACY", 60)),
    ];

    [Fact]
    public void Auto_picks_best_by_role_and_skips_keeper_for_set_pieces()
    {
        Assert.Equal(0, TeamJobPicker.Auto(TeamJob.Captain, xi)!.Index);       // OVR cao nhất (thủ môn được làm đội trưởng)
        Assert.Equal(3, TeamJobPicker.Auto(TeamJob.Penalty, xi)!.Index);
        Assert.Equal(2, TeamJobPicker.Auto(TeamJob.LongFreeKick, xi)!.Index);
        Assert.Equal(2, TeamJobPicker.Auto(TeamJob.LeftCorner, xi)!.Index);
    }

    [Fact]
    public void Chosen_starter_wins_otherwise_auto()
    {
        Assert.Equal(1, TeamJobPicker.Resolve(TeamJob.Penalty, xi[1].PlayerId, xi)!.Index);
        Assert.Equal(3, TeamJobPicker.Resolve(TeamJob.Penalty, Guid.NewGuid(), xi)!.Index);   // người được chọn đã xuống dự bị
        Assert.Null(TeamJobPicker.Auto(TeamJob.Captain, []));
    }
}
