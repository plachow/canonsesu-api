// ── State ──
let idcode = null;
let data = null;
let formValues = {};

// ── Boot ──
document.addEventListener('DOMContentLoaded', () => {
  const params = new URLSearchParams(window.location.search);
  idcode = params.get('idcode');

  if (!idcode || idcode.trim() === '') {
    renderErrorState('Chybí přístupový kód', 'Odkaz v emailu není platný nebo je neúplný. Zkontrolujte, zda jste zkopírovali celý odkaz z emailu.');
    return;
  }

  renderLoading();
  fetchDevices();
});

// ── API ──
async function fetchDevices() {
  try {
    const resp = await fetch(`/api/user/${encodeURIComponent(idcode)}`);
    if (resp.status === 404) {
      renderErrorState('Neplatný nebo expirovaný odkaz', 'Tento odkaz není platný nebo již vypršela jeho platnost. Hlášení se vztahuje vždy k aktuálnímu měsíci — minulé odkazy nejsou funkční.');
      return;
    }
    if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
    data = await resp.json();
    renderForm();
  } catch (e) {
    renderErrorState('Chyba připojení', 'Nepodařilo se načíst data. Zkontrolujte připojení k internetu a zkuste stránku obnovit.');
  }
}

async function handleSubmit() {
  const btn = document.getElementById('btnSubmit');
  btn.disabled = true;
  btn.textContent = 'Odesílám…';

  if (data.alreadySubmitted) {
    const ok = confirm('Toto hlášení bylo již odesláno. Opravdu chcete odeslat nové hodnoty?');
    if (!ok) {
      btn.disabled = false;
      btn.textContent = 'Odeslat hlášení';
      return;
    }
  }

  const payload = data.devices.map(d => ({
    recordId: d.recordId,
    aktualniStavPocitadla: parseInt(formValues[d.recordId]?.value, 10),
    poznamka: formValues[d.recordId]?.poznamka || null
  }));

  try {
    const resp = await fetch(`/api/user/${encodeURIComponent(idcode)}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    if (resp.status === 422) {
      const msg = await resp.text();
      alert('Hlášení nelze odeslat: ' + msg);
      btn.disabled = false;
      btn.textContent = 'Odeslat hlášení';
      return;
    }

    if (!resp.ok) throw new Error(`HTTP ${resp.status}`);

    document.getElementById('submitBar').style.display = 'none';
    renderSuccess(payload);
  } catch (e) {
    alert('Odeslání se nezdařilo. Zkuste to prosím znovu.');
    btn.disabled = false;
    btn.textContent = 'Odeslat hlášení';
  }
}

// ── Renderers ──
function renderLoading() {
  document.getElementById('app').innerHTML = `
    <div class="skeleton-card">
      <div class="skeleton skeleton-header"></div>
      <div class="skeleton-row">
        <div class="skeleton skeleton-block" style="flex:1;height:60px;"></div>
        <div class="skeleton skeleton-block" style="width:130px;height:60px;"></div>
        <div class="skeleton skeleton-block" style="width:180px;height:60px;"></div>
      </div>
      <div class="skeleton-row" style="border-top:1px solid #eee;">
        <div class="skeleton skeleton-block" style="flex:1;height:60px;"></div>
        <div class="skeleton skeleton-block" style="width:130px;height:60px;"></div>
        <div class="skeleton skeleton-block" style="width:180px;height:60px;"></div>
      </div>
    </div>
    <div class="skeleton-card" style="animation-delay:.1s">
      <div class="skeleton skeleton-header"></div>
      <div class="skeleton-row">
        <div class="skeleton skeleton-block" style="flex:1;height:60px;"></div>
        <div class="skeleton skeleton-block" style="width:130px;height:60px;"></div>
        <div class="skeleton skeleton-block" style="width:180px;height:60px;"></div>
      </div>
    </div>
    <p style="text-align:center;color:var(--ink-faint);font-size:13px;margin-top:12px;">Načítám data…</p>
  `;
}

function renderErrorState(title, body) {
  document.getElementById('submitBar').style.display = 'none';
  document.getElementById('app').innerHTML = `
    <div class="state-page">
      <div class="state-icon icon-error">
        <svg viewBox="0 0 24 24" fill="none" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="12" cy="12" r="10"/>
          <line x1="12" y1="8" x2="12" y2="12"/>
          <line x1="12" y1="16" x2="12.01" y2="16"/>
        </svg>
      </div>
      <div class="state-title">${esc(title)}</div>
      <div class="state-body">${esc(body)}</div>
    </div>
  `;
}

function renderSuccess(payload) {
  const rows = data.devices.map(d => {
    const submitted = payload.find(p => p.recordId === d.recordId);
    return `
      <tr>
        <td>${esc(d.typStroje)}</td>
        <td style="font-family:monospace">${esc(d.vyrobniCislo)}</td>
        <td>${esc(d.nazevPocitadla)}</td>
        <td style="text-align:right;font-variant-numeric:tabular-nums;">${submitted?.aktualniStavPocitadla?.toLocaleString('cs-CZ') ?? '—'}</td>
        <td><span class="tag-submitted">✓ Odesláno</span></td>
      </tr>
    `;
  }).join('');

  document.getElementById('app').innerHTML = `
    <div class="state-page" style="max-width:700px;text-align:left;">
      <div style="text-align:center;">
        <div class="state-icon icon-success" style="margin-bottom:16px;">
          <svg viewBox="0 0 24 24" fill="none" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
            <path d="M20 6L9 17l-5-5"/>
          </svg>
        </div>
        <div class="state-title">Hlášení bylo úspěšně odesláno</div>
        <div class="state-body" style="margin-bottom:24px;">Děkujeme za nahlášení stavu počítadel. Níže je přehled odeslaných hodnot.</div>
      </div>
      <table class="summary-table">
        <thead>
          <tr>
            <th>Typ stroje</th>
            <th>Sér. číslo</th>
            <th>Počítadlo</th>
            <th style="text-align:right">Odeslaný stav</th>
            <th>Stav</th>
          </tr>
        </thead>
        <tbody>${rows}</tbody>
      </table>
      <p style="font-size:13px;color:var(--ink-muted);margin-top:16px;line-height:1.5;">
        Toto okno můžete zavřít. V případě opravy použijte stejný odkaz z emailu a hodnoty odešlete znovu.
      </p>
    </div>
  `;
}

function renderForm() {
  const app = document.getElementById('app');
  const html = [];

  const period = formatPeriod(data.period);
  const deadline = formatDate(data.deadline);
  const deadlineClass = data.isPastDeadline ? 'deadline-past' : '';

  html.push(`
    <div class="period-bar">
      <div class="period-item">
        <span class="period-label">Období hlášení:</span>
        <span class="period-value">${period}</span>
      </div>
      <div class="period-item">
        <span class="period-label">Termín odevzdání:</span>
        <span class="period-value ${deadlineClass}">${deadline}</span>
      </div>
    </div>
  `);

  if (data.isPastDeadline) {
    html.push(`
      <div class="banner banner-warn">
        <div class="banner-icon">!</div>
        <div class="banner-text">
          <strong>Termín pro odevzdání hlášení uplynul</strong>
          Termín pro odevzdání hlášení již uplynul. Kontaktujte servisní oddělení.
        </div>
      </div>
    `);
  } else if (data.alreadySubmitted) {
    html.push(`
      <div class="banner banner-info">
        <div class="banner-icon">i</div>
        <div class="banner-text">
          <strong>Hlášení bylo již odesláno</strong>
          Toto hlášení bylo již odesláno. Hodnoty můžete opravit a odeslat znovu.
        </div>
      </div>
    `);
  }

  html.push(`
    <div class="formula-hint">
      <button class="formula-toggle" id="formulaToggle" onclick="toggleFormula()">
        <span>ℹ Výpočet počítadel Total BW / Total Colour</span>
        <svg class="arrow" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="6 9 12 15 18 9"/>
        </svg>
      </button>
      <div class="formula-body" id="formulaBody">
        <p>Pokud Váš stroj neobsahuje počítadla <strong>Total BW</strong> a <strong>Total Colour</strong>, použijte prosím tato počítadla, která jsou na stroji nastavena:</p>
        <div class="formula-code">
          Total BW &nbsp;&nbsp;&nbsp;= 2 × 112 (Black/Large) + 113 (Black/Small)<br>
          Total Colour = 2 × 122 (Color/Large) + 123 (Color/Small)
        </div>
        <p>Počítadla 112, 113, 122, 123 najdete v menu tiskárny v sekci počítadel.</p>
      </div>
    </div>
  `);

  const groups = groupDevices(data.devices);
  for (const group of groups) {
    html.push(`
      <div class="printer-card">
        <div class="printer-header">
          <div class="printer-icon">
            <svg viewBox="0 0 24 24"><path d="M6 9V2h12v7M6 18H4a2 2 0 01-2-2v-5a2 2 0 012-2h16a2 2 0 012 2v5a2 2 0 01-2 2h-2M6 14h12v8H6z"/></svg>
          </div>
          <div class="printer-info">
            <div class="printer-model">${esc(group.typStroje)}</div>
            <div class="printer-serial">${esc(group.vyrobniCislo)}</div>
            <div class="printer-config-label">Konfigurace: ${esc(group.typKonfigurace)}</div>
          </div>
        </div>
        ${group.devices.map(d => renderCounterRow(d)).join('')}
      </div>
    `);
  }

  app.innerHTML = html.join('');

  if (data.alreadySubmitted) {
    data.devices.forEach(d => {
      if (d.aktualniStavPocitadla != null) {
        formValues[d.recordId] = { value: String(d.aktualniStavPocitadla), poznamka: d.poznamka || '' };
        const input = document.getElementById(`val-${d.recordId}`);
        if (input) { input.value = d.aktualniStavPocitadla; validateField(d.recordId); }
        const note = document.getElementById(`note-${d.recordId}`);
        if (note) note.value = d.poznamka || '';
      }
    });
  } else {
    data.devices.forEach(d => { formValues[d.recordId] = { value: '', poznamka: '' }; });
  }

  if (!data.isPastDeadline) {
    document.getElementById('submitBar').style.display = '';
  }

  updateProgress();
}

function renderCounterRow(d) {
  const disabled = data.isPastDeadline ? 'disabled' : '';
  const prevFormatted = d.posledniStavPocitadla != null
    ? d.posledniStavPocitadla.toLocaleString('cs-CZ')
    : '—';

  return `
    <div class="counter-row" id="row-${d.recordId}">
      <div class="counter-meta">
        <div class="counter-name">${esc(d.nazevPocitadla)}</div>
        <div class="counter-type">Typ: ${esc(d.typPocitadla)}</div>
      </div>
      <div class="counter-prev">
        <div class="prev-label">Stav minulého hlášení</div>
        <div class="prev-value">${prevFormatted}</div>
      </div>
      <div class="counter-inputs">
        <div class="field-group">
          <label for="val-${d.recordId}">Aktuální stav počítadla *</label>
          <input
            type="number"
            id="val-${d.recordId}"
            placeholder="Zadejte aktuální stav"
            min="${d.posledniStavPocitadla ?? 0}"
            step="1"
            ${disabled}
            oninput="onValueInput(${d.recordId}, ${d.posledniStavPocitadla ?? 0})"
            onblur="onValueBlur(${d.recordId}, ${d.posledniStavPocitadla ?? 0})"
          />
          <div class="field-error" id="err-${d.recordId}"></div>
          <div class="field-diff" id="diff-${d.recordId}"></div>
        </div>
        <div class="field-group">
          <label for="note-${d.recordId}">Poznámka</label>
          <textarea
            id="note-${d.recordId}"
            placeholder="Volitelná poznámka"
            rows="2"
            ${disabled}
            oninput="onNoteInput(${d.recordId})"
          ></textarea>
        </div>
      </div>
    </div>
  `;
}

// ── Validation ──
function validateField(recordId, prev, touched = false) {
  const input  = document.getElementById(`val-${recordId}`);
  const errEl  = document.getElementById(`err-${recordId}`);
  const diffEl = document.getElementById(`diff-${recordId}`);
  const row    = document.getElementById(`row-${recordId}`);
  const rawVal = input.value.trim();

  input.classList.remove('input-valid', 'input-error');
  errEl.classList.remove('visible');
  diffEl.classList.remove('visible');
  row.classList.remove('row-valid', 'row-error');

  if (rawVal === '') {
    if (touched) {
      errEl.textContent = 'Toto pole je povinné';
      errEl.classList.add('visible');
      input.classList.add('input-error');
      row.classList.add('row-error');
    }
    formValues[recordId] = { ...formValues[recordId], value: '' };
    updateProgress();
    return false;
  }

  if (!/^-?\d+$/.test(rawVal)) {
    errEl.textContent = 'Zadejte celé číslo';
    errEl.classList.add('visible');
    input.classList.add('input-error');
    row.classList.add('row-error');
    formValues[recordId] = { ...formValues[recordId], value: '' };
    updateProgress();
    return false;
  }

  const val = parseInt(rawVal, 10);
  const prevVal = prev ?? 0;

  if (val < prevVal) {
    errEl.textContent = `Hodnota nesmí být nižší než minulý stav (${prevVal.toLocaleString('cs-CZ')})`;
    errEl.classList.add('visible');
    input.classList.add('input-error');
    row.classList.add('row-error');
    formValues[recordId] = { ...formValues[recordId], value: '' };
    updateProgress();
    return false;
  }

  input.classList.add('input-valid');
  row.classList.add('row-valid');
  formValues[recordId] = { ...formValues[recordId], value: String(val) };
  diffEl.textContent = `+${(val - prevVal).toLocaleString('cs-CZ')} kopií od minulého hlášení`;
  diffEl.classList.add('visible');
  updateProgress();
  return true;
}

function onValueInput(recordId, prev) {
  const input = document.getElementById(`val-${recordId}`);
  if (input.value !== '') {
    validateField(recordId, prev, true);
  } else {
    input.classList.remove('input-valid', 'input-error');
    document.getElementById(`err-${recordId}`).classList.remove('visible');
    document.getElementById(`diff-${recordId}`).classList.remove('visible');
    document.getElementById(`row-${recordId}`).classList.remove('row-valid', 'row-error');
    formValues[recordId] = { ...formValues[recordId], value: '' };
    updateProgress();
  }
}

function onValueBlur(recordId, prev) { validateField(recordId, prev, true); }

function onNoteInput(recordId) {
  formValues[recordId] = { ...formValues[recordId], poznamka: document.getElementById(`note-${recordId}`).value };
}

// ── Progress ──
function updateProgress() {
  if (!data) return;
  const total  = data.devices.length;
  const filled = data.devices.filter(d => formValues[d.recordId]?.value !== '').length;
  const pct    = total > 0 ? Math.round((filled / total) * 100) : 0;

  document.getElementById('progressFilled').textContent = filled;
  document.getElementById('progressTotal').textContent  = total;
  document.getElementById('progressFill').style.width   = pct + '%';
  document.getElementById('btnSubmit').disabled = filled < total;
}

// ── Formula toggle ──
function toggleFormula() {
  document.getElementById('formulaToggle').classList.toggle('open');
  document.getElementById('formulaBody').classList.toggle('open');
}

// ── Grouping ──
function groupDevices(devices) {
  const map = new Map();
  for (const d of devices) {
    const key = `${d.vyrobniCislo}||${d.typKonfigurace}`;
    if (!map.has(key)) {
      map.set(key, { typStroje: d.typStroje, vyrobniCislo: d.vyrobniCislo, typKonfigurace: d.typKonfigurace, devices: [] });
    }
    map.get(key).devices.push(d);
  }
  return Array.from(map.values());
}

// ── Helpers ──
function esc(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;').replace(/</g, '&lt;')
    .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function formatPeriod(iso) {
  if (!iso) return '—';
  return new Date(iso).toLocaleDateString('cs-CZ', { month: 'long', year: 'numeric' });
}

function formatDate(iso) {
  if (!iso) return '—';
  return new Date(iso).toLocaleDateString('cs-CZ', { day: 'numeric', month: 'numeric', year: 'numeric' });
}
