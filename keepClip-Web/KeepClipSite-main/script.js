// script.js
document.addEventListener('DOMContentLoaded', () => {

    // Autozmiana języka
    const langSelect = document.getElementById('langSelect');
    const langForm = document.getElementById('langForm');

    if (langSelect && langForm) {
        langSelect.addEventListener('change', () => {
            langForm.submit();
        });
    }

    // Pobieranie instalatora — stały link GitHuba zawsze wskazuje najnowsze wydanie.
    const DOWNLOAD_URL = 'https://github.com/Kamilr210/KeepClip/releases/latest/download/KeepClip-Setup.exe';
    ['downloadBtn', 'downloadBtn2'].forEach((id) => {
        const btn = document.getElementById(id);
        if (btn) btn.addEventListener('click', () => { window.location.href = DOWNLOAD_URL; });
    });

    // Logika otwierania/zamykania FAQ
    const faqButtons = document.querySelectorAll('.faq-q');
    faqButtons.forEach(btn => {
        btn.addEventListener('click', function() {
            const item = this.closest('.faq-item');
            const wasOpen = item.classList.contains('open');

            // Zamknij pozostałe otwarte zakładki
            document.querySelectorAll('.faq-item').forEach(i => i.classList.remove('open'));

            // Jeśli zakładka nie była otwarta — otwórz ją
            if (!wasOpen) {
                item.classList.add('open');
            }
        });
    });
});
