# 팔레트 열 가지: 검, 흰, 회1, 회2 + RGB 세 색 + CMY 세 색.
# 음영은 순색을 그대로 쓰고, 글자색은 같은 색상을 흰 바탕에서 읽히는 밝기까지 내려서 쓴다.
# 밝기를 내릴 때 세 채널을 같은 비율로 줄이므로 색상과 채도는 그대로다(최소 채널이 0 이라 HSL 채도 100% 유지).
NAMES = ["검","흰","회1","회2","빨강","초록","파랑","청록","자홍","노랑"]
PURE  = {"검":"#000000","흰":"#FFFFFF","회1":"#C0C0C0","회2":"#808080",
         "빨강":"#FF0000","초록":"#00FF00","파랑":"#0000FF",
         "청록":"#00FFFF","자홍":"#FF00FF","노랑":"#FFFF00"}
INK_TARGET_Y = 0.09        # 흰 바탕에서 1.05/(0.09+0.05) = 7.5:1
BLACK, WHITE = "#000000", "#FFFFFF"

def to_lin(v): return v/12.92 if v <= 0.03928 else ((v+0.055)/1.055)**2.4
def to_srgb(c): return 12.92*c if c <= 0.0031308 else 1.055*c**(1/2.4) - 0.055
def chans(hx): return [int(hx[i:i+2],16)/255 for i in (1,3,5)]
def lum_of(ch): return 0.2126*to_lin(ch[0]) + 0.7152*to_lin(ch[1]) + 0.0722*to_lin(ch[2])
def lum(hx): return lum_of(chans(hx))
def hexof(ch): return "#%02X%02X%02X" % tuple(round(min(1,max(0,c))*255) for c in ch)

def ratio(a,b):
    la,lb = lum(a),lum(b); hi,lo = max(la,lb),min(la,lb)
    return (hi+0.05)/(lo+0.05)

def darken_to(hx, target):
    """세 채널을 선형 공간에서 같은 비율로 줄여 목표 휘도에 맞춘다. 못 내려가면 원색 그대로."""
    lin = [to_lin(c) for c in chans(hx)]
    y = 0.2126*lin[0] + 0.7152*lin[1] + 0.0722*lin[2]
    if y <= target: return hx
    f = target / y
    return hexof([to_srgb(c*f) for c in lin])

SHADES = [(n, PURE[n]) for n in NAMES]
INKS   = []
for n in NAMES:
    if n == "흰":    INKS.append((n, WHITE))         # 어두운 음영 위에 쓰는 글자색
    elif n == "검":  INKS.append((n, BLACK))
    elif n == "회1": INKS.append((n, "#737373"))      # 회색은 목표 휘도로 내리면 둘이 겹쳐서 두 단계로 직접 잡는다
    elif n == "회2": INKS.append((n, "#3C3C3C"))
    else:            INKS.append((n, darken_to(PURE[n], INK_TARGET_Y)))

def auto_ink(bg):
    """글자색을 고르지 않은 칸의 기본 글자색: 그 음영에서 대비가 높은 쪽."""
    return BLACK if ratio(bg, BLACK) >= ratio(bg, WHITE) else WHITE

if __name__ == "__main__":
    print(f"{'이름':<5}{'음영(순색)':<12}{'글자색':<10}{'흰 바탕 대비':>12}{'자동 글자':>10}{'그 대비':>9}")
    for (n,sv),(_,iv) in zip(SHADES, INKS):
        a = auto_ink(sv)
        print(f"{n:<5}{sv:<12}{iv:<10}{ratio(WHITE,iv):>11.2f}:1"
              f"{'검정' if a==BLACK else '흰색':>10}{ratio(sv,a):>8.2f}:1")
    bad_ink = [n for (n,iv) in INKS if n != "흰" and ratio(WHITE, iv) < 4.5]
    bad_bg  = [n for (n,sv) in SHADES if ratio(sv, auto_ink(sv)) < 4.5]
    print(f"\n흰 바탕에서 못 읽는 글자색: {bad_ink or '없음'}")
    print(f"자동 글자로 못 읽는 음영: {bad_bg or '없음'}")
