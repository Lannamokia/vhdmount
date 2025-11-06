// 公共方法
async function getJSON(url) {
  const res = await fetch(url, { credentials: 'include' });
  if (!res.ok) throw new Error(`GET ${url} ${res.status}`);
  return res.json();
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

function setAuthUI(isAuth) {
  document.getElementById('loginBtn').hidden = !!isAuth;
  document.getElementById('logoutBtn').hidden = !isAuth;
  document.getElementById('authMessage').hidden = !!isAuth;
  document.getElementById('machinesPanel').hidden = !isAuth;
}

// 加载与渲染机台列表
async function loadMachines() {
  try {
    const data = await getJSON('/api/machines');
    const tbody = document.getElementById('machinesTbody');
    const search = document.getElementById('searchInput').value.trim().toLowerCase();
    let machines = data?.machines || [];
    if (search) {
      machines = machines.filter(m => (m.machine_id || '').toLowerCase().includes(search));
    }
    if (!machines.length) {
      tbody.innerHTML = '<tr><td colspan="5" class="muted">暂无机台数据</td></tr>';
      return;
    }
    tbody.innerHTML = '';
    for (const m of machines) {
      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td><code>${m.machine_id}</code></td>
        <td>${m.vhd_keyword || '—'}</td>
        <td>${m.protected ? '启用' : '关闭'}</td>
        <td>${m.last_seen ? new Date(m.last_seen).toLocaleString() : '—'}</td>
        <td class="actions">
          <input class="input" style="width:160px" placeholder="新VHD关键词" data-act="vhd-input" />
          <button class="btn" data-act="vhd-update">更新</button>
          <button class="btn" data-act="protect-toggle">${m.protected ? '取消保护' : '保护'}</button>
          <button class="btn" data-act="delete" style="color:#ffb4b4">删除</button>
        </td>
      `;
      tr.querySelector('[data-act="vhd-update"]').addEventListener('click', async () => {
        const val = tr.querySelector('[data-act="vhd-input"]').value.trim().toUpperCase();
        if (!val) { toast('请输入有效的VHD关键词', false); return; }
        try {
          await postJSON(`/api/machines/${encodeURIComponent(m.machine_id)}/vhd`, { vhdKeyword: val });
          toast('机台VHD已更新');
          await loadMachines();
        } catch (e) { toast(e.message, false); }
      });
      tr.querySelector('[data-act="protect-toggle"]').addEventListener('click', async () => {
        try {
          await postJSON('/api/protect', { machineId: m.machine_id, protected: !m.protected });
          toast('保护状态已更新');
          await loadMachines();
        } catch (e) { toast(e.message, false); }
      });
      tr.querySelector('[data-act="delete"]').addEventListener('click', async () => {
        if (!confirm(`确认删除机台 ${m.machine_id} ?`)) return;
        try {
          await del(`/api/machines/${encodeURIComponent(m.machine_id)}`);
          toast('机台已删除');
          await loadMachines();
        } catch (e) { toast(e.message, false); }
      });
      tbody.appendChild(tr);
    }
  } catch (e) {
    const tbody = document.getElementById('machinesTbody');
    tbody.innerHTML = `<tr><td colspan="5" class="muted">加载失败: ${e.message}</td></tr>`;
  }
}

// 入口
document.addEventListener('DOMContentLoaded', async () => {
  // 认证
  try {
    const data = await getJSON('/api/auth/check');
    setAuthUI(!!data.isAuthenticated);
    if (data.isAuthenticated) {
      await loadMachines();
    }
  } catch (e) {
    setAuthUI(false);
  }

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
      setAuthUI(true);
      await loadMachines();
    } catch (e) { toast(e.message || '登录失败', false); }
  });

  // 登出
  document.getElementById('logoutBtn').addEventListener('click', async () => {
    try {
      const res = await postJSON('/api/auth/logout');
      toast(res.message || '已登出');
      setAuthUI(false);
    } catch (e) { toast(e.message || '登出失败', false); }
  });

  // 搜索与刷新
  document.getElementById('searchBtn').addEventListener('click', loadMachines);
  document.getElementById('refreshBtn').addEventListener('click', loadMachines);
});