import { League, Team, Player, Match, TransferRequest, NewsItem, User } from './types';

// Mock Users
export const mockUsers: User[] = [
  {
    id: 'user_admin',
    name: 'Nguyễn Minh Đức (BTC)',
    email: 'admin@pes17.vn',
    role: 'admin',
    avatar: 'https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=150&auto=format&fit=crop&q=80',
  },
  {
    id: 'hlv_mu',
    name: 'Phạm Quốc Thái (HLV MU)',
    email: 'hlv.mu@pes17.vn',
    role: 'coach',
    teamId: 'team_mu',
    parsecId: 'QuocThai#4829',
    avatar: 'https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=150&auto=format&fit=crop&q=80',
  },
  {
    id: 'hlv_chelsea',
    name: 'Lê Hoàng Nam (HLV Chelsea)',
    email: 'hlv.chelsea@pes17.vn',
    role: 'coach',
    teamId: 'team_chelsea',
    parsecId: 'HoangNam#1102',
    avatar: 'https://images.unsplash.com/photo-1500648767791-00dcc994a43e?w=150&auto=format&fit=crop&q=80',
  },
  {
    id: 'hlv_mc',
    name: 'Trần Thế Anh (HLV Man City)',
    email: 'hlv.mancity@pes17.vn',
    role: 'coach',
    teamId: 'team_mc',
    parsecId: 'TheAnh#9988',
    avatar: 'https://images.unsplash.com/photo-1519085360753-af0119f7cbe7?w=150&auto=format&fit=crop&q=80',
  },
  {
    id: 'hlv_arsenal',
    name: 'Vũ Đức Hải (HLV Arsenal)',
    email: 'hlv.arsenal@pes17.vn',
    role: 'coach',
    teamId: 'team_arsenal',
    parsecId: 'DucHai#3321',
    avatar: 'https://images.unsplash.com/photo-1472099645785-5658abf4ff4e?w=150&auto=format&fit=crop&q=80',
  },
];

// Mock Leagues
export const mockLeagues: League[] = [
  {
    id: 'league_epl',
    name: 'Ngoại Hạng Anh',
    season: 'Mùa 1 - 2026',
    startDate: '2026-06-01',
    endDate: '2026-08-30',
  },
  {
    id: 'league_laliga',
    name: 'La Liga',
    season: 'Mùa 1 - 2026',
    startDate: '2026-06-10',
    endDate: '2026-09-10',
  },
  {
    id: 'league_seriea',
    name: 'Serie A',
    season: 'Mùa 1 - 2026',
    startDate: '2026-06-15',
    endDate: '2026-09-15',
  },
  {
    id: 'league_bundesliga',
    name: 'Bundesliga',
    season: 'Mùa 1 - 2026',
    startDate: '2026-06-20',
    endDate: '2026-09-20',
  },
  {
    id: 'league_ligue1',
    name: 'Ligue 1',
    season: 'Mùa 1 - 2026',
    startDate: '2026-06-25',
    endDate: '2026-09-25',
  },
];

// Mock Teams
export const mockTeams: Team[] = [
  // EPL Teams
  {
    id: 'team_mu',
    name: 'Manchester United',
    logoColor: 'from-red-600 to-red-800',
    leagueId: 'league_epl',
    coachId: 'hlv_mu',
    coachName: 'Phạm Quốc Thái',
    parsecId: 'QuocThai#4829',
    matchesPlayed: 4,
    wins: 3,
    draws: 1,
    losses: 0,
    goalsFor: 10,
    goalsAgainst: 4,
    goalDifference: 6,
    points: 10,
    streak: ['W', 'W', 'D', 'W'],
  },
  {
    id: 'team_chelsea',
    name: 'Chelsea FC',
    logoColor: 'from-blue-600 to-blue-800',
    leagueId: 'league_epl',
    coachId: 'hlv_chelsea',
    coachName: 'Lê Hoàng Nam',
    parsecId: 'HoangNam#1102',
    matchesPlayed: 4,
    wins: 2,
    draws: 1,
    losses: 1,
    goalsFor: 8,
    goalsAgainst: 6,
    goalDifference: 2,
    points: 7,
    streak: ['W', 'L', 'D', 'W'],
  },
  {
    id: 'team_mc',
    name: 'Manchester City',
    logoColor: 'from-sky-400 to-sky-600',
    leagueId: 'league_epl',
    coachId: 'hlv_mc',
    coachName: 'Trần Thế Anh',
    parsecId: 'TheAnh#9988',
    matchesPlayed: 4,
    wins: 2,
    draws: 0,
    losses: 2,
    goalsFor: 9,
    goalsAgainst: 7,
    goalDifference: 2,
    points: 6,
    streak: ['L', 'W', 'W', 'L'],
  },
  {
    id: 'team_arsenal',
    name: 'Arsenal FC',
    logoColor: 'from-red-500 to-red-700',
    leagueId: 'league_epl',
    coachId: 'hlv_arsenal',
    coachName: 'Vũ Đức Hải',
    parsecId: 'DucHai#3321',
    matchesPlayed: 4,
    wins: 0,
    draws: 2,
    losses: 2,
    goalsFor: 3,
    goalsAgainst: 8,
    goalDifference: -5,
    points: 2,
    streak: ['D', 'L', 'D', 'L'],
  },
  // La Liga (empty stats initially or minor)
  {
    id: 'team_real',
    name: 'Real Madrid',
    logoColor: 'from-zinc-100 to-zinc-300 border-zinc-400',
    leagueId: 'league_laliga',
    coachId: 'hlv_real',
    coachName: 'Zinedine Zidane',
    parsecId: 'Zizou#5544',
    matchesPlayed: 0,
    wins: 0,
    draws: 0,
    losses: 0,
    goalsFor: 0,
    goalsAgainst: 0,
    goalDifference: 0,
    points: 0,
    streak: [],
  },
  {
    id: 'team_barca',
    name: 'FC Barcelona',
    logoColor: 'from-blue-800 to-red-700',
    leagueId: 'league_laliga',
    coachId: 'hlv_barca',
    coachName: 'Pep Guardiola',
    parsecId: 'Pep#3341',
    matchesPlayed: 0,
    wins: 0,
    draws: 0,
    losses: 0,
    goalsFor: 0,
    goalsAgainst: 0,
    goalDifference: 0,
    points: 0,
    streak: [],
  },
];

// Mock Players
export const mockPlayers: Player[] = [
  // Man United Players
  { id: 'p_1', name: 'Zlatan Ibrahimović', nationality: 'Sweden', position: 'CF', ovr: 90, value: 35.5, teamId: 'team_mu', goalsCount: 5, assistsCount: 1, isTransferListed: false },
  { id: 'p_2', name: 'Paul Pogba', nationality: 'France', position: 'CMF', ovr: 88, value: 65, teamId: 'team_mu', goalsCount: 2, assistsCount: 4, isTransferListed: false },
  { id: 'p_3', name: 'Marcus Rashford', nationality: 'England', position: 'LWF', ovr: 82, value: 45, teamId: 'team_mu', goalsCount: 3, assistsCount: 2, isTransferListed: true, transferPrice: 42.0 },
  { id: 'p_4', name: 'David De Gea', nationality: 'Spain', position: 'GK', ovr: 90, value: 50, teamId: 'team_mu', goalsCount: 0, assistsCount: 0, isTransferListed: false },
  { id: 'p_5', name: 'Eric Bailly', nationality: 'Ivory Coast', position: 'CB', ovr: 83, value: 28, teamId: 'team_mu', goalsCount: 0, assistsCount: 0, isTransferListed: false },
  { id: 'p_6', name: 'Juan Mata', nationality: 'Spain', position: 'AMF', ovr: 84, value: 24, teamId: 'team_mu', goalsCount: 0, assistsCount: 2, isTransferListed: false },
  { id: 'p_7', name: 'Henrikh Mkhitaryan', nationality: 'Armenia', position: 'RWF', ovr: 85, value: 30, teamId: 'team_mu', goalsCount: 0, assistsCount: 1, isTransferListed: false },

  // Chelsea Players
  { id: 'p_8', name: 'Diego Costa', nationality: 'Spain', position: 'CF', ovr: 87, value: 48, teamId: 'team_chelsea', goalsCount: 4, assistsCount: 0, isTransferListed: false },
  { id: 'p_9', name: 'Eden Hazard', nationality: 'Belgium', position: 'LWF', ovr: 92, value: 92, teamId: 'team_chelsea', goalsCount: 3, assistsCount: 3, isTransferListed: false },
  { id: 'p_10', name: 'N\'Golo Kanté', nationality: 'France', position: 'DMF', ovr: 89, value: 60, teamId: 'team_chelsea', goalsCount: 0, assistsCount: 1, isTransferListed: false },
  { id: 'p_11', name: 'Cesc Fábregas', nationality: 'Spain', position: 'CMF', ovr: 85, value: 25, teamId: 'team_chelsea', goalsCount: 1, assistsCount: 3, isTransferListed: true, transferPrice: 22.0 },
  { id: 'p_12', name: 'Gary Cahill', nationality: 'England', position: 'CB', ovr: 82, value: 12, teamId: 'team_chelsea', goalsCount: 0, assistsCount: 0, isTransferListed: false },
  { id: 'p_13', name: 'Thibaut Courtois', nationality: 'Belgium', position: 'GK', ovr: 89, value: 45, teamId: 'team_chelsea', goalsCount: 0, assistsCount: 0, isTransferListed: false },

  // Man City Players
  { id: 'p_14', name: 'Sergio Agüero', nationality: 'Argentina', position: 'CF', ovr: 90, value: 68, teamId: 'team_mc', goalsCount: 5, assistsCount: 1, isTransferListed: false },
  { id: 'p_15', name: 'Kevin De Bruyne', nationality: 'Belgium', position: 'AMF', ovr: 91, value: 85, teamId: 'team_mc', goalsCount: 2, assistsCount: 5, isTransferListed: false },
  { id: 'p_16', name: 'David Silva', nationality: 'Spain', position: 'AMF', ovr: 87, value: 35, teamId: 'team_mc', goalsCount: 1, assistsCount: 2, isTransferListed: false },
  { id: 'p_17', name: 'Raheem Sterling', nationality: 'England', position: 'RWF', ovr: 84, value: 50, teamId: 'team_mc', goalsCount: 1, assistsCount: 1, isTransferListed: false },
  { id: 'p_18', name: 'Vincent Kompany', nationality: 'Belgium', position: 'CB', ovr: 86, value: 20, teamId: 'team_mc', goalsCount: 0, assistsCount: 0, isTransferListed: false },

  // Arsenal Players
  { id: 'p_19', name: 'Alexis Sánchez', nationality: 'Chile', position: 'LWF', ovr: 89, value: 65, teamId: 'team_arsenal', goalsCount: 2, assistsCount: 1, isTransferListed: false },
  { id: 'p_20', name: 'Mesut Özil', nationality: 'Germany', position: 'AMF', ovr: 88, value: 45, teamId: 'team_arsenal', goalsCount: 1, assistsCount: 2, isTransferListed: false },
  { id: 'p_21', name: 'Olivier Giroud', nationality: 'France', position: 'CF', ovr: 83, value: 18, teamId: 'team_arsenal', goalsCount: 0, assistsCount: 0, isTransferListed: true, transferPrice: 15.0 },
  { id: 'p_22', name: 'Laurent Koscielny', nationality: 'France', position: 'CB', ovr: 85, value: 20, teamId: 'team_arsenal', goalsCount: 0, assistsCount: 0, isTransferListed: false },
  { id: 'p_23', name: 'Petr Čech', nationality: 'Czech Republic', position: 'GK', ovr: 86, value: 10, teamId: 'team_arsenal', goalsCount: 0, assistsCount: 0, isTransferListed: false },

  // Real Madrid Players
  { id: 'p_24', name: 'Cristiano Ronaldo', nationality: 'Portugal', position: 'LWF', ovr: 94, value: 110, teamId: 'team_real', goalsCount: 0, assistsCount: 0, isTransferListed: false },
  { id: 'p_25', name: 'Karim Benzema', nationality: 'France', position: 'CF', ovr: 87, value: 40, teamId: 'team_real', goalsCount: 0, assistsCount: 0, isTransferListed: false },
  { id: 'p_26', name: 'Gareth Bale', nationality: 'Wales', position: 'RWF', ovr: 90, value: 72, teamId: 'team_real', goalsCount: 0, assistsCount: 0, isTransferListed: false },
  { id: 'p_27', name: 'Luka Modrić', nationality: 'Croatia', position: 'CMF', ovr: 89, value: 45, teamId: 'team_real', goalsCount: 0, assistsCount: 0, isTransferListed: false },
  { id: 'p_28', name: 'Sergio Ramos', nationality: 'Spain', position: 'CB', ovr: 89, value: 38, teamId: 'team_real', goalsCount: 0, assistsCount: 0, isTransferListed: false },

  // Barcelona Players
  { id: 'p_29', name: 'Lionel Messi', nationality: 'Argentina', position: 'RWF', ovr: 94, value: 120, teamId: 'team_barca', goalsCount: 0, assistsCount: 0, isTransferListed: false },
  { id: 'p_30', name: 'Luis Suárez', nationality: 'Uruguay', position: 'CF', ovr: 92, value: 75, teamId: 'team_barca', goalsCount: 0, assistsCount: 0, isTransferListed: false },
  { id: 'p_31', name: 'Neymar Jr', nationality: 'Brazil', position: 'LWF', ovr: 91, value: 100, teamId: 'team_barca', goalsCount: 0, assistsCount: 0, isTransferListed: false },
  { id: 'p_32', name: 'Andrés Iniesta', nationality: 'Spain', position: 'CMF', ovr: 88, value: 25, teamId: 'team_barca', goalsCount: 0, assistsCount: 0, isTransferListed: false },
  { id: 'p_33', name: 'Gerard Piqué', nationality: 'Spain', position: 'CB', ovr: 87, value: 30, teamId: 'team_barca', goalsCount: 0, assistsCount: 0, isTransferListed: false },

  // Free Agents / Transfer Listed Outside
  { id: 'p_34', name: 'Ronaldinho', nationality: 'Brazil', position: 'AMF', ovr: 93, value: 80, teamId: 'free_agent', goalsCount: 0, assistsCount: 0, isTransferListed: true, transferPrice: 75.0 },
  { id: 'p_35', name: 'Thierry Henry', nationality: 'France', position: 'CF', ovr: 92, value: 60, teamId: 'free_agent', goalsCount: 0, assistsCount: 0, isTransferListed: true, transferPrice: 55.0 },
  { id: 'p_36', name: 'Andrea Pirlo', nationality: 'Italy', position: 'CMF', ovr: 86, value: 15, teamId: 'free_agent', goalsCount: 0, assistsCount: 0, isTransferListed: true, transferPrice: 12.0 },
];

// Mock Matches
export const mockMatches: Match[] = [
  // EPL Completed Matches
  {
    id: 'm_1',
    leagueId: 'league_epl',
    round: 1,
    isReturn: false,
    homeTeamId: 'team_mu',
    awayTeamId: 'team_chelsea',
    homeScore: 3,
    awayScore: 1,
    date: '2026-06-02 20:00',
    status: 'completed',
    reporterId: 'hlv_mu',
    reporterName: 'Phạm Quốc Thái',
    evidenceImage: 'https://images.unsplash.com/photo-1508098682722-e99c43a406b2?w=400&auto=format&fit=crop&q=60',
    goalScorers: [
      { playerId: 'p_1', name: 'Zlatan Ibrahimović', goals: 2 },
      { playerId: 'p_2', name: 'Paul Pogba', goals: 1 },
      { playerId: 'p_8', name: 'Diego Costa', goals: 1 }
    ],
    assists: [
      { playerId: 'p_2', name: 'Paul Pogba', assists: 2 },
      { playerId: 'p_9', name: 'Eden Hazard', assists: 1 }
    ]
  },
  {
    id: 'm_2',
    leagueId: 'league_epl',
    round: 1,
    isReturn: false,
    homeTeamId: 'team_mc',
    awayTeamId: 'team_arsenal',
    homeScore: 4,
    awayScore: 1,
    date: '2026-06-03 21:00',
    status: 'completed',
    reporterId: 'hlv_mc',
    reporterName: 'Trần Thế Anh',
    evidenceImage: 'https://images.unsplash.com/photo-1540747737956-378724044432?w=400&auto=format&fit=crop&q=60',
    goalScorers: [
      { playerId: 'p_14', name: 'Sergio Agüero', goals: 3 },
      { playerId: 'p_15', name: 'Kevin De Bruyne', goals: 1 },
      { playerId: 'p_19', name: 'Alexis Sánchez', goals: 1 }
    ],
    assists: [
      { playerId: 'p_15', name: 'Kevin De Bruyne', assists: 3 },
      { playerId: 'p_20', name: 'Mesut Özil', assists: 1 }
    ]
  },
  {
    id: 'm_3',
    leagueId: 'league_epl',
    round: 2,
    isReturn: false,
    homeTeamId: 'team_chelsea',
    awayTeamId: 'team_mc',
    homeScore: 2,
    awayScore: 1,
    date: '2026-06-05 19:30',
    status: 'completed',
    reporterId: 'hlv_chelsea',
    reporterName: 'Lê Hoàng Nam',
    evidenceImage: 'https://images.unsplash.com/photo-1518063319789-7217e6706b04?w=400&auto=format&fit=crop&q=60',
    goalScorers: [
      { playerId: 'p_9', name: 'Eden Hazard', goals: 2 },
      { playerId: 'p_14', name: 'Sergio Agüero', goals: 1 }
    ],
    assists: [
      { playerId: 'p_11', name: 'Cesc Fábregas', assists: 1 },
      { playerId: 'p_15', name: 'Kevin De Bruyne', assists: 1 }
    ]
  },
  {
    id: 'm_4',
    leagueId: 'league_epl',
    round: 2,
    isReturn: false,
    homeTeamId: 'team_arsenal',
    awayTeamId: 'team_mu',
    homeScore: 1,
    awayScore: 3,
    date: '2026-06-06 20:45',
    status: 'completed',
    reporterId: 'hlv_mu',
    reporterName: 'Phạm Quốc Thái',
    evidenceImage: 'https://images.unsplash.com/photo-1577223625856-45ef56e33bc7?w=400&auto=format&fit=crop&q=60',
    goalScorers: [
      { playerId: 'p_20', name: 'Mesut Özil', goals: 1 },
      { playerId: 'p_1', name: 'Zlatan Ibrahimović', goals: 1 },
      { playerId: 'p_3', name: 'Marcus Rashford', goals: 2 }
    ],
    assists: [
      { playerId: 'p_19', name: 'Alexis Sánchez', assists: 1 },
      { playerId: 'p_2', name: 'Paul Pogba', assists: 2 }
    ]
  },
  {
    id: 'm_5',
    leagueId: 'league_epl',
    round: 3,
    isReturn: false,
    homeTeamId: 'team_mu',
    awayTeamId: 'team_mc',
    homeScore: 2,
    awayScore: 1,
    date: '2026-06-08 21:00',
    status: 'completed',
    reporterId: 'hlv_mc',
    reporterName: 'Trần Thế Anh',
    evidenceImage: 'https://images.unsplash.com/photo-1508098682722-e99c43a406b2?w=400&auto=format&fit=crop&q=60',
    goalScorers: [
      { playerId: 'p_1', name: 'Zlatan Ibrahimović', goals: 1 },
      { playerId: 'p_2', name: 'Paul Pogba', goals: 1 },
      { playerId: 'p_14', name: 'Sergio Agüero', goals: 1 }
    ],
    assists: [
      { playerId: 'p_7', name: 'Henrikh Mkhitaryan', assists: 1 },
      { playerId: 'p_16', name: 'David Silva', assists: 1 }
    ]
  },
  {
    id: 'm_6',
    leagueId: 'league_epl',
    round: 3,
    isReturn: false,
    homeTeamId: 'team_chelsea',
    awayTeamId: 'team_arsenal',
    homeScore: 1,
    awayScore: 1,
    date: '2026-06-09 20:00',
    status: 'completed',
    reporterId: 'hlv_chelsea',
    reporterName: 'Lê Hoàng Nam',
    evidenceImage: 'https://images.unsplash.com/photo-1518063319789-7217e6706b04?w=400&auto=format&fit=crop&q=60',
    goalScorers: [
      { playerId: 'p_11', name: 'Cesc Fábregas', goals: 1 },
      { playerId: 'p_19', name: 'Alexis Sánchez', goals: 1 }
    ],
    assists: [
      { playerId: 'p_10', name: 'N\'Golo Kanté', assists: 1 },
      { playerId: 'p_20', name: 'Mesut Özil', assists: 1 }
    ]
  },

  // Matches pending approval (reported but not finalized)
  {
    id: 'm_7',
    leagueId: 'league_epl',
    round: 4,
    isReturn: false,
    homeTeamId: 'team_mu',
    awayTeamId: 'team_arsenal',
    homeScore: 2,
    awayScore: 1,
    date: '2026-06-10 18:00',
    status: 'pending_approval',
    reporterId: 'hlv_mu',
    reporterName: 'Phạm Quốc Thái',
    evidenceImage: 'https://images.unsplash.com/photo-1508098682722-e99c43a406b2?w=400&auto=format&fit=crop&q=60',
    goalScorers: [
      { playerId: 'p_1', name: 'Zlatan Ibrahimović', goals: 1 },
      { playerId: 'p_3', name: 'Marcus Rashford', goals: 1 },
      { playerId: 'p_20', name: 'Mesut Özil', goals: 1 }
    ],
    assists: [
      { playerId: 'p_6', name: 'Juan Mata', assists: 2 }
    ]
  },

  // Future scheduled match
  {
    id: 'm_8',
    leagueId: 'league_epl',
    round: 4,
    isReturn: false,
    homeTeamId: 'team_chelsea',
    awayTeamId: 'team_mc',
    date: '2026-06-12 21:00',
    status: 'scheduled',
  },
  {
    id: 'm_9',
    leagueId: 'league_epl',
    round: 5,
    isReturn: false,
    homeTeamId: 'team_mc',
    awayTeamId: 'team_mu',
    date: '2026-06-15 19:00',
    status: 'scheduled',
  },
  {
    id: 'm_10',
    leagueId: 'league_epl',
    round: 5,
    isReturn: false,
    homeTeamId: 'team_arsenal',
    awayTeamId: 'team_chelsea',
    date: '2026-06-16 20:00',
    status: 'scheduled',
  },
];

// Mock Transfer Requests
export const mockTransfers: TransferRequest[] = [
  {
    id: 'tr_1',
    playerId: 'p_3',
    playerName: 'Marcus Rashford',
    fromTeamId: 'team_mu',
    fromTeamName: 'Manchester United',
    toTeamId: 'team_chelsea',
    toTeamName: 'Chelsea FC',
    price: 45.0,
    status: 'pending',
    date: '2026-06-10',
  },
  {
    id: 'tr_2',
    playerId: 'p_11',
    playerName: 'Cesc Fábregas',
    fromTeamId: 'team_chelsea',
    fromTeamName: 'Chelsea FC',
    toTeamId: 'team_mc',
    toTeamName: 'Manchester City',
    price: 24.5,
    status: 'approved',
    date: '2026-06-08',
  },
  {
    id: 'tr_3',
    playerId: 'p_34',
    playerName: 'Ronaldinho',
    fromTeamId: 'free_agent',
    fromTeamName: 'Tự do',
    toTeamId: 'team_mu',
    toTeamName: 'Manchester United',
    price: 78.0,
    status: 'approved',
    date: '2026-06-09',
  }
];

// Mock News Feed
export const mockNews: NewsItem[] = [
  {
    id: 'n_1',
    title: 'Phạm Quốc Thái lập chuỗi 4 trận bất bại ấn tượng cùng Manchester United!',
    summary: 'HLV Phạm Quốc Thái vừa có bài phỏng vấn dài về tham vọng vô địch giải PES 17 Online League mùa này.',
    content: 'Với 3 trận thắng thuyết phục và 1 trận hòa trước kình địch Chelsea, Manchester United đang băng băng dẫn đầu bảng xếp hạng giải Ngoại Hạng Anh Mùa 1. "Sức mạnh của lối đá Pes 17 cơ bản nằm ở việc tận dụng khả năng tì đè siêu hạng của Ibrahimović và sự bùng nổ của Rashford từ cánh trái," HLV Quốc Thái phát biểu sau chiến thắng đầy kịch tính trước Manchester City của HLV Thế Anh.',
    category: 'league',
    date: '2026-06-10',
    imageUrl: 'https://images.unsplash.com/photo-1508098682722-e99c43a406b2?w=800&auto=format&fit=crop&q=80',
  },
  {
    id: 'n_2',
    title: 'Thỏa thuận thế kỷ: Ronaldinho chính thức cập bến Manchester United từ diện Tự do',
    summary: 'Ban tổ chức giải vừa chính thức phê duyệt thương vụ chuyển nhượng kinh điển đưa "Răng hô" về Old Trafford.',
    content: 'Sau 3 ngày đàm phán nghẹt thở, Barca và Real lỡ cơ hội chiêu mộ, HLV Quốc Thái đã bấm nút chiêu mộ thành công ảo thuật gia sân cỏ Ronaldinho với giá trị kỷ lục 78 triệu coin. Đây được kỳ vọng là mảnh ghép lý tưởng để xoay tua cùng Paul Pogba ở trung tâm hàng tiền vệ.',
    category: 'transfer',
    date: '2026-06-09',
    imageUrl: 'https://images.unsplash.com/photo-1518063319789-7217e6706b04?w=800&auto=format&fit=crop&q=80',
  },
  {
    id: 'n_3',
    title: 'Drama chuyển nhượng: Chelsea gửi lời đề nghị 45 triệu coin mua đứt Marcus Rashford',
    summary: 'HLV Lê Hoàng Nam quyết phá két để nâng cấp hàng công Chelsea với mục tiêu đè bẹp các đối thủ.',
    content: 'Theo rò rỉ từ hòm thư nội bộ, Chelsea đã chính thức gửi đề nghị trị giá 45 triệu coin gửi tới ban lãnh đạo Manchester United để đổi lấy sự phục vụ của tiền đạo chạy cánh người Anh Marcus Rashford. Mặc dù HLV Quốc Thái tỏ thái độ giữ chân nhưng quyền quyết định cuối cùng đang thuộc về sự thương thảo giữa hai câu lạc bộ và phán quyết tối cao của Admin.',
    category: 'transfer',
    date: '2026-06-10',
    imageUrl: 'https://images.unsplash.com/photo-1577223625856-45ef56e33bc7?w=800&auto=format&fit=crop&q=80',
  }
];
export const APP_STORAGE_KEY = 'pes17league_data';
