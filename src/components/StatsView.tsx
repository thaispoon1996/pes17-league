import React from 'react';
import { Player, Team, League } from '../types';
import { Trophy, Shield, Bolt, Sparkles, Star, ChevronRight, Zap, Target } from 'lucide-react';

interface StatsViewProps {
  players: Player[];
  teams: Team[];
  leagues: League[];
  activeLeagueId: string;
}

export default function StatsView({
  players,
  teams,
  leagues,
  activeLeagueId,
}: StatsViewProps) {
  // Grab current active league data
  const currentLeague = leagues.find(l => l.id === activeLeagueId) || leagues[0];
  const leagueTeams = teams.filter(t => t.leagueId === activeLeagueId);

  // Collect player stats of current active league clubs
  const leagueTeamIds = leagueTeams.map(t => t.id);
  const leaguePlayers = players.filter(p => leagueTeamIds.includes(p.teamId));

  // Top Scorers (goalsCount descending)
  const topScorers = [...leaguePlayers]
    .filter(p => p.goalsCount > 0)
    .sort((a, b) => b.goalsCount - a.goalsCount)
    .slice(0, 8);

  const maxGoals = topScorers.length > 0 ? topScorers[0].goalsCount : 1;

  // Top Assists (assistsCount descending)
  const topAssists = [...leaguePlayers]
    .filter(p => p.assistsCount > 0)
    .sort((a, b) => b.assistsCount - a.assistsCount)
    .slice(0, 8);

  const maxAssists = topAssists.length > 0 ? topAssists[0].assistsCount : 1;

  const getTeamName = (id: string) => teams.find(t => t.id === id)?.name || 'Cầu thủ Tự do';
  const getTeamColor = (id: string) => teams.find(t => t.id === id)?.logoColor || 'from-slate-505 to-slate-705';

  // Club Record holders in current active league
  const bestAttack = leagueTeams.length > 0 ? [...leagueTeams].sort((a,b) => b.goalsFor - a.goalsFor)[0] : null;
  const bestDefense = leagueTeams.length > 0 ? [...leagueTeams].sort((a,b) => a.goalsAgainst - b.goalsAgainst)[0] : null;
  const longestStreak = leagueTeams.length > 0 ? [...leagueTeams].sort((a,b) => {
    const aWins = a.streak.filter(s => s === 'W').length;
    const bWins = b.streak.filter(s => s === 'W').length;
    return bWins - aWins;
  })[0] : null;

  return (
    <div id="stats-dashboard" className="space-y-8 animate-in fade-in duration-200">
      {/* Jumbotron Title */}
      <div className="bg-slate-900 border border-slate-800 p-5 rounded-2xl shadow-xl flex justify-between items-center bg-[radial-gradient(ellipse_at_top_right,_var(--tw-gradient-stops))] from-red-950/20 via-slate-900 to-slate-900">
        <div>
          <div className="flex items-center space-x-1 text-slate-450 font-mono text-[10px] uppercase font-extrabold tracking-wider">
            <Sparkles className="w-4 h-4 text-amber-500" />
            <span>Thống kê thăng hoa - {currentLeague.name}</span>
          </div>
          <h2 className="text-xl font-black text-white mt-1">Đường Đua Cá Nhân &amp; Tập Thể Xuất Sắc</h2>
          <p className="text-xs text-slate-450">
            Dữ liệu ghi nhận tự động dựa trên biên bản nộp ảnh minh chứng được BTC đóng dấu phê duyệt.
          </p>
        </div>
      </div>

      {/* Bento highlights for Best Performing Teams */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {/* Card 1: Best Attack */}
        <div className="bg-slate-900 border border-slate-800 p-5 rounded-2xl relative overflow-hidden shadow-lg flex flex-col justify-between group">
          <div className="absolute right-3 top-3 opacity-5 group-hover:scale-110 transition-transform duration-300">
            <Zap className="w-20 h-20 text-white" />
          </div>
          <div>
            <span className="text-[9px] font-mono font-bold text-red-500 uppercase tracking-widest bg-red-950 px-2 py-0.5 rounded">
              BÃI MÌN BẮN PHÁ (Attack)
            </span>
            <h4 className="font-extrabold text-sm text-slate-300 mt-3 uppercase font-mono">Tấn công mạnh nhất</h4>
          </div>

          {bestAttack ? (
            <div className="mt-4 flex items-center justify-between">
              <div>
                <p className="font-black text-white text-base truncate max-w-[150px]">{bestAttack.name}</p>
                <p className="text-[10px] font-mono text-slate-400 mt-0.5">Hiệu suất dội bom cực đại</p>
              </div>
              <div className="text-right">
                <span className="text-2xl font-black text-white font-mono">{bestAttack.goalsFor}</span>
                <span className="text-[10px] text-slate-500 block uppercase font-mono">Bàn thắng</span>
              </div>
            </div>
          ) : (
            <p className="text-xs text-slate-500 mt-4 font-mono">Chưa ghi nhận bàn thắng</p>
          )}
        </div>

        {/* Card 2: Safe custody / Best Defense */}
        <div className="bg-slate-900 border border-slate-800 p-5 rounded-2xl relative overflow-hidden shadow-lg flex flex-col justify-between group">
          <div className="absolute right-3 top-3 opacity-5 group-hover:scale-110 transition-transform duration-300">
            <Shield className="w-20 h-20 text-white" />
          </div>
          <div>
            <span className="text-[9px] font-mono font-bold text-indigo-400 uppercase tracking-widest bg-indigo-950 px-2 py-0.5 rounded">
              PHÁO ĐÀI THÉP (Defense)
            </span>
            <h4 className="font-extrabold text-sm text-slate-300 mt-3 uppercase font-mono">Hàng thủ bê tông</h4>
          </div>

          {bestDefense ? (
            <div className="mt-4 flex items-center justify-between">
              <div>
                <p className="font-black text-white text-base truncate max-w-[150px]">{bestDefense.name}</p>
                <p className="text-[10px] font-mono text-slate-400 mt-0.5">Số lần lưới rung ít nhất</p>
              </div>
              <div className="text-right">
                <span className="text-2xl font-black text-white font-mono">{bestDefense.goalsAgainst}</span>
                <span className="text-[10px] text-slate-500 block uppercase font-mono">Thủng lưới</span>
              </div>
            </div>
          ) : (
            <p className="text-xs text-slate-500 mt-4 font-mono">Chưa lọt lưới</p>
          )}
        </div>

        {/* Card 3: Longest series wins */}
        <div className="bg-slate-900 border border-slate-800 p-5 rounded-2xl relative overflow-hidden shadow-lg flex flex-col justify-between group">
          <div className="absolute right-3 top-3 opacity-5 group-hover:scale-110 transition-transform duration-300">
            <Trophy className="w-20 h-20 text-white" />
          </div>
          <div>
            <span className="text-[9px] font-mono font-bold text-emerald-400 uppercase tracking-widest bg-emerald-950 px-2 py-0.5 rounded">
              CHUỖI PHONG ĐỘ (Form)
            </span>
            <h4 className="font-extrabold text-sm text-slate-300 mt-3 uppercase font-mono">Chuỗi thắng bùng nổ</h4>
          </div>

          {longestStreak ? (
            <div className="mt-4 flex items-center justify-between">
              <div>
                <p className="font-black text-white text-base truncate max-w-[150px]">{longestStreak.name}</p>
                <p className="text-[10px] font-mono text-slate-440 mt-0.5">Chuỗi tháp chiến thắng của HLV</p>
              </div>
              <div className="text-right">
                <span className="text-2xl font-black text-emerald-400 font-mono">
                  {longestStreak.streak.filter(s => s === 'W').length} TR
                </span>
                <span className="text-[10px] text-slate-500 block uppercase font-mono">Thắng / 5 trận</span>
              </div>
            </div>
          ) : (
            <p className="text-xs text-slate-505 mt-4 font-mono">Chưa thi đấu</p>
          )}
        </div>
      </div>

      {/* Grid: Scorers comparison and Assisters comparison */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-8">
        {/* Top 8 Goalscorers */}
        <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 shadow-2xl space-y-4">
          <div className="flex items-center space-x-2 pb-3.5 border-b border-slate-800">
            <Target className="text-red-500 w-5 h-5 flex-shrink-0" />
            <h3 className="font-extrabold text-white text-sm uppercase tracking-wider font-mono">
              Vua Rồng Phá Lưới (Top Goalscorers)
            </h3>
          </div>

          <div className="space-y-4">
            {topScorers.length === 0 ? (
              <div className="text-center py-10 text-slate-500 font-mono">
                Chưa ghi nhận số bàn thắng nào phát sinh trong giải đấu này.
              </div>
            ) : (
              topScorers.map((p, idx) => {
                const percentage = Math.max(12, Math.floor((p.goalsCount / maxGoals) * 100));
                return (
                  <div key={p.id} className="space-y-1.5">
                    <div className="flex justify-between items-center text-xs">
                      <div className="flex items-center space-x-2.5 truncate">
                        <span className="w-5 h-5 bg-slate-950 font-black text-[10px] border border-slate-800 rounded-lg flex items-center justify-center text-slate-300 font-mono">
                          {idx + 1}
                        </span>
                        <div>
                          <p className="font-extrabold text-white">{p.name}</p>
                          <p className="text-[9px] text-slate-500 font-mono">{getTeamName(p.teamId)} ({p.position})</p>
                        </div>
                      </div>

                      <div className="flex items-center space-x-2 font-mono">
                        <span className="font-black text-white text-sm">{p.goalsCount}</span>
                        <span className="text-[10px] text-slate-500 font-medium font-serif">Bàn</span>
                      </div>
                    </div>

                    {/* Progress indicator */}
                    <div className="w-full bg-slate-950 h-2 rounded-full overflow-hidden border border-slate-850">
                      <div
                        style={{ width: `${percentage}%` }}
                        className="bg-gradient-to-r from-red-650 to-red-500 h-full rounded-full transition-all duration-500"
                      />
                    </div>
                  </div>
                );
              })
            )}
          </div>
        </div>

        {/* Top 8 playmakers assists */}
        <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 shadow-2xl space-y-4">
          <div className="flex items-center space-x-2 pb-3.5 border-b border-slate-800">
            <Star className="text-amber-500 w-5 h-5 flex-shrink-0 animate-spin-slow" />
            <h3 className="font-extrabold text-white text-sm uppercase tracking-wider font-mono">
              Thượng Thừa Kiến Tạo (Top Assists)
            </h3>
          </div>

          <div className="space-y-4">
            {topAssists.length === 0 ? (
              <div className="text-center py-10 text-slate-500 font-mono">
                Chưa ghi nhận đường kiến tạo bàn thắng nào được báo cáo.
              </div>
            ) : (
              topAssists.map((p, idx) => {
                const percentage = Math.max(12, Math.floor((p.assistsCount / maxAssists) * 100));
                return (
                  <div key={p.id} className="space-y-1.5">
                    <div className="flex justify-between items-center text-xs">
                      <div className="flex items-center space-x-2.5 truncate">
                        <span className="w-5 h-5 bg-slate-950 font-black text-[10px] border border-slate-800 rounded-lg flex items-center justify-center text-slate-350 font-mono">
                          {idx + 1}
                        </span>
                        <div>
                          <p className="font-extrabold text-white">{p.name}</p>
                          <p className="text-[9px] text-slate-500 font-mono">{getTeamName(p.teamId)} ({p.position})</p>
                        </div>
                      </div>

                      <div className="flex items-center space-x-2 font-mono">
                        <span className="font-black text-white text-sm">{p.assistsCount}</span>
                        <span className="text-[10px] text-slate-500 font-medium font-serif">K/T</span>
                      </div>
                    </div>

                    {/* Progress indicator */}
                    <div className="w-full bg-slate-950 h-2 rounded-full overflow-hidden border border-slate-850">
                      <div
                        style={{ width: `${percentage}%` }}
                        className="bg-gradient-to-r from-amber-600 to-amber-400 h-full rounded-full transition-all duration-500"
                      />
                    </div>
                  </div>
                );
              })
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
