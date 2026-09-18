(() => {
  const root = document.documentElement;
  const header = document.querySelector('.main-header');
  const navLinks = Array.from(document.querySelectorAll('.center-nav a[href^="#"]'));
  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  const updateHeader = () => {
    if (header) header.classList.toggle('scrolled', window.scrollY > 12);
  };
  updateHeader();
  window.addEventListener('scroll', updateHeader, { passive: true });

  const sections = Array.from(document.querySelectorAll('main section, body > section'));
  if (sections.length) {
    const updateActive = () => {
      const line = window.scrollY + (header ? header.offsetHeight : 0) + 4;
      let current = null;
      sections.forEach((section) => {
        if (section.getBoundingClientRect().top + window.scrollY <= line) current = section;
      });
      const id = current && current.id ? `#${current.id}` : null;
      navLinks.forEach((link) => {
        link.classList.toggle('nav-active-tab', !!id && link.getAttribute('href') === id);
      });
    };
    updateActive();
    window.addEventListener('scroll', updateActive, { passive: true });
    window.addEventListener('resize', updateActive);
  }

  const anime = window.anime;
  if (!anime || reduceMotion || !('IntersectionObserver' in window)) {
    root.classList.remove('js-motion');
    return;
  }

  const { animate, createTimeline, stagger, onScroll, utils } = anime;
  const $ = (selector) => utils.$(selector);

  const revealObserver = new IntersectionObserver((entries) => {
    entries.forEach((entry) => {
      if (!entry.isIntersecting) return;
      const plays = entry.target.__reveals || [];
      entry.target.__reveals = [];
      plays.forEach((play) => play());
      revealObserver.unobserve(entry.target);
    });
  }, { rootMargin: '0px 0px -8% 0px', threshold: 0.05 });

  const revealGroup = (targets, trigger, params) => {
    const { from = {}, ...rest } = params;
    utils.set(targets, { opacity: 0, y: 28, x: 0, scale: 1, ...from });
    const play = () => animate(targets, {
      opacity: 1,
      y: 0,
      x: 0,
      scale: 1,
      duration: 800,
      ease: 'out(3)',
      delay: stagger(110),
      ...rest,
    });
    trigger.__reveals = trigger.__reveals || [];
    trigger.__reveals.push(play);
    revealObserver.observe(trigger);
  };

  const reveal = (selector, params = {}, observer = {}) => {
    const targets = $(selector);
    if (!targets.length) return;
    if (observer.target) {
      const trigger = $(observer.target)[0];
      if (trigger) revealGroup(targets, trigger, params);
      return;
    }
    targets.forEach((target) => revealGroup([target], target, { ...params, delay: 0 }));
  };

  const scrolled = (selector, params, observer) => {
    const targets = $(selector);
    if (!targets.length) return;
    animate(targets, {
      ease: 'linear',
      ...params,
      autoplay: onScroll({ sync: 0.35, ...observer }),
    });
  };

  const progressFill = document.querySelector('.scroll-progress-fill');
  if (progressFill) {
    animate(progressFill, {
      scaleX: [0, 1],
      ease: 'linear',
      autoplay: onScroll({ target: document.body, enter: 'top top', leave: 'bottom bottom', sync: true }),
    });
  }

  const intro = createTimeline({ defaults: { ease: 'out(3)', duration: 900 } });
  utils.set('.hero-title, .hero-description, .btn-cta-download', { opacity: 0, y: 26 });
  utils.set('.floating-bottom-bar', { opacity: 0, y: 20 });
  intro
    .add('.hero-title', { opacity: 1, y: 0 }, 80)
    .add('.hero-description', { opacity: 1, y: 0 }, '-=650')
    .add('.btn-cta-download', { opacity: 1, y: 0 }, '-=650')
    .add('.floating-bottom-bar', { opacity: 1, y: 0, duration: 600 }, '-=500');

  scrolled('.hero-info', { y: [0, -40], opacity: [1, 0.5] },
    { target: '.hero-section', enter: 'top top', leave: 'top bottom' });

  reveal('.feature-card', { from: { y: 40 }, delay: stagger(140) }, { target: '.features-grid' });
  reveal('.feature-icon', { from: { scale: 0.5, y: 0 }, ease: 'outElastic(1, .6)', duration: 1300, delay: stagger(140, { start: 250 }) },
    { target: '.features-grid' });

  reveal('.section-head, .about-app-section .section-heading');
  reveal('.clip-card', { from: { y: 50, scale: 0.96 }, delay: stagger(130) }, { target: '.showcase-grid' });
  scrolled('.clip-card', { y: (el, index) => [24 + index * 18, -24 - index * 18] },
    { target: '.showcase-section', enter: 'bottom top', leave: 'top bottom' });

  reveal('.step-card', { from: { y: 46 }, delay: stagger(160) }, { target: '.steps-container' });
  reveal('.step-arrow', { from: { x: -14, y: 0 }, delay: stagger(160, { start: 220 }) }, { target: '.steps-container' });
  scrolled('.step-number', { scale: [1, 1.18], opacity: [0.7, 1] },
    { target: '.steps-container', enter: 'bottom top', leave: 'center center' });

  reveal('.opensource-copy', { from: { x: -34, y: 0 } }, { target: '.opensource-card' });
  reveal('.opensource-point', { from: { x: 34, y: 0 }, delay: stagger(120, { start: 150 }) }, { target: '.opensource-card' });

  reveal('.shot-big, .shot-sm', { from: { y: 36, scale: 0.97 }, delay: stagger(140) }, { target: '.screenshots-row' });
  reveal('.spec-fact', { from: { x: -18, y: 0 }, delay: stagger(80, { start: 300 }) }, { target: '.screenshots-row' });

  reveal('.faq-item', { from: { y: 22 }, delay: stagger(70) }, { target: '.faq-list' });

  reveal('.cta-tag, .cta-title, .cta-sub, .cta-btns, .cta-note', { delay: stagger(120) }, { target: '.cta-section' });
  scrolled('.cta-title', { scale: [0.94, 1] },
    { target: '.cta-section', enter: 'bottom top', leave: 'center center' });

  document.querySelectorAll('.feature-card, .clip-card, .step-card, .opensource-point').forEach((card) => {
    card.addEventListener('pointerenter', () => {
      const icon = card.querySelector('.feature-icon, .step-number, .opensource-point-number');
      if (icon) animate(icon, { scale: [1, 1.12, 1], duration: 500, ease: 'inOut(2)' });
    });
  });
})();
