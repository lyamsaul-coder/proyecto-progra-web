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

const API_URL = 'https://localhost:44354/api';

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
    if (!response.ok) throw new Error(data.message || 'Error en la peticion');
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

// Formatear moneda
function formatCurrency(amount) {
    return `L. ${parseFloat(amount).toLocaleString('es-HN', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    })}`;
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
        'confirmed': '<span class="badge badge-success">Confirmada</span>',
        'pending': '<span class="badge badge-warning">Pendiente</span>',
        'sin reservar': '<span class="badge badge-muted">Sin reservar</span>'
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