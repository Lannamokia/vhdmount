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

// 通用确认弹窗（替代原生confirm，避免环境差异）
function showConfirmModal(message) {
  return new Promise(resolve => {
    const modal = document.getElementById('confirmModal');
    const msgEl = document.getElementById('confirmMessage');
    const okBtn = document.getElementById('confirmOk');
    const cancelBtn = document.getElementById('confirmCancel');
    const closeBtn = document.getElementById('confirmClose');
    msgEl.textContent = message || '';
    modal.hidden = false;
    const cleanup = () => {
      modal.hidden = true;
      okBtn.onclick = null;
      cancelBtn.onclick = null;
      closeBtn.onclick = null;
    };
    okBtn.onclick = () => { cleanup(); resolve(true); };
    cancelBtn.onclick = () => { cleanup(); resolve(false); };
    closeBtn.onclick = () => { cleanup(); resolve(false); };
  });
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

// 旧版EVHD解密工具已移除，统一改用RSA封装下发到客户端

async function copyToClipboard(text) {
  try {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch (_) {}
  try {
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.setAttribute('readonly', '');
    ta.style.position = 'fixed';
    ta.style.top = '-9999px';
    document.body.appendChild(ta);
    ta.focus();
    ta.select();
    const ok = document.execCommand('copy');
    document.body.removeChild(ta);
    return ok;
  } catch (_) {
    return false;
  }
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
      tbody.innerHTML = '<tr><td colspan="9" class="muted">暂无机台数据</td></tr>';
      return;
    }
    tbody.innerHTML = '';
    for (const m of machines) {
      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td><code>${m.machine_id}</code></td>
        <td>${m.vhd_keyword || '—'}</td>
        <td>${m.evhd_password ? '已设置' : '未设置'}</td>
        <td>${m.protected ? '启用' : '关闭'}</td>
        <td>${m.last_seen ? new Date(m.last_seen).toLocaleString() : '—'}</td>
        <td>${m.key_id || '—'}</td>
        <td>${m.key_type || '—'}</td>
        <td>${m.approved ? '已审批' : '未审批'}</td>
        <td class="actions">
          <input class="input" style="width:160px" placeholder="新VHD关键词" data-act="vhd-input" />
          <button class="btn" data-act="vhd-update">更新</button>
          <input class="input" style="width:160px" type="password" placeholder="EVHD密码" data-act="evhd-input" value="${m.evhd_password || ''}" />
          <button class="btn" data-act="evhd-update">更新EVHD密码</button>
          <button class="btn" data-act="evhd-show">查询明文</button>
          <button class="btn" data-act="approve-toggle">${m.approved ? '取消审批' : '审批通过'}</button>
          <button class="btn" type="button" data-act="revoke">重置注册状态</button>
          <button class="btn" type="button" data-act="protect-toggle">${m.protected ? '取消保护' : '保护'}</button>
          <button class="btn" type="button" data-act="delete" style="color:#ffb4b4">删除</button>
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
      tr.querySelector('[data-act="evhd-update"]').addEventListener('click', async () => {
        const val = (tr.querySelector('[data-act="evhd-input"]')?.value || '').trim();
        try {
          await postJSON(`/api/machines/${encodeURIComponent(m.machine_id)}/evhd-password`, { evhdPassword: val });
          toast('EVHD密码已更新');
          await loadMachines();
        } catch (e) { toast(e.message, false); }
      });
      tr.querySelector('[data-act="evhd-show"]').addEventListener('click', async () => {
        try {
          const resp = await getJSON(`/api/evhd-password/plain?machineId=${encodeURIComponent(m.machine_id)}`);
          const pw = resp?.evhdPassword || '';
          if (!pw) { toast('该机台未设置EVHD密码', false); return; }
          const copied = await copyToClipboard(pw);
          toast(copied ? '已复制EVHD明文到剪贴板' : `EVHD明文：${pw}`);
        } catch (e) { toast(e.message || '查询明文失败', false); }
      });
      tr.querySelector('[data-act="approve-toggle"]').addEventListener('click', async () => {
        try {
          await postJSON(`/api/machines/${encodeURIComponent(m.machine_id)}/approve`, { approved: !m.approved });
          toast('审批状态已更新');
          await loadMachines();
        } catch (e) { toast(e.message, false); }
      });
      const resetBtn = tr.querySelector('[data-act="revoke"]');
      resetBtn.addEventListener('click', async (ev) => {
        ev.preventDefault();
        ev.stopPropagation();
        const ok = await showConfirmModal(`确认重置机台 ${m.machine_id} 的注册状态？\n这将删除密钥并重置审批为未审批。`);
        if (!ok) return;
        try {
          resetBtn.disabled = true;
          await postJSON(`/api/machines/${encodeURIComponent(m.machine_id)}/revoke`, {});
          toast('已重置机台注册状态');
          await loadMachines();
        } catch (e) { toast(e.message, false); }
        finally {
          resetBtn.disabled = false;
        }
      });
      const protectBtn = tr.querySelector('[data-act="protect-toggle"]');
      protectBtn.addEventListener('click', async (ev) => {
        ev.preventDefault();
        ev.stopPropagation();
        const label = m.protected ? '取消保护' : '保护';
        const ok = await showConfirmModal(`确认${label}机台 ${m.machine_id}？`);
        if (!ok) return;
        try {
          protectBtn.disabled = true;
          await postJSON('/api/protect', { machineId: m.machine_id, protected: !m.protected });
          toast('保护状态已更新');
          await loadMachines();
        } catch (e) { toast(e.message, false); }
        finally { protectBtn.disabled = false; }
      });

      const delBtn = tr.querySelector('[data-act="delete"]');
      delBtn.addEventListener('click', async (ev) => {
        ev.preventDefault();
        ev.stopPropagation();
        const ok = await showConfirmModal(`确认删除机台 ${m.machine_id}？该操作不可恢复。`);
        if (!ok) return;
        try {
          delBtn.disabled = true;
          await del(`/api/machines/${encodeURIComponent(m.machine_id)}`);
          toast('机台已删除');
          await loadMachines();
        } catch (e) { toast(e.message, false); }
        finally { delBtn.disabled = false; }
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

  // 添加机台
  const addBtn = document.getElementById('addMachineBtn');
  if (addBtn) {
    addBtn.addEventListener('click', async () => {
      const id = (document.getElementById('addMachineId').value || '').trim();
      let vhd = (document.getElementById('addVhdKeyword').value || '').trim().toUpperCase();
      const evhd = (document.getElementById('addEvhdPassword').value || '').trim();
      if (!id) { toast('请填写机台ID', false); return; }
      if (!vhd) vhd = 'SDEZ';
      try {
        // 通过设置VHD关键词的接口实现创建（后端不存在时会自动创建）
        await postJSON(`/api/machines/${encodeURIComponent(id)}/vhd`, { vhdKeyword: vhd });
        // 初次创建时重置审批与吊销状态
        await postJSON(`/api/machines/${encodeURIComponent(id)}/approve`, { approved: false }).catch(() => {});
        // 可选地设置初始EVHD密码
        if (evhd) {
          await postJSON(`/api/machines/${encodeURIComponent(id)}/evhd-password`, { evhdPassword: evhd });
        }
        toast('机台添加成功');
        // 清理输入并刷新列表
        document.getElementById('addMachineId').value = '';
        document.getElementById('addVhdKeyword').value = '';
        document.getElementById('addEvhdPassword').value = '';
        await loadMachines();
      } catch (e) {
        toast(e.message || '添加机台失败', false);
      }
    });
  }
});