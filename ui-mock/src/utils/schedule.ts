import { Match, Team } from '../types';

/**
 * Generates an automatic double round-robin (lượt đi & lượt về) schedule for teams.
 * Implements the Circle Method.
 */
export function generateRoundRobinSchedule(leagueId: string, teamIds: string[]): Match[] {
  if (teamIds.length < 2) return [];

  // If there's an odd number of teams, add a 'BYE_DUMMY' spacer
  let teams = [...teamIds];
  const isOdd = teams.length % 2 !== 0;
  if (isOdd) {
    teams.push('BYE_DUMMY_TEAM');
  }

  const numTeams = teams.length;
  const numRounds = numTeams - 1;
  const halfSize = numTeams / 2;

  const matches: Omit<Match, 'id'>[] = [];

  // Leg 1: Lượt đi
  for (let round = 1; round <= numRounds; round++) {
    for (let i = 0; i < halfSize; i++) {
      const home = teams[i];
      const away = teams[numTeams - 1 - i];

      // Skip match if dummy BYE team is playing
      if (home !== 'BYE_DUMMY_TEAM' && away !== 'BYE_DUMMY_TEAM') {
        const matchDay = 1 + Math.floor((round - 1) / 3); // 3 matches per day approx
        matches.push({
          leagueId,
          round,
          isReturn: false,
          homeTeamId: home,
          awayTeamId: away,
          date: `2026-07-${String(10 + matchDay).padStart(2, '0')} 19:30`,
          status: 'scheduled',
        });
      }
    }

    // Rotate teams in the circle mechanism: fix index [0], shift remainder clockwise
    teams = [teams[0], teams[numTeams - 1], ...teams.slice(1, numTeams - 1)];
  }

  // Leg 2: Lượt về (Reverse leg with home-away swapped)
  const firstLegMatchesCount = matches.length;
  const firstLegRoundsCount = numRounds;

  for (let i = 0; i < firstLegMatchesCount; i++) {
    const m = matches[i];
    const matchDay = 1 + Math.floor((m.round - 1) / 3);
    matches.push({
      leagueId,
      round: m.round + firstLegRoundsCount,
      isReturn: true,
      homeTeamId: m.awayTeamId,
      awayTeamId: m.homeTeamId,
      date: `2026-08-${String(10 + matchDay).padStart(2, '0')} 19:30`,
      status: 'scheduled',
    });
  }

  // Map to generate high-quality sequential match IDs
  return matches.map((m, index) => ({
    ...m,
    id: `m_gen_${leagueId}_${index + 1}`,
  }));
}

/**
 * Resets all team statistics and recalculates the table standings using completed matches
 * sorted by dates to accurately build streak values and order by points, goal difference, goals scored.
 */
export function recalculateStandings(teams: Team[], matches: Match[], currentLeagueId: string): Team[] {
  // Resets stats for teams in this league
  const leagueTeams = teams.map((t) => {
    if (t.leagueId === currentLeagueId) {
      return {
        ...t,
        matchesPlayed: 0,
        wins: 0,
        draws: 0,
        losses: 0,
        goalsFor: 0,
        goalsAgainst: 0,
        goalDifference: 0,
        points: 0,
        streak: [] as ('W' | 'D' | 'L')[],
      };
    }
    return t;
  });

  // Filter completed matches for this league
  const completedMatches = matches
    .filter((m) => m.leagueId === currentLeagueId && m.status === 'completed')
    .sort((a, b) => a.date.localeCompare(b.date)); // Sort by date to get chronological streaks

  completedMatches.forEach((m) => {
    if (m.homeScore === undefined || m.awayScore === undefined) return;

    const homeTeam = leagueTeams.find((t) => t.id === m.homeTeamId);
    const awayTeam = leagueTeams.find((t) => t.id === m.awayTeamId);

    if (homeTeam) {
      homeTeam.matchesPlayed += 1;
      homeTeam.goalsFor += m.homeScore;
      homeTeam.goalsAgainst += m.awayScore;
      if (m.homeScore > m.awayScore) {
        homeTeam.wins += 1;
        homeTeam.points += 3;
        homeTeam.streak.push('W');
      } else if (m.homeScore === m.awayScore) {
        homeTeam.draws += 1;
        homeTeam.points += 1;
        homeTeam.streak.push('D');
      } else {
        homeTeam.losses += 1;
        homeTeam.streak.push('L');
      }
    }

    if (awayTeam) {
      awayTeam.matchesPlayed += 1;
      awayTeam.goalsFor += m.awayScore;
      awayTeam.goalsAgainst += m.homeScore;
      if (m.awayScore > m.homeScore) {
        awayTeam.wins += 1;
        awayTeam.points += 3;
        awayTeam.streak.push('W');
      } else if (m.awayScore === m.homeScore) {
        awayTeam.draws += 1;
        awayTeam.points += 1;
        awayTeam.streak.push('D');
      } else {
        awayTeam.losses += 1;
        awayTeam.streak.push('L');
      }
    }
  });

  // Re-adjust stats and limit streak lists to latest 5 games
  leagueTeams.forEach((t) => {
    if (t.leagueId === currentLeagueId) {
      t.goalDifference = t.goalsFor - t.goalsAgainst;
      t.streak = t.streak.slice(-5);
    }
  });

  // Sort logic for this league (Rest remain unchanged in list, sorted relative to this league block)
  const nonLeagueTeams = leagueTeams.filter((t) => t.leagueId !== currentLeagueId);
  const targetLeagueTeams = leagueTeams.filter((t) => t.leagueId === currentLeagueId);

  targetLeagueTeams.sort((a, b) => {
    if (b.points !== a.points) return b.points - a.points;
    if (b.goalDifference !== a.goalDifference) return b.goalDifference - a.goalDifference;
    if (b.goalsFor !== a.goalsFor) return b.goalsFor - a.goalsFor;
    return a.name.localeCompare(b.name);
  });

  return [...targetLeagueTeams, ...nonLeagueTeams];
}
