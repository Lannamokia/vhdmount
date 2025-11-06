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

// ===== EVHD 密码查询与解密工具 =====
function computeEvhdSecretTz8() {
  const now = new Date();
  // 转换到UTC+8小时
  const tz8 = new Date(now.getTime() + (8 - now.getTimezoneOffset() / 60) * 60 * 60 * 1000);
  const YY = String(tz8.getUTCFullYear()).slice(-2);
  const MM = String(tz8.getUTCMonth() + 1).padStart(2, '0');
  const DD = String(tz8.getUTCDate()).padStart(2, '0');
  const HH = String(tz8.getUTCHours()).padStart(2, '0');
  return `evhd${YY}${MM}${DD}${HH}`;
}

function strToUtf8Bytes(str) {
  return new TextEncoder().encode(str);
}

async function sha256Bytes(dataBytes) {
  const buf = await crypto.subtle.digest('SHA-256', dataBytes);
  return new Uint8Array(buf);
}

function base64ToBytes(b64) {
  const bin = atob(b64);
  const arr = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
  return arr;
}

async function decryptEvhdPassword(secret, cipherB64) {
  const keyHash = await sha256Bytes(strToUtf8Bytes(secret));
  const ivHash = await sha256Bytes(strToUtf8Bytes(secret + '_iv'));
  const iv = ivHash.slice(0, 16);
  const key = await crypto.subtle.importKey(
    'raw',
    keyHash,
    { name: 'AES-CBC' },
    false,
    ['decrypt']
  );
  const cipherBytes = base64ToBytes(cipherB64);
  const plainBuf = await crypto.subtle.decrypt({ name: 'AES-CBC', iv }, key, cipherBytes);
  return new TextDecoder().decode(plainBuf);
}

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
      tbody.innerHTML = '<tr><td colspan="5" class="muted">暂无机台数据</td></tr>';
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
        <td class="actions">
          <input class="input" style="width:160px" placeholder="新VHD关键词" data-act="vhd-input" />
          <button class="btn" data-act="vhd-update">更新</button>
          <input class="input" style="width:160px" type="password" placeholder="EVHD密码" data-act="evhd-input" value="${m.evhd_password || ''}" />
          <button class="btn" data-act="evhd-update">更新EVHD密码</button>
          <button class="btn" data-act="evhd-query">查询EVHD密码</button>
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
      tr.querySelector('[data-act="evhd-update"]').addEventListener('click', async () => {
        const val = (tr.querySelector('[data-act="evhd-input"]')?.value || '').trim();
        try {
          await postJSON(`/api/machines/${encodeURIComponent(m.machine_id)}/evhd-password`, { evhdPassword: val });
          toast('EVHD密码已更新');
          await loadMachines();
        } catch (e) { toast(e.message, false); }
      });
      tr.querySelector('[data-act="evhd-query"]').addEventListener('click', async () => {
        try {
          const secret = computeEvhdSecretTz8();
          const data = await getJSON(`/api/evhd-password?machineId=${encodeURIComponent(m.machine_id)}&secret=${encodeURIComponent(secret)}`);
          const cipher = data?.evhdPassword || '';
          if (!cipher) { toast('未设置EVHD密码', false); return; }
          try {
            const plain = await decryptEvhdPassword(secret, cipher);
            const copied = await copyToClipboard(plain);
            if (copied) {
              toast('EVHD密码已复制到剪贴板');
            } else {
              alert(`机台 ${m.machine_id} 的EVHD密码：\n${plain}\n\n复制失败，请手动复制。`);
            }
          } catch (decErr) {
            console.error('解密失败', decErr);
            toast('解密失败，请确认时间或密文', false);
          }
        } catch (e) {
          toast(e.message || '查询失败', false);
        }
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