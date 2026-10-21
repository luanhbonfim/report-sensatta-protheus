// Recolher/expandir a sidebar (desktop recolhe, mobile abre por cima).
function toggleSidebar() {
  if (window.matchMedia('(max-width: 767.98px)').matches) {
    document.body.classList.toggle('sidebar-open');
  } else {
    document.body.classList.toggle('sidebar-closed');
  }
}
function closeSidebar() {
  document.body.classList.remove('sidebar-open');
}
