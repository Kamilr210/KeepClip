document.addEventListener('DOMContentLoaded', () => {
    const translations = window.keepClipTranslations ?? {};
    const supportedLanguages = Object.keys(translations);
    const langSelect = document.getElementById('langSelect');

    const readSavedLanguage = () => {
        try {
            return window.localStorage.getItem('keepclip-language');
        } catch {
            return null;
        }
    };

    const saveLanguage = (language) => {
        try {
            window.localStorage.setItem('keepclip-language', language);
        } catch {}
    };

    const applyLanguage = (language, updateAddress = true) => {
        const selectedLanguage = supportedLanguages.includes(language) ? language : 'en';
        const selectedTranslations = translations[selectedLanguage] ?? translations.en;

        document.documentElement.lang = selectedLanguage;
        document.querySelectorAll('[data-i18n]').forEach((element) => {
            const key = element.dataset.i18n;
            if (Object.hasOwn(selectedTranslations, key)) {
                element.innerHTML = selectedTranslations[key];
            }
        });

        document.title = selectedTranslations.title;
        if (langSelect) langSelect.value = selectedLanguage;
        saveLanguage(selectedLanguage);

        if (updateAddress) {
            try {
                const url = new URL(window.location.href);
                if (selectedLanguage === 'en') url.searchParams.delete('lang');
                else url.searchParams.set('lang', selectedLanguage);
                window.history.replaceState(null, '', url);
            } catch {}
        }
    };

    const languageFromAddress = new URLSearchParams(window.location.search).get('lang');
    const savedLanguage = readSavedLanguage();
    const initialLanguage = supportedLanguages.includes(languageFromAddress)
        ? languageFromAddress
        : supportedLanguages.includes(savedLanguage)
            ? savedLanguage
            : 'en';

    applyLanguage(initialLanguage, supportedLanguages.includes(languageFromAddress));

    if (langSelect) {
        langSelect.addEventListener('change', () => applyLanguage(langSelect.value));
    }

    const header = document.querySelector('.main-header');
    const mobileMenuToggle = document.getElementById('mobileMenuToggle');
    const closeMobileMenu = () => {
        if (!header || !mobileMenuToggle) return;
        header.classList.remove('menu-open');
        mobileMenuToggle.setAttribute('aria-expanded', 'false');
    };

    if (header && mobileMenuToggle) {
        mobileMenuToggle.addEventListener('click', () => {
            const open = header.classList.toggle('menu-open');
            mobileMenuToggle.setAttribute('aria-expanded', String(open));
        });
        document.querySelectorAll('.center-nav a').forEach((link) => {
            link.addEventListener('click', closeMobileMenu);
        });
        document.addEventListener('keydown', (event) => {
            if (event.key === 'Escape') closeMobileMenu();
        });
        window.addEventListener('resize', () => {
            if (window.innerWidth > 900) closeMobileMenu();
        });
    }

    const DOWNLOAD_URL = 'https://github.com/Kamilr210/KeepClip/releases/latest/download/KeepClip-Setup.exe';
    ['downloadBtn', 'downloadBtn2'].forEach((id) => {
        const btn = document.getElementById(id);
        if (btn) btn.addEventListener('click', () => { window.location.href = DOWNLOAD_URL; });
    });

    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const setFaqOpen = (item, open) => {
        const answer = item.querySelector('.faq-a');
        const anime = window.anime;
        if (item.classList.contains('open') === open) return;
        if (!anime || reduceMotion || !answer) {
            item.classList.toggle('open', open);
            return;
        }
        if (open) {
            item.classList.add('open');
            const height = answer.scrollHeight;
            anime.animate(answer, {
                height: [0, height],
                opacity: [0, 1],
                duration: 360,
                ease: 'out(3)',
                onComplete: () => { answer.style.height = ''; answer.style.opacity = ''; },
            });
        } else {
            const height = answer.scrollHeight;
            anime.animate(answer, {
                height: [height, 0],
                opacity: [1, 0],
                duration: 260,
                ease: 'out(3)',
                onComplete: () => {
                    item.classList.remove('open');
                    answer.style.height = '';
                    answer.style.opacity = '';
                },
            });
        }
    };

    document.querySelectorAll('.faq-q').forEach((btn) => {
        btn.addEventListener('click', function () {
            const item = this.closest('.faq-item');
            const wasOpen = item.classList.contains('open');
            document.querySelectorAll('.faq-item.open').forEach((other) => {
                if (other !== item) setFaqOpen(other, false);
            });
            setFaqOpen(item, !wasOpen);
        });
    });
});
