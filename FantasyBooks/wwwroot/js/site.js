// Stripe checkout: show gold "Loading…" overlay while redirecting to Stripe.
(function () {
  const overlay = document.getElementById('checkout-loading-overlay');
  if (!overlay) return;

  document.querySelectorAll('form[data-checkout-loading]').forEach((form) => {
    form.addEventListener('submit', () => {
      overlay.classList.remove('hidden');
    });
  });
})();

// Product detail gallery carousel (only when multiple images).
(function () {
  const root = document.querySelector('[data-product-gallery].product-gallery--multi');
  if (!root) return;

  const slides = Array.from(root.querySelectorAll('[data-gallery-slide]'));
  const dots = Array.from(root.querySelectorAll('[data-gallery-dot]'));
  const prevBtn = root.querySelector('[data-gallery-prev]');
  const nextBtn = root.querySelector('[data-gallery-next]');
  if (slides.length < 2) return;

  let index = 0;
  let touchX = null;

  function goTo(next) {
    const target = ((next % slides.length) + slides.length) % slides.length;
    if (target === index) return;

    const current = slides[index];
    current.classList.remove('is-active');
    current.classList.add('is-exit');
    current.setAttribute('aria-hidden', 'true');
    window.setTimeout(() => current.classList.remove('is-exit'), 560);

    index = target;
    slides[index].classList.add('is-active');
    slides[index].setAttribute('aria-hidden', 'false');

    dots.forEach((dot, i) => {
      const active = i === index;
      dot.classList.toggle('is-active', active);
      dot.setAttribute('aria-selected', active ? 'true' : 'false');
    });
  }

  prevBtn?.addEventListener('click', () => goTo(index - 1));
  nextBtn?.addEventListener('click', () => goTo(index + 1));
  dots.forEach((dot) => {
    dot.addEventListener('click', () => {
      const i = Number(dot.getAttribute('data-gallery-dot'));
      if (!Number.isNaN(i)) goTo(i);
    });
  });

  root.addEventListener('keydown', (e) => {
    if (e.key === 'ArrowLeft') {
      e.preventDefault();
      goTo(index - 1);
    } else if (e.key === 'ArrowRight') {
      e.preventDefault();
      goTo(index + 1);
    }
  });

  root.addEventListener(
    'touchstart',
    (e) => {
      touchX = e.changedTouches[0]?.clientX ?? null;
    },
    { passive: true }
  );

  root.addEventListener(
    'touchend',
    (e) => {
      if (touchX == null) return;
      const dx = (e.changedTouches[0]?.clientX ?? touchX) - touchX;
      touchX = null;
      if (Math.abs(dx) < 40) return;
      goTo(dx < 0 ? index + 1 : index - 1);
    },
    { passive: true }
  );
})();
