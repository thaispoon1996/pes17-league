import React, { useState } from 'react';
import { User, UserRole } from '../types';
import { Trophy, ShieldCheck, UserCircle, LogIn, ChevronDown, Wifi, Swords, RefreshCw } from 'lucide-react';

interface NavbarProps {
  currentUser: User | null;
  onSelectRole: (role: UserRole, email?: string) => void;
  activeTab: string;
  setActiveTab: (tab: string) => void;
  availableUsers: User[];
  onUpdateParsecId: (newParsecId: string) => void;
}

export default function Navbar({
  currentUser,
  onSelectRole,
  activeTab,
  setActiveTab,
  availableUsers,
  onUpdateParsecId,
}: NavbarProps) {
  const [showUserDropdown, setShowUserDropdown] = useState(false);
  const [showParsecModal, setShowParsecModal] = useState(false);
  const [tempParsecId, setTempParsecId] = useState(currentUser?.parsecId || '');

  const navigationItems = [
    { id: 'home', label: 'Trang Chủ', icon: Trophy },
    { id: 'leagues', label: 'Giải Đấu & Lịch Trình', icon: Swords },
    { id: 'teams', label: 'Đội Bóng & Đội Hình', icon: ShieldCheck },
    { id: 'transfers', label: 'Chợ Chuyển Nhượng', icon: Trophy }, // custom mapped later
    { id: 'stats', label: 'Thống Kê Thống Soái', icon: Trophy },
  ];

  if (currentUser?.role === 'admin') {
    navigationItems.push({ id: 'admin', label: 'Quản Trị BTC', icon: ShieldCheck });
  }

  const handleParsecSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onUpdateParsecId(tempParsecId);
    setShowParsecModal(false);
  };

  return (
    <header id="app-navbar" className="sticky top-0 z-50 w-full bg-slate-900 text-white border-b border-slate-800 shadow-lg">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex items-center justify-between h-16">
          {/* Logo Brand */}
          <div className="flex items-center space-x-3 cursor-pointer" onClick={() => setActiveTab('home')}>
            <div className="bg-red-600 hover:bg-red-500 text-white p-2.5 rounded-lg flex items-center justify-center shadow-lg shadow-red-900/30 transition-all duration-300 transform hover:scale-105">
              <span className="font-extrabold text-sm tracking-widest uppercase">PES 17</span>
            </div>
            <div>
              <h1 className="text-lg font-bold tracking-tight bg-gradient-to-r from-white via-slate-200 to-slate-400 bg-clip-text text-transparent">
                Online League
              </h1>
              <p className="text-[10px] text-slate-400 font-mono tracking-wider uppercase">Vietnam Championship</p>
            </div>
          </div>

          {/* Main Desktop Tour Navigation */}
          <nav className="hidden md:flex space-x-1">
            {navigationItems.map((item) => {
              const isActive = activeTab === item.id;
              return (
                <button
                  key={item.id}
                  onClick={() => setActiveTab(item.id)}
                  className={`px-3.5 py-2 rounded-lg text-sm font-semibold tracking-wide transition-all duration-200 ${
                    isActive
                      ? 'bg-red-600/10 text-red-500 border-b-2 border-red-500 rounded-b-none'
                      : 'text-slate-300 hover:text-white hover:bg-slate-800'
                  }`}
                >
                  {item.label}
                </button>
              );
            })}
          </nav>

          {/* User Role Quick Switcher & Parsec integration */}
          <div className="flex items-center space-x-3">
            {/* Parsec Info Badge */}
            {currentUser && currentUser.role === 'coach' && (
              <button
                onClick={() => {
                  setTempParsecId(currentUser.parsecId || '');
                  setShowParsecModal(true);
                }}
                className="hidden sm:flex items-center space-x-1.5 px-3 py-1.5 bg-indigo-950/40 text-indigo-400 border border-indigo-800/60 rounded-full hover:bg-indigo-900/40 hover:text-indigo-300 transition-all text-xs font-mono"
              >
                <Wifi className="w-3 H-3 text-indigo-400 animate-pulse" />
                <span className="opacity-90">Parsec:</span>
                <span className="font-semibold underline leading-none">
                  {currentUser.parsecId || 'Nhấn để thêm'}
                </span>
              </button>
            )}

            {/* Simulated Live status indicator for Guest */}
            {(!currentUser || currentUser.role === 'guest') && (
              <span className="hidden sm:inline-flex items-center space-x-1.5 px-2.5 py-1 bg-emerald-950/40 text-emerald-400 border border-emerald-800/40 rounded-full text-[10px] font-mono font-medium tracking-wide">
                <span className="w-1.5 h-1.5 bg-emerald-400 rounded-full animate-ping"></span>
                <span>CHẾ ĐỘ KHÁCH</span>
              </span>
            )}

            {/* Profile Dropdown / Switcher */}
            <div className="relative">
              <button
                onClick={() => setShowUserDropdown(!showUserDropdown)}
                className="flex items-center space-x-2 bg-slate-800/90 text-slate-200 hover:text-white px-3.5 py-1.5 rounded-lg border border-slate-700 hover:border-slate-600 transition-all font-medium text-xs sm:text-sm text-left"
              >
                <div className="w-6 h-6 rounded-full overflow-hidden bg-slate-700 flex-shrink-0 border border-slate-600">
                  <img
                    referrerPolicy="no-referrer"
                    src={currentUser?.avatar || 'https://images.unsplash.com/photo-1544005313-94ddf0286df2?w=100&auto=format&fit=crop&q=80'}
                    alt="avatar"
                    className="w-full h-full object-cover"
                  />
                </div>
                <div className="hidden lg:block max-w-[120px] truncate">
                  <p className="leading-tight text-white font-semibold truncate">
                    {currentUser?.name || 'Khách viếng thăm'}
                  </p>
                  <p className="text-[10px] text-slate-400 font-mono leading-none capitalize">
                    {currentUser?.role === 'admin' ? 'Ban Tổ Chức' : currentUser?.role === 'coach' ? 'Huấn Luyện Viên' : 'Người Xem'}
                  </p>
                </div>
                <ChevronDown className="w-4 h-4 text-slate-400 flex-shrink-0" />
              </button>

              {showUserDropdown && (
                <div className="absolute right-0 mt-2 w-72 bg-slate-900 border border-slate-800 rounded-xl shadow-2xl overflow-hidden py-1.5 animate-in fade-in slide-in-from-top-3 duration-200">
                  <div className="px-4 py-3 border-b border-indigo-950/30 bg-slate-950/40">
                    <p className="text-xs text-slate-400 font-mono uppercase tracking-wider">Tài khoản hiện tại</p>
                    <p className="font-bold text-white text-sm mt-0.5">{currentUser?.name || 'Khách viếng thăm'}</p>
                    <p className="text-xs text-slate-400 mt-1">{currentUser?.email || 'Xem toàn cảnh hệ thống'}</p>
                    {currentUser?.role === 'coach' && currentUser?.parsecId && (
                      <p className="text-[10px] font-mono text-indigo-400 mt-1">ID Parsec: {currentUser.parsecId}</p>
                    )}
                  </div>

                  <div className="px-3 py-2 bg-slate-950/20 text-[11px] text-slate-400 border-b border-slate-800">
                    <p className="font-semibold text-slate-300">Đăng nhập tài khoản khác (Giả lập chuyển vai)</p>
                    <p className="mt-0.5">Trải nghiệm phân quyền Admin / HLV MU / Chelsea:</p>
                  </div>

                  {/* Built-in role switcher options */}
                  <div className="max-h-56 overflow-y-auto">
                    {availableUsers.map((u) => {
                      const isMe = currentUser?.id === u.id;
                      return (
                        <button
                          key={u.id}
                          onClick={() => {
                            onSelectRole(u.role, u.email);
                            setShowUserDropdown(false);
                          }}
                          className={`w-full flex items-center justify-between px-4 py-2.5 text-left text-xs transition-colors ${
                            isMe ? 'bg-red-600/10 text-red-400 font-semibold' : 'text-slate-300 hover:bg-slate-800/80 hover:text-white'
                          }`}
                        >
                          <div className="flex items-center space-x-2 truncate">
                            <span className="w-1.5 h-1.5 rounded-full bg-slate-500"></span>
                            <div className="truncate">
                              <p className="truncate font-semibold">{u.name}</p>
                              <p className="text-[10px] text-slate-400 truncate">{u.role === 'admin' ? 'Ban Tổ Chức' : 'Coach'} - {u.email}</p>
                            </div>
                          </div>
                          {u.role === 'admin' ? (
                            <span className="bg-red-950 text-red-400 border border-red-900/55 px-1.5 py-0.5 rounded text-[9px] font-mono">BTC</span>
                          ) : (
                            <span className="bg-slate-800 text-slate-300 px-1.5 py-0.5 rounded text-[9px] font-mono">HLV</span>
                          )}
                        </button>
                      );
                    })}

                    <button
                      onClick={() => {
                        onSelectRole('guest');
                        setShowUserDropdown(false);
                      }}
                      className={`w-full flex items-center justify-between px-4 py-2.5 text-left text-xs border-t border-slate-800 transition-colors ${
                        !currentUser || currentUser.role === 'guest'
                          ? 'bg-red-600/10 text-red-500 font-semibold'
                          : 'text-slate-400 hover:bg-slate-800 hover:text-white'
                      }`}
                    >
                      <span className="flex items-center space-x-2">
                        <UserCircle className="w-3.5 h-3.5 text-slate-400" />
                        <span>Xem với tư cách KHÁCH</span>
                      </span>
                      <span className="bg-emerald-950/50 text-emerald-400 border border-emerald-900/40 px-1.5 py-0.5 rounded text-[9px] font-mono">GUEST</span>
                    </button>
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Mobile Tab menu helper */}
        <div className="flex md:hidden border-t border-slate-800 overflow-x-auto scrollbar-none py-2 space-x-1">
          {navigationItems.map((item) => {
            const isActive = activeTab === item.id;
            return (
              <button
                key={item.id}
                onClick={() => setActiveTab(item.id)}
                className={`flex-shrink-0 px-3 py-1.5 rounded-full text-xs font-semibold whitespace-nowrap transition-all ${
                  isActive ? 'bg-red-600 text-white' : 'bg-slate-800 text-slate-300 hover:bg-slate-700'
                }`}
              >
                {item.label}
              </button>
            );
          })}
        </div>
      </div>

      {/* Modal to update Parsec Connection ID */}
      {showParsecModal && (
        <div className="fixed inset-0 bg-slate-950/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-800 w-full max-w-md rounded-2xl p-6 shadow-2xl animate-in zoom-in-95 duration-150">
            <h3 className="text-lg font-bold text-white flex items-center space-x-2">
              <Wifi className="text-indigo-400 animate-pulse w-5 h-5" />
              <span>Thiết lập thông tin kết nối Parsec</span>
            </h3>
            <p className="text-xs text-slate-400 mt-2">
              Lưu ID Parsec giúp đối thủ có thể tìm kiếm, gửi yêu cầu kết nối chơi game trực tiếp thông qua ứng dụng khách Parsec với hiệu năng mượt mà nhất.
            </p>

            <form onSubmit={handleParsecSubmit} className="mt-4 space-y-4">
              <div>
                <label className="block text-xs font-semibold text-slate-300 font-mono tracking-wider uppercase mb-1">
                  Nhập ID Parsec của HLV
                </label>
                <input
                  type="text"
                  required
                  placeholder="Ví dụ: QuocThai#4829"
                  className="w-full bg-slate-950 border border-slate-800 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-red-500 font-mono"
                  value={tempParsecId}
                  onChange={(e) => setTempParsecId(e.target.value)}
                />
              </div>

              <div className="flex justify-end space-x-3 pt-2">
                <button
                  type="button"
                  className="px-4 py-2 text-xs text-slate-400 hover:text-white hover:bg-slate-850 rounded-lg Transition"
                  onClick={() => setShowParsecModal(false)}
                >
                  Hủy bỏ
                </button>
                <button
                  type="submit"
                  className="px-4 py-2 text-xs bg-indigo-600 hover:bg-indigo-500 text-white font-bold rounded-lg transition shadow-lg shadow-indigo-900/40"
                >
                  Xác nhận & Lưu
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </header>
  );
}
