const dukhiInspectorStyles = `
  .surface {
    --surface: #191720;
    --raised: #211e2a;
    --line: #383341;
    --muted: #aaa2b4;
    --accent: #e4a9d4;
    --selected: #3a2841;
    --mono: 12px/1.5 Consolas, monospace;
    display: grid;
    grid-template-rows: auto auto minmax(0, 1fr) auto;
    height: 100%;
    background: var(--surface);
    border-left: 1px solid #67516d;
    color: #f0eaf3;
    font: 13px/1.4 'Segoe UI', sans-serif;
  }
  *, *::before, *::after { box-sizing: border-box; }
  [hidden] { display: none !important; }
  .grip { position: absolute; top: 0; bottom: 0; left: -3px; width: 7px; z-index: 2; cursor: ew-resize; touch-action: none; }
  .grip::after { content: ''; position: absolute; inset: calc(50% - 16px) 1px auto; height: 32px; border-radius: 5px; background: #94739c; }
  .grip:hover, .grip:focus-visible, .grip.dragging { background: #a775a8; }
  header, .tools, .tabs, .chips, .detail-tabs, .crumbs, .footer { display: flex; align-items: center; gap: 5px; }
  header { min-height: 39px; padding: 0 7px; background: var(--raised); border-bottom: 1px solid var(--line); }
  .tabs { min-width: 0; align-self: stretch; gap: 1px; }
  .tabs button { padding: 6px 10px; color: var(--muted); border-width: 0 0 2px; border-radius: 0; }
  .tabs button[aria-selected='true'] { background: #322638; color: #fff0fa; border-bottom-color: var(--accent); }
  .icon { min-width: 29px; padding: 2px 6px; font-size: 16px; border-color: transparent; }
  .close { margin-left: auto; font-size: 19px; }
  .tools { min-height: 40px; flex-wrap: wrap; padding: 5px 8px; border-bottom: 1px solid var(--line); }
  .search { flex: 1 1 145px; min-width: 100px; }
  .tools input, .details input { padding: 5px 8px; background: #15131c; border-color: var(--line); }
  .details input { width: 100%; }
  input::placeholder { color: #a198a9; }
  button, input {
    color: inherit; font: inherit; border: 1px solid transparent; border-radius: 5px;
    background: transparent; min-width: 0;
  }
  button { padding: 3px 7px; cursor: pointer; }
  button:hover { background: #342b3c; }
  button:focus-visible, input:focus-visible, .view:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
  button[aria-pressed='true'] { background: var(--selected); border-color: #6c4c73; }
  button:disabled { opacity: .5; cursor: default; }
  .chips { flex-wrap: wrap; gap: 2px; }
  .chips button { padding: 3px 6px; font-size: 12px; }
  .tools .action { border-color: var(--line); }
  .view { min-height: 0; overflow: hidden; }
  .scroll { overflow: auto; min-width: 0; min-height: 0; overscroll-behavior: contain; scrollbar-color: #514659 var(--surface); }
  .stack { display: grid; height: 100%; grid-template-rows: minmax(118px, 1fr) auto minmax(110px, .8fr); }
  .network { min-height: 100%; height: 100%; display: grid; grid-template-rows: minmax(0, 1fr) auto; }
  .network.has-detail { grid-template-rows: minmax(120px, 1fr) minmax(110px, .85fr); }
  .footer { min-height: 25px; padding: 2px 8px; border-top: 1px solid var(--line); font-size: 11px; color: var(--muted); }
  .empty { color: var(--muted); margin: 15px 10px; }
  pre, code { font: var(--mono); }
  pre { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
  .logs { height: 100%; }
  .log {
    display: grid; grid-template-columns: max-content minmax(0, 1fr); column-gap: 9px;
    padding: 6px 9px; border-bottom: 1px solid #2d2836;
  }
  .log-time { padding-top: 1px; color: var(--muted); font: 11px Consolas, monospace; }
  .log-body { min-width: 0; }
  .log-meta { display: flex; align-items: center; flex-wrap: wrap; gap: 5px; color: var(--muted); font-size: 11px; }
  .log.warn { background: #30271e; color: #ffdcaa; }
  .log.error { background: #36212a; color: #ffb8c8; }
  .log.debug { color: #c2aecf; }
  .source, .badge { color: inherit; opacity: .86; overflow-wrap: anywhere; }
  .badge { padding: 0 4px; border: 1px solid currentColor; border-radius: 5px; }
  .log details { padding-top: 3px; }
  .log summary { cursor: pointer; font: var(--mono); }
  .log .kv { margin-top: 3px; }
  .tree { padding: 4px; }
  .tree-row { display: flex; align-items: center; min-height: 24px; padding-left: min(var(--depth) * 13px, 35%); }
  .tree-row[data-selected] { background: var(--selected); border-radius: 4px; }
  .tree-row:hover { background: #2d2536; }
  .twisty, .spacer { width: 20px; flex: 0 0 20px; padding: 0; color: var(--muted); }
  .tree-line { flex: 1; text-align: left; border-radius: 0; font: var(--mono); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .tree-line:hover { background: none; }
  .element-menu {
    position: fixed; z-index: 3; width: 198px; max-width: calc(var(--dukhi-inspector-width, 42vw) - 8px);
    padding: 4px; background: var(--raised); border: 1px solid var(--line);
    border-radius: 7px; box-shadow: 0 5px 16px #0d0a10b3;
  }
  .element-menu button { display: block; width: 100%; padding: 6px 8px; text-align: left; }
  .tag { color: #e5a9d7; }
  .attr { color: #b5c6f2; }
  .value { color: #b8dab1; }
  .crumbs { overflow-x: auto; min-height: 30px; padding: 2px 5px; border-block: 1px solid var(--line); background: var(--raised); scrollbar-width: thin; scrollbar-color: #514659 var(--raised); }
  .crumbs button { max-width: 160px; flex-shrink: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font: var(--mono); }
  .crumbs button[aria-current='true'] { color: var(--accent); }
  .detail { border-top: 1px solid var(--line); display: grid; grid-template-rows: auto minmax(0, 1fr); min-height: 0; }
  .detail-tabs { padding: 3px 7px; background: var(--raised); border-bottom: 1px solid var(--line); }
  .detail-tabs button[aria-selected='true'] { color: var(--accent); background: var(--selected); }
  .detail-tabs .icon { margin-left: auto; }
  .details { padding: 8px; overflow-wrap: anywhere; }
  .details h3 { margin: 0 0 5px; font-size: 13px; }
  .kv { display: grid; grid-template-columns: minmax(90px, 37%) minmax(0, 1fr); margin: 3px 0 0; font: var(--mono); }
  .kv :is(dt, dd) { margin: 0; padding: 3px 5px; border-top: 1px solid #312a3a; overflow-wrap: anywhere; }
  .kv dt { color: #c6accb; }
  .dimensions { margin: 0 0 8px; color: var(--muted); }
  .element-info { display: flex; flex-wrap: wrap; align-items: baseline; gap: 5px 10px; margin-bottom: 8px; }
  .element-info .dimensions { margin: 0; }
  .details .action { border-color: var(--line); }
  .element-info textarea {
    width: 100%; min-width: 0; min-height: 90px; resize: vertical; padding: 6px;
    background: #15131c; color: inherit; border: 1px solid var(--line); border-radius: 5px; font: var(--mono);
  }
  .network table { width: 100%; border-spacing: 0; table-layout: fixed; font-size: 12px; }
  th { position: sticky; top: 0; background: var(--raised); color: #d2c6d8; text-align: left; font-weight: normal; }
  th, td { border-bottom: 1px solid #302a37; padding: 2px 5px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  th button { padding: 1px 0; white-space: nowrap; }
  th:first-child { width: 43%; }
  th:not(:first-child) { width: 14%; }
  tr:nth-child(even) td { background: #1d1a24; }
  tr[data-active] td, tr:hover td { background: var(--selected); }
  .request-link { text-align: left; display: block; width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; padding-left: 0; }
  .status-ok { color: #acd9b1; }
  .status-error { color: #ffacbd; }
  .timing-track { margin-top: 8px; height: 12px; border-radius: 4px; display: flex; overflow: hidden; background: #332a3b; }
  .timing-track span { height: 100%; }
  .highlight { position: fixed; pointer-events: none; outline: 2px solid #d9a0ff; background: #c49af34d; }
  .surface.compact .wide { display: none; }
  .surface.compact th:first-child { width: 54%; }
  .surface.compact th:not(:first-child) { width: 15%; }
`;
