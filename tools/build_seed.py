"""
ETL dữ liệu V.League 1 cho ScoutVN (Nhóm 14).
Đầu vào : tools/fotmob_export_2026-11-02.json  (trích từ trang công khai FotMob, giải 9088 – V.League 1)
Đầu ra  : Data/Seed/vleague_seed.json           (được DbSeeder đọc khi khởi tạo CSDL)

Các bước: chuẩn hóa tên (họ đứng trước), ánh xạ vị trí, quy đổi giá trị chuyển nhượng,
ghép thống kê 2 mùa, tính ƯỚC LƯỢNG 6 chỉ số năng lực (FotMob không công bố chỉ số này
cho V.League) theo công thức mô tả ở hàm estimate_attributes().
Chạy:  python tools/build_seed.py
"""
import json
import random, math, hashlib, os, collections as C
from datetime import date

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "tools", "fotmob_export_2026-11-02.json")
DST = os.path.join(ROOT, "Data", "Seed", "vleague_seed.json")
REF_DATE = date(2026, 11, 2)          # ngày chốt dữ liệu
EUR_TO_MIL_VND = 0.031                # 1 EUR ≈ 31.000 VNĐ (tỷ giá bán NH, 6/2026) => triệu VNĐ = EUR × 0,031

# Trọng số chỉ số tổng theo vị trí (giống PlayerRating.Overall trong C#)
OVR_W = {"GK": [.10, 0, .20, .05, .40, .25], "CB": [.10, 0, .10, .05, .45, .30],
         "LB": [.25, 0, .15, .10, .30, .20], "RB": [.25, 0, .15, .10, .30, .20],
         "DM": [.05, .05, .25, .10, .35, .20], "CM": [.10, .10, .30, .20, .15, .15],
         "AM": [.10, .20, .30, .30, 0, .10], "LW": [.30, .20, .15, .30, 0, .05],
         "RW": [.30, .20, .15, .30, 0, .05], "ST": [.20, .40, .05, .20, 0, .15]}


def foot_of(p):
    """Chân thuận: lấy từ FotMob nếu có; nguồn thiếu (~96% cầu thủ) thì GIẢ LẬP ngẫu nhiên có điều kiện theo vị trí
    (cố định theo mã cầu thủ để lần chạy sau ra cùng kết quả). Không phải dữ liệu thật."""
    known = {"R": "Right", "L": "Left", "B": "Both"}.get(p["foot"])
    if known: return known
    r = random.Random(p["pid"]).random()
    left = {"LB": .60, "LW": .55, "RB": .08, "RW": .12}.get(p["pos"], .20)
    return "Left" if r < left else ("Both" if r < left + .05 else "Right")


def market_value(p):
    """Giá trị ước tính (triệu VNĐ) theo mặt bằng chuyển nhượng nội địa.
    FotMob định giá nhiều tuyển thủ Việt Nam ở mức sàn 50.000 € (≈ 1,5 tỷ), thấp xa mức
    hợp đồng thực tế ở V.League (cầu thủ hàng đầu ~ 9–10 tỷ/mùa, báo chí 2024). Vì vậy:
      ước tính = 3 tỷ × 1,25^(chỉ số tổng − 65) × hệ số tuổi
      giá trị  = max(giá FotMob quy đổi, ước tính), làm tròn 10 triệu."""
    ovr = round(sum(w * a for w, a in zip(OVR_W[p["pos"]], p["attr"])))
    age = age_of(p["dob"]) or 27
    fa = 1.25 if age <= 21 else 1.15 if age <= 24 else 1.0 if age <= 29 else 0.75 if age <= 32 else 0.5
    est = 3000 * 1.25 ** (ovr - 65) * fa
    fot = p["val"] * EUR_TO_MIL_VND if p["val"] > 0 else 0
    return int(round(max(est, fot) / 10) * 10)

# FotMob id -> (tên, viết tắt, tỉnh/thành, sân nhà)
TEAMS = {
    614354:  ("Công An Hà Nội", "CAHN", "Hà Nội", "Hàng Đẫy"),
    743070:  ("Thể Công – Viettel", "TCVT", "Hà Nội", "Mỹ Đình"),
    1235223: ("Ninh Bình FC", "NBFC", "Ninh Bình", None),
    179219:  ("Hà Nội FC", "HNFC", "Hà Nội", "Hàng Đẫy"),
    179228:  ("Công An TP. Hồ Chí Minh", "CATP", "TP. Hồ Chí Minh", "Thống Nhất"),
    109375:  ("Thép Xanh Nam Định", "TXND", "Nam Định", "Thiên Trường"),
    179222:  ("Hải Phòng", "HPFC", "Hải Phòng", "Lạch Tray"),
    1123220: ("Hồng Lĩnh Hà Tĩnh", "HLHT", "Hà Tĩnh", "Hà Tĩnh"),
    179221:  ("Sông Lam Nghệ An", "SLNA", "Nghệ An", "Vinh"),
    179227:  ("Hoàng Anh Gia Lai", "HAGL", "Gia Lai", "Pleiku"),
    179226:  ("Đông Á Thanh Hóa", "THFC", "Thanh Hóa", "Thanh Hóa"),
    179220:  ("SHB Đà Nẵng", "SHBĐN", "Đà Nẵng", "Chi Lăng"),
    1105640: ("PVF-CAND", "PVF", "Hưng Yên", "PVF"),
    109374:  ("Becamex TP. Hồ Chí Minh", "BHCM", "TP. Hồ Chí Minh", "Bình Dương"),
    1801359: ("Bắc Ninh FC", "BNFC", "Bắc Ninh", None),
    614353:  ("Trường Tươi Đồng Nai", "TTĐN", "Đồng Nai", None),
}
COUNTRY_VI = {"Vietnam": "Việt Nam", "Brazil": "Brazil", "France": "Pháp", "Nigeria": "Nigeria", "Australia": "Úc",
              "Serbia": "Serbia", "Cameroon": "Cameroon", "USA": "Mỹ", "Colombia": "Colombia", "Canada": "Canada",
              "Ireland": "Ireland", "Uganda": "Uganda", "Japan": "Nhật Bản", "South Korea": "Hàn Quốc", "Korea Republic": "Hàn Quốc",
              "Germany": "Đức", "Netherlands": "Hà Lan", "England": "Anh", "Spain": "Tây Ban Nha", "Portugal": "Bồ Đào Nha",
              "Argentina": "Argentina", "Ghana": "Ghana", "Romania": "Romania", "Czechia": "Séc", "Czech Republic": "Séc",
              "Montenegro": "Montenegro", "Croatia": "Croatia", "Mali": "Mali", "Senegal": "Senegal", "Ivory Coast": "Bờ Biển Ngà",
              "Côte d'Ivoire": "Bờ Biển Ngà", "Congo DR": "CHDC Congo", "DR Congo": "CHDC Congo", "Angola": "Angola", "Poland": "Ba Lan",
              "Russia": "Nga", "Ukraine": "Ukraina", "Sweden": "Thụy Điển", "Norway": "Na Uy", "Belgium": "Bỉ", "Italy": "Ý",
              "Philippines": "Philippines", "Thailand": "Thái Lan", "Laos": "Lào", "Cambodia": "Campuchia", "Indonesia": "Indonesia",
              "South Africa": "Nam Phi", "Bosnia and Herzegovina": "Bosnia & Herzegovina", "North Macedonia": "Bắc Macedonia",
              "Haiti": "Haiti", "Jamaica": "Jamaica", "Venezuela": "Venezuela", "Uruguay": "Uruguay", "Chile": "Chile",
              "Kenya": "Kenya", "Zambia": "Zambia", "Burkina Faso": "Burkina Faso", "Guinea": "Guinea", "Gabon": "Gabon",
              "Togo": "Togo", "Benin": "Benin", "Liberia": "Liberia", "Sierra Leone": "Sierra Leone", "Gambia": "Gambia",
              "Iran": "Iran", "Uzbekistan": "Uzbekistan", "New Zealand": "New Zealand", "Scotland": "Scotland", "Wales": "Xứ Wales",
              "Slovakia": "Slovakia", "Slovenia": "Slovenia", "Hungary": "Hungary", "Bulgaria": "Bulgaria", "Greece": "Hy Lạp",
              "Turkey": "Thổ Nhĩ Kỳ", "Georgia": "Gruzia", "Armenia": "Armenia", "Albania": "Albania", "Kosovo": "Kosovo",
              "Lithuania": "Litva", "Latvia": "Latvia", "Estonia": "Estonia", "Finland": "Phần Lan", "Denmark": "Đan Mạch",
              "Switzerland": "Thụy Sĩ", "Austria": "Áo", "Mexico": "Mexico", "Costa Rica": "Costa Rica", "Panama": "Panama",
              "Honduras": "Honduras", "Peru": "Peru", "Ecuador": "Ecuador", "Paraguay": "Paraguay", "Bolivia": "Bolivia"}

# Ánh xạ vị trí FotMob -> 10 vị trí của hệ thống
POS_MAP = {"GK": "GK", "CB": "CB", "LB": "LB", "LWB": "LB", "RB": "RB", "RWB": "RB", "CDM": "DM", "CM": "CM",
           "CAM": "AM", "LM": "LW", "LW": "LW", "RM": "RW", "RW": "RW", "ST": "ST", "CF": "ST"}
USUAL_MAP = {0: "GK", 1: "CB", 2: "CM", 3: "ST"}   # nhóm vị trí FotMob dùng trong đội hình ra sân
GROUP = {"GK": "GK", "CB": "DEF", "LB": "DEF", "RB": "DEF", "DM": "MID", "CM": "MID", "AM": "MID", "LW": "ATT", "RW": "ATT", "ST": "ATT"}

SURNAMES = set("nguyen tran le pham hoang huynh phan vu vo dang bui do ho ngo duong ly dinh trinh luong mai doan cao truong chu ta kieu giap lai tong thai quan vuong ha lam chau to la luu dao tang van khuat hua au phung nong trieu mac sam nghiem quach khong thach dam dau tieu nham luc que".split())
AMBIG_FIRST = set("hoang dinh mai ha lam van thai quan tong".split())
ACCENT = str.maketrans("àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđ",
                       "aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyyd")

def plain(s): return s.lower().translate(ACCENT)

def vn_name(name, cc):
    """FotMob ghi tên Việt theo thứ tự Tên – Đệm – Họ; đưa họ lên trước nếu nhận ra được."""
    parts = name.split()
    if cc != "VIE" or len(parts) < 2: return name
    first, last = plain(parts[0]), plain(parts[-1])
    if last in SURNAMES and (first not in SURNAMES or first in AMBIG_FIRST):
        parts = [parts[-1]] + parts[:-1]
    return " ".join(parts)

# ----- Thêm dấu tiếng Việt cho tên: đối chiếu danh sách đội hình trên Wikipedia tiếng Việt -----
WIKI = os.path.join(ROOT, "tools", "wikipedia_vi_squads_2026-11-08.txt")
COACH_VI = {"vu hong viet": "Vũ Hồng Việt", "chu dinh nghiem": "Chu Đình Nghiêm", "van sy son": "Văn Sỹ Sơn",
            "mai xuan hop": "Mai Xuân Hợp", "le quang trai": "Lê Quang Trãi", "phan nhu thuat": "Phan Như Thuật",
            "dang van thanh": "Đặng Văn Thành", "nguyen viet thang": "Nguyễn Việt Thắng", "le duc tuan": "Lê Đức Tuấn"}

def key(name):
    return " ".join(sorted(plain(name).replace("-", " ").split()))

def load_wiki():
    """{khóa tên không dấu (sắp xếp từ) -> [(fotmob team id, số áo, tên có dấu)]}"""
    idx = {}
    if not os.path.exists(WIKI): return idx
    for item in open(WIKI, encoding="utf-8").read().split(";"):
        parts = item.split("|")
        if len(parts) != 3: continue
        tid, no, nm = parts
        nm = nm.replace("[[", "").replace("]]", "").strip()
        if not nm: continue
        idx.setdefault(key(nm), []).append((int(tid), no, nm))
    return idx

STATS_ACCENT = {"matched": 0, "team": 0, "unmatched": []}

def accent_name(raw, cc, team, shirt, idx):
    """Trả về tên có dấu nếu khớp chắc chắn với Wikipedia: cùng tập âm tiết (không dấu);
    nếu nhiều ứng viên khác nhau thì ưu tiên cùng đội rồi cùng số áo; còn mơ hồ thì giữ nguyên."""
    if cc != "VIE": return None
    cands = idx.get(key(raw), [])
    names = {c[2] for c in cands}
    if len(names) == 1:
        STATS_ACCENT["matched"] += 1; return names.pop()
    same = {c[2] for c in cands if c[0] == team}
    if len(same) == 1:
        STATS_ACCENT["team"] += 1; return same.pop()
    same_no = {c[2] for c in cands if c[0] == team and c[1] == str(shirt)}
    if len(same_no) == 1:
        STATS_ACCENT["team"] += 1; return same_no.pop()
    return None

def syllable_map(idx):
    """Âm tiết không dấu -> dạng có dấu, chỉ giữ âm tiết luôn viết một cách duy nhất trong danh sách Wikipedia."""
    seen = {}
    for lst in idx.values():
        for _, _, nm in lst:
            for w in nm.split():
                seen.setdefault(plain(w), set()).add(w)
    return {k: next(iter(v)) for k, v in seen.items() if len(v) == 1}

def accent_by_syllables(name, smap):
    """Thêm dấu theo từng âm tiết; chỉ áp dụng khi MỌI âm tiết đều có dạng duy nhất, tránh tên dấu nửa vời."""
    out = []
    for w in name.split():
        a = smap.get(plain(w))
        if a is None: return None
        out.append(a)
    return " ".join(out)

def h(*a):
    return int(hashlib.md5("|".join(map(str, a)).encode()).hexdigest()[:8], 16)

def age_of(dob):
    if not dob: return None
    y, m, d = map(int, dob.split("-"))
    return REF_DATE.year - y - ((REF_DATE.month, REF_DATE.day) < (m, d))

# Hồ sơ năng lực gốc theo vị trí: tốc độ, dứt điểm, chuyền, rê, phòng ngự, thể lực
TEMPLATE = {"GK": [45, 20, 52, 35, 66, 64], "CB": [56, 32, 52, 45, 68, 70], "LB": [68, 40, 58, 58, 62, 64],
            "RB": [68, 40, 58, 58, 62, 64], "DM": [58, 45, 63, 57, 66, 68], "CM": [60, 52, 66, 62, 56, 64],
            "AM": [64, 60, 68, 68, 40, 58], "LW": [72, 58, 60, 68, 36, 58], "RW": [72, 58, 60, 68, 36, 58],
            "ST": [66, 64, 52, 60, 32, 68]}

def pct_rank(values):
    """giá trị -> phân vị [0,1] trong danh sách"""
    s = sorted(values); n = len(s)
    return lambda v: (sum(1 for x in s if x <= v) - 1) / max(1, n - 1)

def main():
    d = json.load(open(SRC, encoding="utf-8"))
    S1 = {r[0]: r for r in d["S1"]}; S2 = {r[0]: r for r in d["S2"]}
    matches = d["M"]
    games2627 = C.Counter()
    for m in matches["2026/2027"]:
        games2627[m[3]] += 1; games2627[m[4]] += 1

    players = []
    widx = load_wiki()
    smap = syllable_map(widx)
    for p in d["P"]:
        pid, name, dob, hgt, foot, cc, country, shirt, posdesc, tid, val, inj = p
        cand = [x.strip() for x in posdesc.split(",") if x.strip()]
        pos = POS_MAP.get(cand[0]) if cand else None
        if not pos:
            up = next((r[11] for r in (S2.get(pid), S1.get(pid)) if r and r[11] >= 0), None)
            pos = USUAL_MAP.get(up)
        has_stats = pid in S1 or pid in S2
        if not pos:
            if not has_stats: continue          # không có vị trí lẫn số liệu -> loại
            pos = "ST" if (S1.get(pid, [0]*12)[5] + S2.get(pid, [0]*12)[5]) > 0 else "CM"
            pos_guessed = True
        else:
            pos_guessed = False
        if not dob or not (14 <= (age_of(dob) or 0) <= 45): continue   # thiếu / sai ngày sinh -> loại
        acc = accent_name(name, cc, tid, shirt, widx)
        if cc == "VIE" and not acc:
            acc = accent_by_syllables(vn_name(name, cc), smap)
            if acc: STATS_ACCENT.setdefault("syll", []).append(acc)
            else: STATS_ACCENT["unmatched"].append(vn_name(name, cc))
        players.append(dict(pid=pid, name=acc or vn_name(name, cc), dob=dob, h=hgt or None, foot=foot, cc=cc,
                            country=COUNTRY_VI.get(country, country), shirt=shirt or None, pos=pos, alt=cand,
                            guessed=pos_guessed, tid=tid or None, val=val, inj=inj))

    # ----- tín hiệu chất lượng -----
    vals = [p["val"] for p in players if p["val"] > 0]
    vrank = pct_rank(vals)
    def goals_total(pid): return (S1.get(pid, [0]*12)[5]) + (S2.get(pid, [0]*12)[5])
    grp_goals = C.defaultdict(list)
    for p in players: grp_goals[GROUP[p["pos"]]].append(goals_total(p["pid"]))
    grank = {g: pct_rank(v) for g, v in grp_goals.items()}

    for p in players:
        pid, pos, g = p["pid"], p["pos"], GROUP[p["pos"]]
        sig = []   # (giá trị [0,1], trọng số)
        if p["val"] > 0: sig.append((vrank(p["val"]), 0.40))
        s2 = S2.get(pid)
        if s2 and games2627[s2[1]] > 0:
            share = min(1.0, s2[4] / (games2627[s2[1]] * 90)); sig.append((share, 0.35))
        if g in ("MID", "ATT"):
            sig.append((grank[g](goals_total(pid)), 0.25))
        elif s2 and s2[2] > 0:
            sig.append((s2[3] / s2[2], 0.25))   # tỉ lệ đá chính
        q = sum(v * w for v, w in sig) / sum(w for _, w in sig) if sig else 0.30
        shift = (q - 0.5) * 20
        a = [t + shift for t in TEMPLATE[pos]]
        age = age_of(p["dob"]) or 26
        if g != "GK": a[1] += min(8, 3 * math.sqrt(goals_total(pid)))
        if p["h"]: a[5] += max(-4, min(5, (p["h"] - 178) * 0.4))
        if age <= 22: a[0] += 3
        if age > 29: a[0] -= (age - 29) * 1.5
        if age > 31: a[5] -= (age - 31)
        if g == "GK" and s2 and s2[2] > 0: a[4] += 8 * s2[9] / s2[2]
        a = [int(round(max(35, min(90, x + (h(pid, k) % 7) - 3)))) for k, x in enumerate(a)]
        p["attr"] = a; p["q"] = round(q, 3)

    out_players = []
    for p in players:
        notes = []
        if len(p["alt"]) > 1: notes.append("Vị trí có thể chơi (FotMob): " + ", ".join(p["alt"]))
        if p["guessed"]: notes.append("Vị trí suy ra từ số liệu (nguồn không ghi vị trí cụ thể).")
        out_players.append(dict(
            src=p["pid"], name=p["name"], dob=p["dob"], nat=p["country"], pos=p["pos"],
            foot=foot_of(p),
            height=p["h"], shirt=p["shirt"] if p["shirt"] and 1 <= p["shirt"] <= 99 else None,
            team=p["tid"], status="Injured" if p["inj"] else ("Active" if p["tid"] else "LeftLeague"),
            value=market_value(p),
            attr=p["attr"], notes=" ".join(notes) or None))
    kept = {p["src"] for p in out_players}

    teams = []
    current = {t["id"] for t in d["t2627"]}
    for tid, (nm, short, city, stadium) in TEAMS.items():
        info = d["teams"].get(str(tid), {})
        coach = vn_name(info.get("n", ""), info.get("cc", "")) or None
        ck = {key(k): v for k, v in COACH_VI.items()}
        if coach and key(coach) in ck: coach = ck[key(coach)]
        desc = ("Câu lạc bộ thi đấu tại V.League 1 mùa 2026/27." if tid in current
                else "Thi đấu V.League 1 mùa 2025/26, xuống hạng Nhất sau mùa giải.")
        if tid not in current: coach = None   # BHL hiện tại không thuộc phạm vi dữ liệu
        teams.append(dict(src=tid, name=nm, short=short, city=city, stadium=stadium, coach=coach, desc=desc))

    def stat_rows(rows, detail):
        res = []
        for r in rows:
            if r[0] not in kept: continue
            pid, tid, apps, starts, mins, goals, pen, yc, rc, cs, gc, _ = r
            if detail:
                res.append(dict(p=pid, t=tid, apps=apps, starts=starts, min=mins, g=goals, pen=None, yc=yc, rc=rc, cs=cs, gc=gc))  # nguồn không có phạt đền mùa 2026/27
            else:
                if goals == 0 and yc == 0 and rc == 0: continue
                res.append(dict(p=pid, t=tid, apps=0, starts=0, min=0, g=goals, pen=pen, yc=yc, rc=rc, cs=0, gc=0))
        return res

    def match_rows(ms):
        out = []
        for m in ms:
            hg, ag = map(int, m[5].split(" - "))
            out.append(dict(src=int(m[0]), round=int(m[1]), utc=m[2], h=m[3], a=m[4], hg=hg, ag=ag))
        return sorted(out, key=lambda x: (x["round"], x["utc"]))

    seed = dict(
        source="FotMob – V.League 1 (giải 9088), trích xuất ngày 02/11/2026",
        refDate=str(REF_DATE), teams=teams, players=out_players,
        tournaments=[
            dict(key="2526", name="V.League 1", season="2025/26", start="2025-08-15", end="2026-06-07", detail=False,
                 desc="Mùa giải đã kết thúc (26 vòng, 14 đội). Có đầy đủ kết quả 182 trận; số liệu cá nhân chỉ gồm bàn thắng, phạt đền và thẻ phạt.",
                 stats=stat_rows(d["S1"], False), matches=match_rows(matches["2025/2026"])),
            dict(key="2627", name="V.League 1", season="2026/27", start="2026-09-04", end=None, detail=True,
                 desc="Mùa giải đang diễn ra – số liệu cập nhật đến hết vòng 6. Có số trận, đá chính, số phút, bàn thắng, thẻ phạt, giữ sạch lưới theo từng trận (chưa có số liệu phạt đền).",
                 stats=stat_rows(d["S2"], True), matches=match_rows(matches["2026/2027"])),
        ])
    json.dump(seed, open(DST, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
    print("players", len(out_players), "of", len(d["P"]), "| stats", [len(t["stats"]) for t in seed["tournaments"]],
          "| matches", [len(t["matches"]) for t in seed["tournaments"]], "|", os.path.getsize(DST), "bytes")
    print("pos", C.Counter(p["pos"] for p in out_players))
    print("accent: khớp duy nhất", STATS_ACCENT["matched"], "| khớp theo đội", STATS_ACCENT["team"], "| theo âm tiết", len(STATS_ACCENT.get("syll", [])), "| chưa khớp", len(STATS_ACCENT["unmatched"]))
    print("  theo âm tiết:", STATS_ACCENT.get("syll", []))
    print("  chưa khớp:", STATS_ACCENT["unmatched"])
    print("status", C.Counter(p["status"] for p in out_players))
    top = sorted(players, key=lambda p: -(sum(p["attr"])))[:5]
    print([(p["name"], p["pos"], p["attr"]) for p in top])

if __name__ == "__main__":
    main()
