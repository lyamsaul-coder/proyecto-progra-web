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
        navUser.innerHTML = `
            <span class="navbar-user-name">${user.fullname}</span>
            <span class="navbar-role">${role === 'manager' ? 'Gerente' : 'Huesped'}</span>
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