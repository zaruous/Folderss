// JSON Lines(.jsonl) 언어 — Monaco의 json 언어는 파일 전체를 JSON 하나로 보므로 줄마다 JSON인 파일에 쓰면
// 둘째 줄부터 오류가 찍히고 Format Document가 레코드를 여러 줄로 펼쳐 파일을 깨뜨린다. 그래서 따로 둔다.
//
// - 구문 강조: 작은 Monarch 토크나이저(키·값 문자열, 숫자, true/false/null, 괄호).
// - Format Document(Shift+Alt+F, 커맨드 팔레트): 레코드마다 문자열 밖 공백만 지워 한 줄로(JSONL 본래 모양).
// - 명령 "JSONL: 레코드 펼쳐 보기": 레코드마다 들여쓰기해 여러 줄로. 저장하면 JSONL이 아니므로 이름에 적어 둔다.
//   펼쳐진 레코드는 줄을 모아 다시 파싱되는 지점까지 한 레코드로 보므로 압축으로 되돌릴 수 있다.
// - 값은 JSON.parse로 "유효한지"만 보고, 글자는 원문을 그대로 옮긴다 — 큰 정수 자릿수와 키 순서가 바뀌지 않는다.
//   파싱이 안 되는 줄은 손대지 않는다.
//
// 브라우저(monaco-app.html)와 node(테스트) 양쪽에서 읽힌다.
(function (root) {
  'use strict';

  function isValidJson(text) {
    try { JSON.parse(text); return true; } catch (e) { return false; }
  }

  /** 문자열 밖의 공백을 모두 지운다(JSON 문법상 공백은 문자열 밖에서만 의미 없음). */
  function stripWhitespace(text) {
    var out = '', inString = false, escaped = false;
    for (var i = 0; i < text.length; i++) {
      var c = text[i];
      if (inString) {
        out += c;
        if (escaped) escaped = false;
        else if (c === '\\') escaped = true;
        else if (c === '"') inString = false;
      } else if (c === '"') {
        inString = true;
        out += c;
      } else if (c !== ' ' && c !== '\t' && c !== '\r' && c !== '\n') {
        out += c;
      }
    }
    return out;
  }

  /** 문자열 밖의 여는 괄호 수 − 닫는 괄호 수. 0보다 크면 아직 닫히지 않은 컨테이너가 있다(펼쳐진 레코드의 중간). */
  function bracketDepth(text) {
    var depth = 0, inString = false, escaped = false;
    for (var i = 0; i < text.length; i++) {
      var c = text[i];
      if (inString) {
        if (escaped) escaped = false;
        else if (c === '\\') escaped = true;
        else if (c === '"') inString = false;
      } else if (c === '"') inString = true;
      else if (c === '{' || c === '[') depth++;
      else if (c === '}' || c === ']') depth--;
    }
    return depth;
  }

  /** 공백 없는 JSON 글자를 들여쓰기한다(값·키 순서는 그대로). */
  function prettify(compact, indent, eol) {
    var pad = function (depth) { var s = ''; for (var k = 0; k < depth; k++) s += indent; return s; };
    var out = '', depth = 0, inString = false, escaped = false;
    for (var i = 0; i < compact.length; i++) {
      var c = compact[i];
      if (inString) {
        out += c;
        if (escaped) escaped = false;
        else if (c === '\\') escaped = true;
        else if (c === '"') inString = false;
        continue;
      }
      switch (c) {
        case '"': inString = true; out += c; break;
        case '{': case '[':
          var next = compact[i + 1];
          if (next === '}' || next === ']') { out += c + next; i++; break; } // 빈 {} · []
          depth++;
          out += c + eol + pad(depth);
          break;
        case '}': case ']':
          depth--;
          out += eol + pad(depth) + c;
          break;
        case ',': out += ',' + eol + pad(depth); break;
        case ':': out += ': '; break;
        default: out += c;
      }
    }
    return out;
  }

  /**
   * 글을 레코드 목록으로 자른다. 항목은 { json: '공백 없는 JSON' } 또는 { raw: '원문 그대로' }.
   * 보통 한 줄이 레코드 하나. 한 줄로 파싱이 안 되면 다음 줄을 이어 붙여 파싱되는 지점까지 한 레코드로 본다(펼쳐진 레코드).
   * 모은 줄에 열린 괄호가 없는데도(깊이 0) 파싱이 안 되면 잘못된 줄이다: 다음에 혼자서 파싱되는 줄이 오면 모은 줄은 원문 그대로 두고 새로 시작한다.
   * 열린 괄호가 남은 채 끝까지 파싱이 안 되면(예: `{ oops` 뒤) 그 뒤는 모두 원문 그대로 남는다.
   */
  function splitRecords(text) {
    var lines = text.split(/\r\n|\n/);
    var records = [], buffer = [];
    var flushRaw = function () {
      if (buffer.length) { records.push({ raw: buffer.join('\n') }); buffer = []; }
    };
    for (var i = 0; i < lines.length; i++) {
      var line = lines[i];
      if (buffer.length === 0 && line.trim() === '') { records.push({ raw: line }); continue; }
      buffer.push(line);
      var joined = buffer.join('\n');
      if (isValidJson(joined)) { records.push({ json: stripWhitespace(joined) }); buffer = []; continue; }
      if (buffer.length > 1 && bracketDepth(buffer.slice(0, -1).join('\n')) <= 0 && isValidJson(line)) {
        buffer.pop();
        flushRaw();
        records.push({ json: stripWhitespace(line) });
      }
    }
    flushRaw();
    return records;
  }

  /** 레코드마다 한 줄로. 바뀐 것이 없으면 원문과 같은 글을 돌려준다. */
  function compactJsonl(text, eol) {
    return splitRecords(text).map(function (r) { return 'json' in r ? r.json : r.raw; }).join(eol);
  }

  /** 레코드마다 들여쓰기해 여러 줄로. */
  function expandJsonl(text, eol, indent) {
    indent = indent || '  ';
    return splitRecords(text).map(function (r) { return 'json' in r ? prettify(r.json, indent, eol) : r.raw; }).join(eol);
  }

  /** Monaco에 jsonl 언어·포매터를 등록한다. 모델을 만들기 전에 한 번 부른다. */
  function registerJsonl(monaco) {
    monaco.languages.register({ id: 'jsonl', extensions: ['.jsonl', '.ndjson'], aliases: ['JSON Lines', 'jsonl'] });
    monaco.languages.setLanguageConfiguration('jsonl', {
      brackets: [['{', '}'], ['[', ']']],
      autoClosingPairs: [{ open: '{', close: '}' }, { open: '[', close: ']' }, { open: '"', close: '"', notIn: ['string'] }],
      surroundingPairs: [{ open: '{', close: '}' }, { open: '[', close: ']' }, { open: '"', close: '"' }]
    });
    monaco.languages.setMonarchTokensProvider('jsonl', {
      tokenizer: {
        root: [
          [/"(?:[^"\\]|\\.)*"(?=\s*:)/, 'string.key.json'],
          [/"(?:[^"\\]|\\.)*"/, 'string.value.json'],
          [/-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?/, 'number'],
          [/\b(?:true|false|null)\b/, 'keyword'],
          [/[{}\[\]]/, '@brackets'],
          [/[,:]/, 'delimiter'],
          [/\s+/, 'white'],
          [/./, 'invalid']
        ]
      }
    });
    monaco.languages.registerDocumentFormattingEditProvider('jsonl', {
      provideDocumentFormattingEdits: function (model) {
        var text = model.getValue();
        var out = compactJsonl(text, model.getEOL());
        return out === text ? [] : [{ range: model.getFullModelRange(), text: out }];
      }
    });
  }

  /** 편집기에 JSONL 명령(커맨드 팔레트·오른쪽 메뉴)을 붙인다. jsonl 언어일 때만 보인다. */
  function addJsonlActions(editor) {
    var replaceAll = function (ed, convert) {
      var m = ed.getModel();
      if (!m) return;
      var text = m.getValue();
      var out = convert(text, m.getEOL());
      if (out !== text) ed.executeEdits('jsonl', [{ range: m.getFullModelRange(), text: out }]);
    };
    editor.addAction({
      id: 'folderss.jsonl.expand',
      label: 'JSONL: 레코드 펼쳐 보기 (저장하면 JSONL 형식이 아님)',
      precondition: 'editorLangId == jsonl',
      contextMenuGroupId: 'modification',
      contextMenuOrder: 1.5,
      run: function (ed) { replaceAll(ed, function (t, eol) { return expandJsonl(t, eol, '  '); }); }
    });
    editor.addAction({
      id: 'folderss.jsonl.compact',
      label: 'JSONL: 레코드 한 줄로 압축 (= Format Document)',
      precondition: 'editorLangId == jsonl',
      contextMenuGroupId: 'modification',
      contextMenuOrder: 1.6,
      run: function (ed) { replaceAll(ed, compactJsonl); }
    });
  }

  var api = {
    splitRecords: splitRecords,
    compactJsonl: compactJsonl,
    expandJsonl: expandJsonl,
    registerJsonl: registerJsonl,
    addJsonlActions: addJsonlActions
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.jsonlTools = api;
})(typeof window !== 'undefined' ? window : this);
