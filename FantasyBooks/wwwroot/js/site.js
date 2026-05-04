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
