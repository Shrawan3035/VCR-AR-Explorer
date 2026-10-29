"""Checks the poster PDF: 4 pages, A3 landscape, renders previews and decodes every QR code.

    python poster/verify_poster.py
Writes poster/previews/sheet-N.png and poster/verification.json; exits 1 on failure.
"""
import json, os, sys
import cv2
import numpy as np
import pymupdf

HERE = os.path.dirname(os.path.abspath(__file__))
pdf = os.path.join(HERE, "VCR-AR-Explorer-A3.pdf")
targets = json.load(open(os.path.join(HERE, "qr_targets.json")))
# Chromium rounds the page to whole points (≈420.2 mm); trim the media box to exact A3 (1190.55 × 841.89 pt)
A3 = pymupdf.Rect(0, 0, 420 / 25.4 * 72, 297 / 25.4 * 72)
_d = pymupdf.open(pdf)
if any(abs(pg.rect.width - A3.width) > 0.01 or abs(pg.rect.height - A3.height) > 0.01 for pg in _d):
    for pg in _d:
        if abs(pg.rect.width - A3.width) < 3 and abs(pg.rect.height - A3.height) < 3:
            pg.set_mediabox(A3)
    _d.save(pdf + ".tmp", garbage=3, deflate=True)
    _d.close()
    os.replace(pdf + ".tmp", pdf)
else:
    _d.close()
doc = pymupdf.open(pdf)
res = {"file": os.path.basename(pdf), "pages": doc.page_count, "sizes_mm": [], "qr": {}, "ok": True}
os.makedirs(os.path.join(HERE, "previews"), exist_ok=True)
det = cv2.QRCodeDetector()
for i, pg in enumerate(doc):
    w, h = pg.rect.width * 25.4 / 72, pg.rect.height * 25.4 / 72
    res["sizes_mm"].append([round(w, 2), round(h, 2)])
    if abs(w - 420) > 0.1 or abs(h - 297) > 0.1: res["ok"] = False
    pg.get_pixmap(dpi=110).save(os.path.join(HERE, "previews", f"sheet-{i + 1}.png"))
    pix = pg.get_pixmap(dpi=200)
    img = np.frombuffer(pix.samples, np.uint8).reshape(pix.h, pix.w, pix.n)[:, :, :3].copy()
    ok, decoded, _, _ = det.detectAndDecodeMulti(img)
    found = set(d for d in (decoded if ok else []) if d)
    # header "Explore it in 3D" code: decode a crop of the top-right corner at higher resolution
    clip = pymupdf.Rect(pg.rect.width * 0.76, 0, pg.rect.width * 0.90, pg.rect.height * 0.18)
    cp = pg.get_pixmap(dpi=300, clip=clip)
    crop = np.frombuffer(cp.samples, np.uint8).reshape(cp.h, cp.w, cp.n)[:, :, :3].copy()
    val, _, _ = det.detectAndDecode(crop)
    res.setdefault("header_qr", {})[f"sheet{i + 1}"] = val
    if val: found.add(val)
    res["qr"][f"sheet{i + 1}"] = sorted(found)
if doc.page_count != 4: res["ok"] = False
if targets["url"]:
    want = {targets["url"], targets["ar_url"]}
    found = set(res["qr"]["sheet4"])
    res["qr_sheet4_matches_targets"] = want <= found
    res["qr_all_found_values_valid"] = all(v in want for s in res["qr"].values() for v in s)
    res["header_qr_all_sheets"] = all(v == targets["url"] for v in res["header_qr"].values())
    res["ok"] = res["ok"] and res["qr_sheet4_matches_targets"] and res["qr_all_found_values_valid"] and res["header_qr_all_sheets"]
json.dump(res, open(os.path.join(HERE, "verification.json"), "w"), indent=1)
print(json.dumps(res, indent=1))
sys.exit(0 if res["ok"] else 1)
