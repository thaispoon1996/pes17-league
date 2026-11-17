export type UserRole = 'admin' | 'coach' | 'guest';

export interface User {
  id: string;
  name: string;
  email: string;
  role: UserRole;
  teamId?: string; // If coach, associated team
  parsecId?: string;
  avatar?: string;
}

export interface League {
  id: string;
  name: string;
  season: string;
  startDate: string;
  endDate: string;
}

export interface Team {
  id: string;
  name: string;
  logoColor: string; // Gradient or solid color for stylized display
  leagueId: string;
  coachId: string;
  coachName: string;
  parsecId: string;
  // Stats
  matchesPlayed: number;
  wins: number;
  draws: number;
  losses: number;
  goalsFor: number;
  goalsAgainst: number;
  goalDifference: number;
  points: number;
  streak: ('W' | 'D' | 'L')[];
}

export interface Player {
  id: string;
  name: string;
  nationality: string;
  position: 'GK' | 'CB' | 'LB' | 'RB' | 'DMF' | 'CMF' | 'AMF' | 'LWF' | 'RWF' | 'CF';
  ovr: number; // 40-99
  value: number; // in millions EUR or coins
  teamId: string; // "free_agent" if not in a team, or specific teamId
  goalsCount: number;
  assistsCount: number;
  isTransferListed: boolean;
  transferPrice?: number;
}

export interface GoalScorerRecord {
  playerId: string;
  name: string;
  goals: number;
}

export interface AssistRecord {
  playerId: string;
  name: string;
  assists: number;
}

export interface Match {
  id: string;
  leagueId: string;
  round: number; // Round 1, 2, 3,...
  isReturn: boolean; // false = lượt đi, true = lượt về
  homeTeamId: string;
  awayTeamId: string;
  homeScore?: number;
  awayScore?: number;
  date: string; // Game time formatted
  status: 'scheduled' | 'live' | 'pending_approval' | 'completed';
  reporterId?: string;
  reporterName?: string;
  evidenceImage?: string; // base64 or placeholder
  goalScorers?: GoalScorerRecord[];
  assists?: AssistRecord[];
}

export interface TransferRequest {
  id: string;
  playerId: string;
  playerName: string;
  fromTeamId: string;
  fromTeamName: string;
  toTeamId: string;
  toTeamName: string;
  price: number;
  status: 'pending' | 'approved' | 'rejected';
  date: string;
}

export interface NewsItem {
  id: string;
  title: string;
  summary: string;
  content: string;
  category: 'transfer' | 'match' | 'league' | 'general';
  date: string;
  imageUrl?: string;
}
