'use strict';

// ── State ─────────────────────────────────
var _content = '';
var _mode    = 'preview';
var _theme   = 'dark';
var _themeLight = false;
var _modified = false;
var _renderTimer = null;
var _tocVisible = true;
var _largeFileMode = false;
var _tocObserver = null;
var _findHits = [];
var _findIndex = -1;
var _findQuery = '';
var _findMatches = [];
var _printSurfaceReady = false;
var _mermaidCache = new Map();
var _filePath = null;

function mermaidTheme() {
  return _themeLight ? 'default' : 'dark';
}

// C#(ViewerTheme)이 보낸 { light, colors }를 body.themed의 CSS 변수(--bg 등)로 넣는다.
function applyTheme(theme) {
  var colors = (theme && theme.colors) || {};
  _themeLight = !!(theme && theme.light);
  _theme = 'themed';
  document.getElementById('folderss-theme').textContent = 'body.themed{'
    + Object.keys(colors).map(function(k) { return '--' + k + ':' + colors[k]; }).join(';') + '}';
}

// ── Preferences ───────────────────────────
// 목차 표시·너비는 모든 문서에 공통, 보기 모드는 파일별로 기억한다 (WebView2 사용자 데이터 폴더의 localStorage라 앱을 다시 켜도 유지).
// 저장소를 못 쓰는 환경이면 기본값으로 동작한다.
var PREFS_KEY = 'folderss.markdown.prefs';
var MAX_REMEMBERED_MODES = 200;

function loadPrefs() {
  try {
    var p = JSON.parse(localStorage.getItem(PREFS_KEY));
    return p && typeof p === 'object' ? p : {};
  } catch (e) { return {}; }
}

function updatePrefs(change) {
  try {
    var p = loadPrefs();
    change(p);
    localStorage.setItem(PREFS_KEY, JSON.stringify(p));
  } catch (e) {}
}

function rememberMode(mode) {
  if (!_filePath || _largeFileMode) return;
  updatePrefs(function(p) {
    var modes = p.modes && typeof p.modes === 'object' ? p.modes : {};
    delete modes[_filePath];          // 다시 넣어 가장 최근 항목으로 옮긴다
    modes[_filePath] = mode;
    var keys = Object.keys(modes);
    for (var i = 0; i < keys.length - MAX_REMEMBERED_MODES; i++) delete modes[keys[i]];
    p.modes = modes;
  });
}

function rememberedMode(filePath) {
  var modes = loadPrefs().modes;
  var mode = modes && filePath ? modes[filePath] : null;
  return mode === 'edit' || mode === 'split' || mode === 'preview' ? mode : null;
}

// ── Lazy libraries ────────────────────────
// Mermaid(3.2MB)·KaTeX·highlight.js는 페이지 로딩 시간의 대부분이라, 문서에 다이어그램·수식·코드 블록이 있을 때만 불러온다.
// 한 번 불러오면 이후 렌더링은 동기로 처리해 기존과 같이 깜빡임 없이 그린다.
var LAZY_LIBS = { mermaid: 'mermaid.min.js', katex: 'katex.min.js', hljs: 'highlight.min.js' };
var _libPromises = {};
var _renderSeq = 0;

function libLoaded(name) {
  return typeof window[name] !== 'undefined';
}

function loadLib(name) {
  if (!_libPromises[name]) {
    _libPromises[name] = new Promise(function(resolve, reject) {
      var script = document.createElement('script');
      script.src = LAZY_LIBS[name];
      script.onload = function() {
        if (name === 'mermaid') configureMermaid();
        resolve();
      };
      script.onerror = function() {
        delete _libPromises[name];   // 다음 렌더링에서 다시 시도
        reject(new Error(name + ' load failed'));
      };
      document.head.appendChild(script);
    });
  }
  return _libPromises[name];
}

// 라이브러리가 있으면 바로, 없으면 불러온 뒤 실행한다. 불러오는 사이 다시 그려졌으면(입력 등) 지난 결과에는 적용하지 않는다.
function withLib(name, renderId, fn) {
  if (libLoaded(name)) {
    fn();
    return;
  }
  loadLib(name).then(function() {
    if (renderId !== _renderSeq) return;
    fn();
    // 늦게 바뀐 코드·수식 안에는 검색 하이라이트가 없으므로 검색 중이면 다시 칠한다.
    if (_findQuery) updateFindHighlights();
  }).catch(function() {});
}

function configureMermaid() {
  if (libLoaded('mermaid'))
    mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: mermaidTheme() });
}

// ── Init ──────────────────────────────────
marked.use({ gfm: true, breaks: false });

// marked는 본문의 HTML을 그대로 통과시키므로(<img onerror=...> 등), 렌더링 결과를 DOMPurify로 걸러
// 문서 안 스크립트가 chrome.webview.postMessage로 파일 저장·실행을 요청하지 못하게 한다.
// URI 허용 목록은 DOMPurify 기본값에 file:와 Windows 드라이브 경로(C:\, C:/, marked가 인코딩한 C:%5C)를 더했다.
var SANITIZE_OPTIONS = {
  ALLOWED_URI_REGEXP: /^(?:(?:(?:f|ht)tps?|mailto|tel|file):|[a-z]:(?:[\\/]|%5c)|[^a-z]|[a-z+.\-]+(?:[^a-z+.\-:]|$))/i
};

function sanitizeHtml(html) {
  return DOMPurify.sanitize(html, SANITIZE_OPTIONS);
}

// 마크다운 문법 이미지(![](...))는 marked 렌더러가 경로를 바꾸지만, 본문에 직접 쓴 <img src="images/a.png">는 거치지 않으므로
// 정화가 끝난(위험 URI가 걸러진) 뒤 같은 규칙으로 바꾼다. 이미 바뀐 주소(https:)는 resolveImageSrc가 그대로 둔다.
DOMPurify.addHook('afterSanitizeAttributes', function(node) {
  if (node.tagName === 'IMG' && node.hasAttribute('src'))
    node.setAttribute('src', resolveImageSrc(node.getAttribute('src')));
});

// 상대 경로(또는 로컬 절대 경로) 이미지 링크는 브라우저 오리진(folderss-viewer, Resources 폴더)이 아니라
// 현재 열려 있는 마크다운 파일 기준으로 찾아야 하므로, 스킴이 없는 경로는 전용 가상 호스트로 우회시켜
// C# 쪽(MarkdownViewer.ResolveLocalLinkPath)에서 실제 파일을 읽어오게 한다.
marked.use({
  renderer: {
    image: function(href, title, text) {
      var src = resolveImageSrc(href);
      var html = '<img src="' + escapeHtmlAttr(src) + '" alt="' + escapeHtmlAttr(text || '') + '"';
      if (title) html += ' title="' + escapeHtmlAttr(title) + '"';
      html += '>';
      return html;
    }
  }
});

// 수식은 marked가 먼저 처리하면 본문의 _·*·\ 가 강조·이스케이프로 바뀌어 깨지므로(예: $a_1 + b_1$),
// marked 확장으로 수식 구간을 통째로 떼어 data-tex에 담고, 정화 뒤에 KaTeX로 그린다.
// $...$ 는 Pandoc 규칙: 여는 $ 뒤와 닫는 $ 앞이 공백이 아니고 닫는 $ 뒤에 숫자가 없어야 수식 ("$5 and $10" 같은 금액은 글자 그대로).
function mathHtml(tex, display, block) {
  var tag = block ? 'div' : 'span';
  return '<' + tag + ' class="' + (display ? 'math-display' : 'math-inline') + '" data-tex="' + escapeHtmlAttr(tex.trim()) + '">'
    + escapeHtml(tex) + '</' + tag + '>';
}

marked.use({
  extensions: [
    {
      name: 'mathBlock',
      level: 'block',
      start: function(src) { var i = src.search(/\$\$|\\\[/); return i < 0 ? undefined : i; },
      tokenizer: function(src) {
        var m = /^\$\$([\s\S]+?)\$\$[ \t]*(?:\n+|$)/.exec(src) || /^\\\[([\s\S]+?)\\\][ \t]*(?:\n+|$)/.exec(src);
        if (m) return { type: 'mathBlock', raw: m[0], text: m[1] };
      },
      renderer: function(token) { return mathHtml(token.text, true, true); }
    },
    {
      name: 'mathInline',
      level: 'inline',
      start: function(src) { var i = src.search(/\$|\\\(|\\\[/); return i < 0 ? undefined : i; },
      tokenizer: function(src) {
        var m = /^\$\$((?:\\.|[^\\$])+?)\$\$/.exec(src) || /^\\\[([\s\S]+?)\\\]/.exec(src);
        if (m) return { type: 'mathInline', raw: m[0], text: m[1], display: true };
        m = /^\$(?![\s$])((?:\\.|[^\\$])+?)(?<!\s)\$(?!\d)/.exec(src) || /^\\\(([\s\S]+?)\\\)/.exec(src);
        if (m) return { type: 'mathInline', raw: m[0], text: m[1], display: false };
      },
      renderer: function(token) { return mathHtml(token.text, token.display, false); }
    }
  ]
});

function renderMath(root) {
  root.querySelectorAll('[data-tex]').forEach(function(el) {
    try {
      katex.render(el.getAttribute('data-tex'), el, {
        displayMode: el.classList.contains('math-display'),
        throwOnError: false
      });
    } catch (e) { /* 그리지 못하면 원문 수식을 그대로 둔다 */ }
  });
}

function resolveImageSrc(href) {
  if (!href) return href;
  // data:, https:, file: 등 실제 웹/스킴 링크는 그대로 둔다.
  // (Windows 절대 경로 "C:\..."도 콜론을 포함하지만 스킴이 아니므로 별도로 걸러낸다.)
  // http 이미지는 이 페이지(https)에서 혼합 콘텐츠로 막히므로 C#이 대신 받아 전달한다(허용 여부는 C#의 네트워크 설정이 판정).
  if (/^http:/i.test(href))
    return 'https://folderss-doc-asset/remote?u=' + encodeURIComponent(href);
  if (/^(https|data|file|ftp|mailto):/i.test(href))
    return href;
  return 'https://folderss-doc-asset/resolve?p=' + encodeURIComponent(href);
}

function escapeHtmlAttr(s) {
  return String(s).replace(/&/g,'&amp;').replace(/"/g,'&quot;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}

// ── Toolbar mode buttons ──────────────────
function resetEditorPaneSizeIfNotSplit(mode) {
  // split 모드의 드래그 리사이즈로 남은 고정폭을 해제해, split이 아닐 때는 화면 너비에 맞게 채운다
  if (mode === 'split')
    return;
  var editorPane = document.getElementById('mdv-editor-pane');
  editorPane.style.flex = '';
  editorPane.style.width = '';
}

function setMode(mode) {
  _mode = mode;
  document.body.className = _theme + ' mode-' + mode;
  document.getElementById('btnEdit').classList.toggle('active', mode === 'edit');
  document.getElementById('btnSplit').classList.toggle('active', mode === 'split');
  document.getElementById('btnPreview').classList.toggle('active', mode === 'preview');
  resetEditorPaneSizeIfNotSplit(mode);
  applyTocVisibility();
  rememberMode(mode);
  updateFindHighlights();
}

// TOC only in preview mode
function applyTocVisibility() {
  if (_mode !== 'preview') return;
  document.getElementById('mdv-toc-panel').classList.toggle('hidden', !_tocVisible);
  document.getElementById('mdv-toc-handle').style.display = _tocVisible ? '' : 'none';
}

function toggleToc() {
  _tocVisible = !_tocVisible;
  applyTocVisibility();
  var visible = _tocVisible;
  updatePrefs(function(p) { p.tocVisible = visible; });
}

// ── Export menu ───────────────────────────
function toggleExportMenu() {
  document.getElementById('exportMenu').classList.toggle('open');
}
document.addEventListener('click', function(e) {
  if (!e.target.closest('.tb-dropdown'))
    document.getElementById('exportMenu').classList.remove('open');
});

document.addEventListener('keydown', function(e) {
  // 편집기 밖(Preview 모드 등)에 포커스가 있어도 편집한 내용을 저장할 수 있게 문서 전체에서 받는다.
  if (e.ctrlKey && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 's') {
    e.preventDefault();
    window.chrome.webview.postMessage({ type: 'save-request', content: editor.value });
    return;
  }
  if (e.ctrlKey && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 'f') {
    e.preventDefault();
    openFindBar();
    return;
  }
  if (e.key === 'Escape' && document.getElementById('findBar').classList.contains('open')) {
    e.preventDefault();
    closeFindBar();
    return;
  }
  if (e.key === 'Enter' && document.activeElement === document.getElementById('findInput')) {
    e.preventDefault();
    if (e.shiftKey) findPrevious();
    else findNext();
  }
});

function exportHtml() {
  document.getElementById('exportMenu').classList.remove('open');
  var full = buildStandaloneHtml();
  window.chrome.webview.postMessage({ type: 'export-html', content: full });
}

function exportPdf() {
  document.getElementById('exportMenu').classList.remove('open');
  // PDF에서는 목차가 있어도 제외하고 본문만 내보낸다.
  createOperationSurface({ includeToc: false });
  // PrintToPdfAsync가 beforeprint를 발화시키면 아래 리스너가 surface를 목차 포함으로
  // 재빌드해버리므로, 미리 구성한 surface를 그대로 쓰도록 플래그로 보호한다.
  _printSurfaceReady = true;
  window.chrome.webview.postMessage({ type: 'export-pdf' });
}

function clearOperationSurface() {
  _printSurfaceReady = false;
  document.getElementById('mdv-operation-surface').innerHTML = '';
}

function createOperationSurface(options) {
  var includeToc = !options || options.includeToc !== false;
  var surface = document.getElementById('mdv-operation-surface');
  surface.innerHTML = '';

  if (includeToc && _mode === 'preview' && _tocVisible) {
    var toc = document.getElementById('mdv-toc-panel').cloneNode(true);
    toc.removeAttribute('id');
    toc.className = 'operation-toc';
    // 목차 클릭 이동은 JS 핸들러(data-target) 기반이라 내보낸 HTML에서는 동작하지
    // 않으므로, 본문 제목 id를 가리키는 실제 앵커(href="#h-N")로 바꿔준다.
    Array.from(toc.querySelectorAll('a[data-target]')).forEach(function(a) {
      a.setAttribute('href', '#' + a.dataset.target);
      a.classList.remove('active');
    });
    surface.appendChild(toc);
  }

  if (_mode === 'edit' || _mode === 'split') {
    var editorPane = document.createElement('section');
    editorPane.className = _mode === 'split' ? 'operation-pane' : 'operation-content';
    var pre = document.createElement('pre');
    pre.className = 'operation-editor';
    pre.textContent = editor.value;
    editorPane.appendChild(pre);
    surface.appendChild(editorPane);
  }

  if (_mode === 'preview' || _mode === 'split') {
    var previewPane = document.createElement('section');
    previewPane.className = (_mode === 'split' ? 'operation-pane ' : 'operation-content ') + 'operation-preview';
    previewPane.innerHTML = document.getElementById('mdv-preview-pane').innerHTML;
    surface.appendChild(previewPane);
  }
  return surface;
}

function operationStyles() {
  // 복제해온 페이지 CSS의 html,body{height:100%;overflow:hidden}을 덮어써
  // 내보낸 HTML 문서가 스크롤되도록 한다 (이 스타일이 뒤에 삽입되므로 우선함).
  // body에 overflow:auto를 주면 body가 별도 스크롤 컨테이너가 되어 목차의
  // position:sticky가 뷰포트에 걸리지 않으므로, 스크롤은 html에만 준다.
  return 'html,body{height:auto}html{overflow:auto}body{overflow:visible}'
    + 'html{scroll-behavior:smooth}'
    + '.operation-preview{overflow:visible}'
    + '.operation-preview::after{content:none;height:0}'
    + 'body{margin:24px;color:var(--fg,#24292f);background:var(--bg,#fff);font:14px/1.7 var(--font-sans,Segoe UI,Arial,sans-serif)}'
    + '.operation-layout{display:flex;align-items:flex-start;gap:0}'
    // 뷰어처럼 스크롤 중에도 목차가 화면에 고정되도록 sticky 사이드바로 렌더링
    + '.operation-toc{width:190px;flex:0 0 190px;align-self:flex-start;position:sticky;top:0;'
    + 'max-height:100vh;overflow-y:auto;padding:12px 16px 12px 0;border-right:1px solid var(--border,#ddd)}'
    + '.operation-toc a{display:block;color:var(--fg2,#555);text-decoration:none;padding:3px 0;'
    + 'font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}'
    + '.operation-toc a:hover{color:var(--accent,#0550ae)}'
    + '.operation-toc .toc-h2{padding-left:12px}.operation-toc .toc-h3{padding-left:24px}'
    + '.operation-toc .toc-h4{padding-left:36px}.operation-toc .toc-h5{padding-left:48px}'
    + '.operation-toc .toc-h6{padding-left:60px}'
    + '.operation-content,.operation-pane{flex:1;min-width:0;padding:0 18px}'
    + '.operation-pane+.operation-pane{border-left:1px solid var(--border,#ddd)}'
    + '.operation-editor{margin:0;white-space:pre-wrap;overflow-wrap:anywhere;font:13px/1.6 var(--font-mono,Consolas,monospace)}'
    + '.operation-preview img{max-width:100%}.operation-preview table{border-collapse:collapse;width:100%}'
    + '.operation-preview th,.operation-preview td{border:1px solid var(--border,#ddd);padding:6px 10px}'
    + '.operation-preview pre{white-space:pre-wrap;background:var(--panel,#f6f8fa);padding:12px}'
    + '@media print{pre,table,img,blockquote{break-inside:avoid}}';
}

function renderedContentStyles() {
  return Array.from(document.styleSheets)
    .map(function(sheet) {
      try {
        return Array.from(sheet.cssRules).map(function(rule) { return rule.cssText; }).join('\n');
      } catch (e) { return ''; }
    })
    .join('\n')
    .replace(/#mdv-preview-pane/g, '.operation-preview');
}

function buildStandaloneHtml() {
  var surface = createOperationSurface();
  var html = '<!DOCTYPE html><html lang="ko"><head><meta charset="utf-8"><title>'
    + escapeHtml(document.title) + '</title><style>' + renderedContentStyles() + '\n'
    + operationStyles() + '</style></head><body class="' + _theme + '">'
    + '<main class="operation-layout">' + surface.innerHTML + '</main></body></html>';
  clearOperationSurface();
  return html;
}

function printContent() {
  createOperationSurface();
  window.print();
}

async function shareContent() {
  var surface = createOperationSurface();
  var text = surface.innerText;
  clearOperationSurface();
  if (navigator.share) {
    try { await navigator.share({ title: document.title, text: text }); } catch (e) {}
    return;
  }
  if (navigator.clipboard)
    await navigator.clipboard.writeText(text);
}

window.addEventListener('beforeprint', function() {
  // exportPdf()가 목차 없는 surface를 이미 준비해둔 경우 덮어쓰지 않는다.
  if (_printSurfaceReady) return;
  createOperationSurface();
});
window.addEventListener('afterprint', clearOperationSurface);

// ── Rendering ─────────────────────────────
function renderPreview() {
  if (_largeFileMode) {
    var previewLarge = document.getElementById('mdv-preview-pane');
    previewLarge.innerHTML = '<p style="color:var(--fg2);padding:16px">대용량 Markdown 파일은 성능 보호를 위해 미리보기를 렌더링하지 않습니다.</p>';
    document.getElementById('mdv-toc-panel').innerHTML = '';
    return;
  }

  var raw = _content;
  var frontmatter = '';

  // strip YAML front matter
  if (raw.startsWith('---\n') || raw.startsWith('---\r\n')) {
    var end = raw.indexOf('\n---', 3);
    if (end > 0) {
      frontmatter = raw.substring(4, end);
      raw = raw.substring(end + 4).replace(/^\r?\n/, '');
    }
  }

  var html = '';
  if (frontmatter)
    html += '<div class="frontmatter-box">' + escapeHtml(frontmatter) + '</div>';

  html += sanitizeHtml(marked.parse(raw));

  var preview = document.getElementById('mdv-preview-pane');
  // Split에서 입력 중 다시 그리면 미리보기 높이가 바뀌며 scroll 이벤트가 나는데, 이것이 편집기를 끌어당기지 않게 편집기를 기준으로 고정한다.
  if (_mode === 'split') {
    _scrollDriver = editor;
    clearTimeout(_scrollDriverTimer);
    _scrollDriverTimer = setTimeout(function() { _scrollDriver = null; }, 150);
  }
  preview.innerHTML = html;
  clearFindHighlights();
  var renderId = ++_renderSeq;

  // syntax highlight code blocks
  var codeBlocks = Array.from(preview.querySelectorAll('pre code:not(.language-mermaid)'));
  if (codeBlocks.length) {
    withLib('hljs', renderId, function() {
      codeBlocks.forEach(function(el) { hljs.highlightElement(el); });
    });
  }

  // mermaid — 편집 중에는 300ms마다 다시 그리므로, 바뀌지 않은 다이어그램은 이전에 그린 SVG를 다시 쓴다 (깜빡임·CPU 감소).
  var mermaidBlocks = [];
  var mermaidKeys = [];
  preview.querySelectorAll('code.language-mermaid').forEach(function(el) {
    var wrap = document.createElement('div');
    wrap.className = 'mermaid-wrap';
    var inner = document.createElement('div');
    inner.className = 'mermaid';
    var key = mermaidTheme() + '\n' + el.textContent;
    var cached = _mermaidCache.get(key);
    if (cached) {
      inner.innerHTML = cached;
      inner.setAttribute('data-processed', 'true');
    } else {
      inner.textContent = el.textContent;
      mermaidBlocks.push(inner);
      mermaidKeys.push(key);
    }
    wrap.appendChild(inner);
    el.parentNode.parentNode.replaceChild(wrap, el.parentNode);
  });
  if (mermaidBlocks.length) {
    withLib('mermaid', renderId, function() {
      mermaid.run({ nodes: mermaidBlocks }).catch(function(){}).then(function() {
        mermaidBlocks.forEach(function(node, i) {
          if (!node.querySelector('svg')) return;
          if (_mermaidCache.size >= 100) _mermaidCache.clear();
          _mermaidCache.set(mermaidKeys[i], node.innerHTML);
        });
      });
    });
  }

  // KaTeX (수식 구간은 marked 확장이 data-tex로 떼어 둔다)
  if (preview.querySelector('[data-tex]'))
    withLib('katex', renderId, function() { renderMath(preview); });

  // intercept links
  preview.querySelectorAll('a[href]').forEach(function(a) {
    a.addEventListener('click', function(e) {
      var href = a.getAttribute('href') || '';
      e.preventDefault();
      if (href.charAt(0) === '#') {
        // marked가 한글 등을 퍼센트 인코딩하므로 풀어서 찾는다. 주소(hash)는 바꾸지 않고 미리보기만 스크롤한다.
        scrollToAnchor(href.substring(1));
        return;
      }
      window.chrome.webview.postMessage({ type: 'open-link', uri: a.href, href: href });
    });
  });

  buildToc();
  updateFindHighlights();
}

function escapeHtml(s) {
  return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}

// ── Find ──────────────────────────────────
var findInput = document.getElementById('findInput');
findInput.addEventListener('input', function() {
  searchMarkdown(findInput.value);
});

function openFindBar() {
  document.getElementById('findBar').classList.add('open');
  document.getElementById('btnFind').classList.add('active');
  findInput.focus();
  findInput.select();
  searchMarkdown(findInput.value);
}

function closeFindBar() {
  document.getElementById('findBar').classList.remove('open');
  document.getElementById('btnFind').classList.remove('active');
  findInput.value = '';
  clearSearch();
}

function clearSearch() {
  _findQuery = '';
  _findMatches = [];
  _findIndex = -1;
  clearFindHighlights();
  postFindResult();
}

function findNext() {
  if (!_findMatches.length) return;
  setActiveFindHit((_findIndex + 1) % _findMatches.length, true);
}

function findPrevious() {
  if (!_findMatches.length) return;
  setActiveFindHit((_findIndex + _findMatches.length - 1) % _findMatches.length, true);
}

function updateFindHighlights() {
  var query = _findQuery;
  clearFindHighlights();
  _findMatches = [];
  if (!query) {
    _findIndex = -1;
    postFindResult();
    return;
  }

  if (_mode === 'preview' || _mode === 'split') {
    highlightTextNodes(document.getElementById('mdv-preview-pane'), query);
    _findHits = Array.from(document.getElementById('mdv-preview-pane').querySelectorAll('mark.md-find-hit'));
  }

  // 원문과 렌더링 결과는 글자가 달라(**, 링크 URL 등) 일치 개수가 다르므로, 개수·이동은 한쪽만 기준으로 삼는다.
  // Preview는 렌더링된 하이라이트, Edit·Split은 편집기 원문 기준 (Split의 미리보기 하이라이트는 표시만 한다).
  _findMatches = _mode === 'preview' ? _findHits : findTextMatches(editor.value, query);
  if (!_findMatches.length) {
    _findIndex = -1;
    postFindResult();
    return;
  }

  _findIndex = 0;
  if (_mode === 'preview')
    activatePreviewHit(_findIndex, false);

  postFindResult();
}

function findTextMatches(text, query) {
  var matches = [];
  var lowerText = text.toLowerCase();
  var lowerQuery = query.toLowerCase();
  var pos = lowerText.indexOf(lowerQuery);
  while (pos >= 0) {
    matches.push({ start: pos, end: pos + query.length });
    pos = lowerText.indexOf(lowerQuery, pos + query.length);
  }
  return matches;
}

function selectEditorMatch(index) {
  var match = _findMatches[index];
  if (!match) return;
  editor.setSelectionRange(match.start, match.end);
  var before = editor.value.substring(0, match.start);
  var line = before.split(/\r\n|\r|\n/).length - 1;
  var lineHeight = parseFloat(getComputedStyle(editor).lineHeight) || 20;
  editor.scrollTop = Math.max(0, line * lineHeight - editor.clientHeight / 2);
}

function activatePreviewHit(index, focusEditor) {
  _findHits.forEach(function(hit) { hit.classList.remove('active'); });
  var hit = _findHits[index];
  if (!hit)
    return;

  hit.classList.add('active');
  hit.scrollIntoView({ block: 'center', inline: 'nearest' });
  if (focusEditor)
    editor.focus();
}

function clearFindHighlights() {
  document.querySelectorAll('mark.md-find-hit').forEach(function(mark) {
    var text = document.createTextNode(mark.textContent);
    mark.parentNode.replaceChild(text, mark);
    text.parentNode.normalize();
  });
  _findHits = [];
}

function highlightTextNodes(root, query) {
  var lowerQuery = query.toLowerCase();
  var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode: function(node) {
      if (!node.nodeValue || !node.nodeValue.toLowerCase().includes(lowerQuery))
        return NodeFilter.FILTER_REJECT;
      var parent = node.parentElement;
      // Mermaid 다이어그램(SVG)·KaTeX 수식 안에 <mark>를 끼우면 렌더링이 깨지므로 제외한다.
      if (!parent || parent.closest('script,style,textarea,mark,svg,.mermaid-wrap,.katex'))
        return NodeFilter.FILTER_REJECT;
      return NodeFilter.FILTER_ACCEPT;
    }
  });
  var nodes = [];
  while (walker.nextNode()) nodes.push(walker.currentNode);
  nodes.forEach(function(node) { wrapMatches(node, query); });
}

function wrapMatches(node, query) {
  var text = node.nodeValue;
  var lowerText = text.toLowerCase();
  var lowerQuery = query.toLowerCase();
  var frag = document.createDocumentFragment();
  var pos = 0;
  var idx = lowerText.indexOf(lowerQuery);
  while (idx >= 0) {
    if (idx > pos)
      frag.appendChild(document.createTextNode(text.substring(pos, idx)));
    var mark = document.createElement('mark');
    mark.className = 'md-find-hit';
    mark.textContent = text.substring(idx, idx + query.length);
    frag.appendChild(mark);
    pos = idx + query.length;
    idx = lowerText.indexOf(lowerQuery, pos);
  }
  if (pos < text.length)
    frag.appendChild(document.createTextNode(text.substring(pos)));
  node.parentNode.replaceChild(frag, node);
}

function setActiveFindHit(index, focusEditor) {
  _findIndex = index;
  if (_mode === 'preview')
    activatePreviewHit(_findIndex, false);
  else
    selectEditorMatch(_findIndex);
  if (focusEditor && _mode === 'edit')
    editor.focus();
  postFindResult();
}

function searchMarkdown(query) {
  _findQuery = query || '';
  updateFindHighlights();
}

function postFindResult() {
  document.getElementById('findCount').textContent =
    _findMatches.length > 0 && _findIndex >= 0
      ? (_findIndex + 1) + ' / ' + _findMatches.length
      : '0 / 0';
}

// ── TOC ───────────────────────────────────
function buildToc() {
  var preview = document.getElementById('mdv-preview-pane');
  var tocPanel = document.getElementById('mdv-toc-panel');
  var headings = Array.from(preview.querySelectorAll('h1,h2,h3,h4,h5,h6'));

  if (_tocObserver) {
    _tocObserver.disconnect();
    _tocObserver = null;
  }

  if (!headings.length) {
    tocPanel.innerHTML = '';
    return;
  }

  var frag = document.createDocumentFragment();
  var usedSlugs = {};
  headings.forEach(function(h) {
    // 문서 안 링크 [x](#설치-방법)가 동작하도록 GitHub와 같은 규칙으로 제목 id를 만든다 (본문 HTML에 직접 준 id는 유지).
    var id = h.id || uniqueSlug(slugify(h.textContent), usedSlugs);
    h.id = id;
    var a = document.createElement('a');
    a.className = 'toc-item toc-' + h.tagName.toLowerCase();
    a.textContent = h.textContent;
    a.href = '#';
    a.dataset.target = id;
    a.addEventListener('click', function(e) {
      e.preventDefault();
      scrollPreviewToHeading(id);
      setActiveTocItem(id);
    });
    frag.appendChild(a);
  });
  tocPanel.innerHTML = '';
  tocPanel.appendChild(frag);

  // IntersectionObserver for active highlight
  _tocObserver = new IntersectionObserver(function(entries) {
    entries.forEach(function(entry) {
      if (!entry.isIntersecting) return;
      setActiveTocItem(entry.target.id);
    });
  }, { root: preview, rootMargin: '-10% 0px -80% 0px', threshold: 0 });

  headings.forEach(function(h) { _tocObserver.observe(h); });
}

// GitHub 제목 앵커 규칙: 소문자로, 글자·숫자·공백·-·_ 외 문자는 지우고, 공백은 하나씩 -로 바꾼다.
function slugify(text) {
  return text.trim().toLowerCase().replace(/[^\p{L}\p{M}\p{N}\s_-]/gu, '').replace(/\s/g, '-');
}

// 같은 제목이 여러 번 나오면 GitHub처럼 -1, -2를 붙인다.
function uniqueSlug(slug, used) {
  var id = slug || 'section';
  var n = 0;
  while (used[id] || (document.getElementById(id) && !document.getElementById('mdv-preview-pane').contains(document.getElementById(id))))
    id = (slug || 'section') + '-' + (++n);
  used[id] = true;
  return id;
}

// 링크의 #조각(퍼센트 인코딩일 수 있음)으로 미리보기의 제목·요소로 이동한다.
function scrollToAnchor(fragment) {
  var target;
  try { target = decodeURIComponent(fragment); } catch (err) { target = fragment; }
  if (scrollPreviewToHeading(target)) setActiveTocItem(target);
}

function scrollPreviewToHeading(id) {
  var preview = document.getElementById('mdv-preview-pane');
  var heading = document.getElementById(id);
  if (!preview || !heading || !preview.contains(heading)) return false;

  // offsetTop은 중간에 위치 지정된 요소가 있으면 기준이 달라지므로 화면 좌표 차이로 계산한다.
  var targetTop = preview.scrollTop + heading.getBoundingClientRect().top - preview.getBoundingClientRect().top - 12;
  preview.scrollTo({ top: Math.max(0, targetTop), behavior: 'smooth' });
  return true;
}

function setActiveTocItem(id) {
  var tocPanel = document.getElementById('mdv-toc-panel');
  tocPanel.querySelectorAll('.toc-item.active').forEach(function(el){ el.classList.remove('active'); });
  var link = tocPanel.querySelector('[data-target="' + id + '"]');
  if (link) link.classList.add('active');
}

// ── Editor ────────────────────────────────
var editor = document.getElementById('mdv-editor-textarea');
var editorGutter = document.getElementById('mdv-editor-gutter');
var _gutterLineCount = 0;

// 키 입력마다 불리므로 split('\n')처럼 줄마다 문자열을 만들지 않고 개행만 센다 (수 MB 문서에서 입력 지연 방지).
function countLines(text) {
  var count = 1;
  var pos = text.indexOf('\n');
  while (pos >= 0) {
    count++;
    pos = text.indexOf('\n', pos + 1);
  }
  return count;
}

function updateLineNumbers() {
  var lineCount = countLines(editor.value);
  if (lineCount !== _gutterLineCount) {
    var lines = new Array(lineCount);
    for (var i = 0; i < lineCount; i++) lines[i] = i + 1;
    editorGutter.textContent = lines.join('\n');
    editorGutter.style.width = (String(lineCount).length + 2) + 'ch';
    _gutterLineCount = lineCount;
  }
  editorGutter.scrollTop = editor.scrollTop;
}

editor.addEventListener('input', function() {
  _content = editor.value;
  if (!_modified) {
    _modified = true;
    window.chrome.webview.postMessage({ type: 'modified', value: true });
  }
  updateLineNumbers();
  clearTimeout(_renderTimer);
  if (!_largeFileMode)
    _renderTimer = setTimeout(renderPreview, 300);
});

editor.addEventListener('scroll', function() {
  editorGutter.scrollTop = editor.scrollTop;
  syncSplitScroll(editor, document.getElementById('mdv-preview-pane'));
});

document.getElementById('mdv-preview-pane').addEventListener('scroll', function() {
  syncSplitScroll(document.getElementById('mdv-preview-pane'), editor);
});

// ── Split scroll sync ─────────────────────
// Split 모드에서 한쪽을 스크롤하면 다른 쪽을 같은 비율 위치로 맞춘다. 맞추면서 생기는 상대편 scroll 이벤트가
// 되돌아와 서로 끌어당기지 않도록, 잠시 동안은 먼저 움직인 쪽만 기준으로 삼는다.
var _scrollDriver = null;
var _scrollDriverTimer = null;

function syncSplitScroll(from, to) {
  if (_mode !== 'split') return;
  if (_scrollDriver && _scrollDriver !== from) return;
  _scrollDriver = from;
  clearTimeout(_scrollDriverTimer);
  _scrollDriverTimer = setTimeout(function() { _scrollDriver = null; }, 150);

  var fromMax = from.scrollHeight - from.clientHeight;
  var ratio = fromMax > 0 ? from.scrollTop / fromMax : 0;
  to.scrollTop = ratio * Math.max(0, to.scrollHeight - to.clientHeight);
}

// ── Editing helpers ───────────────────────
// 모든 편집은 선택 영역을 잡고 insertText로 바꿔 실행 취소(Ctrl+Z) 기록과 input 이벤트(수정 표시·미리보기)를 유지한다.
var INDENT = '    ';

function replaceRange(start, end, text, selStart, selEnd) {
  editor.setSelectionRange(start, end);
  document.execCommand('insertText', false, text);
  if (selStart !== undefined)
    editor.setSelectionRange(selStart, selEnd === undefined ? selStart : selEnd);
}

function lineStartOf(value, pos) {
  return value.lastIndexOf('\n', pos - 1) + 1;
}

// 선택이 여러 줄이면 각 줄 앞에 들여쓰기를 넣거나(Tab) 최대 한 단계 뺀다(Shift+Tab). 한 줄 안이면 Tab은 공백 4칸.
function indentSelection(outdent) {
  var value = editor.value;
  var selStart = editor.selectionStart, selEnd = editor.selectionEnd;
  var multiLine = value.substring(selStart, selEnd).indexOf('\n') >= 0;
  if (!outdent && !multiLine) {
    document.execCommand('insertText', false, INDENT);
    return;
  }

  var blockStart = lineStartOf(value, selStart);
  // 선택 끝이 줄 맨 앞이면 그 줄은 선택한 것으로 보지 않는다.
  var lastLineEnd = selEnd > selStart && value.charAt(selEnd - 1) === '\n' ? selEnd - 1 : selEnd;
  var blockEnd = value.indexOf('\n', lastLineEnd);
  if (blockEnd < 0) blockEnd = value.length;

  var lines = value.substring(blockStart, blockEnd).split('\n');
  var firstDelta = 0, totalDelta = 0;
  var changed = lines.map(function(line, i) {
    var next;
    if (outdent) {
      var m = /^(\t| {1,4})/.exec(line);
      next = m ? line.substring(m[0].length) : line;
    } else {
      next = line.length ? INDENT + line : line;
    }
    var delta = next.length - line.length;
    if (i === 0) firstDelta = delta;
    totalDelta += delta;
    return next;
  });
  var text = changed.join('\n');
  if (text === value.substring(blockStart, blockEnd)) return;

  if (multiLine)
    replaceRange(blockStart, blockEnd, text, blockStart, blockStart + text.length);
  else
    replaceRange(blockStart, blockEnd, text,
      Math.max(blockStart, selStart + firstDelta), Math.max(blockStart, selEnd + totalDelta));
}

// 목록 줄 끝에서 Enter를 누르면 다음 줄에 같은 표시(-, *, +, 1., - [ ])를 이어 붙인다.
// 표시만 있는 빈 항목에서 Enter를 누르면 표시를 지워 목록을 끝낸다.
var LIST_ITEM = /^(\s*)(?:([-*+])|(\d+)([.)]))(\s+)(\[[ xX]\]\s+)?/;

function continueList() {
  var value = editor.value;
  var pos = editor.selectionStart;
  if (pos !== editor.selectionEnd) return false;

  var lineStart = lineStartOf(value, pos);
  var line = value.substring(lineStart, pos);
  var m = LIST_ITEM.exec(line);
  if (!m) return false;
  // 줄 끝이 아니면(문장 중간에서 줄을 나누는 경우) 기본 동작을 따른다.
  var lineEnd = value.indexOf('\n', pos);
  if (value.substring(pos, lineEnd < 0 ? value.length : lineEnd).trim()) return false;

  if (line.length === m[0].length) {
    replaceRange(lineStart, pos, m[1]);
    return true;
  }

  var marker = m[2] ? m[2] : (parseInt(m[3], 10) + 1) + m[4];
  var task = m[6] ? '[ ] ' : '';
  document.execCommand('insertText', false, '\n' + m[1] + marker + m[5] + task);
  return true;
}

editor.addEventListener('keydown', function(e) {
  // 한글 등 IME 조합 중인 키는 조합을 끝내는 용도이므로 건드리지 않는다.
  if (e.isComposing || e.keyCode === 229) return;

  if (e.key === 'Tab' && !e.ctrlKey && !e.altKey) {
    e.preventDefault();
    indentSelection(e.shiftKey);
    return;
  }
  if (e.key === 'Enter' && !e.ctrlKey && !e.altKey && !e.shiftKey && !e.metaKey) {
    if (continueList()) e.preventDefault();
  }
});

// ── Image paste ───────────────────────────
// 클립보드에 이미지만 있으면 C#이 문서 옆 images 폴더에 파일로 저장하고 app.insertText로 이미지 링크를 넣는다.
editor.addEventListener('paste', function(e) {
  var data = e.clipboardData;
  if (!data || data.getData('text/plain')) return;
  var file = Array.from(data.files || []).find(function(f) { return /^image\/(png|jpeg|gif|webp|bmp)$/.test(f.type); });
  if (!file) return;

  e.preventDefault();
  var reader = new FileReader();
  reader.onload = function() {
    var result = String(reader.result);
    var base64 = result.substring(result.indexOf(',') + 1);
    window.chrome.webview.postMessage({ type: 'paste-image', mime: file.type, data: base64 });
  };
  reader.readAsDataURL(file);
});

// ── Resize handles ────────────────────────
function makeResizeHandle(handleEl, getLeftEl, setSize, onEnd) {
  handleEl.addEventListener('mousedown', function(e) {
    e.preventDefault();
    handleEl.classList.add('dragging');
    var startX = e.clientX;
    var startSize = getLeftEl().getBoundingClientRect().width;

    function onMove(ev) { setSize(startSize + ev.clientX - startX); }
    function onUp()   {
      handleEl.classList.remove('dragging');
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
      if (onEnd) onEnd();
    }
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  });
}

makeResizeHandle(
  document.getElementById('mdv-toc-handle'),
  function() { return document.getElementById('mdv-toc-panel'); },
  function(w) {
    var clamped = Math.max(80, Math.min(400, w));
    document.getElementById('mdv-toc-panel').style.width = clamped + 'px';
  },
  function() {
    var width = parseInt(document.getElementById('mdv-toc-panel').style.width, 10);
    if (width) updatePrefs(function(p) { p.tocWidth = width; });
  }
);

(function applySavedLayout() {
  var p = loadPrefs();
  if (p.tocVisible === false) {
    _tocVisible = false;
    applyTocVisibility();
  }
  if (typeof p.tocWidth === 'number')
    document.getElementById('mdv-toc-panel').style.width = Math.max(80, Math.min(400, p.tocWidth)) + 'px';
})();

makeResizeHandle(
  document.getElementById('mdv-split-handle'),
  function() { return document.getElementById('mdv-editor-pane'); },
  function(w) {
    var ca = document.getElementById('mdv-content-area');
    var total = ca.getBoundingClientRect().width;
    var clamped = Math.max(100, Math.min(total - 100, w));
    document.getElementById('mdv-editor-pane').style.flex = 'none';
    document.getElementById('mdv-editor-pane').style.width = clamped + 'px';
  }
);

// ── Public API (called from C# via ExecuteScriptAsync) ────
var app = {
  printContent: printContent,
  saveContentAs: exportHtml,
  shareContent: shareContent,
  clearOperationSurface: clearOperationSurface,
  open: function(content, theme, mode, largeFileMode, filePath) {
    _content = content;
    applyTheme(theme);
    _filePath = filePath || null;
    _largeFileMode = largeFileMode === true;
    // 대용량 파일은 C#이 정한 모드(Edit)를 따르고, 아니면 이 파일에서 마지막으로 쓴 모드를 복원한다.
    _mode    = (!_largeFileMode && rememberedMode(_filePath)) || mode || 'preview';
    _modified = false;
    document.body.className = _theme + ' mode-' + _mode;
    resetEditorPaneSizeIfNotSplit(_mode);
    editor.value = content;
    updateLineNumbers();
    // mermaid theme — 그리기 전에 맞춰야 첫 화면부터 테마 색으로 그려진다 (아직 안 불러왔으면 불러올 때 맞춘다)
    configureMermaid();
    renderPreview();
    // restore mode buttons
    document.getElementById('btnEdit').classList.toggle('active', _mode === 'edit');
    document.getElementById('btnSplit').classList.toggle('active', _mode === 'split');
    document.getElementById('btnPreview').classList.toggle('active', _mode === 'preview');
    applyTocVisibility();
  },

  setTheme: function(theme) {
    applyTheme(theme);
    document.body.className = _theme + ' mode-' + _mode;
    configureMermaid();
    // Mermaid 색을 바꾸려면 다시 렌더링해야 하지만, 읽던 위치는 유지한다.
    var previewPane = document.getElementById('mdv-preview-pane');
    var previewScrollTop = previewPane.scrollTop;
    renderPreview();
    previewPane.scrollTop = previewScrollTop;
  },

  // 붙여넣은 이미지를 저장한 뒤 C#이 이미지 링크를 커서 위치에 넣는다.
  insertText: function(text) {
    editor.focus();
    if (document.activeElement === editor) {
      document.execCommand('insertText', false, text);
      return;
    }
    // 저장하는 사이 Preview로 바꿔 편집기가 숨겨졌으면 포커스를 못 받으므로 값에 직접 넣는다 (이 경우 실행 취소 기록에는 남지 않음).
    editor.setRangeText(text, editor.selectionStart, editor.selectionEnd, 'end');
    editor.dispatchEvent(new Event('input'));
  },

  // 다른 문서의 "파일#제목" 링크로 열렸을 때 C#이 호출한다.
  scrollToAnchor: scrollToAnchor,

  openFind: openFindBar,
  search: searchMarkdown,
  findNext: findNext,
  findPrevious: findPrevious,
  clearSearch: clearSearch,

  markSaved: function() {
    _modified = false;
    window.chrome.webview.postMessage({ type: 'modified', value: false });
  },

  reloadContent: function(content, largeFileMode) {
    var previewPane = document.getElementById('mdv-preview-pane');
    var previewScrollTop = previewPane.scrollTop;
    var editorScrollTop = editor.scrollTop;

    _content = content;
    _largeFileMode = largeFileMode === true;
    _modified = false;
    editor.value = content;
    updateLineNumbers();
    renderPreview();
    window.chrome.webview.postMessage({ type: 'modified', value: false });

    previewPane.scrollTop = previewScrollTop;
    editor.scrollTop = editorScrollTop;
    editorGutter.scrollTop = editorScrollTop;
  }
};

// ── Toolbar wiring ────────────────────────
// CSP가 인라인 스크립트(onclick 속성 포함)를 막으므로 버튼 동작은 여기서 연결한다.
[
  ['btnEdit',       function() { setMode('edit'); }],
  ['btnSplit',      function() { setMode('split'); }],
  ['btnPreview',    function() { setMode('preview'); }],
  ['btnFind',       openFindBar],
  ['btnToc',        toggleToc],
  ['btnExport',     toggleExportMenu],
  ['btnExportHtml', exportHtml],
  ['btnExportPdf',  exportPdf],
  ['findPrev',      findPrevious],
  ['findNext',      findNext],
  ['findClose',     closeFindBar]
].forEach(function(entry) {
  document.getElementById(entry[0]).addEventListener('click', function() { entry[1](); });
});
