import json, re, sys, math, html
src, out = sys.argv[1], sys.argv[2]
md = open(src, encoding='utf-8').read()

def table(title):
    sec = md.split('## ' + title, 1)[1].split('\n## ', 1)[0]
    rows = [l for l in sec.splitlines() if l.startswith('|')]
    head = [c.strip() for c in rows[0].strip('|').split('|')]
    data = []
    for l in rows[2:]:
        cells = [c.strip() for c in l.strip('|').split('|')]
        data.append(cells)
    return head, data

head, times = table('시간 (ms, 중앙값)')
engines = head[2:]
_, changed = table('변경 줄 수 (결과 품질)')
_, mem = table('관리 힙 할당 (MB, 1회)')
_, summary = table('요약')
env = re.search(r'- 환경: (.*)', md).group(1)
when = re.search(r'- 측정 시각: (.*)', md).group(1)

cases = []
for t, c, m in zip(times, changed, mem):
    name, desc = t[1].split('<br>')
    row = {'no': int(t[0]), 'name': name, 'desc': desc, 'cells': []}
    for i, e in enumerate(engines):
        tv = t[2 + i]
        ms = None if tv == '지원 안 함' else float(tv)
        ch = c[2 + i]
        mm = m[2 + i]
        row['cells'].append({'ms': ms, 'changed': ch.replace(' ✓', ''), 'verified': '✓' in ch,
                             'mb': None if mm == '—' else float(mm)})
    cases.append(row)

summ = []
for s in summary:
    summ.append({'name': s[0], 'kind': s[1], 'total': s[2], 'geo': float(s[3].replace('배', '')),
                 'wins': int(s[4]), 'unsupported': int(s[5])})

groups = {  # 경우 성격 — 해석용 묶음
    'typical': [3, 4, 5, 6, 7, 8, 9, 14, 16, 17, 19, 20],
    'worst': [11, 12, 13, 18],
    'other': [1, 2, 10, 15],
}
group_of = {n: g for g, ns in groups.items() for n in ns}
for r in cases:
    r['group'] = group_of[r['no']]

data = {'engines': engines, 'cases': cases, 'summary': summ, 'env': env, 'when': when}
tpl = open(sys.argv[3], encoding='utf-8').read()
open(out, 'w', encoding='utf-8').write(tpl.replace('/*__DATA__*/null', json.dumps(data, ensure_ascii=False)))
print('ok', len(cases), engines)
