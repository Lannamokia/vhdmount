// 简易请求封装
async function getJSON(url) {
  const res = await fetch(url, { credentials: 'include' });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) {
    const err = new Error(data?.message || data?.error || `GET ${url} ${res.status}`);
    err.status = res.status;
    err.data = data;
    throw err;
  }
  return data;
}

async function postJSON(url, body) {
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify(body || {})
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data?.message || data?.error || `POST ${url} ${res.status}`);
  return data;
}

async function del(url) {
  const res = await fetch(url, { method: 'DELETE', credentials: 'include' });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data?.message || data?.error || `DELETE ${url} ${res.status}`);
  return data;
}

// UI工具
function toast(msg, ok = true) {
  const el = document.getElementById('toast');
  el.textContent = msg;
  el.classList.toggle('ok', !!ok);
  el.classList.toggle('bad', !ok);
  el.hidden = false;
  el.classList.add('show');
  if (el._t) clearTimeout(el._t);
  if (el._tHide) clearTimeout(el._tHide);
  el._t = setTimeout(() => {
    el.classList.remove('show');
    el._tHide = setTimeout(() => { el.hidden = true; }, 250);
  }, 2500);
}

function formatUptime(seconds) {
  const s = Math.floor(seconds);
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  const rest = s % 60;
  return [
    d ? `${d}天` : null,
    h ? `${h}小时` : null,
    m ? `${m}分` : null,
    rest ? `${rest}秒` : null,
  ].filter(Boolean).join(' ');
}

function setAuthUI(isAuth) {
  document.getElementById('loginBtn').hidden = !!isAuth;
  document.getElementById('logoutBtn').hidden = !isAuth;
  const machinesLink = document.getElementById('machinesLinkBtn');
  if (machinesLink) machinesLink.hidden = !isAuth;
  document.querySelectorAll('[data-auth-only]').forEach((el) => {
    el.classList.toggle('auth-on', !!isAuth);
  });
}

function setServerStatus(ok) {
  const el = document.getElementById('serverStatus');
  el.textContent = ok ? '状态: 运行中' : '状态: 异常';
  el.classList.toggle('ok', !!ok);
  el.classList.toggle('bad', !ok);
}

// 状态加载
async function loadStatus() {
  try {
    const status = await getJSON('/api/status');
    setServerStatus(true);
    document.getElementById('currentKeyword').textContent = status.BootImageSelected || '—';
    document.getElementById('uptime').textContent = formatUptime(status.uptime || 0);
    document.getElementById('version').textContent = status.version || '—';
  } catch (e) {
    setServerStatus(false);
    toast(`获取状态失败: ${e.message}`, false);
  }
}

// 认证状态
async function loadAuth() {
  try {
    const data = await getJSON('/api/auth/check');
    setAuthUI(!!data.isAuthenticated);
  } catch (e) {
    setAuthUI(false);
  }
}

// 首页不再包含机台列表，迁移至二级页面

// 入口
document.addEventListener('DOMContentLoaded', () => {
  // 初次加载
  loadStatus();
  loadAuth();
  setInterval(loadStatus, 30000);

  // 登录弹窗
  const loginModal = document.getElementById('loginModal');
  document.getElementById('loginBtn').addEventListener('click', () => { loginModal.hidden = false; });
  document.getElementById('loginClose').addEventListener('click', () => { loginModal.hidden = true; });
  document.getElementById('loginSubmit').addEventListener('click', async () => {
    const password = document.getElementById('loginPassword').value;
    if (!password) { toast('请输入密码', false); return; }
    try {
      const res = await postJSON('/api/auth/login', { password });
      toast(res.message || '登录成功');
      loginModal.hidden = true;
      document.getElementById('loginPassword').value = '';
      await loadAuth();
    } catch (e) { toast(e.message || '登录失败', false); }
  });

  // 登出
  document.getElementById('logoutBtn').addEventListener('click', async () => {
    try {
      const res = await postJSON('/api/auth/logout');
      toast(res.message || '已登出');
      await loadAuth();
    } catch (e) { toast(e.message || '登出失败', false); }
  });

  // 设置全局VHD
  document.getElementById('setVhdBtn').addEventListener('click', async () => {
    const val = document.getElementById('setVhdInput').value.trim().toUpperCase();
    if (!val) { toast('请输入有效的VHD关键词', false); return; }
    try {
      const res = await postJSON('/api/set-vhd', { BootImageSelected: val });
      toast(res.message || 'VHD关键词更新成功');
      document.getElementById('setVhdInput').value = '';
      await loadStatus();
    } catch (e) { toast(e.message || '更新失败', false); }
  });

  // 查询机台
  document.getElementById('queryBtn').addEventListener('click', async () => {
    const id = document.getElementById('queryMachineId').value.trim();
    if (!id) { toast('请输入机台ID', false); return; }
    try {
      const prot = await getJSON(`/api/protect?machineId=${encodeURIComponent(id)}`);
      document.getElementById('queryProtected').textContent = prot.protected ? '启用' : '关闭';
      document.getElementById('queryResult').hidden = false;
      toast('查询成功');
    } catch (e) {
      if (e && e.status === 404) {
        document.getElementById('queryProtected').textContent = '未找到机台';
        document.getElementById('queryResult').hidden = false;
        toast(e.message || '机台不存在', false);
      } else {
        document.getElementById('queryResult').hidden = true;
        toast(e.message || '查询失败', false);
      }
    }
  });

  // 修改密码
  document.getElementById('changePwdBtn').addEventListener('click', async () => {
    const currentPassword = document.getElementById('pwdCurrent').value;
    const newPassword = document.getElementById('pwdNew').value;
    const confirmPassword = document.getElementById('pwdConfirm').value;
    if (!currentPassword || !newPassword || !confirmPassword) {
      toast('请填写所有密码字段', false); return;
    }
    try {
      const res = await postJSON('/api/auth/change-password', { currentPassword, newPassword, confirmPassword });
      toast(res.message || '密码修改成功');
      document.getElementById('pwdCurrent').value = '';
      document.getElementById('pwdNew').value = '';
      document.getElementById('pwdConfirm').value = '';
    } catch (e) { toast(e.message || '密码修改失败', false); }
  });
});