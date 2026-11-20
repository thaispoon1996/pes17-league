using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.Design;

namespace Pes17League.Clubs.Infrastructure;

public sealed class ClubsDbContext(DbContextOptions<ClubsDbContext> options) : DbContext(options)
{
    public const string Schema = "clubs";

    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<CareerSeen> CareerSeen => Set<CareerSeen>();
    public DbSet<TeamAssignment> TeamAssignments => Set<TeamAssignment>();
    public DbSet<FootballPlayer> Players => Set<FootballPlayer>();
    public DbSet<PlayerAttributes> PlayerAttributes => Set<PlayerAttributes>();
    public DbSet<SquadMembership> SquadMemberships => Set<SquadMembership>();
    public DbSet<PlayerContract> Contracts => Set<PlayerContract>();
    public DbSet<TransferWindow> TransferWindows => Set<TransferWindow>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<ClubLedgerEntry> Ledger => Set<ClubLedgerEntry>();
    public DbSet<PatchVersion> PatchVersions => Set<PatchVersion>();
    public DbSet<LeagueApplication> LeagueApplications => Set<LeagueApplication>();
    public DbSet<ClubPesMapping> ClubPesMappings => Set<ClubPesMapping>();
    public DbSet<PlayerPesMapping> PlayerPesMappings => Set<PlayerPesMapping>();
    public DbSet<OptionFileBuild> OptionFileBuilds => Set<OptionFileBuild>();
    public DbSet<SeasonEconomy> SeasonEconomies => Set<SeasonEconomy>();
    public DbSet<RoundSettlement> RoundSettlements => Set<RoundSettlement>();
    public DbSet<PlayerPrice> PlayerPrices => Set<PlayerPrice>();
    public DbSet<ContractRenewal> ContractRenewals => Set<ContractRenewal>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<SeasonClub> SeasonClubs => Set<SeasonClub>();
    public DbSet<Matchday> Matchdays => Set<Matchday>();
    public DbSet<Fixture> Fixtures => Set<Fixture>();
    public DbSet<MatchAttempt> MatchAttempts => Set<MatchAttempt>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<MatchRating> MatchRatings => Set<MatchRating>();
    public DbSet<MatchTeamStats> MatchTeamStats => Set<MatchTeamStats>();
    public DbSet<MatchEvent> MatchEvents => Set<MatchEvent>();
    public DbSet<MatchArticle> MatchArticles => Set<MatchArticle>();
    public DbSet<GoalPoll> GoalPolls => Set<GoalPoll>();
    public DbSet<GoalVote> GoalVotes => Set<GoalVote>();
    public DbSet<Manager> Managers => Set<Manager>();
    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();
    public DbSet<FriendlyMatch> FriendlyMatches => Set<FriendlyMatch>();
    public DbSet<FriendlyGoal> FriendlyGoals => Set<FriendlyGoal>();
    public DbSet<MatchmakingTicket> MatchmakingTickets => Set<MatchmakingTicket>();
    public DbSet<FriendlyChatMessage> FriendlyChat => Set<FriendlyChatMessage>();
    public DbSet<MailMessage> MailMessages => Set<MailMessage>();
    public DbSet<MailRecipient> MailRecipients => Set<MailRecipient>();
    public DbSet<MailBlock> MailBlocks => Set<MailBlock>();
    public DbSet<TransferOffer> TransferOffers => Set<TransferOffer>();
    public DbSet<ClubTraining> ClubTrainings => Set<ClubTraining>();
    public DbSet<ClubFinance> ClubFinances => Set<ClubFinance>();
    public DbSet<PlayerCondition> PlayerConditions => Set<PlayerCondition>();
    public DbSet<PressConference> PressConferences => Set<PressConference>();
    public DbSet<PlayerTalk> PlayerTalks => Set<PlayerTalk>();
    public DbSet<BoardConfidence> BoardConfidences => Set<BoardConfidence>();
    public DbSet<ClubStaff> ClubStaff => Set<ClubStaff>();
    public DbSet<YouthIntake> YouthIntakes => Set<YouthIntake>();
    public DbSet<ScoutReport> ScoutReports => Set<ScoutReport>();
    public DbSet<MatchPhoto> MatchPhotos => Set<MatchPhoto>();
    public DbSet<AttributeChange> AttributeChanges => Set<AttributeChange>();
    public DbSet<ClubTactics> Tactics => Set<ClubTactics>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<CompetitionEdition> Editions => Set<CompetitionEdition>();
    public DbSet<EditionClub> EditionClubs => Set<EditionClub>();
    public DbSet<AdminAuditEntry> AdminAudit => Set<AdminAuditEntry>();
    public DbSet<Sanction> Sanctions => Set<Sanction>();
    public DbSet<Coach> Coaches => Set<Coach>();
    public DbSet<CoachContract> CoachContracts => Set<CoachContract>();
    public DbSet<CoachApplication> CoachApplications => Set<CoachApplication>();
    public DbSet<CoachLedgerEntry> CoachLedger => Set<CoachLedgerEntry>();
    public DbSet<CoachLetter> CoachLetters => Set<CoachLetter>();
    public DbSet<CoachPhoto> CoachPhotos => Set<CoachPhoto>();
    public DbSet<CoachAsset> CoachAssets => Set<CoachAsset>();
    public DbSet<CoachDrill> CoachDrills => Set<CoachDrill>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<Club>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.ShortName).HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Country).HasMaxLength(3);
            e.Property(x => x.LogoPath).HasMaxLength(300);
            e.Property(x => x.SquadVersion).IsConcurrencyToken();
        });

        b.Entity<TeamAssignment>(e =>
        {
            e.Property(x => x.ControllerType).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
            e.HasIndex(x => x.ClubId).HasFilter("\"To\" IS NULL").IsUnique();
        });

        b.Entity<FootballPlayer>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Position).HasConversion<string>().HasMaxLength(5);
            e.Property(x => x.Nationality).HasMaxLength(50);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.OriginClubId);
        });

        b.Entity<PlayerAttributes>(e =>
        {
            e.HasKey(x => new { x.PlayerId, x.SeasonId });
            // Tự chuyển JSON (Npgsql 8 cần EnableDynamicJson nếu để nó tự map Dictionary).
            e.Property(x => x.Values).HasColumnType("jsonb").HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                s => JsonSerializer.Deserialize<Dictionary<string, int>>(s, (JsonSerializerOptions?)null) ?? new(),
                new ValueComparer<Dictionary<string, int>>(
                    (a, c) => a!.Count == c!.Count && !a.Except(c).Any(),
                    v => v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key, kv.Value)),
                    v => new Dictionary<string, int>(v)));
            e.HasOne<FootballPlayer>().WithMany().HasForeignKey(x => x.PlayerId);
        });

        b.Entity<SquadMembership>(e =>
        {
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
            e.HasOne<FootballPlayer>().WithMany().HasForeignKey(x => x.PlayerId);
            // Bất biến của squad hiện tại, DB chặn lần cuối nếu code có lỗi.
            e.HasIndex(x => x.PlayerId).HasFilter("\"LeftAt\" IS NULL").IsUnique();
            e.HasIndex(x => new { x.ClubId, x.ShirtNumber }).HasFilter("\"LeftAt\" IS NULL").IsUnique();
            e.HasIndex(x => new { x.ClubId, x.SlotOrder }).HasFilter("\"LeftAt\" IS NULL");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_SquadMembership_ShirtNumber", "\"ShirtNumber\" BETWEEN 1 AND 99");
                t.HasCheckConstraint("CK_SquadMembership_SlotOrder", "\"SlotOrder\" BETWEEN 0 AND 31");
            });
        });

        b.Entity<PlayerContract>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.EndReason).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
            e.HasOne<FootballPlayer>().WithMany().HasForeignKey(x => x.PlayerId);
            e.HasIndex(x => x.PlayerId).HasFilter("\"Status\" = 'Active'").IsUnique();
            // C16; hợp đồng nhập từ patch chưa có độ dài (null).
            e.ToTable(t => t.HasCheckConstraint("CK_PlayerContract_MatchesTotal", "\"MatchesTotal\" IS NULL OR \"MatchesTotal\" BETWEEN 10 AND 60"));
        });

        b.Entity<TransferWindow>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.SeasonId);
            e.HasIndex(x => x.MatchdayId);
        });

        b.Entity<Transfer>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RejectReason).HasMaxLength(50);
            e.HasOne<TransferWindow>().WithMany().HasForeignKey(x => x.TransferWindowId);
            e.HasOne<FootballPlayer>().WithMany().HasForeignKey(x => x.PlayerId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.FromClubId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ToClubId);
            e.ToTable(t => t.HasCheckConstraint("CK_Transfer_Fee", "\"Fee\" >= 0"));
        });

        b.Entity<YouthIntake>(e => e.HasIndex(x => new { x.SeasonId, x.ClubId }));
        b.Entity<ScoutReport>(e => e.HasKey(x => new { x.ClubId, x.PlayerId }));
        b.Entity<BoardConfidence>(e => e.HasKey(x => new { x.ClubId, x.SeasonId }));
        b.Entity<CareerSeen>(e => { e.HasKey(x => new { x.ParticipantId, x.Key }); e.Property(x => x.Key).HasMaxLength(80); });
        b.Entity<ClubStaff>(e =>
        {
            e.HasKey(x => new { x.ClubId, x.Role });
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<PressConference>(e =>
        {
            e.HasIndex(x => new { x.FixtureId, x.ClubId }).IsUnique();
            e.Property(x => x.Tone).HasConversion<string>().HasMaxLength(20);
        });
        // HLV career (PO 19/11): hồ sơ, hợp đồng, ứng tuyển, ví, hòm thư riêng, ảnh, tài sản, bài tập riêng.
        b.Entity<Coach>(e =>
        {
            e.HasIndex(x => x.ManagerId).IsUnique();
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Nickname).HasMaxLength(40);
            e.Property(x => x.Nationality).HasMaxLength(40);
            e.Property(x => x.Formation).HasMaxLength(10);
            e.Property(x => x.Style).HasMaxLength(40);
            e.Property(x => x.Hair).HasMaxLength(20);
            e.Property(x => x.HairColor).HasMaxLength(9);
            e.Property(x => x.Outfit).HasMaxLength(20);
            e.Property(x => x.Skin).HasMaxLength(9);
            e.Property(x => x.Origin).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Trait).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });
        b.Entity<CoachContract>(e =>
        {
            e.HasIndex(x => new { x.CoachId, x.Status });
            e.Property(x => x.Objective).HasMaxLength(60);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });
        b.Entity<CoachApplication>(e =>
        {
            e.HasIndex(x => new { x.CoachId, x.SeasonId, x.ClubId });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });
        b.Entity<CoachLedgerEntry>(e =>
        {
            e.HasIndex(x => x.CoachId);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(200);
        });
        b.Entity<CoachLetter>(e =>
        {
            e.HasIndex(x => new { x.CoachId, x.Tick });
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.From).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Sender).HasMaxLength(120);
            e.Property(x => x.Subject).HasMaxLength(160);
            e.Property(x => x.Body).HasMaxLength(4000);
            e.Property(x => x.Key).HasMaxLength(40);
            e.Property(x => x.Options).HasMaxLength(1000);
            e.Property(x => x.Data).HasMaxLength(400);
            e.Property(x => x.Link).HasMaxLength(200);
        });
        b.Entity<CoachPhoto>(e =>
        {
            e.HasIndex(x => x.CoachId);
            e.Property(x => x.Caption).HasMaxLength(160);
        });
        b.Entity<CoachAsset>(e =>
        {
            e.HasIndex(x => x.CoachId);
            e.Property(x => x.Key).HasMaxLength(40);
        });
        b.Entity<CoachDrill>(e => e.HasIndex(x => new { x.ClubId, x.Applied }));

        b.Entity<PlayerTalk>(e =>
        {
            e.HasIndex(x => new { x.PlayerId, x.RoundKey }).IsUnique();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<PlayerCondition>(e =>
        {
            e.HasKey(x => x.PlayerId);
            e.Property(x => x.Injury).HasMaxLength(60);
            e.HasOne<FootballPlayer>().WithMany().HasForeignKey(x => x.PlayerId);
        });

        b.Entity<ClubFinance>(e =>
        {
            e.HasKey(x => x.ClubId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        });

        b.Entity<ClubTraining>(e =>
        {
            e.HasKey(x => x.ClubId);
            e.Property(x => x.Focus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Intensity).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        });

        b.Entity<TransferOffer>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.FailReason).HasMaxLength(80);
            e.HasOne<TransferWindow>().WithMany().HasForeignKey(x => x.TransferWindowId);
            e.HasOne<FootballPlayer>().WithMany().HasForeignKey(x => x.PlayerId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.BuyerClubId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.SellerClubId);
            e.HasIndex(x => new { x.SellerClubId, x.Status });
            e.HasIndex(x => new { x.BuyerClubId, x.Status });
            e.ToTable(t => t.HasCheckConstraint("CK_TransferOffer_Fee", "\"Fee\" >= 0"));
        });

        b.Entity<ClubLedgerEntry>(e =>
        {
            e.Property(x => x.Reason).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RefType).HasMaxLength(30);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
            e.HasIndex(x => x.ClubId);
        });

        b.Entity<PatchVersion>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.BaseOptionFileSha256).HasMaxLength(64);
            e.Property(x => x.BaseOptionFilePath).HasMaxLength(500);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<ClubPesMapping>(e =>
        {
            e.HasKey(x => new { x.ClubId, x.PatchVersionId });
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
            e.HasOne<PatchVersion>().WithMany().HasForeignKey(x => x.PatchVersionId);
            e.HasIndex(x => new { x.PatchVersionId, x.PesClubIndex }).IsUnique();
        });

        b.Entity<PlayerPesMapping>(e =>
        {
            e.HasKey(x => new { x.PlayerId, x.PatchVersionId });
            e.HasOne<FootballPlayer>().WithMany().HasForeignKey(x => x.PlayerId);
            e.HasOne<PatchVersion>().WithMany().HasForeignKey(x => x.PatchVersionId);
            e.HasIndex(x => new { x.PatchVersionId, x.PesPlayerId }).IsUnique();
        });

        b.Entity<OptionFileBuild>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.StoragePath).HasMaxLength(500);
            e.Property(x => x.SquadSnapshot).HasColumnType("jsonb");
            e.Property(x => x.Errors).HasColumnType("jsonb");
            e.HasOne<PatchVersion>().WithMany().HasForeignKey(x => x.PatchVersionId);
            e.HasIndex(x => x.MatchdayId);
        });

        b.Entity<SeasonEconomy>(e =>
        {
            e.HasKey(x => x.SeasonId);
            e.Property(x => x.FriendlyWinPrize).HasDefaultValue(FriendlyRules.DefaultWinPrize);
            e.Property(x => x.FriendlyDrawPrize).HasDefaultValue(FriendlyRules.DefaultDrawPrize);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_SeasonEconomy_SoftWageCap", "\"SoftWageCap\" >= 0");
                t.HasCheckConstraint("CK_SeasonEconomy_WagePermille", "\"WagePermille\" >= 0");
                t.HasCheckConstraint("CK_SeasonEconomy_InitialBudgetFund", "\"InitialBudgetFund\" >= 0");
                t.HasCheckConstraint("CK_SeasonEconomy_DefaultContractMatches", "\"DefaultContractMatches\" BETWEEN 10 AND 60");
            });
        });

        b.Entity<ContractRenewal>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RejectReason).HasMaxLength(50);
            e.HasOne<PlayerContract>().WithMany().HasForeignKey(x => x.ContractId);
            e.HasOne<FootballPlayer>().WithMany().HasForeignKey(x => x.PlayerId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.OriginClubId);
            // Mỗi hợp đồng tối đa 1 đề nghị đang chờ.
            e.HasIndex(x => x.ContractId).HasFilter("\"Status\" = 'Pending'").IsUnique();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_ContractRenewal_Matches", "\"Matches\" BETWEEN 10 AND 60");
                t.HasCheckConstraint("CK_ContractRenewal_Fee", "\"Fee\" >= 0");
            });
        });

        b.Entity<RoundSettlement>(e => e.HasIndex(x => new { x.SeasonId, x.MatchdayId }).IsUnique());

        b.Entity<PlayerPrice>(e =>
        {
            e.HasKey(x => new { x.PlayerId, x.SeasonId });
            e.HasOne<FootballPlayer>().WithMany().HasForeignKey(x => x.PlayerId);
        });

        // M2.2. Các cột SeasonId/MatchdayId cũ (TransferWindow, RoundSettlement, PlayerPrice…) chưa gắn khoá ngoại — làm sau.
        b.Entity<Season>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne<PatchVersion>().WithMany().HasForeignKey(x => x.PatchVersionId);
        });

        b.Entity<SeasonClub>(e =>
        {
            e.HasKey(x => new { x.SeasonId, x.ClubId });
            e.HasOne<Season>().WithMany().HasForeignKey(x => x.SeasonId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        });

        b.Entity<Matchday>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Stage).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Season>().WithMany().HasForeignKey(x => x.SeasonId);
            e.HasOne<TransferWindow>().WithMany().HasForeignKey(x => x.TransferWindowId);
            e.HasOne<OptionFileBuild>().WithMany().HasForeignKey(x => x.OptionFileBuildId);
            e.HasIndex(x => new { x.SeasonId, x.Number }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_Matchday_Leg", "\"Leg\" IS NULL OR \"Leg\" IN (1, 2)"));
        });

        b.Entity<Fixture>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ResultSource).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Matchday>().WithMany().HasForeignKey(x => x.MatchdayId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.HomeClubId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.AwayClubId);
            e.HasIndex(x => x.MatchdayId);
            e.Property(x => x.Stage).HasConversion<string>().HasMaxLength(20);
            e.HasOne<CompetitionEdition>().WithMany().HasForeignKey(x => x.CompetitionEditionId);
            e.HasIndex(x => x.CompetitionEditionId);
            e.HasIndex(x => x.TieId);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Fixture_Leg", "\"Leg\" IS NULL OR \"Leg\" IN (1, 2)");
                t.HasCheckConstraint("CK_Fixture_Clubs", "\"HomeClubId\" <> \"AwayClubId\"");
                t.HasCheckConstraint("CK_Fixture_Goals", "(\"HomeGoals\" IS NULL OR \"HomeGoals\" >= 0) AND (\"AwayGoals\" IS NULL OR \"AwayGoals\" >= 0)");
                t.HasCheckConstraint("CK_Fixture_Penalties", "(\"HomePenalties\" IS NULL AND \"AwayPenalties\" IS NULL) OR "
                    + "(\"HomePenalties\" >= 0 AND \"AwayPenalties\" >= 0 AND \"HomePenalties\" <> \"AwayPenalties\")");
            });
        });

        // Liên đoàn nhiều giải (docs/domain-admin-design.md, PO duyệt 05/11).
        b.Entity<Competition>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.ShortName).HasMaxLength(20);
            e.Property(x => x.Country).HasMaxLength(3);
            e.Property(x => x.LogoPath).HasMaxLength(300);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Rules).HasColumnType("jsonb").HasConversion(RulesConverter, RulesComparer);
            e.HasOne<Competition>().WithMany().HasForeignKey(x => x.PromotesToId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Competition>().WithMany().HasForeignKey(x => x.RelegatesToId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Name).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_Competition_Tier", "\"Tier\" IS NULL OR \"Tier\" >= 1"));
        });

        b.Entity<CompetitionEdition>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RulesSnapshot).HasColumnType("jsonb").HasConversion(RulesConverter!, RulesComparer);
            e.HasOne<Competition>().WithMany().HasForeignKey(x => x.CompetitionId);
            e.HasOne<Season>().WithMany().HasForeignKey(x => x.SeasonId);
            e.HasIndex(x => new { x.CompetitionId, x.SeasonId }).IsUnique();
        });

        b.Entity<EditionClub>(e =>
        {
            e.HasKey(x => new { x.EditionId, x.ClubId });
            e.HasOne<CompetitionEdition>().WithMany().HasForeignKey(x => x.EditionId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        });

        b.Entity<Sanction>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.RevokeReason).HasMaxLength(500);
            e.Property(x => x.Actor).HasMaxLength(100);
            e.Property(x => x.BannedFixtureIds).HasMaxLength(4000);
            e.HasOne<Season>().WithMany().HasForeignKey(x => x.SeasonId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
            e.HasOne<Manager>().WithMany().HasForeignKey(x => x.ManagerId);
            e.HasOne<CompetitionEdition>().WithMany().HasForeignKey(x => x.EditionId);
            e.HasIndex(x => x.SeasonId);
            e.Ignore(x => x.Active);
            e.Ignore(x => x.BannedFixtures);
        });

        b.Entity<AdminAuditEntry>(e =>
        {
            e.Property(x => x.Actor).HasMaxLength(100);
            e.Property(x => x.EntityType).HasMaxLength(50);
            e.Property(x => x.Action).HasMaxLength(50);
            e.Property(x => x.Before).HasColumnType("jsonb");
            e.Property(x => x.After).HasColumnType("jsonb");
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        b.Entity<MatchAttempt>(e =>
        {
            e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ContainerName).HasMaxLength(100);
            e.Property(x => x.RecordingPath).HasMaxLength(500);
            e.HasOne<Fixture>().WithMany().HasForeignKey(x => x.FixtureId);
            // Mỗi trận tối đa 1 lần chạy đang diễn ra.
            e.HasIndex(x => x.FixtureId).HasFilter("\"Outcome\" = 'Running'").IsUnique();
        });

        b.Entity<Goal>(e =>
        {
            e.HasOne<Fixture>().WithMany().HasForeignKey(x => x.FixtureId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
            e.HasOne<FootballPlayer>().WithMany().HasForeignKey(x => x.ScorerPlayerId);
            e.HasIndex(x => x.FixtureId);
            e.HasIndex(x => x.ScorerPlayerId);
        });

        b.Entity<MatchRating>(e =>
        {
            e.HasOne<Fixture>().WithMany().HasForeignKey(x => x.FixtureId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
            e.HasOne<FootballPlayer>().WithMany().HasForeignKey(x => x.PlayerId);
            e.Property(x => x.Rating).HasPrecision(3, 1);
            e.HasIndex(x => new { x.FixtureId, x.PlayerId }).IsUnique();
            e.HasIndex(x => x.PlayerId);
        });

        b.Entity<MatchTeamStats>(e =>
        {
            e.HasKey(x => new { x.FixtureId, x.ClubId });
            e.HasOne<Fixture>().WithMany().HasForeignKey(x => x.FixtureId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
        });

        b.Entity<MatchEvent>(e =>
        {
            e.HasOne<Fixture>().WithMany().HasForeignKey(x => x.FixtureId);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId);
            e.HasOne<FootballPlayer>().WithMany().HasForeignKey(x => x.PlayerId);
            e.HasOne<FootballPlayer>().WithMany().HasForeignKey(x => x.OtherPlayerId);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.FixtureId);
        });

        b.Entity<MatchArticle>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(180);
            e.Property(x => x.Body).HasColumnType("text");
            e.HasOne<Fixture>().WithMany().HasForeignKey(x => x.FixtureId);
            e.HasIndex(x => x.FixtureId).IsUnique();
            e.HasIndex(x => x.PublishedAt);
        });

        b.Entity<Manager>(e =>
        {
            e.Property(x => x.Username).HasMaxLength(40);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(60);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
        });
        b.Entity<InviteCode>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.CreatedBy).HasMaxLength(60);
            e.Property(x => x.Note).HasMaxLength(100);
            e.HasOne<Manager>().WithMany().HasForeignKey(x => x.UsedByManagerId);
        });
        b.Entity<Manager>().Property(x => x.Elo).HasDefaultValue(FriendlyRules.StartElo);
        b.Entity<FriendlyMatch>(e =>
        {
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.HomeTeamName).HasMaxLength(60);
            e.Property(x => x.AwayTeamName).HasMaxLength(60);
            e.Property(x => x.LastError).HasMaxLength(300);
            e.HasOne<Manager>().WithMany().HasForeignKey(x => x.HomeManagerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Manager>().WithMany().HasForeignKey(x => x.AwayManagerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.HomeClubId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.AwayClubId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Season>().WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.HomeManagerId, x.CreatedAt });
            e.HasIndex(x => new { x.AwayManagerId, x.CreatedAt });
            e.HasIndex(x => new { x.SeasonId, x.Mode });
        });
        b.Entity<FriendlyGoal>(e =>
        {
            e.Property(x => x.ScorerName).HasMaxLength(60);
            e.HasOne<FriendlyMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.MatchId);
        });
        b.Entity<MatchmakingTicket>(e =>
        {
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(10);
            e.HasOne<Manager>().WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<FriendlyMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.Mode, x.ExpiresAt });
            e.HasIndex(x => x.ManagerId);
        });
        b.Entity<FriendlyChatMessage>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(FriendlyRules.ChatMax);
            e.HasOne<Manager>().WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<FriendlyMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.MatchId, x.At });
        });
        b.Entity<MailMessage>(e =>
        {
            e.Property(x => x.Subject).HasMaxLength(MailTemplates.SubjectMax);
            e.Property(x => x.Body).HasMaxLength(MailTemplates.BodyMax);
            e.Property(x => x.Template).HasMaxLength(40);
            e.HasOne<Manager>().WithMany().HasForeignKey(x => x.FromManagerId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.FromClubId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.ThreadId);
            e.HasIndex(x => new { x.FromManagerId, x.At });
        });
        b.Entity<MailRecipient>(e =>
        {
            e.HasOne<MailMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Manager>().WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.ManagerId, x.ReadAt });
            e.HasIndex(x => x.MessageId);
        });
        b.Entity<MailBlock>(e =>
        {
            e.HasKey(x => new { x.ManagerId, x.BlockedManagerId });
            e.HasOne<Manager>().WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Manager>().WithMany().HasForeignKey(x => x.BlockedManagerId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<LeagueApplication>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.Reason).HasMaxLength(300);
            e.Property(x => x.DecidedBy).HasMaxLength(100);
            e.HasOne<Manager>().WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.PreferredClubId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.ManagerId, x.Status });
        });
        b.Entity<GoalPoll>(e =>
        {
            e.HasOne<Matchday>().WithMany().HasForeignKey(x => x.MatchdayId);
            e.HasOne<CompetitionEdition>().WithMany().HasForeignKey(x => x.CompetitionEditionId);
            e.HasOne<Goal>().WithMany().HasForeignKey(x => x.WinnerGoalId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.MatchdayId, x.CompetitionEditionId }).IsUnique().AreNullsDistinct(false);
            e.HasIndex(x => x.SeasonId);
        });

        b.Entity<GoalVote>(e =>
        {
            e.Property(x => x.VoterKey).HasMaxLength(64);
            e.Property(x => x.IpHash).HasMaxLength(64);
            e.HasOne<GoalPoll>().WithMany().HasForeignKey(x => x.PollId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Goal>().WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.PollId, x.VoterKey }).IsUnique();
            e.HasIndex(x => new { x.PollId, x.IpHash });
        });

        b.Entity<MatchPhoto>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(80);
            e.Property(x => x.Caption).HasMaxLength(200);
            e.HasOne<MatchArticle>().WithMany().HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ArticleId, x.Sort }).IsUnique();
        });

        b.Entity<AttributeChange>(e =>
        {
            e.HasOne<FootballPlayer>().WithMany().HasForeignKey(x => x.PlayerId);
            e.HasOne<Fixture>().WithMany().HasForeignKey(x => x.FixtureId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Field).HasMaxLength(40);
            e.HasIndex(x => new { x.SeasonId, x.PlayerId });
        });

        b.Entity<ClubTactics>(e =>
        {
            e.HasKey(x => x.ClubId);
            e.HasOne<Club>().WithOne().HasForeignKey<ClubTactics>(x => x.ClubId);
            e.Property(x => x.Formation).HasMaxLength(10);
        });
    }

    // Luật giải lưu jsonb; record bất biến → so sánh theo giá trị.
    private static readonly ValueConverter<CompetitionRules, string> RulesConverter = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        s => JsonSerializer.Deserialize<CompetitionRules>(s, (JsonSerializerOptions?)null) ?? new());
    private static readonly ValueComparer<CompetitionRules> RulesComparer = new((a, c) => a == c, v => v.GetHashCode(), v => v);
}

/// <summary>Cho dotnet-ef lúc thiết kế (tạo migration), không cần DB thật.</summary>
public sealed class ClubsDbContextFactory : IDesignTimeDbContextFactory<ClubsDbContext>
{
    public ClubsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ClubsDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("PES17_DB") ?? "Host=localhost;Database=pes17league",
            o => o.MigrationsHistoryTable("__EFMigrationsHistory", ClubsDbContext.Schema))
        .Options);
}
