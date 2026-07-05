// Gedeeld gedrag voor alle pagina's: mobiel menu + contactformulier-naar-mailto.

// Mobiel menu
const toggle = document.querySelector('.nav-toggle');
const nav = document.querySelector('.site-nav');
if (toggle && nav) {
  toggle.addEventListener('click', () => {
    const open = nav.classList.toggle('open');
    toggle.setAttribute('aria-expanded', String(open));
  });
}

// Contactformulier: geen mail-backend, dus we openen het e-mailprogramma van de
// bezoeker met een voorgevuld bericht (mailto).
const form = document.getElementById('contact-form');
if (form) {
  form.addEventListener('submit', (e) => {
    e.preventDefault();
    const data = new FormData(form);
    const onderwerp = `Contact via zetadesk.net — ${data.get('naam')}`;
    const regels = [
      `Naam: ${data.get('naam')}`,
      `E-mail: ${data.get('email')}`,
      data.get('organisatie') ? `Organisatie: ${data.get('organisatie')}` : null,
      '',
      String(data.get('bericht')),
    ].filter((r) => r !== null);
    const url = `mailto:info@zetadesk.net?subject=${encodeURIComponent(onderwerp)}&body=${encodeURIComponent(regels.join('\n'))}`;
    window.location.href = url;
  });
}
