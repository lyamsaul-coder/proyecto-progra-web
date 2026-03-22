// ============================================================
// TEMA - MODO OSCURO
// ============================================================

function initTheme() {
    const saved = localStorage.getItem('probook_theme') || 'light';
    document.documentElement.setAttribute('data-theme', saved);
}

function toggleTheme() {
    const current = document.documentElement.getAttribute('data-theme') || 'light';
    const next = current === 'light' ? 'dark' : 'light';
    document.documentElement.setAttribute('data-theme', next);
    localStorage.setItem('probook_theme', next);
    updateThemeBtn();
}

function updateThemeBtn() {
    const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
    document.querySelectorAll('.theme-toggle').forEach(btn => {
        btn.innerHTML = isDark
            ? '<span>☀️</span> Claro'
            : '<span>🌙</span> Oscuro';
    });
}

// Inicializar tema al cargar
initTheme();
document.addEventListener('DOMContentLoaded', updateThemeBtn);

// ============================================================
// ProBook - JavaScript Global
// Maneja la comunicacion con el backend y utilidades comunes
// ============================================================

const API_URL = window.location.origin + '/api';

// ============================================================
// MANEJO DE TOKEN JWT
// ============================================================

function getToken() {
    return localStorage.getItem('probook_token');
}

function getUser() {
    const user = localStorage.getItem('probook_user');
    return user ? JSON.parse(user) : null;
}

function saveSession(token, user) {
    localStorage.setItem('probook_token', token);
    localStorage.setItem('probook_user', JSON.stringify(user));
}

function clearSession() {
    localStorage.removeItem('probook_token');
    localStorage.removeItem('probook_user');
}

function isLoggedIn() {
    return !!getToken();
}

function getRole() {
    const user = getUser();
    return user ? user.role : null;
}

// Redirigir si no esta autenticado
function requireAuth(role = null) {
    if (!isLoggedIn()) {
        window.location.href = '/login.html';
        return false;
    }
    if (role && getRole() !== role) {
        window.location.href = '/login.html';
        return false;
    }
    return true;
}

// Cerrar sesion
function logout() {
    clearSession();
    window.location.href = '/login.html';
}

// Limpiar sesion cuando el token expira (401) o al cerrar el servidor
function handleUnauthorized() {
    localStorage.removeItem('probook_token');
    localStorage.removeItem('probook_user');
    window.location.href = '/login.html';
}

// ============================================================
// PETICIONES AL BACKEND
// ============================================================

async function apiGet(endpoint) {
    const response = await fetch(`${API_URL}${endpoint}`, {
        headers: {
            'Authorization': `Bearer ${getToken()}`,
            'Content-Type': 'application/json'
        }
    });
    if (response.status === 401) { handleUnauthorized(); return; }
    const data = await response.json();
    if (!response.ok) throw new Error(data.message || 'Error en la peticion');
    return data;
}

async function apiPost(endpoint, body, requireToken = true) {
    const headers = { 'Content-Type': 'application/json' };
    if (requireToken) headers['Authorization'] = `Bearer ${getToken()}`;

    const response = await fetch(`${API_URL}${endpoint}`, {
        method: 'POST',
        headers,
        body: JSON.stringify(body)
    });
    const data = await response.json();
    if (response.status === 401) { handleUnauthorized(); return; }
    if (!response.ok) {
        const err = new Error(data.message || 'Error en la peticion');
        err.status = response.status;
        err.data   = data;
        throw err;
    }
    return data;
}

async function apiPut(endpoint, body) {
    const response = await fetch(`${API_URL}${endpoint}`, {
        method: 'PUT',
        headers: {
            'Authorization': `Bearer ${getToken()}`,
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(body)
    });
    const data = await response.json();
    if (response.status === 401) { handleUnauthorized(); return; }
    if (!response.ok) throw new Error(data.message || 'Error en la peticion');
    return data;
}

async function apiDelete(endpoint) {
    const response = await fetch(`${API_URL}${endpoint}`, {
        method: 'DELETE',
        headers: {
            'Authorization': `Bearer ${getToken()}`,
            'Content-Type': 'application/json'
        }
    });
    if (response.status === 204) return true;
    const data = await response.json();
    if (response.status === 401) { handleUnauthorized(); return; }
    if (!response.ok) throw new Error(data.message || 'Error en la peticion');
    return data;
}

// ============================================================
// UTILIDADES DE UI
// ============================================================

function showAlert(containerId, message, type = 'danger') {
    const container = document.getElementById(containerId);
    if (!container) return;
    container.innerHTML = `<div class="alert alert-${type}">${message}</div>`;
    container.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

function clearAlert(containerId) {
    const container = document.getElementById(containerId);
    if (container) container.innerHTML = '';
}

function setLoading(btn, loading) {
    if (loading) {
        btn.disabled = true;
        btn.dataset.originalText = btn.innerHTML;
        btn.innerHTML = '<span class="spinner"></span>';
    } else {
        btn.disabled = false;
        btn.innerHTML = btn.dataset.originalText || btn.innerHTML;
    }
}

function openModal(id) {
    document.getElementById(id).classList.add('active');
}

function closeModal(id) {
    document.getElementById(id).classList.remove('active');
}

// Formatear fecha de dd-MM-yyyy a texto legible
function formatDate(dateStr) {
    if (!dateStr) return '-';
    const parts = dateStr.split('-');
    if (parts.length !== 3) return dateStr;
    const months = ['Ene', 'Feb', 'Mar', 'Abr', 'May', 'Jun',
        'Jul', 'Ago', 'Sep', 'Oct', 'Nov', 'Dic'];
    const day = parseInt(parts[0]);
    const month = parseInt(parts[1]) - 1;
    const year = parts[2];
    return `${day} ${months[month]} ${year}`;
}

// ============================================================
// CONFIGURACIÓN DE MONEDA — cargada desde el backend al iniciar
// ============================================================

window._currency = {
    symbol:  'L.',
    code:    'HNL',
    locale:  'es-HN',
    taxRate: 0.15,
    name:    'Lempira hondureño'
};

async function loadCurrencySettings() {
    try {
        const API = window.location.origin + '/api';
        const res = await fetch(`${API}/Settings/currency`);
        if (res.ok) {
            const data = await res.json();
            window._currency = {
                symbol:  data.symbol  || 'L.',
                code:    data.code    || 'HNL',
                locale:  data.locale  || 'es-HN',
                taxRate: data.taxRate ?? 0.15,
                name:    data.name    || ''
            };
        }
    } catch { /* usar valores por defecto si falla */ }
}

// Cargar configuración tan pronto como se ejecute app.js
loadCurrencySettings();

// Formatear moneda usando la configuración activa
function formatCurrency(amount) {
    const c = window._currency;
    const formatted = parseFloat(amount).toLocaleString(c.locale, {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });
    return `${c.symbol} ${formatted}`;
}

// Obtener la tasa de impuesto activa (para cálculos en el frontend)
function getTaxRate() {
    return window._currency.taxRate ?? 0.15;
}

// Obtener fecha de hoy en formato dd-MM-yyyy
function todayFormatted() {
    const d = new Date();
    const day = String(d.getDate()).padStart(2, '0');
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const year = d.getFullYear();
    return `${day}-${month}-${year}`;
}

// Convertir input type=date (yyyy-MM-dd) a formato dd-MM-yyyy
function inputDateToApi(inputValue) {
    if (!inputValue) return '';
    const parts = inputValue.split('-');
    return `${parts[2]}-${parts[1]}-${parts[0]}`;
}

// Convertir dd-MM-yyyy a input type=date (yyyy-MM-dd)
function apiDateToInput(dateStr) {
    if (!dateStr) return '';
    const parts = dateStr.split('-');
    if (parts.length !== 3) return '';
    return `${parts[2]}-${parts[1]}-${parts[0]}`;
}

// Obtener badge HTML segun estado
function getStatusBadge(status) {
    const map = {
        'confirmed':   '<span class="badge badge-success">Confirmada</span>',
        'pending':     '<span class="badge badge-warning">Pendiente</span>',
        'cancelled':   '<span class="badge badge-muted">Cancelada</span>',
        'completed':   '<span class="badge badge-info" style="background:rgba(44,95,138,0.12);color:#2C5F8A;border:1px solid rgba(44,95,138,0.25)">Completada</span>',
        'sin reservar':'<span class="badge badge-muted">Sin reservar</span>'
    };
    return map[status] || `<span class="badge badge-muted">${status}</span>`;
}

// Renderizar navbar con datos del usuario
function renderNavbar(role) {
    const user = getUser();
    if (!user) return;

    const navUser = document.getElementById('nav-user');
    if (navUser) {
        const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
        navUser.innerHTML = `
            <span class="navbar-user-name">${user.fullname}</span>
            <span class="navbar-role">${role === 'manager' ? 'Gerente' : 'Huesped'}</span>
            <button class="theme-toggle" onclick="toggleTheme()">
                <span>${isDark ? '☀️' : '🌙'}</span> ${isDark ? 'Claro' : 'Oscuro'}
            </button>
            <button class="btn btn-outline btn-sm" onclick="openChangePasswordModal()" title="Cambiar contraseña">🔑</button>
            <button class="btn btn-outline btn-sm" onclick="logout()">Salir</button>
        `;
    }

    // Agregar boton hamburguesa si hay sidebar
    const sidebar = document.querySelector('.sidebar');
    const navbar  = document.querySelector('.navbar');
    if (sidebar && navbar && !document.getElementById('hamburger-btn')) {

        // Boton hamburguesa en la navbar
        const ham = document.createElement('button');
        ham.id        = 'hamburger-btn';
        ham.className = 'hamburger';
        ham.innerHTML = '&#9776;';
        ham.setAttribute('aria-label', 'Menu');
        navbar.insertBefore(ham, navbar.firstChild);

        // Overlay oscuro
        const overlay = document.createElement('div');
        overlay.className = 'sidebar-overlay';
        overlay.id        = 'sidebar-overlay';
        document.body.appendChild(overlay);

        // Abrir/cerrar sidebar
        ham.addEventListener('click', function() {
            sidebar.classList.toggle('open');
            overlay.classList.toggle('active');
        });

        // Cerrar al hacer click en el overlay
        overlay.addEventListener('click', function() {
            sidebar.classList.remove('open');
            overlay.classList.remove('active');
        });

        // Cerrar al hacer click en un link del sidebar en movil
        sidebar.querySelectorAll('.sidebar-link').forEach(function(link) {
            link.addEventListener('click', function() {
                sidebar.classList.remove('open');
                overlay.classList.remove('active');
            });
        });
    }
}


// ============================================================
// VERIFICACION DE SESION ACTIVA
// Se ejecuta al cargar cada pagina del panel Y cada 30 segundos
// Si el servidor no responde o el token expiro, cierra sesion
// ============================================================

const publicPages = ['/index.html', '/login.html', '/register.html', '/forgot-password.html', '/'];

function isPublicPage() {
    const p = window.location.pathname;
    return publicPages.some(pub => p.endsWith(pub) || p === pub);
}

async function verifySession() {
    if (isPublicPage() || !isLoggedIn()) return;

    try {
        const response = await fetch(`${API_URL}/Test/health`, {
            headers: { 'Authorization': `Bearer ${getToken()}` }
        });

        if (!response.ok) {
            handleUnauthorized();
        }
    } catch (error) {
        // Servidor no disponible — cerrar sesion inmediatamente
        localStorage.removeItem('probook_token');
        localStorage.removeItem('probook_user');
        window.location.href = '/login.html';
    }
}

// Verificar inmediatamente al cargar la pagina
verifySession();

// Y repetir cada 30 segundos por si el servidor se cae mientras esta navegando
setInterval(verifySession, 30000);

// ============================================================
// SISTEMA DE NOTIFICACIONES - ProBook
// Campana con badge, panel desplegable y polling al backend
// ============================================================

const NOTIF_STORAGE_KEY  = 'probook_notif_seen';   // IDs ya leidos
const NOTIF_POLL_INTERVAL = 30000;                  // Polling cada 30 seg
let   _notifPollTimer     = null;
let   _notifPanelOpen     = false;

// ---- Obtener y guardar IDs leidos en localStorage ----

function getSeenIds() {
    try { return JSON.parse(localStorage.getItem(NOTIF_STORAGE_KEY) || '[]'); }
    catch { return []; }
}

function markAllSeen(ids) {
    localStorage.setItem(NOTIF_STORAGE_KEY, JSON.stringify(ids));
}

// ---- Fetch de notificaciones al backend ----

async function fetchNotifications() {
    if (!isLoggedIn()) return [];
    try {
        const res = await fetch(`${API_URL}/Notifications`, {
            headers: { 'Authorization': `Bearer ${getToken()}` }
        });
        if (!res.ok) return [];
        return await res.json();
    } catch { return []; }
}

// ---- Renderizar el badge de cantidad ----

function updateBadge(count) {
    const badge = document.getElementById('notif-badge');
    if (!badge) return;
    if (count > 0) {
        badge.textContent = count > 99 ? '99+' : count;
        badge.style.display = 'flex';
    } else {
        badge.style.display = 'none';
    }
}

// ---- Formatear timestamp para mostrar en el panel ----

function formatNotifTime(tsRaw) {
    if (!tsRaw) return '';
    // Soporte para string "dd-MM-yyyy HH:mm" o ISO
    try {
        let d;
        if (typeof tsRaw === 'string' && tsRaw.includes('-') && tsRaw.length <= 16) {
            // formato "18-03-2026 14:30"
            const [datePart, timePart = '00:00'] = tsRaw.split(' ');
            const [dd, mm, yyyy] = datePart.split('-');
            d = new Date(`${yyyy}-${mm}-${dd}T${timePart}:00`);
        } else {
            d = new Date(tsRaw);
        }
        if (isNaN(d)) return tsRaw;
        const now  = new Date();
        const diff = Math.floor((now - d) / 60000); // minutos
        if (diff < 1)   return 'Ahora';
        if (diff < 60)  return `Hace ${diff} min`;
        if (diff < 1440) return `Hace ${Math.floor(diff / 60)} h`;
        return d.toLocaleDateString('es-HN', { day: '2-digit', month: 'short' });
    } catch { return ''; }
}

// ---- Renderizar items dentro del panel ----

function renderNotifItems(notifications, seenIds) {
    const list = document.getElementById('notif-list');
    if (!list) return;

    if (!notifications.length) {
        list.innerHTML = `
            <div class="notif-empty">
                <span class="notif-empty-icon">🔔</span>
                <p>Sin notificaciones por ahora</p>
            </div>`;
        return;
    }

    list.innerHTML = notifications.map(n => {
        const unseen = !seenIds.includes(n.id);
        return `
        <div class="notif-item ${unseen ? 'notif-item--unseen' : ''}">
            <span class="notif-item-icon">${n.icon || '📌'}</span>
            <div class="notif-item-body">
                <div class="notif-item-title">${n.title}</div>
                <div class="notif-item-msg">${n.message}</div>
                <div class="notif-item-time">${formatNotifTime(n.timestamp)}</div>
            </div>
        </div>`;
    }).join('');
}

// ---- Abrir / cerrar el panel ----

function toggleNotifPanel(notifications, seenIds) {
    const panel = document.getElementById('notif-panel');
    if (!panel) return;

    _notifPanelOpen = !_notifPanelOpen;
    panel.classList.toggle('notif-panel--open', _notifPanelOpen);

    if (_notifPanelOpen) {
        // Marcar todo como leido y actualizar badge
        const allIds = notifications.map(n => n.id);
        markAllSeen(allIds);
        updateBadge(0);
        renderNotifItems(notifications, allIds);

        // Cerrar si hace click afuera
        setTimeout(() => {
            document.addEventListener('click', closePanelOnOutsideClick);
        }, 50);
    } else {
        document.removeEventListener('click', closePanelOnOutsideClick);
    }
}

function closePanelOnOutsideClick(e) {
    const wrapper = document.getElementById('notif-wrapper');
    if (wrapper && !wrapper.contains(e.target)) {
        const panel = document.getElementById('notif-panel');
        if (panel) panel.classList.remove('notif-panel--open');
        _notifPanelOpen = false;
        document.removeEventListener('click', closePanelOnOutsideClick);
    }
}

// ---- Ciclo de polling ----

async function pollNotifications() {
    const notifications = await fetchNotifications();
    const seenIds       = getSeenIds();
    const unread        = notifications.filter(n => !seenIds.includes(n.id)).length;

    updateBadge(unread);

    // Si el panel esta abierto, refrescar los items en vivo
    if (_notifPanelOpen) {
        renderNotifItems(notifications, seenIds);
    }

    // Guardar referencia global para que el boton siempre tenga los datos actuales
    window._notifCache = notifications;
}

// ---- Inicializar: inyectar campana en la navbar ----

function initNotifications() {
    if (isPublicPage() || !isLoggedIn()) return;

    const navUser = document.getElementById('nav-user');
    if (!navUser) return;

    // Crear el wrapper con campana + panel
    const wrapper = document.createElement('div');
    wrapper.id        = 'notif-wrapper';
    wrapper.className = 'notif-wrapper';
    wrapper.innerHTML = `
        <button id="notif-btn" class="notif-btn" title="Notificaciones" aria-label="Notificaciones">
            <span class="notif-bell">🔔</span>
            <span id="notif-badge" class="notif-badge" style="display:none">0</span>
        </button>
        <div id="notif-panel" class="notif-panel">
            <div class="notif-panel-header">
                <span class="notif-panel-title">Notificaciones</span>
                <button class="notif-panel-close" onclick="document.getElementById('notif-panel').classList.remove('notif-panel--open');_notifPanelOpen=false;">✕</button>
            </div>
            <div id="notif-list" class="notif-list">
                <div class="notif-empty">
                    <span class="notif-empty-icon">🔔</span>
                    <p>Cargando...</p>
                </div>
            </div>
        </div>`;

    // Insertar como PRIMER hijo de nav-user para que quede junto al nombre
    navUser.insertBefore(wrapper, navUser.firstChild);

    // Evento del boton
    document.getElementById('notif-btn').addEventListener('click', (e) => {
        e.stopPropagation();
        toggleNotifPanel(window._notifCache || [], getSeenIds());
    });

    // Primera carga inmediata
    pollNotifications();

    // Polling cada 30 segundos
    if (_notifPollTimer) clearInterval(_notifPollTimer);
    _notifPollTimer = setInterval(pollNotifications, NOTIF_POLL_INTERVAL);
}

// Arrancar cuando el DOM esta listo
document.addEventListener('DOMContentLoaded', initNotifications);

// ============================================================
// MODAL CAMBIAR CONTRASEÑA — disponible para huésped y manager
// Uso: llamar initChangePasswordModal() en cualquier página
// y agregar <div id="change-pwd-modal-root"></div> al body
// ============================================================

function initChangePasswordModal() {
    // Inyectar HTML del modal si no existe ya
    if (document.getElementById('modal-change-pwd')) return;

    // Usar el div raíz si existe, o inyectar directamente en body como fallback
    let root = document.getElementById('change-pwd-modal-root');
    if (!root) {
        root = document.createElement('div');
        root.id = 'change-pwd-modal-root';
        document.body.appendChild(root);
    }

    root.innerHTML = `
    <div id="modal-change-pwd" class="modal-overlay-change-pwd">
        <div class="modal-box-change-pwd">
            <div class="modal-pwd-header">
                <div>
                    <div class="modal-pwd-title">Cambiar contraseña</div>
                    <div class="modal-pwd-desc">Ingresa tu contraseña actual y elige una nueva</div>
                </div>
                <button class="modal-pwd-close" onclick="closeChangePasswordModal()">×</button>
            </div>
            <div id="change-pwd-alert" style="margin-bottom:0.75rem"></div>
            <div class="form-group">
                <label class="form-label">Contraseña actual</label>
                <div class="pwd-input-wrap">
                    <input type="password" id="pwd-current" class="form-control"
                           placeholder="Tu contraseña actual" autocomplete="current-password">
                    <button class="pwd-toggle" onclick="togglePwdVisibility('pwd-current', this)" tabindex="-1">👁</button>
                </div>
            </div>
            <div class="form-group">
                <label class="form-label">Nueva contraseña</label>
                <div class="pwd-input-wrap">
                    <input type="password" id="pwd-new" class="form-control"
                           placeholder="Mínimo 6 caracteres" autocomplete="new-password"
                           oninput="checkPwdStrength(this.value)">
                    <button class="pwd-toggle" onclick="togglePwdVisibility('pwd-new', this)" tabindex="-1">👁</button>
                </div>
                <div class="pwd-strength-bar"><div id="pwd-strength-fill" class="pwd-strength-fill"></div></div>
                <div id="pwd-strength-label" class="pwd-strength-label"></div>
            </div>
            <div class="form-group" style="margin-bottom:1.5rem">
                <label class="form-label">Confirmar nueva contraseña</label>
                <div class="pwd-input-wrap">
                    <input type="password" id="pwd-confirm" class="form-control"
                           placeholder="Repite la nueva contraseña" autocomplete="new-password">
                    <button class="pwd-toggle" onclick="togglePwdVisibility('pwd-confirm', this)" tabindex="-1">👁</button>
                </div>
            </div>
            <div style="display:flex;gap:0.75rem;justify-content:flex-end">
                <button class="btn btn-outline" onclick="closeChangePasswordModal()">Cancelar</button>
                <button id="btn-save-pwd" class="btn btn-primary" onclick="submitChangePassword()">
                    Guardar contraseña
                </button>
            </div>
        </div>
    </div>`;

    // Inyectar CSS si no está ya
    if (!document.getElementById('change-pwd-styles')) {
        const style = document.createElement('style');
        style.id = 'change-pwd-styles';
        style.textContent = `
            .modal-overlay-change-pwd {
                display: none; position: fixed; inset: 0;
                background: rgba(0,0,0,0.5); z-index: 2000;
                align-items: center; justify-content: center;
            }
            .modal-overlay-change-pwd.active { display: flex; }
            .modal-box-change-pwd {
                background: var(--bg-card); border-radius: var(--radius-lg);
                padding: 2rem; width: 90%; max-width: 440px;
                box-shadow: var(--shadow-lg); border: 1px solid var(--border);
                animation: fadeInUp 0.25s ease;
            }
            .modal-pwd-header {
                display: flex; justify-content: space-between; align-items: flex-start;
                margin-bottom: 1.5rem;
            }
            .modal-pwd-title {
                font-family: var(--font-display); font-size: 1.15rem;
                font-weight: 700; color: var(--text); margin-bottom: 0.2rem;
            }
            .modal-pwd-desc { font-size: 0.8rem; color: var(--text-muted); }
            .modal-pwd-close {
                background: none; border: none; font-size: 1.4rem;
                cursor: pointer; color: var(--text-muted); line-height: 1;
                padding: 0; margin-left: 1rem; margin-top: -2px;
            }
            .modal-pwd-close:hover { color: var(--text); }
            .pwd-input-wrap { position: relative; }
            .pwd-input-wrap .form-control { padding-right: 2.5rem; }
            .pwd-toggle {
                position: absolute; right: 0.75rem; top: 50%; transform: translateY(-50%);
                background: none; border: none; cursor: pointer; font-size: 0.95rem;
                color: var(--text-muted); padding: 0; line-height: 1;
            }
            .pwd-toggle:hover { color: var(--text); }
            .pwd-strength-bar {
                height: 4px; background: var(--border); border-radius: 2px;
                margin-top: 0.4rem; overflow: hidden;
            }
            .pwd-strength-fill {
                height: 100%; border-radius: 2px; width: 0;
                transition: width 0.3s ease, background 0.3s ease;
            }
            .pwd-strength-label {
                font-size: 0.72rem; margin-top: 0.25rem; color: var(--text-muted);
                min-height: 1rem;
            }
            [data-theme="dark"] .modal-box-change-pwd {
                background: #1A1D26; border-color: #2A2D3A;
            }
        `;
        document.head.appendChild(style);
    }
}

function openChangePasswordModal() {
    initChangePasswordModal();

    // Limpiar campos (con guardas por si el modal aún no está en DOM)
    const setClear = (id, prop, val) => { const el = document.getElementById(id); if (el) el[prop] = val; };
    setClear('pwd-current',        'value', '');
    setClear('pwd-new',            'value', '');
    setClear('pwd-confirm',        'value', '');
    setClear('pwd-strength-fill',  'style.width', '0');
    setClear('pwd-strength-label', 'textContent', '');

    const fill = document.getElementById('pwd-strength-fill');
    if (fill) fill.style.background = '';

    clearAlert('change-pwd-alert');

    const modal = document.getElementById('modal-change-pwd');
    if (modal) {
        modal.classList.add('active');
        setTimeout(() => {
            const el = document.getElementById('pwd-current');
            if (el) el.focus();
        }, 100);
    }
}

function closeChangePasswordModal() {
    const m = document.getElementById('modal-change-pwd');
    if (m) m.classList.remove('active');
}

function togglePwdVisibility(inputId, btn) {
    const input = document.getElementById(inputId);
    if (!input) return;
    if (input.type === 'password') {
        input.type = 'text';
        btn.textContent = '🙈';
    } else {
        input.type = 'password';
        btn.textContent = '👁';
    }
}

function checkPwdStrength(pwd) {
    const fill  = document.getElementById('pwd-strength-fill');
    const label = document.getElementById('pwd-strength-label');
    if (!fill || !label) return;

    let score = 0;
    if (pwd.length >= 6)  score++;
    if (pwd.length >= 10) score++;
    if (/[A-Z]/.test(pwd)) score++;
    if (/[0-9]/.test(pwd)) score++;
    if (/[^A-Za-z0-9]/.test(pwd)) score++;

    const levels = [
        { pct: '0%',   color: '',          text: '' },
        { pct: '25%',  color: '#E05C4F',   text: 'Muy débil' },
        { pct: '50%',  color: '#E67E22',   text: 'Débil' },
        { pct: '70%',  color: '#F0B429',   text: 'Aceptable' },
        { pct: '85%',  color: '#3DAA7A',   text: 'Buena' },
        { pct: '100%', color: '#2E7D5E',   text: 'Muy fuerte' },
    ];
    const lv = levels[Math.min(score, 5)];
    fill.style.width      = lv.pct;
    fill.style.background = lv.color;
    label.textContent     = lv.text;
    label.style.color     = lv.color || 'var(--text-muted)';
}

async function submitChangePassword() {
    const current  = document.getElementById('pwd-current').value;
    const newPwd   = document.getElementById('pwd-new').value;
    const confirm  = document.getElementById('pwd-confirm').value;
    const btn      = document.getElementById('btn-save-pwd');

    clearAlert('change-pwd-alert');

    if (!current) {
        showAlert('change-pwd-alert', 'Ingresa tu contraseña actual.', 'danger'); return;
    }
    if (!newPwd || newPwd.length < 6) {
        showAlert('change-pwd-alert', 'La nueva contraseña debe tener al menos 6 caracteres.', 'danger'); return;
    }
    if (newPwd !== confirm) {
        showAlert('change-pwd-alert', 'Las contraseñas nuevas no coinciden.', 'danger'); return;
    }
    if (current === newPwd) {
        showAlert('change-pwd-alert', 'La nueva contraseña debe ser diferente a la actual.', 'danger'); return;
    }

    setLoading(btn, true);
    try {
        await apiPost('/Auth/change-password', {
            currentPassword: current,
            newPassword:     newPwd,
            confirmPassword: confirm
        });
        closeChangePasswordModal();
        // Mostrar éxito brevemente en el contenedor de alertas de la página
        const alertHost = document.getElementById('alert-container');
        if (alertHost) showAlert('alert-container', '✅ Contraseña actualizada correctamente.', 'success');
    } catch (err) {
        showAlert('change-pwd-alert', err.message || 'Error al cambiar la contraseña.', 'danger');
    } finally {
        setLoading(btn, false);
    }
}