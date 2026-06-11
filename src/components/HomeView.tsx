import { NewsItem, Match, Team, League } from '../types';
import { Trophy, Calendar, Sparkles, TrendingUp, ChevronRight, User, ShieldAlert, Newspaper, Ticket, PlaySquare } from 'lucide-react';

interface HomeViewProps {
  news: NewsItem[];
  allMatches: Match[];
  teams: Team[];
  leagues: League[];
  activeLeagueId: string;
  setActiveLeagueId: (leagueId: string) => void;
  setActiveTab: (tab: string) => void;
  onViewMatchDetail?: (match: Match) => void;
}

export default function HomeView({
  news,
  allMatches,
  teams,
  leagues,
  activeLeagueId,
  setActiveLeagueId,
  setActiveTab,
  onViewMatchDetail,
}: HomeViewProps) {
  // Get active league data
  const currentLeague = leagues.find(l => l.id === activeLeagueId) || leagues[0];
  const leagueTeams = teams.filter(t => t.leagueId === activeLeagueId);

  // Filter matches for current league
  const leagueMatches = allMatches.filter(m => m.leagueId === activeLeagueId);

  // Upcoming matches (scheduled status)
  const upcomingMatches = leagueMatches
    .filter(m => m.status === 'scheduled')
    .slice(0, 4);

  // Recent results (completed status, sorted descending by date/round)
  const recentResults = leagueMatches
    .filter(m => m.status === 'completed' || m.status === 'pending_approval')
    .slice(0, 4);

  // Featured main news article
  const featuredNews = news[0];
  const secondaryNews = news.slice(1, 3);

  // Find team details helper
  const getTeamName = (id: string) => teams.find(t => t.id === id)?.name || 'Unknown CLB';
  const getTeamColor = (id: string) => teams.find(t => t.id === id)?.logoColor || 'from-slate-500 to-slate-700';

  return (
    <div id="home-view-container" className="space-y-8 animate-in fade-in duration-200">
      {/* Dynamic League Switcher in Hero section */}
      <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 shadow-xl flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <div className="flex items-center space-x-2 text-red-500 font-bold text-xs uppercase tracking-wider font-mono">
            <span className="w-2 h-2 rounded-full bg-red-500 animate-ping"></span>
            <span>Giải Đấu Đang Khởi Tranh</span>
          </div>
          <h2 className="text-xl font-extrabold text-white mt-1">
            Được tài trợ bởi Parsec & Cộng đồng PES 17 Vietnam
          </h2>
          <p className="text-xs text-slate-400 mt-0.5">
            Chọn một giải đấu bên dưới để cập nhật bảng xếp hạng, lịch thi đấu và sự kiện chuyển nhượng thời gian thực.
          </p>
        </div>

        <div className="flex flex-wrap gap-2">
          {leagues.map(l => (
            <button
              key={l.id}
              onClick={() => setActiveLeagueId(l.id)}
              className={`px-4 py-1.5 rounded-lg text-xs font-bold transition-all ${
                activeLeagueId === l.id
                  ? 'bg-red-600 text-white shadow-lg shadow-red-900/30 font-extrabold'
                  : 'bg-slate-800 text-slate-300 hover:text-white hover:bg-slate-700'
              }`}
            >
              {l.name}
            </button>
          ))}
        </div>
      </div>

      {/* Grid: Featured Banner and Highlights */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
        {/* Main News Flash (Column 1 & 2) */}
        <div className="lg:col-span-2 space-y-6">
          <div className="relative h-[340px] rounded-2xl overflow-hidden group shadow-2xl">
            {/* Dark background and overlay */}
            <div className="absolute inset-0 bg-slate-950">
              <img
                src={featuredNews?.imageUrl || "https://images.unsplash.com/photo-1508098682722-e99c43a406b2?w=800&auto=format&fit=crop&q=80"}
                alt="Highlight Banner"
                className="w-full h-full object-cover opacity-50 group-hover:scale-105 transition-transform duration-700"
              />
            </div>
            <div className="absolute inset-0 bg-gradient-to-t from-slate-950 via-slate-950/40 to-transparent"></div>

            {/* Badges in Banner */}
            <div className="absolute top-4 left-4 flex gap-2">
              <span className="bg-red-600 text-white text-[10px] font-bold px-3 py-1 rounded-full uppercase tracking-widest shadow">
                TIN NÓNG
              </span>
              <span className="bg-slate-900/90 text-slate-300 backdrop-blur-md text-[10px] font-serif px-3 py-1 rounded-full font-semibold">
                {featuredNews?.date}
              </span>
            </div>

            {/* Content in Banner */}
            <div className="absolute bottom-0 inset-x-0 p-6 sm:p-8 space-y-2">
              <span className="text-red-500 text-xs font-mono font-bold uppercase tracking-wider">
                # {featuredNews?.category === 'transfer' ? 'Chuyển Nhượng' : 'Bình Luận League'}
              </span>
              <h3 className="text-xl sm:text-2xl font-black text-white hover:text-red-400 transition cursor-pointer leading-tight">
                {featuredNews?.title}
              </h3>
              <p className="text-slate-200 text-xs sm:text-sm max-w-2xl line-clamp-2">
                {featuredNews?.summary}
              </p>
            </div>
          </div>

          {/* Sub headlines */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {secondaryNews.map(item => (
              <div
                key={item.id}
                className="bg-slate-900/90 border border-slate-800 rounded-xl p-4 hover:border-slate-700 hover:bg-slate-850 transition relative group"
              >
                <div className="flex items-center justify-between mb-2">
                  <span className={`text-[10px] font-mono uppercase tracking-wider font-bold ${
                    item.category === 'transfer' ? 'text-indigo-400' : 'text-emerald-400'
                  }`}>
                    {item.category === 'transfer' ? 'Chuyển Nhượng' : 'Ấn Phẩm CLB'}
                  </span>
                  <span className="text-[10px] text-slate-500 font-mono">{item.date}</span>
                </div>
                <h4 className="font-extrabold text-sm text-white group-hover:text-red-400 transition line-clamp-1">
                  {item.title}
                </h4>
                <p className="text-xs text-slate-400 mt-1 line-clamp-2">
                  {item.summary}
                </p>
              </div>
            ))}
          </div>
        </div>

        {/* Mini Leaderboard (Column 3) */}
        <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 flex flex-col justify-between shadow-xl">
          <div>
            <div className="flex items-center justify-between mb-4">
              <div className="flex items-center space-x-2">
                <Trophy className="text-amber-500 w-5 h-5 flex-shrink-0" />
                <h3 className="font-extrabold text-sm text-white uppercase tracking-wider font-mono">
                  Bảng Xếp Hạng Tạm Thời
                </h3>
              </div>
              <button
                onClick={() => setActiveTab('leagues')}
                className="text-red-400 hover:text-red-300 text-xs font-semibold flex items-center space-x-0.5"
              >
                <span>Xem hết</span>
                <ChevronRight className="w-3.5 h-3.5" />
              </button>
            </div>

            <div className="text-slate-400 text-[11px] uppercase tracking-wider font-mono px-2 py-1 bg-slate-950/40 rounded-lg flex justify-between font-bold">
              <span>Đại Diện CLB</span>
              <div className="flex space-x-4 w-24 justify-end">
                <span className="w-6 text-center">Tr</span>
                <span className="w-6 text-center">H/S</span>
                <span className="w-8 text-center text-amber-500 font-bold">Đ</span>
              </div>
            </div>

            {/* Standings listing limited to first 4 */}
            <div className="space-y-2 mt-3">
              {leagueTeams.length === 0 ? (
                <div className="text-center py-6">
                  <ShieldAlert className="w-8 h-8 text-slate-600 mx-auto" />
                  <p className="text-slate-400 text-xs mt-2">Chưa có đội bóng đăng ký giải này</p>
                </div>
              ) : (
                leagueTeams.slice(0, 5).map((t, idx) => (
                  <div
                    key={t.id}
                    className="flex justify-between items-center text-xs p-2 rounded-lg bg-slate-950/20 border border-transparent hover:border-slate-850 hover:bg-slate-950/50 transition-colors"
                  >
                    <div className="flex items-center space-x-2.5 truncate">
                      <span className={`w-5 h-5 rounded-full flex items-center justify-center font-bold font-mono text-[10px] ${
                        idx === 0 ? 'bg-amber-600 text-white' : idx === 1 ? 'bg-slate-400 text-slate-950' : 'bg-slate-800 text-slate-400'
                      }`}>
                        {idx + 1}
                      </span>
                      <div className={`w-2.5 h-2.5 rounded bg-gradient-to-r ${t.logoColor}`} />
                      <span className="text-slate-200 font-bold truncate max-w-[120px]">{t.name}</span>
                    </div>

                    <div className="flex space-x-4 w-24 justify-end font-mono">
                      <span className="w-6 text-center text-slate-400">{t.matchesPlayed}</span>
                      <span className={`w-6 text-center font-medium ${t.goalDifference >= 0 ? 'text-emerald-500' : 'text-rose-500'}`}>
                        {t.goalDifference > 0 ? `+${t.goalDifference}` : t.goalDifference}
                      </span>
                      <span className="w-8 text-center text-white font-extrabold">{t.points}</span>
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>

          <div className="pt-4 border-t border-slate-800/80 mt-4 text-center">
            <p className="text-[10px] text-slate-400 font-mono">
              * Tự động xếp hạng theo Điểm &raquo; Hiệu Số &raquo; Số Bàn Thắng.
            </p>
          </div>
        </div>
      </div>

      {/* Fixtures and Results Segment */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-8">
        {/* Lịch thi đấu sắp diễn ra */}
        <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 shadow-xl">
          <div className="flex items-center justify-between mb-4 border-b border-slate-800 pb-3">
            <div className="flex items-center space-x-2.5">
              <Calendar className="text-red-500 w-5 h-5" />
              <h3 className="font-extrabold text-white text-sm uppercase tracking-wider font-mono">
                Lịch Thi Đấu Sắp Diễn Ra
              </h3>
            </div>
            <span className="bg-red-950 text-red-500 text-[10px] font-mono px-2.5 py-1 rounded-md font-bold">
              ROUND MATCHES
            </span>
          </div>

          <div className="space-y-3.5">
            {upcomingMatches.length === 0 ? (
              <div className="text-center py-12 text-slate-400">
                <Ticket className="w-10 h-10 text-slate-700 mx-auto mb-2" />
                <p className="text-xs font-semibold text-slate-300">Không có trận đấu sắp diễn ra</p>
                <p className="text-[11px] text-slate-500 mt-0.5">Admin BTC chưa tạo lịch đấu hoặc mùa giải đã hoàn tất.</p>
              </div>
            ) : (
              upcomingMatches.map(m => (
                <div
                  key={m.id}
                  className="bg-slate-950/40 border border-slate-850 p-3 rounded-xl flex items-center justify-between hover:border-slate-800 transition-colors"
                >
                  <div className="flex items-center space-x-2 w-5/12 text-right justify-end">
                    <span className="text-slate-300 text-xs font-extrabold truncate max-w-[100px] sm:max-w-none">{getTeamName(m.homeTeamId)}</span>
                    <div className={`w-3 h-3 rounded bg-gradient-to-r ${getTeamColor(m.homeTeamId)} flex-shrink-0`} />
                  </div>

                  <div className="text-center px-2 w-2/12 flex-shrink-0">
                    <span className="px-2 py-0.5 bg-slate-800 text-[10px] font-mono text-slate-400 rounded-lg">
                      VS
                    </span>
                  </div>

                  <div className="flex items-center space-x-2 w-5/12 text-left justify-start">
                    <div className={`w-3 h-3 rounded bg-gradient-to-r ${getTeamColor(m.awayTeamId)} flex-shrink-0`} />
                    <span className="text-slate-300 text-xs font-extrabold truncate max-w-[100px] sm:max-w-none">{getTeamName(m.awayTeamId)}</span>
                  </div>

                  <div className="text-right pl-3">
                    <p className="text-[10px] text-red-500 font-bold font-mono">{m.date.split(' ')[1]}</p>
                    <p className="text-[8px] text-slate-500 font-mono mt-0.5">{m.date.split(' ')[0]}</p>
                  </div>
                </div>
              ))
            )}
          </div>
        </div>

        {/* Kết quả gần nhất */}
        <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 shadow-xl">
          <div className="flex items-center justify-between mb-4 border-b border-slate-800 pb-3">
            <div className="flex items-center space-x-2.5">
              <PlaySquare className="text-emerald-500 w-5 h-5 animate-pulse" />
              <h3 className="font-extrabold text-white text-sm uppercase tracking-wider font-mono">
                Kết Quả Gần Nhất &amp; Báo Cáo
              </h3>
            </div>
            <button
              onClick={() => setActiveTab('leagues')}
              className="text-xs text-red-400 hover:text-red-300 font-bold"
            >
              Trực tiếp giải
            </button>
          </div>

          <div className="space-y-3.5">
            {recentResults.length === 0 ? (
              <div className="text-center py-12 text-slate-400">
                <PlaySquare className="w-10 h-10 text-slate-700 mx-auto mb-2" />
                <p className="text-xs font-semibold text-slate-300">Chưa có trận nào hoàn thành</p>
                <p className="text-[11px] text-slate-500 mt-0.5">Vui lòng chuyển đổi tài khoản sang HLV để khai báo tỷ số đầu tiên!</p>
              </div>
            ) : (
              recentResults.map(m => {
                const isPending = m.status === 'pending_approval';
                return (
                  <div
                    key={m.id}
                    className="bg-slate-950/40 border border-slate-850 p-3 rounded-xl flex items-center justify-between hover:border-slate-800 cursor-pointer transition-colors"
                    onClick={() => onViewMatchDetail && onViewMatchDetail(m)}
                  >
                    {/* Home Team */}
                    <div className="flex items-center space-x-2 w-4/12 text-right justify-end">
                      <span className="text-slate-300 text-xs font-extrabold truncate">{getTeamName(m.homeTeamId)}</span>
                      <div className={`w-3 h-3 rounded bg-gradient-to-r ${getTeamColor(m.homeTeamId)} flex-shrink-0`} />
                    </div>

                    {/* Scope Score */}
                    <div className="px-2 w-3/12 flex-shrink-0 text-center">
                      <div className="inline-flex items-center space-x-1.5 bg-slate-900 border border-slate-800 px-2.5 py-1 rounded-lg">
                        <span className="text-xs font-bold text-white font-mono">{m.homeScore}</span>
                        <span className="text-[10px] text-slate-600">-</span>
                        <span className="text-xs font-bold text-white font-mono">{m.awayScore}</span>
                      </div>
                      {isPending && (
                        <div className="mt-1">
                          <span className="bg-amber-600/10 text-amber-500 border border-amber-900/40 text-[8px] font-bold tracking-wider font-mono px-1 py-0.5 rounded uppercase uppercase-tight">
                            CHỜ DUYỆT
                          </span>
                        </div>
                      )}
                    </div>

                    {/* Away Team */}
                    <div className="flex items-center space-x-2 w-4/12 text-left justify-start">
                      <div className={`w-3 h-3 rounded bg-gradient-to-r ${getTeamColor(m.awayTeamId)} flex-shrink-0`} />
                      <span className="text-slate-300 text-xs font-extrabold truncate">{getTeamName(m.awayTeamId)}</span>
                    </div>

                    {/* Evidence Indicator */}
                    <div className="text-right max-w-[50px] flex-shrink-0 pl-1">
                      {m.evidenceImage ? (
                        <span className="text-[9px] bg-red-950 border border-red-900/40 text-red-400 px-1.5 py-0.5 rounded font-mono font-semibold hover:bg-red-900 transition-colors">
                          ẢNH
                        </span>
                      ) : (
                        <span className="text-[9px] text-slate-600 font-mono">KO ẢNH</span>
                      )}
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
