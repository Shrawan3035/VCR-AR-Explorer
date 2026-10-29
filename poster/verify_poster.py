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
doc = pymupdf.open(pdf)
res = {"file": os.path.basename(pdf), "pages": doc.page_count, "sizes_mm": [], "qr": {}, "ok": True}
os.makedirs(os.path.join(HERE, "previews"), exist_ok=True)
det = cv2.QRCodeDetector()
for i, pg in enumerate(doc):
    w, h = pg.rect.width * 25.4 / 72, pg.rect.height * 25.4 / 72
    res["sizes_mm"].append([round(w, 1), round(h, 1)])
    if abs(w - 420) > 1 or abs(h - 297) > 1: res["ok"] = False
    pg.get_pixmap(dpi=110).save(os.path.join(HERE, "previews", f"sheet-{i + 1}.png"))
    pix = pg.get_pixmap(dpi=200)
    img = np.frombuffer(pix.samples, np.uint8).reshape(pix.h, pix.w, pix.n)[:, :, :3].copy()
    ok, decoded, _, _ = det.detectAndDecodeMulti(img)
    res["qr"][f"sheet{i + 1}"] = sorted(set(d for d in (decoded if ok else []) if d))
if doc.page_count != 4: res["ok"] = False
if targets["url"]:
    want = {targets["url"], targets["ar_url"]}
    found = set(res["qr"]["sheet4"])
    res["qr_sheet4_matches_targets"] = want <= found
    res["qr_all_found_values_valid"] = all(v in want for s in res["qr"].values() for v in s)
    res["ok"] = res["ok"] and res["qr_sheet4_matches_targets"] and res["qr_all_found_values_valid"]
json.dump(res, open(os.path.join(HERE, "verification.json"), "w"), indent=1)
print(json.dumps(res, indent=1))
sys.exit(0 if res["ok"] else 1)
