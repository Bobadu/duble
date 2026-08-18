// The report's only script: filter by verdict, search by name, flip the theme. Inlined into every
// report, so it must stay dependency-free.

const filterButtons = document.querySelectorAll('[data-filter]');
const search = document.getElementById('search');
const activeVerdicts = new Set();

function refresh() {
  const phrase = (search.value || '').toLowerCase().trim();
  let visible = 0;
  document.querySelectorAll('article.group').forEach(el => {
    const matchesVerdict = activeVerdicts.size === 0 || activeVerdicts.has(el.dataset.verdict);
    const matchesPhrase = !phrase || el.dataset.search.includes(phrase);
    const shown = matchesVerdict && matchesPhrase;
    el.hidden = !shown;
    if (shown) visible++;
  });
  document.getElementById('counter').textContent = visible;
}

filterButtons.forEach(button => button.addEventListener('click', () => {
  const verdict = button.dataset.filter;
  if (activeVerdicts.has(verdict)) { activeVerdicts.delete(verdict); button.setAttribute('aria-pressed', 'false'); }
  else { activeVerdicts.add(verdict); button.setAttribute('aria-pressed', 'true'); }
  refresh();
}));

search.addEventListener('input', refresh);

document.getElementById('theme').addEventListener('click', () => {
  const current = document.documentElement.getAttribute('data-theme');
  const isLight = current === 'light' || (!current && !window.matchMedia('(prefers-color-scheme: dark)').matches);
  document.documentElement.setAttribute('data-theme', isLight ? 'dark' : 'light');
});

refresh();
