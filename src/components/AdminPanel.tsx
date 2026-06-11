import React, { useState } from 'react';
import { League, Team, Player, Match, TransferRequest, GoalScorerRecord, AssistRecord } from '../types';
import { Shield, Plus, Trash2, Edit2, Check, ArrowRightLeft, Swords, Settings, AlertCircle, RefreshCw } from 'lucide-react';
import { generateRoundRobinSchedule } from '../utils/schedule';

interface AdminPanelProps {
  leagues: League[];
  teams: Team[];
  players: Player[];
  allMatches: Match[];
  transfers: TransferRequest[];
  onAddLeague: (l: Omit<League, 'id'>) => void;
  onAddTeam: (t: Omit<Team, 'id' | 'matchesPlayed' | 'wins' | 'draws' | 'losses' | 'goalsFor' | 'goalsAgainst' | 'goalDifference' | 'points' | 'streak'>) => void;
  onAddPlayer: (p: Omit<Player, 'id' | 'goalsCount' | 'assistsCount'>) => void;
  onDeletePlayer: (id: string) => void;
  onGenerateMatches: (leagueId: string, matches: Match[]) => void;
  onApproveMatch: (matchId: string) => void;
  onRejectMatch: (matchId: string) => void;
  activeLeagueId: string;
}

export default function AdminPanel({
  leagues,
  teams,
  players,
  allMatches,
  transfers,
  onAddLeague,
  onAddTeam,
  onAddPlayer,
  onDeletePlayer,
  onGenerateMatches,
  onApproveMatch,
  onRejectMatch,
  activeLeagueId,
}: AdminPanelProps) {
  const [adminTab, setAdminTab] = useState<'fixtures_control' | 'leagues' | 'teams' | 'players'>('fixtures_control');

  // Add League Form State
  const [leagueName, setLeagueName] = useState('');
  const [leagueSeason, setLeagueSeason] = useState('Mùa 1 - 2026');
  const [leagueStart, setLeagueStart] = useState('2026-06-12');
  const [leagueEnd, setLeagueEnd] = useState('2526-09-12');

  // Add Team Form State
  const [teamName, setTeamName] = useState('');
  const [teamLogoColor, setTeamLogoColor] = useState('from-red-650 to-red-850');
  const [teamCoachName, setTeamCoachName] = useState('');
  const [teamParsecId, setTeamParsecId] = useState('');

  // Add Player Form State
  const [playerName, setPlayerName] = useState('');
  const [playerNationality, setPlayerNationality] = useState('Vietnam');
  const [playerPosition, setPlayerPosition] = useState<'GK' | 'CB' | 'LB' | 'RB' | 'DMF' | 'CMF' | 'AMF' | 'LWF' | 'RWF' | 'CF'>('CF');
  const [playerOvr, setPlayerOvr] = useState<number>(80);
  const [playerValue, setPlayerValue] = useState<number>(15);
  const [playerTeamId, setPlayerTeamId] = useState('');

  const currentLeague = leagues.find(l => l.id === activeLeagueId) || leagues[0];
  const leagueTeams = teams.filter(t => t.leagueId === activeLeagueId);
  const leagueMatches = allMatches.filter(m => m.leagueId === activeLeagueId);

  const getTeamName = (id: string) => {
    if (id === 'free_agent') return 'Tự do';
    return teams.find(t => t.id === id)?.name || 'Cầu thủ Tự do';
  };

  // Filter pending reported matches
  const pendingMatches = allMatches.filter(m => m.status === 'pending_approval');

  // Trigger Automatic double Leg generation
  const handleGenerateClick = () => {
    if (leagueTeams.length < 2) {
      alert('Vui lòng tạo tối thiểu 2 đội bóng đăng ký ở giải này để hệ thống có thể tạo lịch thi đấu xoay vòng!');
      return;
    }
    const txt = `Bạn có chắc chắn muốn sinh lịch thi đấu song song (Lượt đi & Lượt về) tự động cho giải "${currentLeague.name}"?\nHành động này sẽ xóa các trận đấu chưa đấu cũ của giải này!`;
    if (window.confirm && !window.confirm(txt)) return;

    const teamIds = leagueTeams.map(t => t.id);
    const generated = generateRoundRobinSchedule(activeLeagueId, teamIds);
    onGenerateMatches(activeLeagueId, generated);
    alert(`Đã sinh tự động thành công ${generated.length} trận đấu (bao gồm lượt đi và lượt về vòng tròn tính điểm)!`);
  };

  return (
    <div id="admin-panel" className="space-y-8 animate-in fade-in duration-200">
      {/* Title block banner */}
      <div className="bg-slate-905 border border-slate-800 p-5 rounded-2xl shadow-xl flex items-center justify-between bg-zinc-950">
        <div className="flex items-center space-x-3.5">
          <div className="p-3 bg-red-600/10 text-red-500 rounded-xl border border-red-900/30">
            <Shield className="w-6 h-6" />
          </div>
          <div>
            <h2 className="text-xl font-black text-white uppercase tracking-wider font-mono">Bảng Điều Khiển Ban Tổ Chức (BTC)</h2>
            <p className="text-xs text-slate-450 mt-0.5">
              Cấp quyền tuyệt đối khởi tạo câu lạc bộ, đăng ký cầu thủ xuất chúng và quản lý phán quyết kết quả trận đấu.
            </p>
          </div>
        </div>
      </div>

      {/* Admin sub-menu router tabs */}
      <div className="flex flex-wrap border-b border-slate-800 gap-1 sm:gap-2">
        <button
          onClick={() => setAdminTab('fixtures_control')}
          className={`px-4 py-2 text-xs font-bold font-mono uppercase tracking-wide rounded-t-xl transition-all ${
            adminTab === 'fixtures_control' ? 'bg-red-650/15 text-red-500 border-t border-x border-slate-800 font-extrabold' : 'text-slate-400 hover:text-white'
          }`}
        >
          Duyệt Trận Đấu ({pendingMatches.length})
        </button>
        <button
          onClick={() => setAdminTab('leagues')}
          className={`px-4 py-2 text-xs font-bold font-mono uppercase tracking-wide rounded-t-xl transition-all ${
            adminTab === 'leagues' ? 'bg-red-650/15 text-red-500 border-t border-x border-slate-800 font-extrabold' : 'text-slate-400 hover:text-white'
          }`}
        >
          Quản Lý Giải Đấu ({leagues.length})
        </button>
        <button
          onClick={() => setAdminTab('teams')}
          className={`px-4 py-2 text-xs font-bold font-mono uppercase tracking-wide rounded-t-xl transition-all ${
            adminTab === 'teams' ? 'bg-red-650/15 text-red-500 border-t border-x border-slate-800 font-extrabold' : 'text-slate-400 hover:text-white'
          }`}
        >
          Quản Lý Đội Bóng ({teams.length})
        </button>
        <button
          onClick={() => setAdminTab('players')}
          className={`px-4 py-2 text-xs font-bold font-mono uppercase tracking-wide rounded-t-xl transition-all ${
            adminTab === 'players' ? 'bg-red-650/15 text-red-500 border-t border-x border-slate-800 font-extrabold' : 'text-slate-400 hover:text-white'
          }`}
        >
          Quản Lý Cầu Thủ ({players.length})
        </button>
      </div>

      {/***** Sub-tab 1: Duyệt kết quả reported *****/}
      {adminTab === 'fixtures_control' && (
        <div className="space-y-6">
          {/* Automated double robin leg trigger */}
          <div className="bg-slate-900 border border-slate-800 p-5 rounded-2xl flex flex-col md:flex-row md:items-center justify-between gap-4 shadow-lg">
            <div className="space-y-1">
              <span className="text-[10px] font-mono font-black text-amber-500 uppercase tracking-widest bg-amber-950 px-2 py-0.5 rounded">
                DÀNH CHO GIẢI HIỆN TẠI
              </span>
              <h3 className="text-base font-black text-white">{currentLeague?.name} ({currentLeague?.season})</h3>
              <p className="text-xs text-slate-400">
                Tự động tạo lịch thi đấu vòng tròn 2 lượt (Lượt đi - Lượt về) cho <b>{leagueTeams.length} đội bóng</b> đã thiết lập.
              </p>
            </div>

            <button
              onClick={handleGenerateClick}
              className="bg-red-600 hover:bg-red-500 text-white text-xs font-black px-5 py-2.5 rounded-xl flex items-center space-x-2 transition-transform hover:scale-[1.03] shadow-lg shadow-red-950/30"
            >
              <Swords className="w-4 h-4" />
              <span>TỰ ĐỘNG SINH LỊCH ĐẤU 2 LƯỢT TIÊU CHUẨN</span>
            </button>
          </div>

          {/* Pending scores list */}
          <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 shadow-xl">
            <h3 className="font-extrabold text-sm text-white uppercase tracking-wider font-mono mb-4 pb-1 border-b border-slate-805">
              Danh Sách Kết Quả Trận Đấu Chờ BTC Phê Duyệt ({pendingMatches.length})
            </h3>

            {pendingMatches.length === 0 ? (
              <div className="text-center py-10 text-slate-505 font-mono text-xs">
                Hiện tại không có báo cáo tỷ số nào từ HLV đang chờ đối soát.
              </div>
            ) : (
              <div className="space-y-4">
                {pendingMatches.map(m => {
                  const home = teams.find(t => t.id === m.homeTeamId);
                  const away = teams.find(t => t.id === m.awayTeamId);

                  return (
                    <div
                      key={m.id}
                      className="bg-slate-950/50 border border-amber-600/40 rounded-xl p-4 flex flex-col md:flex-row items-center justify-between gap-4 hover:border-amber-600 transition"
                    >
                      {/* Match presentation */}
                      <div className="flex-1 flex items-center justify-center space-x-4">
                        <span className="text-xs font-bold text-slate-350">{home?.name}</span>
                        <div className="bg-slate-900 border border-slate-800 px-3.5 py-1.5 rounded-lg text-white font-mono font-black text-sm">
                          {m.homeScore} - {m.awayScore}
                        </div>
                        <span className="text-xs font-bold text-slate-350">{away?.name}</span>
                      </div>

                      {/* Reporter badge */}
                      <div className="text-center md:text-left text-[11px] text-slate-400 font-mono">
                        <p>Người báo cáo: <span className="text-white font-bold">{m.reporterName}</span></p>
                        <p className="mt-0.5">Ngày chơi: {m.date || 'Chưa đăng ký'}</p>
                      </div>

                      {/* Evidence thumbnail */}
                      {m.evidenceImage && (
                        <div className="w-16 h-10 border border-slate-800 rounded overflow-hidden">
                          <img src={m.evidenceImage} alt="score evidence" className="w-full h-full object-cover" />
                        </div>
                      )}

                      {/* Decision buttons */}
                      <div className="flex gap-2">
                        <button
                          onClick={() => onRejectMatch(m.id)}
                          className="px-3 py-1.5 bg-red-950 border border-red-900 text-red-400 hover:bg-red-900 text-xs font-bold rounded-lg transition"
                        >
                          Từ Chối
                        </button>
                        <button
                          onClick={() => onApproveMatch(m.id)}
                          className="px-4 py-1.5 bg-emerald-600 hover:bg-emerald-500 text-white hover:text-white text-xs font-extrabold rounded-lg transition"
                        >
                          Phê Duyệt
                        </button>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        </div>
      )}

      {/***** Sub-tab 2: Quản lý League *****/}
      {adminTab === 'leagues' && (
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
          {/* Create form */}
          <div className="lg:col-span-1 bg-slate-900 border border-slate-800 p-5 rounded-2xl shadow-lg">
            <h3 className="font-extrabold text-sm text-white uppercase tracking-wider font-mono mb-4">
              Khởi tạo giải đấu mới
            </h3>

            <form
              onSubmit={(e) => {
                e.preventDefault();
                if (!leagueName) return;
                onAddLeague({ name: leagueName, season: leagueSeason, startDate: leagueStart, endDate: leagueEnd });
                setLeagueName('');
                alert('Đã khởi tạo giải mới thành công!');
              }}
              className="space-y-4 text-xs font-serif"
            >
              <div>
                <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                  Tên giải đấu
                </label>
                <input
                  type="text"
                  required
                  placeholder="Ví dụ: Serie A, Ligue 1..."
                  className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-2 text-sm font-sans focus:outline-none focus:border-red-500"
                  value={leagueName}
                  onChange={(e) => setLeagueName(e.target.value)}
                />
              </div>

              <div>
                <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                  Mùa giải
                </label>
                <input
                  type="text"
                  required
                  placeholder="Ví dụ: Mùa 1 - 2026"
                  className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-2 text-sm font-sans focus:outline-none focus:border-red-500"
                  value={leagueSeason}
                  onChange={(e) => setLeagueSeason(e.target.value)}
                />
              </div>

              <div className="grid grid-cols-2 gap-2">
                <div>
                  <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                    Ngày bắt đầu
                  </label>
                  <input
                    type="date"
                    className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg p-2 text-xs focus:outline-none focus:border-red-500 font-mono"
                    value={leagueStart}
                    onChange={(e) => setLeagueStart(e.target.value)}
                  />
                </div>
                <div>
                  <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                    Ngày kết thúc
                  </label>
                  <input
                    type="date"
                    className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg p-2 text-xs focus:outline-none focus:border-red-500 font-mono"
                    value={leagueEnd}
                    onChange={(e) => setLeagueEnd(e.target.value)}
                  />
                </div>
              </div>

              <button
                type="submit"
                className="w-full bg-indigo-600 hover:bg-indigo-500 text-white font-extrabold tracking-wide py-2 rounded-xl border border-indigo-500 text-xs font-sans mt-2"
              >
                Tạo giải đấu &raquo;
              </button>
            </form>
          </div>

          {/* List display */}
          <div className="lg:col-span-2 bg-slate-900 border border-slate-800 p-5 rounded-2xl shadow-lg">
            <h3 className="font-extrabold text-sm text-white uppercase tracking-wider font-mono mb-4 pb-1 border-b border-slate-805">
              Danh Sách Các Giải Đấu Đã Tạo
            </h3>

            <div className="space-y-2.5">
              {leagues.map(l => (
                <div
                  key={l.id}
                  className="bg-slate-950/45 p-3 rounded-xl border border-slate-850 flex justify-between items-center text-xs text-slate-350"
                >
                  <div>
                    <h4 className="font-black text-white text-sm">{l.name}</h4>
                    <p className="font-mono text-slate-500 text-[10px] mt-0.5">Season: {l.season} | Kéo dài {l.startDate} &raquo; {l.endDate}</p>
                  </div>

                  <span className="bg-red-950 text-red-400 border border-red-900 px-2 py-1 rounded text-[10px] font-mono font-bold">
                    ACTIVE
                  </span>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/***** Sub-tab 3: Quản lý Đội bóng *****/}
      {adminTab === 'teams' && (
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
          {/* Create form */}
          <div className="lg:col-span-1 bg-slate-900 border border-slate-800 p-5 rounded-2xl shadow-lg">
            <h3 className="font-extrabold text-sm text-white uppercase tracking-wider font-mono mb-4">
              Đăng Ký Đội Bóng Mới
            </h3>

            <form
              onSubmit={(e) => {
                e.preventDefault();
                if (!teamName || !teamCoachName) return;
                onAddTeam({
                  name: teamName,
                  logoColor: teamLogoColor,
                  leagueId: activeLeagueId,
                  coachId: `hlv_custom_${Date.now()}`,
                  coachName: teamCoachName,
                  parsecId: teamParsecId || 'Tự do',
                });
                setTeamName('');
                setTeamCoachName('');
                setTeamParsecId('');
                alert(`Đã đăng ký thành công đội bóng "${teamName}" vào giải hiện tại!`);
              }}
              className="space-y-4 text-xs font-serif"
            >
              <div>
                <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                  Đăng ký cho giải:
                </label>
                <div className="bg-slate-950 p-2.5 rounded border border-slate-800 text-slate-300 font-bold font-sans">
                  {currentLeague?.name}
                </div>
              </div>

              <div>
                <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                  Tên Câu Lạc Bộ
                </label>
                <input
                  type="text"
                  required
                  placeholder="Ví dụ: Real Madrid, Juventus FC"
                  className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-2 text-sm font-sans focus:outline-none focus:border-red-500"
                  value={teamName}
                  onChange={(e) => setTeamName(e.target.value)}
                />
              </div>

              <div>
                <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                  Màu sắc gradient (Giao diện)
                </label>
                <select
                  value={teamLogoColor}
                  onChange={(e) => setTeamLogoColor(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-2 text-sm font-mono focus:outline-none focus:border-red-500"
                >
                  <option value="from-red-600 to-red-800">Gradient Đỏ (MU/Ronaldo)</option>
                  <option value="from-blue-600 to-blue-800">Gradient Xanh dương (Chelsea)</option>
                  <option value="from-sky-400 to-sky-600">Gradient Sky (Manchester City)</option>
                  <option value="from-blue-800 to-red-700">Gradient Đỏ Xanh (Barca)</option>
                  <option value="from-zinc-100 to-zinc-300">Gradient Trắng bạc (Real Madrid)</option>
                  <option value="from-yellow-500 to-yellow-700">Gradient Vàng đen (BVB)</option>
                </select>
              </div>

              <div>
                <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                  Họ tên Huấn Luyện Viên
                </label>
                <input
                  type="text"
                  required
                  placeholder="Ví dụ: Phạm Quốc Thái, Hoang Nam"
                  className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-2 text-sm font-sans focus:outline-none focus:border-red-500"
                  value={teamCoachName}
                  onChange={(e) => setTeamCoachName(e.target.value)}
                />
              </div>

              <div>
                <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                  Địa Chỉ Kết Nối Parsec ID
                </label>
                <input
                  type="text"
                  placeholder="Ví dụ: QuocThai#1102"
                  className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-2 text-sm font-mono focus:outline-none focus:border-red-500"
                  value={teamParsecId}
                  onChange={(e) => setTeamParsecId(e.target.value)}
                />
              </div>

              <button
                type="submit"
                className="w-full bg-rose-600 hover:bg-rose-500 text-white font-extrabold tracking-wide py-2 rounded-xl border border-rose-500 text-xs font-sans mt-2"
              >
                Ghi Danh Câu Lạc Bộ &raquo;
              </button>
            </form>
          </div>

          {/* List display */}
          <div className="lg:col-span-2 bg-slate-900 border border-slate-800 p-5 rounded-2xl shadow-lg">
            <h3 className="font-extrabold text-sm text-white uppercase tracking-wider font-mono mb-4 pb-1 border-b border-slate-805">
              Danh Sách Đội Bóng thuộc {currentLeague?.name}
            </h3>

            <div className="space-y-3">
              {leagueTeams.length === 0 ? (
                <p className="text-xs text-slate-505 font-mono text-center py-6">Chưa có CLB nào trong giải này</p>
              ) : (
                leagueTeams.map(t => {
                  const squad = players.filter(p => p.teamId === t.id);
                  return (
                    <div
                      key={t.id}
                      className="bg-slate-950/45 p-3 rounded-xl border border-slate-850 flex justify-between items-center text-xs text-slate-350"
                    >
                      <div className="flex items-center space-x-3">
                        <div className={`w-3.5 h-3.5 rounded bg-gradient-to-r ${t.logoColor}`} />
                        <div>
                          <h4 className="font-black text-white text-sm">{t.name}</h4>
                          <p className="font-mono text-slate-500 text-[10px] mt-0.5">HLV: {t.coachName} | Parsec: {t.parsecId}</p>
                        </div>
                      </div>

                      <span className="bg-slate-800 text-slate-300 font-mono text-[10px] px-2.5 py-1 rounded font-bold">
                        {squad.length} Cầu thủ đăng kí
                      </span>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        </div>
      )}

      {/***** Sub-tab 4: Quản lý Cầu thủ *****/}
      {adminTab === 'players' && (
        <div className="grid grid-cols-1 lg:grid-cols-4 gap-8">
          {/* Create form */}
          <div className="lg:col-span-1 bg-slate-900 border border-slate-800 p-5 rounded-2xl shadow-lg">
            <h3 className="font-extrabold text-sm text-white uppercase tracking-wider font-mono mb-4">
              Sinh Cầu Thủ Siêu Nhật
            </h3>

            <form
              onSubmit={(e) => {
                e.preventDefault();
                if (!playerName) return;
                onAddPlayer({
                  name: playerName,
                  nationality: playerNationality,
                  position: playerPosition,
                  ovr: playerOvr,
                  value: playerValue,
                  teamId: playerTeamId || 'free_agent',
                  isTransferListed: false,
                });
                setPlayerName('');
                alert(`Đã khởi tạo cầu thủ "${playerName}" thành công!`);
              }}
              className="space-y-4 text-xs font-serif"
            >
              <div>
                <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                  Họ Tên Cầu Thủ
                </label>
                <input
                  type="text"
                  required
                  placeholder="Ví dụ: Antoine Griezmann"
                  className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-2.5 py-1.5 focus:outline-none focus:border-red-500"
                  value={playerName}
                  onChange={(e) => setPlayerName(e.target.value)}
                />
              </div>

              <div>
                <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                  Quốc tích
                </label>
                <input
                  type="text"
                  required
                  className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-2.5 py-1.5 focus:outline-none focus:border-red-500"
                  value={playerNationality}
                  onChange={(e) => setPlayerNationality(e.target.value)}
                />
              </div>

              <div className="grid grid-cols-2 gap-2 font-mono">
                <div>
                  <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                    Vị Trí (POS)
                  </label>
                  <select
                    value={playerPosition}
                    onChange={(e) => setPlayerPosition(e.target.value as any)}
                    className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-1.5 py-1.5 focus:outline-none focus:border-red-500 text-xs"
                  >
                    <option value="CF">CF</option>
                    <option value="LWF">LWF</option>
                    <option value="RWF">RWF</option>
                    <option value="AMF">AMF</option>
                    <option value="CMF">CMF</option>
                    <option value="DMF">DMF</option>
                    <option value="CB">CB</option>
                    <option value="GK">GK</option>
                  </select>
                </div>

                <div>
                  <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                    Chỉ số OVR
                  </label>
                  <input
                    type="number"
                    min={40}
                    max={99}
                    className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-1.5 py-1.5 focus:outline-none focus:border-red-500 text-xs text-center"
                    value={playerOvr}
                    onChange={(e) => setPlayerOvr(parseInt(e.target.value) || 80)}
                  />
                </div>
              </div>

              <div className="grid grid-cols-2 gap-2 font-mono">
                <div>
                  <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                    Giá trị (Tr Coin)
                  </label>
                  <input
                    type="number"
                    min={1}
                    className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-1.5 py-1.5 focus:outline-none focus:border-red-500 text-xs text-center"
                    value={playerValue}
                    onChange={(e) => setPlayerValue(parseInt(e.target.value) || 12)}
                  />
                </div>

                <div>
                  <label className="block text-[10px] font-mono text-slate-400 uppercase font-bold mb-1">
                    Đầu quân CLB
                  </label>
                  <select
                    value={playerTeamId}
                    onChange={(e) => setPlayerTeamId(e.target.value)}
                    className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-1.5 py-1.5 focus:outline-none focus:border-red-500 text-xs"
                  >
                    <option value="">Tự do (Free Agent)</option>
                    {teams.map(t => (
                      <option key={t.id} value={t.id}>{t.name}</option>
                    ))}
                  </select>
                </div>
              </div>

              <button
                type="submit"
                className="w-full bg-indigo-600 hover:bg-indigo-500 text-white font-extrabold tracking-wide py-2 rounded-xl border border-indigo-500 text-xs font-sans mt-2"
              >
                Gia Nhập PES League &raquo;
              </button>
            </form>
          </div>

          {/* List display */}
          <div className="lg:col-span-3 bg-slate-900 border border-slate-800 p-5 rounded-2xl shadow-lg">
            <h3 className="font-extrabold text-sm text-white uppercase tracking-wider font-mono mb-4 pb-1 border-b border-slate-805">
              Tổng Quản Danh Sách Cầu Thủ Sân Cỏ ({players.length})
            </h3>

            <div className="overflow-x-auto max-h-[460px] overflow-y-auto">
              <table className="w-full text-left text-xs font-sans">
                <thead>
                  <tr className="border-b border-slate-800 text-slate-500 font-mono uppercase text-[9px] font-bold">
                    <th className="py-2.5 px-3">Họ và Tên</th>
                    <th className="py-2.5 px-3 text-center">Vị Trí</th>
                    <th className="py-2.5 px-3">Quốc Tịch</th>
                    <th className="py-2.5 px-2 text-center">OVR</th>
                    <th className="py-2.5 px-3">CLB Đứng Tên</th>
                    <th className="py-2.5 px-3 text-right">Lương Thượng Thưởng</th>
                    <th className="py-2.5 px-3 text-center">Xóa</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/40 text-slate-350">
                  {players.map(p => (
                    <tr key={p.id} className="hover:bg-slate-950/25 transition">
                      <td className="py-2.5 px-3 font-semibold text-white">{p.name}</td>
                      <td className="py-2.5 px-3 text-center font-mono font-bold text-red-400">{p.position}</td>
                      <td className="py-2.5 px-3 font-serif">{p.nationality}</td>
                      <td className="py-2.5 px-2 text-center font-mono text-amber-500 font-bold">{p.ovr}</td>
                      <td className="py-2.5 px-3 truncate max-w-[140px] font-semibold">{getTeamName(p.teamId)}</td>
                      <td className="py-2.5 px-3 text-right font-mono font-bold">{p.value.toLocaleString()} Tr Coin</td>
                      <td className="py-2.5 px-3 text-center">
                        <button
                          onClick={() => {
                            if (window.confirm && window.confirm(`Bạn có chắc chắn muốn xóa cầu thủ ${p.name}?`)) {
                              onDeletePlayer(p.id);
                            }
                          }}
                          className="p-1 bg-red-950 hover:bg-red-900 border border-red-900/40 text-red-400 rounded transition"
                          title="Xóa cầu thủ"
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
