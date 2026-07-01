/* SODIV Bureau – site.js */

// Autocomplete recherche
(function () {
    const input = document.getElementById('searchInput');
    const dropdown = document.getElementById('autocompleteResults');
    if (!input || !dropdown) return;

    let timer;

    input.addEventListener('input', function () {
        clearTimeout(timer);
        const q = this.value.trim();
        if (q.length < 1) { dropdown.classList.add('d-none'); return; }

        timer = setTimeout(async () => {
            try {
                const res = await fetch(`/api/autocomplete?q=${encodeURIComponent(q)}`);
                const data = await res.json();
                if (!data.length) { dropdown.classList.add('d-none'); return; }

                dropdown.innerHTML = data.map(item =>
                    `<div class="autocomplete-item" onclick="window.location='/Produit/Details/${item.id}'">
                        <i class="fas fa-search me-2 text-muted small"></i>
                        <span>${item.nom}</span>
                        <small class="text-muted ms-2">${item.marque}</small>
                    </div>`
                ).join('');
                dropdown.classList.remove('d-none');
            } catch { dropdown.classList.add('d-none'); }
        }, 300);
    });

    document.addEventListener('click', e => {
        if (!input.contains(e.target)) dropdown.classList.add('d-none');
    });
})();

// Auto-dismiss alerts après 4s
document.querySelectorAll('.alert-dismissible').forEach(alert => {
    setTimeout(() => {
        const bsAlert = bootstrap.Alert.getOrCreateInstance(alert);
        if (bsAlert) bsAlert.close();
    }, 4000);
});

// Confirmation avant suppression
document.querySelectorAll('[data-confirm]').forEach(el => {
    el.addEventListener('click', function (e) {
        if (!confirm(this.dataset.confirm || 'Êtes-vous sûr ?')) e.preventDefault();
    });
});

// Tooltip Bootstrap
document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(el => {
    new bootstrap.Tooltip(el);
});

// Loader sur soumission de formulaires lents
document.querySelectorAll('form.slow-form').forEach(form => {
    form.addEventListener('submit', function () {
        const btn = this.querySelector('[type=submit]');
        if (btn) {
            btn.disabled = true;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Chargement…';
        }
    });
});
