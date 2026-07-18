(function () {
  const root = document.getElementById('product-options');
  const list = document.getElementById('option-groups');
  const addGroupBtn = document.getElementById('add-option-group');
  if (!root || !list || !addGroupBtn) return;

  const maxGroups = Number(root.getAttribute('data-max-groups') || 5);
  const maxChoices = Number(root.getAttribute('data-max-choices') || 20);

  function reindex() {
    const groups = list.querySelectorAll('[data-option-group]');
    groups.forEach((group, gi) => {
      const nameInput = group.querySelector('input[name*=".Name"]');
      if (nameInput) nameInput.name = `OptionGroups[${gi}].Name`;

      const rows = group.querySelectorAll('[data-choice-row]');
      rows.forEach((row, ci) => {
        const input = row.querySelector('input');
        if (input) input.name = `OptionGroups[${gi}].Choices[${ci}]`;
      });
    });
  }

  function choiceRowHtml() {
    return (
      '<div class="flex items-center gap-2" data-choice-row>' +
      '<input type="text" name="OptionGroups[0].Choices[0]" value="" maxlength="80" placeholder="e.g. Red" ' +
      'class="w-full rounded-lg border border-ink/15 bg-white/70 px-3 py-2 text-sm text-ink outline-none focus:ring-2 focus:ring-gold-500/30" />' +
      '<button type="button" class="shrink-0 text-xs font-semibold text-ink/45 hover:text-red-700" data-remove-choice aria-label="Remove choice">×</button>' +
      '</div>'
    );
  }

  function groupHtml() {
    return (
      '<div class="option-group rounded-xl border border-ink/10 bg-white/40 p-4" data-option-group>' +
      '<div class="flex flex-wrap items-end gap-3">' +
      '<div class="min-w-[10rem] flex-1">' +
      '<label class="mb-1 block text-[10px] font-semibold uppercase tracking-wider text-ink/45">Group name</label>' +
      '<input type="text" name="OptionGroups[0].Name" value="" maxlength="80" placeholder="e.g. Colour" ' +
      'class="w-full rounded-lg border border-ink/15 bg-white/70 px-3 py-2 text-sm text-ink outline-none focus:ring-2 focus:ring-gold-500/30" />' +
      '</div>' +
      '<button type="button" class="option-group-remove text-xs font-semibold text-ink/50 transition hover:text-red-700" data-remove-group>Remove group</button>' +
      '</div>' +
      '<div class="mt-3 space-y-2" data-choices>' +
      choiceRowHtml() +
      '</div>' +
      '<button type="button" class="mt-2 text-xs font-semibold text-gold-600 hover:text-gold-500" data-add-choice>+ Add choice</button>' +
      '</div>'
    );
  }

  addGroupBtn.addEventListener('click', () => {
    if (list.querySelectorAll('[data-option-group]').length >= maxGroups) return;
    list.insertAdjacentHTML('beforeend', groupHtml());
    reindex();
  });

  list.addEventListener('click', (e) => {
    const t = e.target;
    if (!(t instanceof HTMLElement)) return;

    if (t.closest('[data-remove-group]')) {
      t.closest('[data-option-group]')?.remove();
      reindex();
      return;
    }

    if (t.closest('[data-add-choice]')) {
      const group = t.closest('[data-option-group]');
      const choices = group?.querySelector('[data-choices]');
      if (!choices) return;
      if (choices.querySelectorAll('[data-choice-row]').length >= maxChoices) return;
      choices.insertAdjacentHTML('beforeend', choiceRowHtml());
      reindex();
      return;
    }

    if (t.closest('[data-remove-choice]')) {
      const row = t.closest('[data-choice-row]');
      const choices = t.closest('[data-choices]');
      if (!row || !choices) return;
      if (choices.querySelectorAll('[data-choice-row]').length <= 1) {
        const input = row.querySelector('input');
        if (input) input.value = '';
        return;
      }
      row.remove();
      reindex();
    }
  });
})();
