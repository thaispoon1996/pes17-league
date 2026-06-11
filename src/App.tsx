import React, { useState, useEffect } from 'react';
import { User, League, Team, Player, Match, TransferRequest, NewsItem, UserRole, GoalScorerRecord, AssistRecord } from './types';
import { mockUsers, mockLeagues, mockTeams, mockPlayers, mockMatches, mockTransfers, mockNews, APP_STORAGE_KEY } from './mockData';
import { recalculateStandings } from './utils/schedule';

// Subcomponents
import Navbar from './components/Navbar';
import HomeView from './components/HomeView';
import LeaguesView from './components/LeaguesView';
import TeamsView from './components/TeamsView';
import TransfersView from './components/TransfersView';
import StatsView from './components/StatsView';
import AdminPanel from './components/AdminPanel';

import { Trophy, HelpCircle, ArrowRight, ShieldCheck, Mail, Users } from 'lucide-react';

export default function App() {
  // Navigation
  const [activeTab, setActiveTab] = useState<string>('home');
  const [activeLeagueId, setActiveLeagueId] = useState<string>('league_epl');

  // Core synchronized state values
  const [users, setUsers] = useState<User[]>([]);
  const [currentUser, setCurrentUser] = useState<User | null>(null);
  const [leagues, setLeagues] = useState<League[]>([]);
  const [teams, setTeams] = useState<Team[]>([]);
  const [players, setPlayers] = useState<Player[]>([]);
  const [matches, setMatches] = useState<Match[]>([]);
  const [transfers, setTransfers] = useState<TransferRequest[]>([]);
  const [news, setNews] = useState<NewsItem[]>([]);

  // State inspection card details (quick view overlay from home)
  const [selectedInspectMatch, setSelectedInspectMatch] = useState<Match | null>(null);

  // Authentication states
  const [emailInput, setEmailInput] = useState('');
  const [passwordInput, setPasswordInput] = useState('');
  const [isLoggedIn, setIsLoggedIn] = useState(false);
  const [authError, setAuthError] = useState('');

  // 1. Load initial dataset from storage, fallback to mocks
  useEffect(() => {
    const raw = localStorage.getItem(APP_STORAGE_KEY);
    if (raw) {
      try {
        const parsed = JSON.parse(raw);
        setUsers(parsed.users || mockUsers);
        setLeagues(parsed.leagues || mockLeagues);
        setTeams(parsed.teams || mockTeams);
        setPlayers(parsed.players || mockPlayers);
        setMatches(parsed.matches || mockMatches);
        setTransfers(parsed.transfers || mockTransfers);
        setNews(parsed.news || mockNews);

        // Persistent login check
        if (parsed.currentUserId) {
          const matched = (parsed.users || mockUsers).find((u: User) => u.id === parsed.currentUserId);
          if (matched) {
            setCurrentUser(matched);
            setIsLoggedIn(true);
          }
        } else {
          // Default to HLV MU for instant interactive testing or guest
          const defUser = (parsed.users || mockUsers).find((u: User) => u.id === 'hlv_mu');
          if (defUser) {
            setCurrentUser(defUser);
            setIsLoggedIn(true);
          }
        }
      } catch (err) {
        console.error("Failed to parse persisted data:", err);
        fallbackToMocks();
      }
    } else {
      fallbackToMocks();
    }
  }, []);

  const fallbackToMocks = () => {
    setUsers(mockUsers);
    setLeagues(mockLeagues);
    setTeams(mockTeams);
    setPlayers(mockPlayers);
    setMatches(mockMatches);
    setTransfers(mockTransfers);
    setNews(mockNews);
    
    // Auto logged-in as HLV MU for prime playability
    const muHLV = mockUsers.find(u => u.id === 'hlv_mu') || null;
    setCurrentUser(muHLV);
    setIsLoggedIn(true);
  };

  // 2. Synchronize to storage whenever state values vary
  useEffect(() => {
    if (leagues.length > 0) {
      const dataToSave = {
        users,
        currentUserId: currentUser?.id || '',
        leagues,
        teams,
        players,
        matches,
        transfers,
        news,
      };
      localStorage.setItem(APP_STORAGE_KEY, JSON.stringify(dataToSave));
    }
  }, [users, currentUser, leagues, teams, players, matches, transfers, news]);

  // Login handler
  const handleLoginSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setAuthError('');

    // Special mock validations
    const foundUser = users.find(u => u.email.trim().toLowerCase() === emailInput.trim().toLowerCase());
    if (foundUser) {
      setCurrentUser(foundUser);
      setIsLoggedIn(true);
      setEmailInput('');
      setPasswordInput('');
    } else {
      setAuthError('Không tìm thấy tài khoản với email này. Hãy nhập email giả lập đầy đủ (ví dụ: admin@pes17.vn, hlv.mu@pes17.vn). Chúng tôi hỗ trợ đăng nhập nhanh bằng hòm Switcher ở góc trên bên phải!');
    }
  };

  // Change logged in Role / credentials instantly (Prime switch feature)
  const handleSelectRole = (role: UserRole, email?: string) => {
    if (role === 'guest') {
      setCurrentUser({
        id: 'user_guest',
        name: 'Khách vãng lai',
        email: 'guest@pes17.vn',
        role: 'guest',
      });
      setIsLoggedIn(true);
      return;
    }

    const matched = users.find(u => u.email === email && u.role === role);
    if (matched) {
      setCurrentUser(matched);
      setIsLoggedIn(true);
    }
  };

  const handleLogout = () => {
    setCurrentUser(null);
    setIsLoggedIn(false);
  };

  // Callback 1: Update HLV Parsec ID
  const handleUpdateParsecId = (newParsecId: string) => {
    if (!currentUser) return;
    const updatedUsers = users.map(u => {
      if (u.id === currentUser.id) {
        return { ...u, parsecId: newParsecId };
      }
      return u;
    });
    setUsers(updatedUsers);

    // Update current visual user
    setCurrentUser({ ...currentUser, parsecId: newParsecId });

    // Update associated team parsec
    if (currentUser.teamId) {
      const updatedTeams = teams.map(t => {
        if (t.id === currentUser.teamId) {
          return { ...t, parsecId: newParsecId };
        }
        return t;
      });
      setTeams(updatedTeams);
    }
  };

  // Callback 2: Add League
  const handleAddLeague = (l: Omit<League, 'id'>) => {
    const newLg: League = {
      ...l,
      id: `league_${Date.now()}`,
    };
    setLeagues([...leagues, newLg]);
  };

  // Callback 3: Add Team
  const handleAddTeam = (t: Omit<Team, 'id' | 'matchesPlayed' | 'wins' | 'draws' | 'losses' | 'goalsFor' | 'goalsAgainst' | 'goalDifference' | 'points' | 'streak'>) => {
    const newTm: Team = {
      ...t,
      id: `team_${Date.now()}`,
      matchesPlayed: 0,
      wins: 0,
      draws: 0,
      losses: 0,
      goalsFor: 0,
      goalsAgainst: 0,
      goalDifference: 0,
      points: 0,
      streak: [],
    };
    setTeams([...teams, newTm]);
  };

  // Callback 4: Add Player
  const handleAddPlayer = (p: Omit<Player, 'id' | 'goalsCount' | 'assistsCount'>) => {
    const newPl: Player = {
      ...p,
      id: `player_${Date.now()}`,
      goalsCount: 0,
      assistsCount: 0,
    };
    setPlayers([...players, newPl]);
  };

  // Callback 5: Delete Player
  const handleDeletePlayer = (id: string) => {
    setPlayers(players.filter(p => p.id !== id));
  };

  // Callback 6: Automatic Matches Generation
  const handleGenerateMatches = (leagueId: string, generatedMatches: Match[]) => {
    // Keep matches of other leagues, wipe current scheduled matches, append newly generated ones
    const filtered = matches.filter(m => m.leagueId !== leagueId);
    const updatedMatches = [...filtered, ...generatedMatches];
    setMatches(updatedMatches);

    // Reset Table standings stats for this league
    const recalculatedTeams = recalculateStandings(teams, updatedMatches, leagueId);
    setTeams(recalculatedTeams);

    // Append new bulletin news
    const currentLgName = leagues.find(l => l.id === leagueId)?.name || 'EPL';
    const lgNews: NewsItem = {
      id: `news_${Date.now()}`,
      title: `Ban Tổ Chức tự động sinh lịch đấu song phương cho giải "${currentLgName}"!`,
      summary: `Tổng số ${generatedMatches.length} trận đấu lượt đi và lượt về vòng tròn tính điểm vừa được ban hành rộng rãi.`,
      content: 'Chính thức kích hoạt mùa giải mới. Các đội đã có lịch cụ thể từng vòng. HLV vui lòng vào tab Lịch trình để đăng ký ngày giờ đấu, nhận kết nối Parsec và nộp biên bản trận đấu ngay khi hoàn thành.',
      category: 'league',
      date: 'Hôm nay',
      imageUrl: 'https://images.unsplash.com/photo-1540747737956-378724044432?w=800&auto=format&fit=crop&q=80',
    };
    setNews([lgNews, ...news]);
  };

  // Callback 7: HLV reports match result
  const handleReportMatch = (
    matchId: string,
    homeScore: number,
    awayScore: number,
    evidenceBase64: string,
    scorers: GoalScorerRecord[],
    assists: AssistRecord[]
  ) => {
    const updated = matches.map(m => {
      if (m.id === matchId) {
        return {
          ...m,
          homeScore,
          awayScore,
          evidenceImage: evidenceBase64,
          status: 'pending_approval' as const,
          reporterId: currentUser?.id,
          reporterName: currentUser?.name || 'Sân cỏ',
          goalScorers: scorers,
          assists: assists,
        };
      }
      return m;
    });
    setMatches(updated);

    // Add news article about the reported match
    const reportedMatchObj = matches.find(m => m.id === matchId);
    if (reportedMatchObj) {
      const parentHome = teams.find(t => t.id === reportedMatchObj.homeTeamId)?.name || 'Home';
      const parentAway = teams.find(t => t.id === reportedMatchObj.awayTeamId)?.name || 'Away';
      const reportNews: NewsItem = {
        id: `news_report_${Date.now()}`,
        title: `Trực tiếp: ${parentHome} và ${parentAway} vừa hoàn thành đá đấu với tỷ số ${homeScore}-${awayScore}!`,
        summary: `Hồ sơ nộp ảnh chụp Parsec vừa được gửi lên BTC để duyệt đóng dấu cộng điểm.`,
        content: `Huấn luyện viên ${currentUser?.name} vừa thay mặt cả hai đội nộp biên bản điện tử hoàn thành trận đấu tại vòng ${reportedMatchObj.round}. Ban Tổ Chức sẽ kiểm tra tính trung thực của ảnh minh chứng kết quả trước khi chính thức ghi nhận vào bảng standings toàn quốc.`,
        category: 'match',
        date: 'Vừa xong',
        imageUrl: evidenceBase64,
      };
      setNews([reportNews, ...news]);
    }
  };

  // Callback 8: BTC signs off match results -> update stats & standings
  const handleApproveMatch = (matchId: string) => {
    const matchObj = matches.find(m => m.id === matchId);
    if (!matchObj || matchObj.homeScore === undefined || matchObj.awayScore === undefined) return;

    // 8a. Set match to completed
    const updatedMatches = matches.map(m => {
      if (m.id === matchId) {
        return { ...m, status: 'completed' as const };
      }
      return m;
    });
    setMatches(updatedMatches);

    // 8b. Dynamic Standings Recalculation
    const recalculatedTeams = recalculateStandings(teams, updatedMatches, matchObj.leagueId);
    setTeams(recalculatedTeams);

    // 8c. Append player goal tallies and play assistance statistics
    const updatedPlayers = players.map(p => {
      let goalsToAdd = 0;
      let assistsToAdd = 0;

      if (matchObj.goalScorers) {
        const found = matchObj.goalScorers.find(s => s.playerId === p.id);
        if (found) goalsToAdd = found.goals;
      }

      if (matchObj.assists) {
        const found = matchObj.assists.find(a => a.playerId === p.id);
        if (found) assistsToAdd = found.assists;
      }

      if (goalsToAdd > 0 || assistsToAdd > 0) {
        return {
          ...p,
          goalsCount: p.goalsCount + goalsToAdd,
          assistsCount: p.assistsCount + assistsToAdd,
        };
      }
      return p;
    });
    setPlayers(updatedPlayers);

    // 8d. Generate congratulatory celebration article on homepage
    const hName = teams.find(t => t.id === matchObj.homeTeamId)?.name || 'Home';
    const aName = teams.find(t => t.id === matchObj.awayTeamId)?.name || 'Away';
    const approveNews: NewsItem = {
      id: `news_app_${Date.now()}`,
      title: `Chính thức: Kết quả ${hName} ${matchObj.homeScore} - ${matchObj.awayScore} ${aName} đã được phê chuẩn!`,
      summary: `Ban Tổ Chức duyệt đóng biên bản thành công. Bảng xếp hạng cập nhật điểm mới ngay lập tức.`,
      content: `Sau quá trình rà soát ảnh chụp màn hình nộp bởi các HLV, Admin tổ chức xác nhận chỉ số kết quả hoàn toàn đúng luật. Điểm đã được quy đổi: Thắng +3đ, Hòa +1đ. Thống kê cá nhân của các cầu thủ ghi bàn sân cỏ cũng chính thức được ghi vào bảng xếp hạng vàng giải quốc gia.`,
      category: 'match',
      date: 'Hôm nay',
      imageUrl: matchObj.evidenceImage,
    };
    setNews([approveNews, ...news]);
  };

  // Callback 9: BTC declines a reported score (revert to scheduled matches)
  const handleRejectMatch = (matchId: string) => {
    const updatedMatches = matches.map(m => {
      if (m.id === matchId) {
        return {
          ...m,
          status: 'scheduled' as const,
          homeScore: undefined,
          awayScore: undefined,
          evidenceImage: undefined,
          goalScorers: [],
          assists: [],
        };
      }
      return m;
    });
    setMatches(updatedMatches);
  };

  // Callback 10: Update match played schedule date
  const handleUpdateMatchDate = (matchId: string, newDate: string) => {
    setMatches(matches.map(m => {
      if (m.id === matchId) {
        return { ...m, date: newDate };
      }
      return m;
    }));
  };

  // Callback 11: Sell Player (list on market)
  const handleSellPlayer = (playerId: string, price: number) => {
    setPlayers(players.map(p => {
      if (p.id === playerId) {
        return { ...p, isTransferListed: true, transferPrice: price };
      }
      return p;
    }));
  };

  // Callback 12: Offer buyout request (Coach buying list)
  const handleOfferBuy = (playerId: string, price: number) => {
    const player = players.find(p => p.id === playerId);
    if (!player) return;

    const fromTeamNameText = player.teamId === 'free_agent' ? 'Tự do' : (teams.find(t => t.id === player.teamId)?.name || 'Chủ quản');
    const toTeamNameText = teams.find(t => t.id === currentUser?.teamId)?.name || 'Bên mua';

    const newTransfer: TransferRequest = {
      id: `tr_${Date.now()}`,
      playerId: player.id,
      playerName: player.name,
      fromTeamId: player.teamId,
      fromTeamName: fromTeamNameText,
      toTeamId: currentUser?.teamId || '',
      toTeamName: toTeamNameText,
      price: price,
      status: 'pending',
      date: 'Hôm nay',
    };
    setTransfers([newTransfer, ...transfers]);

    // Add news bulletin
    const tradeNews: NewsItem = {
      id: `news_tr_${Date.now()}`,
      title: `Thoả thuận đặt thầu: ${toTeamNameText} gửi lời hỏi mua siêu sao ${player.name}!`,
      summary: `Huấn luyện viên ${currentUser?.name} sẵn sàng chi khoản thầu đắt đỏ ${price} triệu coin để nẫng tay trên mục tiêu.`,
      content: `Cuộc chiến giành chữ ký của cầu thủ đắt giá tiếp tục bùng nổ. Bản thiết kế thầu chuyên nhượng đã được trình văn phòng Admin để xác nhận điều kiện tài chính hợp lệ trước khi bấm nút sang nhượng chính thức.`,
      category: 'transfer',
      date: 'Vừa xong',
    };
    setNews([tradeNews, ...news]);
  };

  // Callback 13: Admin signs off on Transfer trades
  const handleManageTransfer = (transferId: string, status: 'approved' | 'rejected') => {
    const trObj = transfers.find(t => t.id === transferId);
    if (!trObj) return;

    // 13a. Update Status check
    const updatedRequests = transfers.map(t => {
      if (t.id === transferId) {
        return { ...t, status };
      }
      return t;
    });
    setTransfers(updatedRequests);

    // 13b. If approved, transfer player to new team
    if (status === 'approved') {
      const updatedPlayers = players.map(p => {
        if (p.id === trObj.playerId) {
          return {
            ...p,
            teamId: trObj.toTeamId,
            isTransferListed: false,
            transferPrice: undefined,
          };
        }
        return p;
      });
      setPlayers(updatedPlayers);

      // Publish celebratory trade press release
      const sellerLabel = trObj.fromTeamId === 'free_agent' ? 'diện Tự do' : `câu lạc bộ ${trObj.fromTeamName}`;
      const congratsNews: NewsItem = {
        id: `news_tr_app_${Date.now()}`,
        title: `QUYẾT ĐỊNH BTC: Phê chuẩn hợp đồng bom tấn của cầu thủ ${trObj.playerName}!`,
        summary: `Thỏa thuận hoàn thành đưa cầu thủ từ ${sellerLabel} cập bến ngôi nhà mới ${trObj.toTeamName}.`,
        content: `Ban Tổ Chức giải PES 17 đã đóng dấu thông qua thương vụ trao đổi của ${trObj.playerName}. Phí chuyển giao ghi nhận: ${trObj.price} triệu coin. Mọi giấy tờ sở hữu hợp pháp đã được bàn giao giúp huấn luyện sử dụng cầu thủ này ngay vòng đấu kế tiếp.`,
        category: 'transfer',
        date: 'Hôm nay',
      };
      setNews([congratsNews, ...news]);
    }
  };

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 flex flex-col font-sans selection:bg-red-600 selection:text-white">
      {/* 1. App Navigation Header */}
      <Navbar
        currentUser={currentUser}
        onSelectRole={handleSelectRole}
        activeTab={activeTab}
        setActiveTab={setActiveTab}
        availableUsers={users}
        onUpdateParsecId={handleUpdateParsecId}
      />

      {/* 2. Login Overlay check */}
      {!isLoggedIn ? (
        <main className="flex-1 max-w-md mx-auto w-full flex items-center justify-center p-4">
          <div className="bg-slate-905 border border-slate-800 p-8 rounded-2xl shadow-2xl w-full text-center space-y-6">
            <div className="w-12 h-12 bg-red-600 rounded-xl flex items-center justify-center mx-auto shadow-lg shadow-red-900/40">
              <span className="font-black text-white text-lg tracking-wider font-mono">PES</span>
            </div>
            <div>
              <h2 className="text-xl font-bold tracking-tight text-white">Đăng nhập PES 17 League</h2>
              <p className="text-xs text-slate-400 mt-1">Hệ thống quản lý, báo cáo tỷ số và giao dịch cầu thủ trực tuyến</p>
            </div>

            {authError && (
              <div className="bg-red-950/40 border border-red-905 p-3 rounded-lg text-xs text-red-400 text-left font-serif">
                {authError}
              </div>
            )}

            <form onSubmit={handleLoginSubmit} className="space-y-4 text-xs text-left">
              <div>
                <label className="block text-[10px] font-mono font-bold text-slate-400 uppercase mb-1">Địa chỉ Email</label>
                <div className="relative">
                  <input
                    type="email"
                    required
                    placeholder="hlv.mu@pes17.vn hoặc admin@pes17.vn"
                    className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-2 pl-9 font-sans text-sm focus:outline-none focus:border-red-500"
                    value={emailInput}
                    onChange={(e) => setEmailInput(e.target.value)}
                  />
                  <Mail className="w-3.5 h-3.5 text-slate-550 absolute left-3 top-3" />
                </div>
              </div>

              <div>
                <label className="block text-[10px] font-mono font-bold text-slate-400 uppercase mb-1">Mật khẩu bảo mật</label>
                <input
                  type="password"
                  placeholder="••••••••"
                  className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-red-500 font-mono"
                  value={passwordInput}
                  onChange={(e) => setPasswordInput(e.target.value)}
                />
              </div>

              <button
                type="submit"
                className="w-full bg-red-600 hover:bg-red-500 text-white font-extrabold tracking-wide py-2.5 rounded-xl border border-red-500 text-sm font-bold shadow-lg shadow-red-950/25 transition-all"
              >
                Đăng Nhập Ngay &raquo;
              </button>
            </form>

            <div className="pt-4 border-t border-slate-800">
              <p className="text-[10px] text-slate-450 leading-relaxed font-mono">
                💡 Lưu ý giả lập: Bạn có thể click mục <b>Switcher</b> ở góc trên bên phải để bỏ qua cửa sổ này và tự động đăng nhập nhanh dưới mọi tài khoản một cách tiện lợi!
              </p>
            </div>
          </div>
        </main>
      ) : (
        /* 3. Main Views router output with standard container margins */
        <main className="flex-1 max-w-7xl mx-auto w-full px-4 sm:px-6 lg:px-8 py-8 space-y-8">
          
          {/* Global warning banner about Simulated environment settings if necessary */}
          {currentUser?.role === 'guest' && (
            <div className="bg-emerald-950/30 border border-emerald-900/50 p-4 rounded-xl flex items-center justify-between text-xs text-emerald-400">
              <div className="flex items-center space-x-2.5">
                <Users className="w-4 h-4 text-emerald-400 flex-shrink-0 animate-pulse" />
                <div>
                  <p className="font-bold">Chế độ Xem với tư cách Khách Viếng Thăm (Guest view only)</p>
                  <p className="text-[11px] text-slate-400">Bảng xếp hạng, tin túc và kết chuyển nhượng vẫn hiển thị đầy đủ. Để thử nghiệm Nhập Tỷ Số hoặc Sinh Lịch Đấu BTC, chọn tài khoản Switcher bên trên !</p>
                </div>
              </div>
              <button
                onClick={() => handleSelectRole('coach', 'hlv.mu@pes17.vn')}
                className="text-xs font-bold underline hover:text-white"
              >
                Vào nhanh MU &raquo;
              </button>
            </div>
          )}

          {activeTab === 'home' && (
            <HomeView
              news={news}
              allMatches={matches}
              teams={teams}
              leagues={leagues}
              activeLeagueId={activeLeagueId}
              setActiveLeagueId={setActiveLeagueId}
              setActiveTab={setActiveTab}
              onViewMatchDetail={(m) => setSelectedInspectMatch(m)}
            />
          )}

          {activeTab === 'leagues' && (
            <LeaguesView
              currentUser={currentUser}
              leagues={leagues}
              activeLeagueId={activeLeagueId}
              setActiveLeagueId={setActiveLeagueId}
              teams={teams}
              allMatches={matches}
              players={players}
              onReportMatch={handleReportMatch}
              onUpdateMatchDate={handleUpdateMatchDate}
              onApproveMatch={handleApproveMatch}
            />
          )}

          {activeTab === 'teams' && (
            <TeamsView
              currentUser={currentUser}
              teams={teams}
              players={players}
              leagues={leagues}
              activeLeagueId={activeLeagueId}
            />
          )}

          {activeTab === 'transfers' && (
            <TransfersView
              currentUser={currentUser}
              players={players}
              teams={teams}
              transfers={transfers}
              onSellPlayer={handleSellPlayer}
              onOfferBuy={handleOfferBuy}
              onManageTransfer={handleManageTransfer}
            />
          )}

          {activeTab === 'stats' && (
            <StatsView
              players={players}
              teams={teams}
              leagues={leagues}
              activeLeagueId={activeLeagueId}
            />
          )}

          {activeTab === 'admin' && currentUser?.role === 'admin' && (
            <AdminPanel
              leagues={leagues}
              teams={teams}
              players={players}
              allMatches={matches}
              transfers={transfers}
              onAddLeague={handleAddLeague}
              onAddTeam={handleAddTeam}
              onAddPlayer={handleAddPlayer}
              onDeletePlayer={handleDeletePlayer}
              onGenerateMatches={handleGenerateMatches}
              onApproveMatch={handleApproveMatch}
              onRejectMatch={handleRejectMatch}
              activeLeagueId={activeLeagueId}
            />
          )}
        </main>
      )}

      {/* 4. Global Footer details */}
      <footer id="app-footer" className="mt-auto py-8 bg-slate-905 border-t border-slate-900 text-xs text-slate-500 font-mono text-center space-y-2">
        <p className="font-semibold text-slate-300">© 2026 PES 17 Vietnam Online League Championship. All Rights Reserved.</p>
        <p className="max-w-xl mx-auto leading-relaxed text-[11px] px-4 text-slate-450">
          Xây dựng cho Ban Tổ Chức và Huấn Luyện Viên PES 17 trực tuyến. Tích hợp định danh Parsec, thuật toán sắp lịch thi đấu xoay vòng tự động và trình trích soát minh chứng tỷ số tối cao.
        </p>
      </footer>

      {/* Inspecting Match details Modal (from home or any list view quickly) */}
      {selectedInspectMatch && (
        <div className="fixed inset-0 bg-slate-950/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-800 w-full max-w-sm rounded-2xl p-6 shadow-2xl relative animate-in zoom-in-95 duration-150 text-xs font-mono text-slate-400">
            <h3 className="text-sm font-sans font-black text-white uppercase border-b border-slate-800 pb-2">
              Báo cáo trận đấu chi tiết
            </h3>

            <div className="py-4 text-center">
              <p className="font-sans font-bold text-slate-200">
                {teams.find(t => t.id === selectedInspectMatch.homeTeamId)?.name}
                <span className="text-red-500 font-mono font-black text-lg mx-3">
                  {selectedInspectMatch.homeScore} - {selectedInspectMatch.awayScore}
                </span>
                {teams.find(t => t.id === selectedInspectMatch.awayTeamId)?.name}
              </p>
              <p className="text-[10px] text-slate-500 mt-2">Ngày đấu: {selectedInspectMatch.date}</p>
            </div>

            {selectedInspectMatch.evidenceImage && (
              <div className="bg-slate-950 p-1.5 rounded-lg border border-slate-850 mb-4">
                <img src={selectedInspectMatch.evidenceImage} alt="Match result screenshot" className="w-full rounded h-32 object-cover" />
              </div>
            )}

            <div className="space-y-1 bg-slate-950 p-3 rounded-lg border border-slate-850">
              <p className="text-white font-bold font-sans">Chi tiết bàn thắng &amp; kiến tạo:</p>
              <p className="text-[11px] mt-1">⚽ Ghi bàn: {selectedInspectMatch.goalScorers?.map(s => `${s.name} (${s.goals} quả)`).join(', ') || 'Không ghi nhận'}</p>
              <p className="text-[11px]">👟 Kiến tạo: {selectedInspectMatch.assists?.map(a => `${a.name} (${a.assists} lần)`).join(', ') || 'Không ghi nhận'}</p>
            </div>

            <div className="flex justify-end pt-4">
              <button
                type="button"
                className="px-4 py-1.5 bg-slate-800 hover:bg-slate-750 text-white font-sans font-bold rounded-lg transition"
                onClick={() => setSelectedInspectMatch(null)}
              >
                Đóng cửa sổ
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
