'use strict';
// Shared icons and home illustration; no application state or demo data.
const icons = {
  home:'<path d="m3 10 9-7 9 7v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z"/><path d="M9 21v-7h6v7"/>',
  network:'<rect x="9" y="2.5" width="6" height="6" rx="1.5"/><rect x="2.5" y="15.5" width="6" height="6" rx="1.5"/><rect x="15.5" y="15.5" width="6" height="6" rx="1.5"/><path d="M12 9v3M5.5 15.5V12h13v3.5"/>',
  archive:'<rect x="4" y="4" width="16" height="4" rx="1"/><path d="M6 8v11a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1V8m-8 4h4"/>',
  activity:'<path d="M3 12h4l3-7 4 14 3-7h4"/>',
  settings:'<path d="M12 2.5v3m0 13v3M5.3 5.3l2.1 2.1m9.2 9.2 2.1 2.1M2.5 12h3m13 0h3M5.3 18.7l2.1-2.1m9.2-9.2 2.1-2.1"/><circle cx="12" cy="12" r="6.5"/><circle cx="12" cy="12" r="2.4"/>',
  layers:'<path d="m12 3 9 4.5-9 4.5L3 7.5 12 3zM3 12l9 4.5 9-4.5M3 16.5l9 4.5 9-4.5"/>',
  plus:'<path d="M12 5v14M5 12h14"/>', right:'<path d="m9 6 6 6-6 6"/>', power:'<path d="M12 3v9m4.8-6.5a8 8 0 1 1-9.6 0"/>',
  zap:'<path d="m13 2-9 12h7l-1 8 10-12h-7l1-8Z"/>', arrow:'<path d="M4 12h15m-6-6 6 6-6 6"/>',
  file:'<path d="M13 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V9zM13 3v6h6m-10 4h6m-6 4h6"/>',
  copy:'<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 4H6a2 2 0 0 0-2 2v10"/>',
  link:'<path d="m10 13 4-4m-6 7-1 1a3.5 3.5 0 0 1-5-5l4-4a3.5 3.5 0 0 1 5 0m2 8a3.5 3.5 0 0 0 5 0l4-4a3.5 3.5 0 0 0-5-5l-1 1"/>',
  refresh:'<path d="M19 10a7 7 0 1 0 .2 4M19 5v5h-5"/>', check:'<path d="m5 12 4 4L19 6"/>', x:'<path d="m6 6 12 12M18 6 6 18"/>',
  upload:'<path d="M12 16V4m-4 4 4-4 4 4M4 15v4a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-4"/>',
  more:'<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>',
  edit:'<path d="m15 5 4 4M4 20l5-1 11-11a2.8 2.8 0 0 0-4-4L5 15z"/>',
  trash:'<path d="M4 7h16m-10 4v6m4-6v6M6 7l1 13h10l1-13M9 7V4h6v3"/>',
  chevron:'<path d="m6 9 6 6 6-6"/>',
  scan:'<path d="M8 3H5a2 2 0 0 0-2 2v3m13-5h3a2 2 0 0 1 2 2v3M3 16v3a2 2 0 0 0 2 2h3m13-5v3a2 2 0 0 1-2 2h-3M7 12h10"/>',
  chip:'<rect x="5" y="5" width="14" height="14" rx="3"/><rect x="9" y="9" width="6" height="6" rx="1"/><path d="M9 2v3m6-3v3M9 19v3m6-3v3M2 9h3m-3 6h3m14-6h3m-3 6h3"/>'
};
document.querySelector('#icons').innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" aria-hidden="true">${Object.entries(icons).map(([name, paths]) => `<symbol id="icon-${name}" viewBox="0 0 24 24">${paths}</symbol>`).join('')}</svg>`;
const icon = (name, cl = '') => `<svg class="icon ${cl}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><use href="#icon-${name}"></use></svg>`;
function routeDiagram(control=''){return `<figure class="route-diagram">
  <svg aria-hidden="true" viewBox="0 0 540 296" fill="none" preserveAspectRatio="xMidYMid meet">
    <defs><pattern id="route-grid" width="24" height="24" patternUnits="userSpaceOnUse"><path d="M24 0H0V24" class="diagram-grid"/></pattern></defs>
    <rect x="0" y="0" width="540" height="296" fill="url(#route-grid)"/>
    <path d="M24 44v-20h20m452 0h20v20M24 252v20h20m452 0h20v-20" class="diagram-corners"/>
    <path d="M73 150h110l43-43h72l40 43h129M73 150h110l43 43h72l40-43h129M73 150h394" class="diagram-traces"/>
    <path d="M73 150h394" class="diagram-link"/>
    <circle class="diagram-dot" cx="184" cy="150" r="3"/><circle class="diagram-dot" cx="338" cy="150" r="3"/>
    <circle class="diagram-dot" cx="226" cy="107" r="3"/><circle class="diagram-dot" cx="298" cy="193" r="3"/>
    <g class="diagram-block"><rect x="54" y="131" width="38" height="38" rx="7"/><path d="M64 142h18v12H64zm4 16h10m-5-4v4"/></g>
    <g class="diagram-block main"><rect x="235" y="123" width="68" height="54" rx="9"/><path d="M252 139h10l12 10h12m-34 0h34m-34 10h10l12-10m7-5 5 5-5 5"/></g>
    <g class="diagram-block"><rect x="448" y="131" width="38" height="38" rx="7"/><circle cx="467" cy="150" r="9"/><path d="M458 150h18m-9-9a15 15 0 0 1 0 18m0-18a15 15 0 0 0 0 18"/></g>
    <text x="73" y="217">本机</text><text x="269" y="217">代理内核</text><text x="467" y="217">出口</text>
  </svg>${control}
</figure>`;}
