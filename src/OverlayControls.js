// CLAUDE : new file. Generic binding: every [data-key] control <-> one ini key; C# owns persistence.
(function () {
  const post = (o) => window.chrome.webview.postMessage(o);
  const ctl = {};
  document.querySelectorAll('[data-key]').forEach(el => { ctl[el.dataset.key] = el; });
  const sections = Array.from(document.querySelectorAll('details[data-sec]'));
  let loading = true;

  function show(el, v) {
    if (el.type === 'checkbox') { el.checked = (v === '1'); }
    else if (el.dataset.hex) { el.value = '#' + (v || '80FFA0').replace('#', ''); }
    else { el.value = v; }
    const out = el.parentElement.querySelector('output');
    if (out) { out.textContent = el.value; }
  }
  function read(el) {
    if (el.type === 'checkbox') { return el.checked ? '1' : '0'; }
    if (el.dataset.hex) { return el.value.replace('#', '').toUpperCase(); }
    return el.value;
  }
  const setKey = (k, v) => { if (ctl[k]) { show(ctl[k], v); } post({ type: 'set', key: k, value: v }); };
  const bind = (key, el) => {
    const send = () => {
      const out = el.parentElement.querySelector('output');
      if (out) { out.textContent = el.value; }
      post({ type: 'set', key: key, value: read(el) });
    };
    el.addEventListener((el.type === 'text' || el.type === 'number') ? 'change' : 'input', send);
  };
  Object.entries(ctl).forEach(([key, el]) => bind(key, el));

  // sections generated from the parameter table the app sends (GLASS / SCENE / POST groups carried over from the TRS80 project)
  let built = false;
  function buildGen(params) {
    if (built) { return; }
    built = true;
    params.forEach(p => {
      const host = document.querySelector('.gen[data-group="' + p.group + '"]');
      if (!host) { return; }
      const lab = document.createElement('label');
      let el;
      if (p.kind === 'toggle') {
        lab.className = 'chk';
        el = document.createElement('input'); el.type = 'checkbox';
        lab.appendChild(el); lab.appendChild(document.createTextNode(' ' + p.label));
      } else if (p.kind === 'select') {
        lab.appendChild(document.createTextNode(p.label));
        el = document.createElement('select');
        p.options.forEach((o, i) => { const op = document.createElement('option'); op.value = String(i); op.textContent = o; el.appendChild(op); });
        lab.appendChild(el);
      } else {
        lab.appendChild(document.createTextNode(p.label));
        const out = document.createElement('output'); lab.appendChild(out);
        el = document.createElement('input'); el.type = 'range';
        el.min = p.min; el.max = p.max; el.step = p.step;
        lab.appendChild(el);
      }
      el.dataset.key = p.id;
      host.appendChild(lab);
      ctl[p.id] = el;
      bind(p.id, el);
    });
  }

  // collapsible sections : the open set is persisted in the ini (UI.OpenSections)
  const saveSections = () => post({ type: 'set', key: 'UI.OpenSections', value: sections.filter(d => d.open).map(d => d.dataset.sec).join(',') });
  sections.forEach(d => d.addEventListener('toggle', () => { if (!loading) { saveSections(); } }));
  const setOpen = (list) => { const s = new Set(list.split(',').filter(x => x)); sections.forEach(d => { d.open = s.has(d.dataset.sec); }); };
  document.getElementById('expandAll').addEventListener('click', () => { sections.forEach(d => { d.open = true; }); });
  document.getElementById('collapseAll').addEventListener('click', () => { sections.forEach(d => { d.open = false; }); });

  const click = (id, msg, confirmText) => document.getElementById(id).addEventListener('click', () => {
    if (confirmText && !confirm(confirmText)) { return; }
    post({ type: msg });
  });
  document.getElementById('resetFit').addEventListener('click', () => {
    [['Look.ScreenOffsetX', '0'], ['Look.ScreenOffsetY', '0'], ['Look.ScreenScaleX', '100'], ['Look.ScreenScaleY', '100']].forEach(([k, v]) => setKey(k, v));
  });
  document.getElementById('resetPicture').addEventListener('click', () => {
    [['Look.PictureOffsetX', '0'], ['Look.PictureOffsetY', '0'], ['Look.PictureScaleX', '100'], ['Look.PictureScaleY', '100']].forEach(([k, v]) => setKey(k, v));
  });
  document.getElementById('resetGlass').addEventListener('click', () => {
    ['Glass.ScaleX', 'Glass.ScaleY', 'Glass.OffsetX', 'Glass.OffsetY'].forEach(k => post({ type: 'set', key: k, value: '' })); // blank = auto
  });
  click('resetPower', 'resetPower');
  click('browse', 'browse');
  click('relaunch', 'relaunch', 'Kill and restart the emulator with the current command line?');
  click('toggleFrame', 'toggleFrame');
  click('toggleOverlay', 'toggleOverlay');
  click('exit', 'exit');

  // presets (.gpc files)
  click('presetLoad', 'presetLoad');
  click('presetSave', 'presetSave');
  click('presetSaveAs', 'presetSaveAs');

  const setText = (id, v) => { document.getElementById(id).textContent = v; };
  const setProfile = (p) => { setText('profile', p || '-'); };
  window.chrome.webview.addEventListener('message', (e) => {
    const m = e.data;
    if (m.type === 'init') {
      if (m.params) { buildGen(m.params); }
      Object.entries(m.values).forEach(([k, v]) => { if (ctl[k]) { show(ctl[k], v); } });
      if (m.values['UI.OpenSections'] !== undefined && loading) { setOpen(m.values['UI.OpenSections']); }
      setTimeout(() => { loading = false; }, 100); // 'toggle' events from setOpen fire async
    }
    else if (m.type === 'toast') { alert(m.text); }
    else if (m.type === 'value') { if (ctl[m.key]) { show(ctl[m.key], m.value); } }
    else if (m.type === 'result') { if (ctl[m.key]) { ctl[m.key].classList.toggle('bad', !m.ok); } }
    else if (m.type === 'presets') { setProfile(m.profile); setText('presetFile', m.file); }
    else if (m.type === 'status') {
      setText('st-state', m.state); setText('st-hwnd', m.hwnd); setText('st-emu', m.emu);
      setText('st-overlay', m.overlay); setText('st-frame', m.frame); setText('st-mode', m.mode); setText('st-rate', m.syncsPerSec);
      setProfile(m.profile);
      const w = ctl['Window.Width'], h = ctl['Window.Height'];
      if (w && document.activeElement !== w && m.winW > 0) { w.value = m.winW; }
      if (h && document.activeElement !== h && m.winH > 0) { h.value = m.winH; }
    }
  });
  post({ type: 'ready' });
})();
