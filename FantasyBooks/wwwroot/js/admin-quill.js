(function () {
  const textarea = document.getElementById('Input_Description') || document.querySelector('textarea[name="Input.Description"]');
  const host = document.getElementById('description-editor');
  if (!textarea || !host || typeof Quill === 'undefined') return;

  const quill = new Quill(host, {
    theme: 'snow',
    placeholder: 'Write a product description…',
    modules: {
      toolbar: [
        [{ header: [2, 3, false] }],
        ['bold', 'italic', 'underline'],
        [{ list: 'ordered' }, { list: 'bullet' }],
        ['link'],
        ['clean'],
      ],
    },
  });

  const initial = textarea.value || '';
  if (initial.trim()) {
    quill.root.innerHTML = initial;
  }

  const sync = () => {
    let html = quill.root.innerHTML;
    if (html === '<p><br></p>' || html === '<p></p>') html = '';
    textarea.value = html;
  };

  quill.on('text-change', sync);

  const form = host.closest('form');
  if (form) {
    form.addEventListener('submit', sync);
  }

  textarea.classList.add('hidden');
  textarea.setAttribute('aria-hidden', 'true');
  textarea.tabIndex = -1;
})();
