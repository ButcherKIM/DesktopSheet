# 저장소 안에서 그대로 실행한다: python3 tools/check_spec.py
# SPEC.md 무결성 검사. 문서에 적힌 수치와 예시를 규칙대로 다시 계산해 맞는지 본다.
import io, re, sys, unicodedata
from decimal import Decimal, ROUND_HALF_UP, getcontext
import os
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
getcontext().prec = 400
S = io.open(os.path.join(HERE, '..', 'SPEC.md'), encoding='utf-8').read()
found = []
def bad(kind, msg): found.append((kind, msg))

# --- 1. 장 번호가 빠짐없이 이어지는가 ---
chaps = [int(m) for m in re.findall(r'^## (\d+)\.', S, re.M)]
if chaps != list(range(0, max(chaps)+1)): bad("구조", f"장 번호가 이어지지 않음: {chaps}")
subs = re.findall(r'^### (\d+)\.(\d+)', S, re.M)
for c in sorted({int(a) for a,_ in subs}):
    got = [int(b) for a,b in subs if int(a)==c]
    if got != list(range(1, len(got)+1)): bad("구조", f"{c}장 절 번호가 이어지지 않음: {got}")

# --- 2. 교차 참조가 실재하는 장·절을 가리키는가 ---
for ref in set(re.findall(r'(\d+)장', S)):
    if int(ref) not in chaps: bad("참조", f"{ref}장을 가리키는데 그런 장이 없음")
# 절 참조는 "8.3에서" 처럼 한 자리 뒤에 조사나 괄호가 붙은 것만 센다.
# 대비 11.54:1 이나 16.7ms 를 절 번호로 잘못 읽지 않게 한다.
for a, b in set(re.findall(r'(?<!\d)(\d{1,2})\.(\d)(?!\d)\s*(?=에|을|의|은|이|과|와|\))', S)):
    if (a, b) not in [(x,y) for x,y in subs]:
        bad("참조", f"{a}.{b} 을 가리키는데 그런 절이 없음")

# --- 2b. 구분선: 모든 장 앞에 --- 가 하나씩 있는가 ---
lines = S.split('\n')
for i, ln in enumerate(lines):
    if ln.startswith('## ') and i >= 2:
        if lines[i-2].strip() != '---': bad("구조", f"{ln[:20]} 앞에 구분선이 없음")
        if i >= 4 and lines[i-4].strip() == '---': bad("구조", f"{ln[:20]} 앞에 구분선이 겹침")

# --- 2c. 낡은 단축키 표기 ---
if 'Ctrl+1~0' in S: bad("단축키", "Ctrl+1~0 표기가 남음 — 15.1 은 Ctrl+Shift / Alt+Shift 다")

# --- 3. 미정 잔재가 남아 있는가 ---
for pat, why in [(r'A-\d에서 (정합니다|확정)', "확정된 항목을 아직 미정인 것처럼 가리킴"),
                 (r'확인을 받지 못했', "확인 대기 문구가 남음"),
                 (r'\| 미정 \|', "미정 표시가 남음"),
                 (r'확인 대기', "확인 대기 문구가 남음")]:
    for m in re.finditer(pat, S):
        line = S[:m.start()].count('\n') + 1
        bad("잔재", f"{line}행: {why} — {m.group(0)}")

# --- 4. 치수 계산 재검산 ---
CW, RH, HDR, COLH, TAB = 13*8, 20, 3*8+8*2, 20, 24
dims = {"셀 폭은 13 x 8 = 104px": CW == 104,
        "창 가로 520 + 40 = 560": 5*CW + HDR == 560,
        "창 세로 400 + 20 + 24 = 444": 20*RH + COLH + TAB == 444,
        "최소 3열 5행 352 x 144": (3*CW+HDR, 5*RH+COLH+TAB) == (352, 144),
        "150% 배율 156 / 840 x 666": (round(CW*1.5), round(560*1.5), round(444*1.5)) == (156, 840, 666),
        "시트 한 장 5,200칸 / 다섯 장 26,000칸": (200*26, 200*26*5) == (5200, 26000),
        "스크롤바를 넣으면 560 -> 568": 560+8 == 568,
        "12pt = 16px": round(12*96/72) == 16}
for name, ok in dims.items():
    if not ok: bad("수치", f"{name} 이 맞지 않음")

# --- 5. 11장 검증표를 규칙 구현으로 재생성 ---
BUDGET = 10
def sci(x):
    sign = "-" if x < 0 else ""; a = abs(x); e = a.adjusted()
    ed = max(2, len(str(abs(e)))); p = 6 - ed
    m = (a / Decimal(10)**e).quantize(Decimal(1).scaleb(-p), rounding=ROUND_HALF_UP)
    if m >= 10:
        m /= 10; e += 1; ed = max(2, len(str(abs(e)))); p = 6 - ed
        m = m.quantize(Decimal(1).scaleb(-p), rounding=ROUND_HALF_UP)
    return f"{sign}{m:.{p}f}E{'+' if e>=0 else '-'}{abs(e):0{ed}d}"
def general(x):
    if x == 0: return "0"
    sign = "-" if x < 0 else ""; a = abs(x)
    if a >= 1:
        if a.adjusted()+1 > BUDGET: return sci(x)
        d = a.quantize(Decimal(1), rounding=ROUND_HALF_UP).adjusted()+1
        if d > BUDGET: return sci(x)
        p = BUDGET-d-1 if d < BUDGET else 0
        v = a.quantize(Decimal(1).scaleb(-p), rounding=ROUND_HALF_UP)
        if v.adjusted()+1 > d: return general(Decimal(sign+str(v)))
        s = f"{v:.{p}f}".rstrip("0").rstrip(".") if p else f"{v:.0f}"
        return sign + s
    p = BUDGET-2
    v = a.quantize(Decimal(1).scaleb(-p), rounding=ROUND_HALF_UP)
    if v == 0: return sci(x)
    return sign + f"{v:.{p}f}".rstrip("0")
for src, want in [("1234.5","1234.5"),("1234.5678901","1234.56789"),("99999999","99999999"),
                  ("100000000","100000000"),("9999999999","9999999999"),("10000000000","1.0000E+10"),
                  ("9999999999.6","1.0000E+10"),("123456789012","1.2346E+11"),("1E+100","1.000E+100"),
                  ("-9999999999","-9999999999"),("0.000001","0.000001"),("0.00000001","0.00000001"),
                  ("0.000000001","1.0000E-09")]:
    got = general(Decimal(src))
    if got != want: bad("표시 규칙", f"{src} -> 규칙은 {got}, 문서는 {want}")
    if want not in S: bad("표시 규칙", f"11장 표에 {want} 가 없음")
    if len(want) > 11: bad("표시 규칙", f"{want} 가 11자를 넘음")

# --- 6. 8.1 우선순위 예시 ---
def ev(s):
    t = re.findall(r'\d+\.?\d*|[-+*/^%()]', s.replace(' ',''))
    i = [0]
    def expr():
        v = term()
        while i[0] < len(t) and t[i[0]] in '+-':
            op = t[i[0]]; i[0]+=1; v = v+term() if op=='+' else v-term()
        return v
    def term():
        v = power()
        while i[0] < len(t) and t[i[0]] in '*/':
            op = t[i[0]]; i[0]+=1; v = v*power() if op=='*' else v/power()
        return v
    def power():
        v = unary()
        while i[0] < len(t) and t[i[0]] == '^':
            i[0]+=1; v = v**unary()
        return v
    def unary():
        if i[0] < len(t) and t[i[0]] == '-': i[0]+=1; return -unary()
        return pct()
    def pct():
        v = atom()
        while i[0] < len(t) and t[i[0]] == '%': i[0]+=1; v /= 100
        return v
    def atom():
        if t[i[0]] == '(':
            i[0]+=1; v = expr(); i[0]+=1; return v
        v = float(t[i[0]]); i[0]+=1; return v
    return expr()
for e, want in [("-2^2", 4), ("2^3^2", 64)]:
    if abs(ev(e)-want) > 1e-9: bad("수식", f"={e} 는 우선순위 표대로면 {ev(e)}, 문서는 {want}")

# --- 7. 15.2 팔레트 표 재계산 ---
from palette import SHADES, INKS, ratio, auto_ink, WHITE, BLACK
tbl = re.findall(r'^\| ([0-9]) \| (\S+) \| `(#[0-9A-F]{6})` \| `(#[0-9A-F]{6})` \| (\S+) \| ([\d.]+) : 1 \|', S, re.M)
if len(tbl) != 10: bad("팔레트", f"팔레트 표가 10줄이 아니라 {len(tbl)}줄")
for (k, name, sv, iv, auto, r) in tbl:
    exp_s = dict(SHADES).get(name); exp_i = dict(INKS).get(name)
    if exp_s != sv: bad("팔레트", f"{name} 음영이 계산은 {exp_s}, 문서는 {sv}")
    if exp_i != iv: bad("팔레트", f"{name} 글자색이 계산은 {exp_i}, 문서는 {iv}")
    a = auto_ink(sv); an = "흰색" if a == WHITE else "검정"
    if an != auto: bad("팔레트", f"{name} 자동 글자가 계산은 {an}, 문서는 {auto}")
    if abs(ratio(sv, a) - float(r)) > 0.005: bad("팔레트", f"{name} 대비가 계산은 {ratio(sv,a):.2f}, 문서는 {r}")
for n, iv in INKS:
    if n != "흰" and ratio(WHITE, iv) < 4.5:
        bad("팔레트", f"{n} 글자색이 흰 바탕에서 {ratio(WHITE,iv):.2f}:1 로 기준 미달")

# --- 8. 오류 문자열이 표시 예산에 들어가는가 ---
for err in re.findall(r'`(#[A-Z/0-9]+[!?])`', S):
    if len(err) > BUDGET: bad("오류 값", f"{err} 가 {len(err)}자로 예산 10자를 넘음")

# --- 9. 단축키가 겹치는가 ---
keys = {}
for m in re.finditer(r'\| (Ctrl\+\S+|Delete|F2|Esc) \| ([^|]+) \|', S):
    k, what = m.group(1), m.group(2).strip()
    if k in keys and keys[k] != what: bad("단축키", f"{k} 가 두 곳에 배정됨: {keys[k]} / {what}")
    keys[k] = what

# --- 10. 날짜 일련번호 ---
import datetime
if (datetime.date(2026,9,11) - datetime.date(1899,12,30)).days != 46276:
    bad("날짜", "2026-09-11 의 일련번호가 46276 이 아님")

print(f"검사 {10}갈래 실행, 지적 {len(found)}건\n")
for kind, msg in found: print(f"  [{kind}] {msg}")
