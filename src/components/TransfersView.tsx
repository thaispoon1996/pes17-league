import React, { useState } from 'react';
import { Player, Team, TransferRequest, User, UserRole } from '../types';
import { DollarSign, MessageSquare, Plus, ArrowRightLeft, Shield, CheckCircle, XCircle, Search, Sparkles, Filter } from 'lucide-react';

interface TransfersViewProps {
  currentUser: User | null;
  players: Player[];
  teams: Team[];
  transfers: TransferRequest[];
  onSellPlayer: (playerId: string, price: number) => void;
  onOfferBuy: (playerId: string, price: number) => void;
  onManageTransfer: (transferId: string, status: 'approved' | 'rejected') => void;
}

export default function TransfersView({
  currentUser,
  players,
  teams,
  transfers,
  onSellPlayer,
  onOfferBuy,
  onManageTransfer,
}: TransfersViewProps) {
  const [activeMarketSub, setActiveMarketSub] = useState<'listings' | 'offers'>('listings');
  const [searchQuery, setSearchQuery] = useState('');
  const [positionFilter, setPositionFilter] = useState('ALL');

  // Sales submission state
  const [sellingPlayerId, setSellingPlayerId] = useState('');
  const [salesPrice, setSalesPrice] = useState<number>(10);
  const [showSalesModal, setShowSalesModal] = useState(false);

  // Buy offer state
  const [buyingPlayer, setBuyingPlayer] = useState<Player | null>(null);
  const [buyOfferPrice, setBuyOfferPrice] = useState<number>(10);

  // Find helper methods
  const getTeamName = (id: string) => {
    if (id === 'free_agent') return 'Cầu thủ Tự do';
    return teams.find(t => t.id === id)?.name || 'CLB Ẩn danh';
  };
  const getTeamColor = (id: string) => teams.find(t => t.id === id)?.logoColor || 'from-slate-500 to-slate-700';

  // Filters
  const marketPlayers = players.filter(p => {
    const isListed = p.isTransferListed;
    const matchesQuery = p.name.toLowerCase().includes(searchQuery.toLowerCase()) || p.nationality.toLowerCase().includes(searchQuery.toLowerCase());
    const matchesPosition = positionFilter === 'ALL' || p.position === positionFilter;
    return isListed && matchesQuery && matchesPosition;
  });

  // Collect current logged-in HLV team players (who are NOT listed already for sale)
  const myTeamId = currentUser?.teamId;
  const mySellablePlayers = players.filter(p => p.teamId === myTeamId && !p.isTransferListed);

  const handleSaleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!sellingPlayerId) return;
    onSellPlayer(sellingPlayerId, salesPrice);
    setShowSalesModal(false);
    setSellingPlayerId('');
  };

  const handleBuySubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!buyingPlayer) return;
    onOfferBuy(buyingPlayer.id, buyOfferPrice);
    setBuyingPlayer(null);
  };

  return (
    <div id="transfers-view-wrapper" className="space-y-6">
      {/* Title block */}
      <div className="bg-slate-905 border border-slate-800 p-5 rounded-2xl shadow-xl flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h2 className="text-xl font-extrabold text-white flex items-center space-x-2">
            <ArrowRightLeft className="text-red-500 w-5 h-5" />
            <span>Thị Trường Chuyển Nhượng Online</span>
          </h2>
          <p className="text-xs text-slate-450 mt-1">
            Đấu giá, liên hệ trao đổi, chuyển quyền sở hữu cầu thủ PES 17 giữa các huấn luyện viên dưới sự kiểm soát của BTC.
          </p>
        </div>

        {/* Coach sale action button */}
        {currentUser && currentUser.role === 'coach' && (
          <button
            onClick={() => setShowSalesModal(true)}
            className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white font-extrabold tracking-wide text-xs rounded-xl flex items-center space-x-1.5 shadow-lg shadow-indigo-950/40"
          >
            <Plus className="w-4 h-4" />
            <span>Đăng bán cầu thủ CLB</span>
          </button>
        )}
      </div>

      {/* Internal Tabs */}
      <div className="flex border-b border-slate-800 space-x-6">
        <button
          onClick={() => setActiveMarketSub('listings')}
          className={`pb-3 text-xs font-bold tracking-wider transition-all uppercase ${
            activeMarketSub === 'listings' ? 'text-indigo-400 border-b-2 border-indigo-500 font-extrabold' : 'text-slate-400 hover:text-white'
          }`}
        >
          DANH SÁCH RAO BÁN ({marketPlayers.length})
        </button>
        <button
          onClick={() => setActiveMarketSub('offers')}
          className={`pb-3 text-xs font-bold tracking-wider transition-all uppercase ${
            activeMarketSub === 'offers' ? 'text-indigo-400 border-b-2 border-indigo-500 font-extrabold' : 'text-slate-400 hover:text-white'
          }`}
        >
          ĐỀ NGHỊ CHUYỂN NHƯỢNG ({transfers.length})
        </button>
      </div>

      {/* SUB-PANEL 1: Listings block */}
      {activeMarketSub === 'listings' && (
        <div className="space-y-4">
          {/* Quick Filters */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3 bg-slate-900 border border-slate-800 p-3.5 rounded-xl text-xs">
            {/* Search Input */}
            <div className="relative">
              <input
                type="text"
                placeholder="Tìm cầu thủ, quốc tịch..."
                className="w-full bg-slate-950 border border-slate-800 text-slate-200 px-3 py-1.5 pl-8 rounded-lg outline-none focus:border-indigo-500 font-mono"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
              />
              <Search className="w-3.5 h-3.5 text-slate-500 absolute left-2.5 top-2.5" />
            </div>

            {/* Position filter selects */}
            <div className="flex items-center space-x-2">
              <span className="text-slate-450 font-mono">Vị trí:</span>
              <select
                value={positionFilter}
                onChange={(e) => setPositionFilter(e.target.value)}
                className="bg-slate-950 border border-slate-800 text-white rounded-lg px-2 py-1.5 focus:outline-none focus:border-indigo-500"
              >
                <option value="ALL">Tất cả vị trí</option>
                <option value="CF">Tiền đạo cắm (CF)</option>
                <option value="LWF">Tiền đạo cánh trái (LWF)</option>
                <option value="RWF">Tiền đạo cánh phải (RWF)</option>
                <option value="AMF">Kiến thiết (AMF)</option>
                <option value="CMF">Trung tâm (CMF)</option>
                <option value="DMF">Đánh chặn (DMF)</option>
                <option value="CB">Trung vệ (CB)</option>
                <option value="GK">Thủ môn (GK)</option>
              </select>
            </div>

            <div className="text-right text-[10px] text-slate-500 flex items-center justify-end font-mono">
              * Gợi ý: HLV MU có ngân sách dồi dào có thể mua Ronaldinho hoặc Giroud!
            </div>
          </div>

          {/* Cards Grid */}
          {marketPlayers.length === 0 ? (
            <div className="bg-slate-900 border border-slate-800 rounded-xl p-10 text-center text-slate-500 font-mono">
              Chưa có cầu thủ nào được đăng bán trên sàn thương mại.
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
              {marketPlayers.map(p => {
                const isMine = currentUser && currentUser.teamId === p.teamId;
                const canBuy = currentUser && currentUser.role === 'coach' && currentUser.teamId !== p.teamId;

                return (
                  <div
                    key={p.id}
                    className="bg-slate-900 border border-slate-800 rounded-2xl p-5 hover:border-slate-700 transition flex flex-col justify-between shadow-lg relative group"
                  >
                    {/* Visual glowing header */}
                    <div className="absolute top-0 inset-x-0 h-1 bg-gradient-to-r from-indigo-500 to-red-500 opacity-60 rounded-t-2xl"></div>

                    <div>
                      {/* Brand details */}
                      <div className="flex justify-between items-start mb-2">
                        <span className="font-mono bg-indigo-950 text-indigo-400 font-extrabold px-2 py-0.5 rounded text-[10px] border border-indigo-900/40 uppercase">
                          OVR {p.ovr} Rating
                        </span>
                        <div className="text-right text-[10px] font-mono text-slate-450 uppercase">
                          Position: <span className="font-bold text-white">{p.position}</span>
                        </div>
                      </div>

                      {/* Name of player */}
                      <h4 className="text-base font-black text-white group-hover:text-amber-400 transition mb-1 leading-none">{p.name}</h4>
                      <p className="text-xs text-slate-400 font-serif mb-4">Quốc tịch: {p.nationality}</p>

                      {/* Current owner segment */}
                      <div className="bg-slate-950/40 border border-slate-850 p-2.5 rounded-lg flex items-center justify-between text-xs">
                        <span className="text-slate-400">Đội chủ quản:</span>
                        <span className="font-bold text-slate-200">{getTeamName(p.teamId)}</span>
                      </div>
                    </div>

                    <div className="mt-5 pt-3.5 border-t border-slate-800/80">
                      <div className="flex justify-between items-center mb-4">
                        <p className="text-xs text-slate-400 font-mono font-medium">Giá chào niêm yết:</p>
                        <p className="text-base font-black text-red-500 font-mono">
                          {(p.transferPrice || p.value).toLocaleString()} Tr Coin
                        </p>
                      </div>

                      {/* Actions buttons */}
                      {canBuy ? (
                        <button
                          onClick={() => {
                            setBuyingPlayer(p);
                            setBuyOfferPrice(p.transferPrice || p.value);
                          }}
                          className="w-full bg-red-600 hover:bg-red-500 text-white font-extrabold py-2 rounded-xl text-xs flex items-center justify-center space-x-1 shadow-lg shadow-red-950/30"
                        >
                          <DollarSign className="w-3.5 h-3.5" />
                          <span>Gửi đề nghị mua cầu thủ này</span>
                        </button>
                      ) : isMine ? (
                        <div className="text-center font-mono text-[10px] text-indigo-400 bg-indigo-950/30 border border-indigo-900/30 py-1.5 rounded-xl">
                          ✍️ Cầu thủ của bạn (Đang đợi thầu)
                        </div>
                      ) : (
                        <div className="text-center font-mono text-[10px] text-slate-500">
                          Đăng nhập Huấn Luyện Viên để nộp đơn đặt thầu
                        </div>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      )}

      {/* SUB-PANEL 2: Active Offers block */}
      {activeMarketSub === 'offers' && (
        <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 shadow-xl">
          <div className="flex items-center justify-between mb-4 pb-3 border-b border-slate-800">
            <h3 className="font-extrabold text-sm text-white uppercase tracking-wider font-mono">
              Bình chọn & Phán quyết chuyển nhượng
            </h3>
            <span className="text-slate-400 text-xs font-mono">Dữ liệu có hiệu lực sau mốc phê chuẩn</span>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead>
                <tr className="border-b border-slate-800 text-slate-405 font-mono uppercase text-[9px] font-black">
                  <th className="py-2.5 px-3">Cầu thần</th>
                  <th className="py-2.5 px-3 text-center">Bên bán</th>
                  <th className="py-2.5 px-3 text-center">Bên thầu mua</th>
                  <th className="py-2.5 px-3 text-right">Phí giao dịch</th>
                  <th className="py-2.5 px-3 text-center">Trạng thái</th>
                  <th className="py-2.5 px-3 text-right">Thao tác</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/40 text-slate-300 font-medium">
                {transfers.length === 0 ? (
                  <tr>
                    <td colSpan={6} className="text-center py-6 text-slate-500 font-mono">
                      Chưa ghi nhận giao dịch chuyển nhượng nào phát sinh.
                    </td>
                  </tr>
                ) : (
                  transfers.map(tr => {
                    const isPending = tr.status === 'pending';
                    const isBTC = currentUser && currentUser.role === 'admin';

                    return (
                      <tr key={tr.id} className="hover:bg-slate-950/25 transition">
                        <td className="py-3.5 px-3 font-bold text-white text-xs sm:text-sm">{tr.playerName}</td>
                        <td className="py-3.5 px-3 text-center font-mono text-slate-400">{tr.fromTeamName}</td>
                        <td className="py-3.5 px-3 text-center font-mono text-white font-extrabold">{tr.toTeamName}</td>
                        <td className="py-3.5 px-3 text-right font-mono font-black text-rose-500">{tr.price.toLocaleString()} Tr Coin</td>
                        <td className="py-3.5 px-3 text-center">
                          {tr.status === 'pending' && (
                            <span className="bg-amber-950 text-amber-500 border border-amber-900 text-[10px] px-2.5 py-0.5 rounded-full font-mono font-bold">
                              Chờ phê duyệt
                            </span>
                          )}
                          {tr.status === 'approved' && (
                            <span className="bg-emerald-950 text-emerald-400 border border-emerald-905/60 text-[10px] px-2.5 py-0.5 rounded-full font-mono font-bold">
                              ✓ Thành công
                            </span>
                          )}
                          {tr.status === 'rejected' && (
                            <span className="bg-red-950 text-red-500 border border-red-900 text-[10px] px-2.5 py-0.5 rounded-full font-mono font-bold">
                              ✖ Bị hủy
                            </span>
                          )}
                        </td>
                        <td className="py-3.5 px-3 text-right">
                          {isPending && isBTC ? (
                            <div className="flex justify-end gap-1.5">
                              <button
                                onClick={() => onManageTransfer(tr.id, 'rejected')}
                                className="p-1 px-2.5 bg-red-950 hover:bg-red-900 border border-red-900 text-red-400 rounded-lg text-[10px] font-bold"
                                title="Từ chối"
                              >
                                Hủy giao dịch
                              </button>
                              <button
                                onClick={() => onManageTransfer(tr.id, 'approved')}
                                className="p-1 px-2.5 bg-emerald-600 hover:bg-emerald-500 text-white rounded-lg text-[10px] font-extrabold"
                                title="Phê duyệt"
                              >
                                Ký duyệt
                              </button>
                            </div>
                          ) : (
                            <span className="text-[10px] font-mono text-slate-500">BTC Quản lý</span>
                          )}
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* SALES MODAL: Post Player for Sale */}
      {showSalesModal && (
        <div className="fixed inset-0 bg-slate-950/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-800 w-full max-w-sm rounded-2xl p-6 shadow-2xl relative animate-in zoom-in-95 duration-150">
            <h3 className="text-lg font-bold text-white flex items-center space-x-2 border-b border-slate-800 pb-3">
              <Plus className="text-indigo-400 w-5 h-5" />
              <span>Đăng Bán Cầu Thủ Lên Thị Trường</span>
            </h3>

            <form onSubmit={handleSaleSubmit} className="mt-4 space-y-4 text-xs font-serif">
              <div>
                <label className="block text-[10px] font-mono font-bold text-slate-350 uppercase mb-1">
                  Chọn Cầu Thủ CLB Cuả Bạn
                </label>
                <select
                  required
                  value={sellingPlayerId}
                  onChange={(e) => setSellingPlayerId(e.target.value)}
                  className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-2 text-sm font-sans focus:outline-none focus:border-indigo-500"
                >
                  <option value="">-- Chọn cầu thủ --</option>
                  {mySellablePlayers.map(p => (
                    <option key={p.id} value={p.id}>
                      {p.name} ({p.position} - OVR {p.ovr})
                    </option>
                  ))}
                </select>
                {mySellablePlayers.length === 0 && (
                  <p className="text-[10px] text-rose-500 font-mono mt-1">
                    Bạn không còn cầu thủ nào hợp lệ chưa treo niêm yết!
                  </p>
                )}
              </div>

              <div>
                <label className="block text-[10px] font-mono font-bold text-slate-350 uppercase mb-1">
                  Đặt Giá Niêm Yết Đăng Bán (Tr Coin)
                </label>
                <input
                  type="number"
                  min={1}
                  required
                  className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-2 text-sm font-mono focus:outline-none focus:border-indigo-500"
                  value={salesPrice}
                  onChange={(e) => setSalesPrice(Math.max(1, parseInt(e.target.value) || 1))}
                />
              </div>

              <div className="flex justify-end space-x-3 pt-4 border-t border-slate-800">
                <button
                  type="button"
                  className="px-4 py-2 text-slate-400 hover:text-white rounded-lg transition text-xs font-sans font-bold"
                  onClick={() => setShowSalesModal(false)}
                >
                  Đóng
                </button>
                <button
                  type="submit"
                  disabled={!sellingPlayerId}
                  className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white font-extrabold rounded-lg transition disabled:opacity-50 text-xs font-sans"
                >
                  Treo Bán Công Khai
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* BUY MODAL: Bid buyout Price */}
      {buyingPlayer && (
        <div className="fixed inset-0 bg-slate-950/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-800 w-full max-w-sm rounded-2xl p-6 shadow-2xl relative animate-in zoom-in-95 duration-150">
            <h3 className="text-lg font-bold text-white flex items-center space-x-2 border-b border-slate-800 pb-3">
              <DollarSign className="text-red-500 w-5 h-5 animate-pulse" />
              <span>Gửi Đơn Đấu Giá Mua Cầu Thủ</span>
            </h3>

            <form onSubmit={handleBuySubmit} className="mt-4 space-y-4 text-xs font-serif">
              <div className="bg-slate-950 border border-slate-850 p-3 rounded-lg font-mono">
                <p className="text-slate-400">Tên cầu thủ đặt thầu:</p>
                <p className="font-extrabold text-white text-sm">{buyingPlayer.name}</p>
                <p className="text-[10px] text-slate-405 mt-1">OVR: {buyingPlayer.ovr} | CLB Gốc: {getTeamName(buyingPlayer.teamId)}</p>
              </div>

              <div>
                <label className="block text-[10px] font-mono font-bold text-slate-350 uppercase mb-1">
                  Nhập số tiền thầu mua sắm (Tr Coin)
                </label>
                <input
                  type="number"
                  min={1}
                  required
                  className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-2 text-sm font-mono focus:outline-none focus:border-red-500"
                  value={buyOfferPrice}
                  onChange={(e) => setBuyOfferPrice(Math.max(1, parseInt(e.target.value) || 1))}
                />
                <p className="text-[10px] text-slate-500 font-mono mt-1">
                  * Giá gốc niêm yết hiện tự động điền để giảm tải sai sót.
                </p>
              </div>

              <div className="flex justify-end space-x-3 pt-3">
                <button
                  type="button"
                  className="px-4 py-2 text-slate-400 hover:text-white rounded-lg transition text-xs font-sans font-bold"
                  onClick={() => setBuyingPlayer(null)}
                >
                  Hủy đơn
                </button>
                <button
                  type="submit"
                  className="px-4 py-2 bg-red-600 hover:bg-red-500 text-white font-extrabold rounded-lg transition text-xs font-sans"
                >
                  Xác Nhận Đấu Thầu
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
