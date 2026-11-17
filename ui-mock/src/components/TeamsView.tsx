import React, { useState } from 'react';
import { Team, Player, League, User } from '../types';
import { Shield, UserCircle, Star, Award, TrendingUp, DollarSign, Activity, Users, FileText } from 'lucide-react';

interface TeamsViewProps {
  currentUser: User | null;
  teams: Team[];
  players: Player[];
  leagues: League[];
  activeLeagueId: string;
}

export default function TeamsView({
  currentUser,
  teams,
  players,
  leagues,
  activeLeagueId,
}: TeamsViewProps) {
  // Find clubs for the active league
  const leagueTeams = teams.filter(t => t.leagueId === activeLeagueId);
  const [selectedTeamId, setSelectedTeamId] = useState<string>(
    leagueTeams.length > 0 ? leagueTeams[0].id : ''
  );

  // Fallback check if state has empty selected team
  const activeTeamId = selectedTeamId || (leagueTeams.length > 0 ? leagueTeams[0].id : '');
  const activeTeam = teams.find(t => t.id === activeTeamId);

  // Grab players of active team
  const teamPlayers = players.filter(p => p.teamId === activeTeamId);

  // Position colors
  const getPositionStyle = (pos: string) => {
    switch (pos) {
      case 'GK': return 'bg-yellow-950/80 text-yellow-500 border-yellow-805/30';
      case 'CB':
      case 'LB':
      case 'RB': return 'bg-blue-950/80 text-blue-400 border-blue-900/30';
      case 'DMF':
      case 'CMF':
      case 'AMF': return 'bg-emerald-950/80 text-emerald-400 border-emerald-900/30';
      default: return 'bg-rose-950/80 text-rose-450 border-rose-900/30'; // CF, LWF, RWF
    }
  };

  // OVR Badge color
  const getOvrColor = (ovr: number) => {
    if (ovr >= 90) return 'text-rose-500 bg-rose-950/20 shadow-rose-950/50 border-rose-800/40';
    if (ovr >= 85) return 'text-amber-500 bg-amber-950/20 border-amber-800/40';
    return 'text-teal-400 bg-teal-950/20 border-teal-800/40';
  };

  return (
    <div id="teams-view-wrapper" className="grid grid-cols-1 lg:grid-cols-4 gap-8 animate-in fade-in duration-200">
      {/* Left Column: Clubs Selector Directory (1 col) */}
      <div className="lg:col-span-1 space-y-4">
        <div className="bg-slate-900 border border-slate-800 rounded-2xl p-4 shadow-xl">
          <div className="flex items-center space-x-2 text-slate-400 font-mono text-[10px] uppercase font-bold tracking-wider mb-2">
            <Users className="w-3.5 h-3.5 text-red-500" />
            <span> Danh Sách Đội Bóng CLB</span>
          </div>
          <p className="text-xs text-slate-500 pb-2.5 mb-2.5 border-b border-slate-800 leading-tight">
            Chọn một CLB để tra cứu đội hình hiện tại và chỉ số nhân sự.
          </p>

          <div className="space-y-1.5">
            {leagueTeams.length === 0 ? (
              <p className="text-xs text-slate-500 font-mono text-center py-4">Chưa có đội</p>
            ) : (
              leagueTeams.map((t) => {
                const isSelected = t.id === activeTeamId;
                const squadSize = players.filter(p => p.teamId === t.id).length;
                return (
                  <button
                    key={t.id}
                    onClick={() => setSelectedTeamId(t.id)}
                    className={`w-full flex items-center justify-between p-3 rounded-xl transition-all border ${
                      isSelected
                        ? 'bg-red-600 border-red-500 text-white font-black hover:bg-red-600 shadow-md shadow-red-950/40'
                        : 'bg-slate-950/40 border-slate-850 hover:bg-slate-850 text-slate-300 hover:text-white'
                    }`}
                  >
                    <div className="flex items-center space-x-3 truncate">
                      <div className={`w-3.5 h-3.5 rounded bg-gradient-to-r ${isSelected ? 'from-white to-slate-200' : t.logoColor} flex-shrink-0`} />
                      <span className="text-xs font-bold truncate">{t.name}</span>
                    </div>
                    <span className={`text-[9px] font-mono font-black px-1.5 py-0.5 rounded ${
                      isSelected ? 'bg-red-950 text-red-400' : 'bg-slate-800 text-slate-400'
                    }`}>
                      {squadSize} C/T
                    </span>
                  </button>
                );
              })
            )}
          </div>
        </div>
      </div>

      {/* Right 3-columns: Club details, metrics & Players roster representation */}
      <div className="lg:col-span-3 space-y-6">
        {activeTeam ? (
          <>
            {/* Club Jumbotron Header card */}
            <div className={`relative overflow-hidden bg-gradient-to-r ${activeTeam.logoColor} border border-white/10 rounded-2xl p-6 sm:p-8 shadow-2xl text-white group`}>
              {/* Backlight layout */}
              <div className="absolute right-6 -bottom-6 opacity-10 group-hover:scale-110 transition-transform duration-500">
                <Shield className="w-48 h-48 text-white" />
              </div>

              <div className="flex flex-col md:flex-row md:items-center justify-between gap-6 relative">
                <div className="space-y-2">
                  <div className="inline-flex items-center space-x-2 px-2.5 py-1 bg-black/35 backdrop-blur-md rounded-full text-[10px] font-mono tracking-wider text-red-400 font-extrabold border border-white/5 uppercase">
                    🏆 PES 17 LEAGUE CONTENDER
                  </div>
                  <h3 className="text-2xl sm:text-3xl font-black tracking-tight">{activeTeam.name}</h3>

                  <div className="flex flex-wrap items-center gap-4 text-xs mt-1 text-slate-200">
                    <p className="flex items-center space-x-1">
                      <UserCircle className="w-4 h-4 text-slate-300" />
                      <span>Huấn Luyện Viên: <span className="font-bold underline">{activeTeam.coachName}</span></span>
                    </p>
                    <p className="font-mono bg-black/25 text-indigo-300 px-2.5 py-0.5 rounded-full text-[11px] border border-indigo-900/40">
                      ID Parsec: {activeTeam.parsecId}
                    </p>
                  </div>
                </div>

                {/* Micro stat block */}
                <div className="grid grid-cols-4 gap-3 bg-slate-950/35 backdrop-blur-md p-4 rounded-xl border border-white/5 text-center min-w-[240px]">
                  <div>
                    <p className="text-[10px] font-mono text-slate-350 leading-none">TRẬN</p>
                    <p className="text-base font-black font-mono mt-1 text-white">{activeTeam.matchesPlayed}</p>
                  </div>
                  <div>
                    <p className="text-[10px] font-mono text-slate-350 leading-none">THẮNG</p>
                    <p className="text-base font-black font-mono mt-1 text-emerald-400">{activeTeam.wins}</p>
                  </div>
                  <div>
                    <p className="text-[10px] font-mono text-slate-350 leading-none">H/S</p>
                    <p className="text-base font-black font-mono mt-1 text-slate-200">
                      {activeTeam.goalDifference > 0 ? `+${activeTeam.goalDifference}` : activeTeam.goalDifference}
                    </p>
                  </div>
                  <div>
                    <p className="text-[10px] font-mono text-red-400 leading-none font-bold">ĐIỂM</p>
                    <p className="text-base font-black font-mono mt-1 text-rose-500 animate-pulse">{activeTeam.points}</p>
                  </div>
                </div>
              </div>
            </div>

            {/* Players Squad Table */}
            <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 shadow-2xl">
              <div className="flex justify-between items-center mb-4 border-b border-slate-805 pb-3">
                <div className="flex items-center space-x-2">
                  <Star className="text-amber-500 w-5 h-5 flex-shrink-0" />
                  <h4 className="font-extrabold text-white text-sm uppercase tracking-wider font-mono">
                    Danh Sách Đăng Ký Đội Hình CLB ({teamPlayers.length})
                  </h4>
                </div>
                <span className="text-[10px] text-slate-400 font-mono">Xếp theo Chỉ số OVR</span>
              </div>

              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs">
                  <thead>
                    <tr className="border-b border-slate-800 text-slate-405 font-mono uppercase text-[9px] font-black">
                      <th className="py-2.5 px-3">Cầu Thần</th>
                      <th className="py-2.5 px-3">Vị Trí</th>
                      <th className="py-2.5 px-3">Quốc Tịch</th>
                      <th className="py-2.5 px-2 text-center w-16">OVR Rating</th>
                      <th className="py-2.5 px-2 text-center w-20">⚽ Bàn</th>
                      <th className="py-2.5 px-2 text-center w-20">👟 K/T</th>
                      <th className="py-2.5 px-3 text-right">Giá Thị Trường</th>
                      <th className="py-2.5 px-3 text-center">Trạng Thái</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/40 font-medium">
                    {teamPlayers.length === 0 ? (
                      <tr>
                        <td colSpan={8} className="py-8 text-center text-slate-500 font-mono">
                          Không có cầu thủ đăng ký cho CLB này hoặc toàn đội đã ra đi theo diện tự do thương mại.
                        </td>
                      </tr>
                    ) : (
                      // Sort by position & OVR
                      [...teamPlayers].sort((a,b) => b.ovr - a.ovr).map((p) => (
                        <tr key={p.id} className="hover:bg-slate-950/30 transition-all">
                          {/* Name */}
                          <td className="py-3 px-3">
                            <span className="text-slate-200 font-black tracking-wide text-xs sm:text-sm block">{p.name}</span>
                          </td>

                          {/* Position Badge */}
                          <td className="py-3 px-3">
                            <span className={`px-2 py-0.5 rounded text-[10px] font-black font-mono border ${getPositionStyle(p.position)}`}>
                              {p.position}
                            </span>
                          </td>

                          {/* Nationality */}
                          <td className="py-3 px-3 text-slate-400 font-serif">
                            <span>{p.nationality}</span>
                          </td>

                          {/* OVR indicator dial placeholder */}
                          <td className="py-3 px-2 text-center">
                            <span className={`font-mono font-black text-xs px-2.5 py-1 rounded inline-block border ${getOvrColor(p.ovr)}`}>
                              {p.ovr}
                            </span>
                          </td>

                          {/* Goals */}
                          <td className="py-3 px-2 text-center font-mono font-bold text-slate-300">
                            {p.goalsCount > 0 ? (
                              <span className="text-red-400 font-black">{p.goalsCount}</span>
                            ) : '-'}
                          </td>

                          {/* Assists */}
                          <td className="py-3 px-2 text-center font-mono font-bold text-slate-300">
                            {p.assistsCount > 0 ? (
                              <span className="text-amber-500 font-black">{p.assistsCount}</span>
                            ) : '-'}
                          </td>

                          {/* Price */}
                          <td className="py-3 px-3 text-right font-mono font-extrabold text-white">
                            {p.value.toLocaleString()} Tr Coin
                          </td>

                          {/* State of Player Transfer list */}
                          <td className="py-3 px-3 text-center">
                            {p.isTransferListed ? (
                              <span className="bg-indigo-950 text-indigo-400 border border-indigo-900/60 text-[9px] font-bold px-2 py-0.5 rounded font-mono">
                                ĐANG ĐĂNG BÁN
                              </span>
                            ) : (
                              <span className="bg-slate-950 text-slate-500 text-[9px] px-2 py-0.5 rounded font-mono">
                                KHÓA BÁN
                              </span>
                            )}
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          </>
        ) : (
          <div className="bg-slate-900 border border-slate-800 rounded-2xl p-10 text-center text-slate-500">
            Hãy chọn một Đội Bóng để theo dõi thành phần cầu thủ.
          </div>
        )}
      </div>
    </div>
  );
}
