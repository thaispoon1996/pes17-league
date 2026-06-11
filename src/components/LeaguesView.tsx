import React, { useState } from 'react';
import { Match, Team, League, Player, User, GoalScorerRecord, AssistRecord } from '../types';
import { Trophy, Calendar, Swords, UploadCloud, Users, ArrowUpRight, Camera, Check, ShieldCheck, HelpCircle, History } from 'lucide-react';

interface LeaguesViewProps {
  currentUser: User | null;
  leagues: League[];
  activeLeagueId: string;
  setActiveLeagueId: (id: string) => void;
  teams: Team[];
  allMatches: Match[];
  players: Player[];
  onReportMatch: (matchId: string, homeScore: number, awayScore: number, evidenceBase64: string, scorers: GoalScorerRecord[], assists: AssistRecord[]) => void;
  onUpdateMatchDate: (matchId: string, newDate: string) => void;
  onApproveMatch: (matchId: string) => void;
}

export default function LeaguesView({
  currentUser,
  leagues,
  activeLeagueId,
  setActiveLeagueId,
  teams,
  allMatches,
  players,
  onReportMatch,
  onUpdateMatchDate,
  onApproveMatch,
}: LeaguesViewProps) {
  const [activeSubTab, setActiveSubTab] = useState<'standings' | 'scheduler'>('standings');
  const [selectedRound, setSelectedRound] = useState<number>(1);
  const [scheduleFilter, setScheduleFilter] = useState<'all' | 'scheduled' | 'pending_approval' | 'completed'>('all');

  // Reporting state
  const [reportingMatch, setReportingMatch] = useState<Match | null>(null);
  const [homeScore, setHomeScore] = useState<number>(0);
  const [awayScore, setAwayScore] = useState<number>(0);
  const [evidenceStr, setEvidenceStr] = useState<string>('');
  const [selectedScorerId, setSelectedScorerId] = useState<string>('');
  const [selectedScorerGoals, setSelectedScorerGoals] = useState<number>(1);
  const [scorersList, setScorersList] = useState<GoalScorerRecord[]>([]);
  const [selectedAssistId, setSelectedAssistId] = useState<string>('');
  const [selectedAssistCount, setSelectedAssistCount] = useState<number>(1);
  const [assistsList, setAssistsList] = useState<AssistRecord[]>([]);
  const [ocrScanning, setOcrScanning] = useState<boolean>(false);

  // Match DateTime edit state
  const [reschedulingMatch, setReschedulingMatch] = useState<Match | null>(null);
  const [newMatchTime, setNewMatchTime] = useState<string>('');

  // Selected tournament match detail
  const [inspectingMatch, setInspectingMatch] = useState<Match | null>(null);

  // Grab data
  const currentLeague = leagues.find(l => l.id === activeLeagueId) || leagues[0];
  const leagueTeams = teams.filter(t => t.leagueId === activeLeagueId);
  const leagueMatches = allMatches.filter(m => m.leagueId === activeLeagueId);

  // Maximum rounds
  const maxRound = leagueMatches.length > 0 ? Math.max(...leagueMatches.map(m => m.round)) : 1;
  const roundsArray = Array.from({ length: maxRound }, (_, i) => i + 1);

  // Match list filter processing
  const filteredMatches = leagueMatches.filter(m => {
    const roundMatch = m.round === selectedRound;
    if (scheduleFilter === 'all') return roundMatch;
    return roundMatch && m.status === scheduleFilter;
  });

  const getTeamName = (id: string) => teams.find(t => t.id === id)?.name || 'Unknown CLB';
  const getTeamColor = (id: string) => teams.find(t => t.id === id)?.logoColor || 'from-slate-500 to-slate-700';
  const getTeamParsec = (id: string) => teams.find(t => t.id === id)?.parsecId || 'Chưa liên kết';
  const getPlayerName = (id: string) => players.find(p => p.id === id)?.name || 'Cầu thủ';

  // Handle Drag-and-drop / manual photo simulation
  const handlePhotoUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      const reader = new FileReader();
      reader.onloadend = () => {
        setEvidenceStr(reader.result as string);
      };
      reader.readAsDataURL(file);
    }
  };

  // Simulating high-fidelity AI OCR scan from mock images
  const triggerAIOcrScan = () => {
    if (!evidenceStr) {
      alert('Vui lòng kéo thả hoặc chọn một ảnh minh chứng tỷ số trước!');
      return;
    }
    setOcrScanning(true);
    // Simulating quick process
    setTimeout(() => {
      // Randomized realistic match results for PES 17
      const randomHome = Math.floor(Math.random() * 4);
      const randomAway = Math.floor(Math.random() * 3);
      setHomeScore(randomHome);
      setAwayScore(randomAway);
      setOcrScanning(false);
    }, 1500);
  };

  // Quick preset loader to skip manual upload in iframe
  const loadPresetScreenshot = (presetName: string) => {
    // Standard high contrast sport screenshot visual placeholders
    const mockupBase64Placeholder = "data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='400' height='250' viewBox='0 0 400 250'><rect width='400' height='250' fill='%230b0f19'/><text x='50%25' y='40%25' dominant-baseline='middle' text-anchor='middle' fill='%23dc2626' font-family='sans-serif' font-weight='black' font-size='24'>PES 17 ONLINE LEAGUE</text><text x='50%25' y='60%25' dominant-baseline='middle' text-anchor='middle' fill='%23ffffff' font-family='monospace' font-size='16'>MATCH EVIDENCE CAPTURED</text><text x='50%25' y='75%25' dominant-baseline='middle' text-anchor='middle' fill='%2364748b' font-family='monospace' font-size='11'>PROCESSED VIA PARSEC API MULTIPLAYER</text></svg>";
    setEvidenceStr(mockupBase64Placeholder);
  };

  // Scorers handling
  const handleAddScorer = () => {
    if (!selectedScorerId) return;
    const player = players.find(p => p.id === selectedScorerId);
    if (!player) return;

    const existingIdx = scorersList.findIndex(s => s.playerId === selectedScorerId);
    if (existingIdx > -1) {
      const updated = [...scorersList];
      updated[existingIdx].goals += selectedScorerGoals;
      setScorersList(updated);
    } else {
      setScorersList([...scorersList, { playerId: player.id, name: player.name, goals: selectedScorerGoals }]);
    }
    setSelectedScorerId('');
  };

  // Assists handling
  const handleAddAssist = () => {
    if (!selectedAssistId) return;
    const player = players.find(p => p.id === selectedAssistId);
    if (!player) return;

    const existingIdx = assistsList.findIndex(a => a.playerId === selectedAssistId);
    if (existingIdx > -1) {
      const updated = [...assistsList];
      updated[existingIdx].assists += selectedAssistCount;
      setAssistsList(updated);
    } else {
      setAssistsList([...assistsList, { playerId: player.id, name: player.name, assists: selectedAssistCount }]);
    }
    setSelectedAssistId('');
  };

  // Submit report to state callback
  const submitMatchReport = () => {
    if (!reportingMatch) return;
    if (!evidenceStr) {
      alert('Vui lòng upload ảnh chụp màn hình kết quả sau trận đấu để làm minh chứng hợp lệ!');
      return;
    }
    onReportMatch(reportingMatch.id, homeScore, awayScore, evidenceStr, scorersList, assistsList);
    // Reset states
    setReportingMatch(null);
    setHomeScore(0);
    setAwayScore(0);
    setEvidenceStr('');
    setScorersList([]);
    setAssistsList([]);
  };

  const handleSubTimeSubmit = () => {
    if (!reschedulingMatch || !newMatchTime) return;
    onUpdateMatchDate(reschedulingMatch.id, newMatchTime);
    setReschedulingMatch(null);
    setNewMatchTime('');
  };

  return (
    <div id="leagues-view-wrapper" className="space-y-6">
      {/* Page Title with responsive league select */}
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 bg-slate-900 border border-slate-800 p-5 rounded-2xl shadow-xl">
        <div>
          <h2 className="text-xl font-extrabold text-white flex items-center space-x-2">
            <Trophy className="text-amber-500 w-6 h-6" />
            <span>Thống Kê Giải Đấu PES 17</span>
          </h2>
          <p className="text-xs text-slate-400 font-medium">
            Quản lý vòng đấu lượt đi, lượt về, cập nhật tỷ số tự động và bảo mật kết quả.
          </p>
        </div>

        <div className="flex items-center space-x-2">
          <span className="text-xs text-slate-400 font-mono">Giải đấu:</span>
          <select
            value={activeLeagueId}
            onChange={(e) => setActiveLeagueId(e.target.value)}
            className="bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-1.5 text-xs font-bold focus:outline-none focus:border-red-500 cursor-pointer"
          >
            {leagues.map(l => (
              <option key={l.id} value={l.id}>{l.name} ({l.season})</option>
            ))}
          </select>
        </div>
      </div>

      {/* Tabs list: Bảng Xếp Hạng vs Lịch Thi Đấu & Kết Quả */}
      <div className="border-b border-slate-800 flex space-x-4">
        <button
          onClick={() => setActiveSubTab('standings')}
          className={`pb-3 text-sm font-bold tracking-wide transition-all uppercase ${
            activeSubTab === 'standings' ? 'text-red-500 border-b-2 border-red-500 font-black' : 'text-slate-450 hover:text-white'
          }`}
        >
          BẢNG XẾP HẠNG CLB
        </button>
        <button
          onClick={() => setActiveSubTab('scheduler')}
          className={`pb-3 text-sm font-bold tracking-wide transition-all uppercase ${
            activeSubTab === 'scheduler' ? 'text-red-500 border-b-2 border-red-500 font-black' : 'text-slate-450 hover:text-white'
          }`}
        >
          LỊCH TRÌNH VÀ KẾT QUẢ
        </button>
      </div>

      {/* SUB-TAB 1: Detailed Standings Table */}
      {activeSubTab === 'standings' && (
        <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 shadow-2xl overflow-hidden">
          <div className="flex items-center justify-between mb-4">
            <h3 className="font-extrabold text-white text-sm uppercase tracking-wider font-mono">
              Bảng xếp hạng tổng hợp - {currentLeague.name}
            </h3>
            <span className="text-slate-400 text-xs font-mono">Cập nhật tự động</span>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead>
                <tr className="border-b border-slate-800 text-slate-400 font-mono uppercase text-[10px] font-bold">
                  <th className="py-3 px-2 text-center w-8">Hạng</th>
                  <th className="py-3 px-3">Tên Đội Bóng (CLB)</th>
                  <th className="py-3 px-3">Huấn Luyện Viên</th>
                  <th className="py-3 px-2 text-center w-12">Trận</th>
                  <th className="py-3 px-2 text-center w-10">Thắng</th>
                  <th className="py-3 px-2 text-center w-10">Hòa</th>
                  <th className="py-3 px-2 text-center w-10">Thua</th>
                  <th className="py-3 px-2 text-center w-14">Bàn thắng</th>
                  <th className="py-3 px-2 text-center w-14">Bàn thua</th>
                  <th className="py-3 px-2 text-center w-12">H/S</th>
                  <th className="py-3 px-3 text-center w-14 text-amber-500 font-extrabold">Điểm</th>
                  <th className="py-3 px-3 text-center w-28 hidden sm:table-cell">5 Trận Gần Nhất</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60 font-medium">
                {leagueTeams.length === 0 ? (
                  <tr>
                    <td colSpan={12} className="text-center py-10 text-slate-500 font-mono">
                      Chưa có đội bóng đăng ký giải này. BTC có thể thêm đội bóng ở bảng điều khiển!
                    </td>
                  </tr>
                ) : (
                  leagueTeams.map((t, idx) => (
                    <tr
                      key={t.id}
                      className="hover:bg-slate-950/40 transition-colors"
                    >
                      <td className="py-3 px-2 text-center">
                        <span className={`w-5 h-5 rounded-full inline-flex items-center justify-center font-bold font-mono text-[10px] ${
                          idx === 0 ? 'bg-amber-600 text-white' : idx === 1 ? 'bg-slate-400 text-slate-950' : 'bg-slate-800 text-slate-450'
                        }`}>
                          {idx + 1}
                        </span>
                      </td>

                      <td className="py-3 px-3 font-extrabold text-white flex items-center space-x-2.5">
                        <div className={`w-3 h-3 rounded bg-gradient-to-r ${t.logoColor}`} />
                        <span>{t.name}</span>
                      </td>

                      <td className="py-3 px-3 text-slate-350 font-serif">
                        <div className="flex flex-col">
                          <span>{t.coachName}</span>
                          <span className="text-[10px] font-mono text-indigo-400">{t.parsecId}</span>
                        </div>
                      </td>

                      <td className="py-3 px-2 text-center font-mono text-slate-300">{t.matchesPlayed}</td>
                      <td className="py-3 px-2 text-center font-mono text-slate-405">{t.wins}</td>
                      <td className="py-3 px-2 text-center font-mono text-slate-405">{t.draws}</td>
                      <td className="py-3 px-2 text-center font-mono text-slate-405">{t.losses}</td>
                      <td className="py-3 px-2 text-center font-mono text-slate-400">{t.goalsFor}</td>
                      <td className="py-3 px-2 text-center font-mono text-slate-400">{t.goalsAgainst}</td>

                      <td className={`py-3 px-2 text-center font-bold font-mono ${t.goalDifference >= 0 ? 'text-emerald-500' : 'text-rose-500'}`}>
                        {t.goalDifference > 0 ? `+${t.goalDifference}` : t.goalDifference}
                      </td>

                      <td className="py-3 px-3 text-center text-sm font-black text-rose-500 font-mono bg-red-650/5">
                        {t.points}
                      </td>

                      <td className="py-3 px-3 text-center hidden sm:table-cell">
                        <div className="flex space-x-1 justify-center">
                          {t.streak.length === 0 ? (
                            <span className="text-[10px] text-slate-500 font-mono">Chưa thi đấu</span>
                          ) : (
                            t.streak.map((s, sIdx) => (
                              <span
                                key={sIdx}
                                className={`w-4 h-4 rounded text-[9px] font-black font-mono flex items-center justify-center ${
                                  s === 'W'
                                    ? 'bg-emerald-600 text-white'
                                    : s === 'D'
                                    ? 'bg-amber-600 text-white'
                                    : 'bg-red-800 text-white'
                                }`}
                              >
                                {s}
                              </span>
                            ))
                          )}
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* SUB-TAB 2: Match scheduler with filters & report scores */}
      {activeSubTab === 'scheduler' && (
        <div className="space-y-4">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 bg-slate-900 border border-slate-800 p-4 rounded-xl shadow">
            {/* Filter 1: Round selection */}
            <div>
              <label className="block text-[10px] font-mono font-bold text-slate-405 uppercase mb-1">
                Vòng thi đấu (Rounds)
              </label>
              <div className="flex flex-wrap gap-1.5">
                {roundsArray.map(r => (
                  <button
                    key={r}
                    onClick={() => setSelectedRound(r)}
                    className={`w-7 h-7 rounded text-xs font-bold transition-all font-mono ${
                      selectedRound === r
                        ? 'bg-red-600 text-white font-black scale-105'
                        : 'bg-slate-950 text-slate-400 hover:text-white hover:bg-slate-800'
                    }`}
                  >
                    {r}
                  </button>
                ))}
              </div>
            </div>

            {/* Filter 2: Leg type indicator */}
            <div className="flex items-center space-x-2">
              <span className="text-xs font-bold text-white uppercase font-mono">
                {selectedRound <= maxRound / 2 ? 'Lượt đi (Leg 1)' : 'Lượt về (Leg 2)'}
              </span>
              <span className="text-[11px] text-slate-500 mt-0.5">
                (Round {selectedRound} of {maxRound})
              </span>
            </div>

            {/* Filter 3: Match status filter */}
            <div>
              <label className="block text-[10px] font-mono font-bold text-slate-450 uppercase mb-1">
                Lọc trạng thái
              </label>
              <select
                value={scheduleFilter}
                onChange={(e) => setScheduleFilter(e.target.value as any)}
                className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-2.5 py-1.5 text-xs focus:outline-none focus:border-red-500"
              >
                <option value="all">Tất cả trận đấu</option>
                <option value="scheduled">Chưa diễn ra</option>
                <option value="pending_approval">Chờ duyệt kết quả</option>
                <option value="completed">Đã hoàn thành</option>
              </select>
            </div>
          </div>

          {/* Matches listing layout */}
          <div className="space-y-4">
            {filteredMatches.length === 0 ? (
              <div className="bg-slate-900 border border-slate-800 rounded-xl p-10 text-center text-slate-500">
                <Calendar className="w-10 h-10 text-slate-700 mx-auto mb-2" />
                <p className="text-xs font-bold text-slate-400">Không tìm thấy trận đấu nào cho vòng này theo bộ lọc.</p>
                <p className="text-[10px] text-slate-500 mt-1">Chọn bộ lọc khác hoặc sinh lịch đấu tự động dưới tư cách BTC.</p>
              </div>
            ) : (
              filteredMatches.map(m => {
                const homeTeam = teams.find(t => t.id === m.homeTeamId);
                const awayTeam = teams.find(t => t.id === m.awayTeamId);

                // Check permissions: A coach can only edit/report if they manage group of these teams!
                const isMyMatch = currentUser && currentUser.role === 'coach' &&
                  (currentUser.teamId === m.homeTeamId || currentUser.teamId === m.awayTeamId);
                const isBTC = currentUser && currentUser.role === 'admin';

                return (
                  <div
                    key={m.id}
                    className={`bg-slate-900 border ${
                      m.status === 'completed'
                        ? 'border-slate-800'
                        : m.status === 'pending_approval'
                        ? 'border-amber-600/60 shadow-lg shadow-amber-950/10'
                        : 'border-slate-850'
                    } rounded-xl p-4 transition-all relative`}
                  >
                    {/* Fixture header metadata */}
                    <div className="flex justify-between items-center mb-3">
                      <span className="text-[10px] font-mono text-slate-500">
                        {m.date ? `Lịch đấu: ${m.date}` : 'Thời gian: Chưa đăng ký/Tự do'}
                      </span>
                      <div className="flex items-center space-x-2">
                        {m.status === 'completed' && (
                          <span className="bg-emerald-950 text-emerald-400 border border-emerald-900/50 text-[9px] font-mono px-2 py-0.5 rounded font-extrabold uppercase">
                            ✓ Hoàn thành
                          </span>
                        )}
                        {m.status === 'pending_approval' && (
                          <span className="bg-amber-950 text-amber-500 border border-amber-900 text-[9px] font-mono px-2 py-0.5 rounded font-extrabold uppercase">
                            ⏳ Chờ BTC Duyệt
                          </span>
                        )}
                        {m.status === 'scheduled' && (
                          <span className="bg-slate-950 text-slate-400 border border-slate-800 text-[9px] font-mono px-2 py-0.5 rounded font-extrabold uppercase">
                            Chưa đá
                          </span>
                        )}
                      </div>
                    </div>

                    {/* Central Match Presentation */}
                    <div className="grid grid-cols-1 md:grid-cols-7 items-center gap-4 py-1">
                      {/* Home team column (3 cols) */}
                      <div className="md:col-span-3 flex items-center justify-end space-x-3 text-right">
                        <div>
                          <p className="font-extrabold text-white text-sm">{homeTeam?.name || 'Home'}</p>
                          <p className="text-[10px] text-slate-400 font-mono">Parsec: {homeTeam?.parsecId || 'Nhập sau'}</p>
                        </div>
                        <div className={`w-8 h-8 rounded-lg bg-gradient-to-r ${homeTeam?.logoColor} border border-slate-700 p-0.5 flex-shrink-0 flex items-center justify-center font-black text-white text-[10px] shadow`}>
                          {homeTeam?.name.slice(0, 2).toUpperCase()}
                        </div>
                      </div>

                      {/* Score or VS emblem (1 col) */}
                      <div className="md:col-span-1 text-center py-2 bg-slate-950/45 rounded-lg border border-slate-850/60 max-w-[140px] mx-auto w-full">
                        {m.status === 'scheduled' ? (
                          <span className="text-xs font-mono font-bold text-slate-500 tracking-wider">VS</span>
                        ) : (
                          <div className="flex items-center justify-center space-x-2 font-mono">
                            <span className="text-base font-black text-white">{m.homeScore}</span>
                            <span className="text-slate-600 font-black">-</span>
                            <span className="text-base font-black text-white">{m.awayScore}</span>
                          </div>
                        )}
                      </div>

                      {/* Away team column (3 cols) */}
                      <div className="md:col-span-3 flex items-center justify-start space-x-3 text-left">
                        <div className={`w-8 h-8 rounded-lg bg-gradient-to-r ${awayTeam?.logoColor} border border-slate-700 p-0.5 flex-shrink-0 flex items-center justify-center font-black text-white text-[10px] shadow`}>
                          {awayTeam?.name.slice(0, 2).toUpperCase()}
                        </div>
                        <div>
                          <p className="font-extrabold text-white text-sm">{awayTeam?.name || 'Away'}</p>
                          <p className="text-[10px] text-slate-400 font-mono">Parsec: {awayTeam?.parsecId || 'Nhập sau'}</p>
                        </div>
                      </div>
                    </div>

                    {/* Match stats & reported visual info if complete/pending */}
                    {(m.status === 'completed' || m.status === 'pending_approval') && (
                      <div className="mt-4 pt-3.5 border-t border-slate-800/80 grid grid-cols-1 md:grid-cols-2 gap-3 text-[11px] text-slate-400 font-mono">
                        <div>
                          {m.goalScorers && m.goalScorers.length > 0 && (
                            <p className="font-bold text-slate-350">
                              ⚽ Ghi bàn: <span className="font-normal text-slate-400">{m.goalScorers.map(s => `${s.name} (${s.goals})`).join(', ')}</span>
                            </p>
                          )}
                          {m.assists && m.assists.length > 0 && (
                            <p className="font-bold text-slate-350 mt-1">
                              👟 Kiến tạo: <span className="font-normal text-slate-400">{m.assists.map(a => `${a.name} (${a.assists})`).join(', ')}</span>
                            </p>
                          )}
                          <p className="text-[10px] text-slate-550 mt-1.5 flex items-center space-x-1">
                            <Check className="w-3 h-3 text-emerald-500" />
                            <span>Báo cáo bởi: {m.reporterName || 'Admin Ban Tổ Chức'}</span>
                          </p>
                        </div>

                        <div className="flex justify-end space-x-2">
                          {m.evidenceImage && (
                            <button
                              onClick={() => setInspectingMatch(m)}
                              className="text-xs bg-slate-850 hover:bg-slate-800 text-slate-300 font-serif font-bold px-3 py-1.5 rounded-lg flex items-center space-x-1.5 border border-slate-700/60"
                            >
                              <Camera className="w-3.5 h-3.5 text-red-500" />
                              <span>Xem Ảnh Minh Chứng</span>
                            </button>
                          )}
                        </div>
                      </div>
                    )}

                    {/* Action controls panel based on permission */}
                    <div className="mt-3 pt-3 border-t border-slate-850 flex justify-end gap-2 text-xs">
                      {/* Schedule datetime edit for involved Coach */}
                      {m.status === 'scheduled' && (isMyMatch || isBTC) && (
                        <button
                          onClick={() => {
                            setReschedulingMatch(m);
                            setNewMatchTime(m.date || '2026-06-15 20:00');
                          }}
                          className="px-3 py-1.5 bg-slate-800 hover:bg-slate-750 text-slate-350 hover:text-white rounded-lg Transition"
                        >
                          Đăng ký/Sửa lịch đấu
                        </button>
                      )}

                      {/* Reporting scores for involved Coach */}
                      {m.status === 'scheduled' && isMyMatch && (
                        <button
                          onClick={() => {
                            setReportingMatch(m);
                            setHomeScore(0);
                            setAwayScore(0);
                            setEvidenceStr('');
                            setScorersList([]);
                            setAssistsList([]);
                          }}
                          className="px-4 py-1.5 bg-red-600 hover:bg-red-500 text-white font-extrabold rounded-lg hover:shadow-lg transition-transform hover:scale-[1.02]"
                        >
                          Nhập tỷ số & Upload minh chứng
                        </button>
                      )}

                      {/* Quick notice if match scheduled and visitor */}
                      {m.status === 'scheduled' && !isMyMatch && (
                        <span className="text-[10px] text-slate-500 font-mono italic">
                          Chỉ HLV trong cuộc hoặc Admin có quyền nhập tỉ số.
                        </span>
                      )}

                      {/* BTC approval action directly here! */}
                      {m.status === 'pending_approval' && isBTC && (
                        <div className="flex space-x-2">
                          <button
                            onClick={() => setInspectingMatch(m)}
                            className="bg-slate-950 text-slate-300 border border-slate-850 px-3 py-1.5 rounded-lg hover:bg-slate-900 hover:text-white transition-colors flex items-center space-x-1"
                          >
                            <span>Xem Minh Chứng</span>
                          </button>
                          <button
                            onClick={() => onApproveMatch(m.id)}
                            className="bg-emerald-600 hover:bg-emerald-500 text-white font-bold px-4 py-1.5 rounded-lg shadow-lg shadow-emerald-950/20 text-xs flex items-center space-x-1 focus:ring-2 focus:ring-emerald-400"
                          >
                            <ShieldCheck className="w-3.5 h-3.5" />
                            <span>Xác nhận kết quả & Cộng điểm !</span>
                          </button>
                        </div>
                      )}
                    </div>
                  </div>
                );
              })
            )}
          </div>
        </div>
      )}

      {/* MODAL: Reporting scores & AI OCR screenshots */}
      {reportingMatch && (
        <div id="report-score-modal" className="fixed inset-0 bg-slate-950/85 backdrop-blur-md z-50 flex items-center justify-center p-4 overflow-y-auto">
          <div className="bg-slate-900 border border-slate-800 w-full max-w-2xl rounded-2xl p-6 shadow-2xl relative my-8 animate-in zoom-in-95 duration-200">
            <h3 className="text-lg font-bold text-white flex items-center space-x-2 border-b border-slate-800 pb-3">
              <UploadCloud className="text-red-500 animate-pulse w-5 h-5" />
              <span>Báo Cáo Tỷ Số Trận Đấu - PES 17</span>
            </h3>

            {/* Match info banner */}
            <div className="bg-slate-950/50 p-4 rounded-xl border border-slate-850 my-4 flex justify-between items-center text-center">
              <div className="w-5/12">
                <span className="text-slate-400 text-xs font-mono">Đội nhà (Home)</span>
                <p className="font-black text-white text-sm mt-0.5">{getTeamName(reportingMatch.homeTeamId)}</p>
              </div>
              <div className="w-2/12 font-bold font-mono text-red-500">VS</div>
              <div className="w-5/12">
                <span className="text-slate-400 text-xs font-mono">Đội khách (Away)</span>
                <p className="font-black text-white text-sm mt-0.5">{getTeamName(reportingMatch.awayTeamId)}</p>
              </div>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              {/* Left col: Scores input & Screen Upload  */}
              <div className="space-y-4">
                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <label className="block text-xs font-mono font-bold text-slate-350 uppercase mb-1">
                      Tỷ số nhà
                    </label>
                    <input
                      type="number"
                      min={0}
                      className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-red-500 font-mono font-bold text-center"
                      value={homeScore}
                      onChange={(e) => setHomeScore(Math.max(0, parseInt(e.target.value) || 0))}
                    />
                  </div>

                  <div>
                    <label className="block text-xs font-mono font-bold text-slate-350 uppercase mb-1">
                      Tỷ số khách
                    </label>
                    <input
                      type="number"
                      min={0}
                      className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-red-500 font-mono font-bold text-center"
                      value={awayScore}
                      onChange={(e) => setAwayScore(Math.max(0, parseInt(e.target.value) || 0))}
                    />
                  </div>
                </div>

                {/* Evidence Screenshot Area */}
                <div>
                  <label className="block text-xs font-mono font-bold text-slate-350 uppercase mb-2">
                    Ảnh rầm rộ minh chứng kết quả (Bắt buộc)
                  </label>

                  <div className="border border-dashed border-slate-800 hover:border-slate-700 bg-slate-950 rounded-xl p-4 text-center cursor-pointer relative group transition-colors">
                    <input
                      type="file"
                      accept="image/*"
                      className="absolute inset-0 opacity-0 cursor-pointer"
                      id="screenshot-uploader-input"
                      onChange={handlePhotoUpload}
                    />

                    {evidenceStr ? (
                      <div className="space-y-2">
                        <img src={evidenceStr} alt="Evidence crop preview" className="max-h-[140px] rounded-lg mx-auto object-contain border border-slate-850" />
                        <p className="text-[10px] text-emerald-400 font-mono">✓ Đã tải lênh ảnh minh chứng thành công</p>
                      </div>
                    ) : (
                      <div className="py-2 space-y-1">
                        <Camera className="w-8 h-8 text-slate-650 mx-auto" />
                        <p className="text-xs text-slate-300 font-bold">Kính chuyển hình ảnh tại đây</p>
                        <p className="text-[10px] text-slate-500 font-mono mt-0.5">Hỗ trợ JPG/PNG hoặc click chọn tệp chụp trực tiếp từ máy</p>
                      </div>
                    )}
                  </div>

                  {/* Simulated AI OCR tool from user settings */}
                  <div className="mt-3 bg-slate-950/60 p-3 rounded-lg border border-slate-850 flex flex-col gap-2">
                    <div className="flex items-center justify-between">
                      <span className="text-[10px] text-indigo-400 font-mono font-extrabold flex items-center space-x-1">
                        <span className="w-1.5 h-1.5 rounded-full bg-indigo-500 animate-pulse" />
                        <span>AI OCR SMART RECOGNITION</span>
                      </span>
                      <span className="bg-slate-800 text-[8px] text-slate-400 px-1 rounded uppercase">V2 PRO</span>
                    </div>
                    <p className="text-[10px] text-slate-400 leading-tight">
                      Bạn có thể click nút Thử Nghiệm Mockup PES 17 để tự động load ảnh ảo sau đó nhấn Tự Động Quét AI OCR để nhận thông tin tỷ số không cần gõ!
                    </p>

                    <div className="flex gap-2.5 mt-1.5">
                      <button
                        type="button"
                        onClick={() => loadPresetScreenshot('pes17-gameplay')}
                        className="text-[10px] bg-indigo-950 hover:bg-indigo-900 border border-indigo-800 text-indigo-400 px-2.5 py-1.5 rounded-lg font-mono font-bold"
                      >
                        [1] Load ảnh ảo
                      </button>
                      <button
                        type="button"
                        onClick={triggerAIOcrScan}
                        disabled={ocrScanning}
                        className="text-[10px] bg-red-600 hover:bg-red-500 text-white px-2.5 py-1.5 rounded-lg flex items-center space-x-1 font-extrabold flex-1 justify-center disabled:opacity-50"
                      >
                        {ocrScanning ? 'Đang quét tỉ số...' : 'Tự Động Quét AI OCR'}
                      </button>
                    </div>
                  </div>
                </div>
              </div>

              {/* Right col: Ghi bàn & Kiến tạo players */}
              <div className="space-y-4 border-t md:border-t-0 md:border-l border-slate-800/80 md:pl-5">
                {/* Score scorer register */}
                <div>
                  <h4 className="text-xs font-mono font-bold text-slate-350 uppercase mb-2">
                    ⚽ Đăng ký cầu thủ ghi bàn
                  </h4>
                  <div className="flex gap-1.5 items-center">
                    <select
                      value={selectedScorerId}
                      onChange={(e) => setSelectedScorerId(e.target.value)}
                      className="flex-1 bg-slate-950 border border-slate-800 text-white rounded-lg px-2.5 py-1.5 text-xs focus:outline-none focus:border-red-500"
                    >
                      <option value="">-- Chọn cầu thủ --</option>
                      <optgroup label={getTeamName(reportingMatch.homeTeamId)}>
                        {players.filter(p => p.teamId === reportingMatch.homeTeamId).map(p => (
                          <option key={p.id} value={p.id}>{p.name} ({p.position} - OVR {p.ovr})</option>
                        ))}
                      </optgroup>
                      <optgroup label={getTeamName(reportingMatch.awayTeamId)}>
                        {players.filter(p => p.teamId === reportingMatch.awayTeamId).map(p => (
                          <option key={p.id} value={p.id}>{p.name} ({p.position} - OVR {p.ovr})</option>
                        ))}
                      </optgroup>
                    </select>
                    <input
                      type="number"
                      min={1}
                      className="w-12 bg-slate-950 border border-slate-800 text-white rounded-lg px-1.5 py-1.5 text-xs font-mono text-center"
                      value={selectedScorerGoals}
                      onChange={(e) => setSelectedScorerGoals(Math.max(1, parseInt(e.target.value) || 1))}
                    />
                    <button
                      type="button"
                      onClick={handleAddScorer}
                      className="bg-slate-800 text-slate-200 px-3 py-1.5 rounded-lg text-xs hover:bg-slate-700 font-extrabold"
                    >
                      Thêm
                    </button>
                  </div>

                  {/* scorers list */}
                  <div className="mt-2.5 space-y-1 max-h-24 overflow-y-auto">
                    {scorersList.length === 0 ? (
                      <span className="text-[10px] text-slate-500 font-mono italic">Chưa nhập cầu thủ ghi bàn</span>
                    ) : (
                      scorersList.map(s => (
                        <div key={s.playerId} className="flex justify-between items-center text-xs bg-slate-950 px-2 py-1 rounded border border-slate-900 font-mono text-slate-300">
                          <span>{s.name}</span>
                          <span className="font-extrabold text-red-500">{s.goals} Bàn</span>
                        </div>
                      ))
                    )}
                  </div>
                </div>

                {/* Assist register */}
                <div>
                  <h4 className="text-xs font-mono font-bold text-slate-350 uppercase mb-2">
                    👟 Đăng ký cầu thủ kiến tạo
                  </h4>
                  <div className="flex gap-1.5 items-center">
                    <select
                      value={selectedAssistId}
                      onChange={(e) => setSelectedAssistId(e.target.value)}
                      className="flex-1 bg-slate-950 border border-slate-800 text-white rounded-lg px-2.5 py-1.5 text-xs focus:outline-none focus:border-red-500"
                    >
                      <option value="">-- Chọn cầu thủ --</option>
                      <optgroup label={getTeamName(reportingMatch.homeTeamId)}>
                        {players.filter(p => p.teamId === reportingMatch.homeTeamId).map(p => (
                          <option key={p.id} value={p.id}>{p.name} ({p.position} - OVR {p.ovr})</option>
                        ))}
                      </optgroup>
                      <optgroup label={getTeamName(reportingMatch.awayTeamId)}>
                        {players.filter(p => p.teamId === reportingMatch.awayTeamId).map(p => (
                          <option key={p.id} value={p.id}>{p.name} ({p.position} - OVR {p.ovr})</option>
                        ))}
                      </optgroup>
                    </select>
                    <input
                      type="number"
                      min={1}
                      className="w-12 bg-slate-950 border border-slate-800 text-white rounded-lg px-1.5 py-1.5 text-xs font-mono text-center"
                      value={selectedAssistCount}
                      onChange={(e) => setSelectedAssistCount(Math.max(1, parseInt(e.target.value) || 1))}
                    />
                    <button
                      type="button"
                      onClick={handleAddAssist}
                      className="bg-slate-800 text-slate-200 px-3 py-1.5 rounded-lg text-xs hover:bg-slate-700 font-extrabold"
                    >
                      Thêm
                    </button>
                  </div>

                  {/* assists tracker list */}
                  <div className="mt-2.5 space-y-1 max-h-24 overflow-y-auto">
                    {assistsList.length === 0 ? (
                      <span className="text-[10px] text-slate-500 font-mono italic">Chưa nhập cầu thủ kiến tạo</span>
                    ) : (
                      assistsList.map(a => (
                        <div key={a.playerId} className="flex justify-between items-center text-xs bg-slate-950 px-2 py-1 rounded border border-slate-900 font-mono text-slate-300">
                          <span>{a.name}</span>
                          <span className="font-extrabold text-amber-500">{a.assists} K/T</span>
                        </div>
                      ))
                    )}
                  </div>
                </div>
              </div>
            </div>

            {/* Modal actions footer */}
            <div className="flex justify-end space-x-3 pt-6 border-t border-slate-800 mt-4 text-xs">
              <button
                type="button"
                className="px-4 py-2 text-slate-400 hover:text-white rounded-lg transition"
                onClick={() => setReportingMatch(null)}
              >
                Hủy bỏ
              </button>
              <button
                type="button"
                onClick={submitMatchReport}
                className="px-6 py-2 bg-red-600 hover:bg-red-500 text-white font-extrabold rounded-lg shadow-lg shadow-red-900/30 font-bold"
              >
                Gửi Báo Cáo Cho BTC Duyệt
              </button>
            </div>
          </div>
        </div>
      )}

      {/* MODAL: Rescheduling Game dates */}
      {reschedulingMatch && (
        <div id="reschedule-modal" className="fixed inset-0 bg-slate-950/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-800 w-full max-w-sm rounded-2xl p-6 shadow-2xl animate-in zoom-in-95 duration-150">
            <h3 className="text-lg font-bold text-white flex items-center space-x-2">
              <Calendar className="text-red-500 w-5 h-5 animate-bounce" />
              <span>Đăng Ký Thời Gian Thi Đấu</span>
            </h3>
            <p className="text-xs text-slate-400 mt-2">
              Huấn luyện viên của hai bên thống nhất thời gian và điền vào ô dưới đây để Ban Tổ Chức cập nhật rộng rãi.
            </p>

            <div className="mt-4 space-y-4 text-xs">
              <div>
                <label className="block text-slate-350 font-mono font-bold mb-1">
                  Đội Nhà: {getTeamName(reschedulingMatch.homeTeamId)}
                </label>
                <div className="bg-slate-950 p-2 text-slate-400 font-mono rounded border border-slate-850">
                  Parsec ID: {getTeamParsec(reschedulingMatch.homeTeamId)}
                </div>
              </div>

              <div>
                <label className="block text-slate-350 font-mono font-bold mb-1">
                  Đội Khách: {getTeamName(reschedulingMatch.awayTeamId)}
                </label>
                <div className="bg-slate-950 p-2 text-slate-400 font-mono rounded border border-slate-850">
                  Parsec ID: {getTeamParsec(reschedulingMatch.awayTeamId)}
                </div>
              </div>

              <div>
                <label className="block text-slate-350 font-mono font-bold mb-1 uppercase tracking-wider">
                  Chọn ngày giờ chơi
                </label>
                <input
                  type="text"
                  placeholder="Ví dụ: 2026-06-15 21:00"
                  className="w-full bg-slate-950 border border-slate-850 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-red-500 font-mono"
                  value={newMatchTime}
                  onChange={(e) => setNewMatchTime(e.target.value)}
                />
              </div>

              <div className="flex justify-end space-x-3 pt-2">
                <button
                  type="button"
                  className="px-4 py-2 text-slate-400 hover:text-white rounded-lg transition"
                  onClick={() => setReschedulingMatch(null)}
                >
                  Hủy
                </button>
                <button
                  type="button"
                  onClick={handleSubTimeSubmit}
                  className="px-4 py-2 bg-red-600 hover:bg-red-500 text-white font-bold rounded-lg transition shadow-lg shadow-red-900/30"
                >
                  Lưu Lịch Lên Website
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* MODAL: Inspecting match result details and screenshot evidence */}
      {inspectingMatch && (
        <div id="inspector-modal" className="fixed inset-0 bg-slate-950/85 backdrop-blur-md z-40 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-800 w-full max-w-md rounded-2xl p-6 shadow-2xl relative animate-in zoom-in-95 duration-150">
            <h3 className="text-sm font-mono font-bold text-slate-400 uppercase tracking-wider mb-2 flex items-center space-x-2">
              <Camera className="text-red-500 w-4 h-4" />
              <span>Biên Bản Minh Chứng Trận Đấu</span>
            </h3>

            <div className="py-2.5 border-t border-b border-slate-800 flex justify-between items-center bg-slate-950/30 px-3 rounded-lg my-3">
              <span className="text-xs font-black text-white">{getTeamName(inspectingMatch.homeTeamId)}</span>
              <span className="font-mono font-black text-red-500 text-sm px-2.5">{inspectingMatch.homeScore} - {inspectingMatch.awayScore}</span>
              <span className="text-xs font-black text-white">{getTeamName(inspectingMatch.awayTeamId)}</span>
            </div>

            <div className="space-y-4">
              {/* Actual base64 or placeholder image */}
              <div className="bg-slate-950 border border-slate-850 p-2.5 rounded-xl">
                <img
                  src={inspectingMatch.evidenceImage}
                  alt="Match Scoreline Screenshot"
                  className="w-full rounded-lg max-h-[220px] object-cover"
                />
              </div>

              <div className="text-xs space-y-2 text-slate-400 font-mono">
                <p><span className="text-slate-200">Hiệp định:</span> Vòng {inspectingMatch.round} ({inspectingMatch.isReturn ? 'Lượt về' : 'Lượt đi'})</p>
                <p><span className="text-slate-200">Người đại diện gửi báo cáo:</span> {inspectingMatch.reporterName}</p>
                <p><span className="text-slate-200">Thời gian đá:</span> {inspectingMatch.date}</p>
                {inspectingMatch.status === 'pending_approval' && (
                  <div className="p-3 bg-amber-950/40 border border-amber-900/60 rounded-lg text-amber-500">
                    ⚠️ Trận đấu này đang ở trạng thái chờ duyệt. BTC đang đối soát OVR tỷ số trước khi cộng trực tiếp vào bảng vàng.
                  </div>
                )}
                {inspectingMatch.status === 'completed' && (
                  <div className="p-2.5 bg-emerald-950/40 border border-emerald-950/60 rounded-lg text-emerald-400 flex items-center space-x-1.5 font-bold">
                    <ShieldCheck className="w-4 h-4 flex-shrink-0 text-emerald-400" />
                    <span>Hồ sơ đã được BTC kí duyệt thành công.</span>
                  </div>
                )}
              </div>

              <div className="flex justify-end pt-3">
                <button
                  type="button"
                  className="px-4 py-2 bg-slate-800 hover:bg-slate-750 text-white rounded-lg transition text-xs font-bold"
                  onClick={() => setInspectingMatch(null)}
                >
                  Đóng cửa sổ
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
