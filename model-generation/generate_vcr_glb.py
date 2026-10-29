"""
Procedural generator for the VCR (Vapour Compression Refrigeration) educational rig.

Builds an ORIGINAL, schematic-but-recognisable 3D model from primitives (boxes, lathes,
swept tubes, fin packs) with numpy and writes a binary glTF 2.0 (.glb) file directly,
without Blender or any third-party exporter.

The geometry is an EDUCATIONAL VISUALISATION. It is not CAD, it is not dimensionally exact,
and it does not represent any specific manufacturer's product.

Usage:
    python generate_vcr_glb.py [output.glb] [--copy-to path ...]

Units: metres, +Y up, +Z towards the viewer (glTF convention). Overall rig ~1.3 x 0.83 x 0.7 m.

Every selectable component is a top-level node named Cxx_Name (see ../data/components.json)
whose origin sits at the centre of its bounding box, so apps can explode / pivot it cleanly.
Fan rotors are separate child nodes ("...Rotor") pivoted on the fan axis so they can spin.
"""
from __future__ import annotations

import json
import math
import os
import shutil
import struct
import sys

import numpy as np

# --------------------------------------------------------------------------------------
# Mesh primitives
# --------------------------------------------------------------------------------------


class Mesh:
    def __init__(self, pos, nrm, uv, idx):
        self.pos = np.asarray(pos, np.float32).reshape(-1, 3)
        self.nrm = np.asarray(nrm, np.float32).reshape(-1, 3)
        self.uv = np.asarray(uv, np.float32).reshape(-1, 2)
        self.idx = np.asarray(idx, np.uint32).reshape(-1, 3)

    def transformed(self, m: np.ndarray) -> "Mesh":
        p = self.pos @ m[:3, :3].T + m[:3, 3]
        nm = np.linalg.inv(m[:3, :3]).T
        n = self.nrm @ nm.T
        n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
        return Mesh(p, n, self.uv, self.idx)


def merge(meshes: list[Mesh]) -> Mesh:
    pos, nrm, uv, idx, off = [], [], [], [], 0
    for m in meshes:
        pos.append(m.pos); nrm.append(m.nrm); uv.append(m.uv); idx.append(m.idx + off)
        off += len(m.pos)
    return Mesh(np.concatenate(pos), np.concatenate(nrm), np.concatenate(uv), np.concatenate(idx))


def fix_winding(m: Mesh) -> Mesh:
    """Make every triangle counter-clockwise relative to its (analytic) vertex normals."""
    a, b, c = m.pos[m.idx[:, 0]], m.pos[m.idx[:, 1]], m.pos[m.idx[:, 2]]
    gn = np.cross(b - a, c - a)
    vn = m.nrm[m.idx[:, 0]] + m.nrm[m.idx[:, 1]] + m.nrm[m.idx[:, 2]]
    flip = np.einsum("ij,ij->i", gn, vn) < 0
    idx = m.idx.copy()
    idx[flip] = idx[flip][:, [0, 2, 1]]
    # drop degenerate triangles
    area = np.linalg.norm(gn, axis=1)
    idx = idx[area > 1e-12]
    return Mesh(m.pos, m.nrm, m.uv, idx)


# ---- matrices ----
def T(x, y, z):
    m = np.eye(4); m[:3, 3] = (x, y, z); return m


def S(x, y=None, z=None):
    y = x if y is None else y; z = x if z is None else z
    return np.diag([x, y, z, 1.0])


def Rx(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0, 0], [0, c, -s, 0], [0, s, c, 0], [0, 0, 0, 1]], float)


def Ry(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s, 0], [0, 1, 0, 0], [-s, 0, c, 0], [0, 0, 0, 1]], float)


def Rz(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, -s, 0, 0], [s, c, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]], float)


def align_y(d) -> np.ndarray:
    """Rotation mapping +Y onto direction d."""
    d = np.asarray(d, float); d /= np.linalg.norm(d)
    y = np.array([0, 1.0, 0])
    v = np.cross(y, d); c = float(np.dot(y, d))
    m = np.eye(4)
    if np.linalg.norm(v) < 1e-9:
        if c < 0: m = Rx(math.pi)
        return m
    vx = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    m[:3, :3] = np.eye(3) + vx + vx @ vx * (1 / (1 + c))
    return m


def chain(*ms):
    out = np.eye(4)
    for m in ms: out = m @ out  # apply in listed order
    return out


# ---- primitives ----
def box(sx, sy, sz) -> Mesh:
    hx, hy, hz = sx / 2, sy / 2, sz / 2
    faces = [((1, 0, 0), (0, 0, -1), (0, 1, 0)), ((-1, 0, 0), (0, 0, 1), (0, 1, 0)),
             ((0, 1, 0), (1, 0, 0), (0, 0, -1)), ((0, -1, 0), (1, 0, 0), (0, 0, 1)),
             ((0, 0, 1), (1, 0, 0), (0, 1, 0)), ((0, 0, -1), (-1, 0, 0), (0, 1, 0))]
    pos, nrm, uv, idx = [], [], [], []
    h = np.array([hx, hy, hz])
    for n, u, v in faces:
        n, u, v = map(np.array, (n, u, v))
        c = n * h
        for su, sv in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            pos.append(c + u * h * su + v * h * sv); nrm.append(n); uv.append(((su + 1) / 2, (sv + 1) / 2))
        b = len(pos) - 4
        idx += [(b, b + 1, b + 2), (b, b + 2, b + 3)]
    return fix_winding(Mesh(pos, nrm, uv, idx))


def lathe(profile, segs=32, a0=0.0, a1=2 * math.pi) -> Mesh:
    """Revolve a (r, y) profile around +Y. Repeat a point to create a hard edge."""
    p = np.asarray(profile, float)
    n = len(p)
    tang = np.zeros_like(p)
    for i in range(n):
        if i + 1 < n and np.allclose(p[i], p[i + 1]):
            t = p[i] - p[i - 1]
        elif i > 0 and np.allclose(p[i], p[i - 1]):
            t = p[i + 1] - p[i]
        elif i == 0:
            t = p[1] - p[0]
        elif i == n - 1:
            t = p[-1] - p[-2]
        else:
            t = p[i + 1] - p[i - 1]
        tang[i] = t / max(np.linalg.norm(t), 1e-12)
    n2 = np.stack([tang[:, 1], -tang[:, 0]], 1)  # (nr, ny)
    pos, nrm, uv = [], [], []
    for j in range(segs + 1):
        a = a0 + (a1 - a0) * j / segs
        ca, sa = math.cos(a), math.sin(a)
        for i in range(n):
            r, y = p[i]
            pos.append((r * ca, y, r * sa))
            nrm.append((n2[i, 0] * ca, n2[i, 1], n2[i, 0] * sa))
            uv.append((j / segs, i / (n - 1)))
    idx = []
    for j in range(segs):
        for i in range(n - 1):
            if np.allclose(p[i], p[i + 1]): continue
            v0 = j * n + i; v1 = (j + 1) * n + i
            idx += [(v0, v1, v1 + 1), (v0, v1 + 1, v0 + 1)]
    return fix_winding(Mesh(pos, nrm, uv, idx))


def cylinder(r, h, segs=24, caps=True) -> Mesh:
    """Cylinder along +Y from y=0 to y=h."""
    prof = [(r, 0), (r, h)]
    if caps: prof = [(0, 0), (r, 0), (r, 0), (r, h), (r, h), (0, h)]
    return lathe(prof, segs)


def torus(R, r, segs=32, rsegs=10) -> Mesh:
    prof = [(R + r * math.cos(t), r * math.sin(t)) for t in np.linspace(-math.pi, math.pi, rsegs + 1)]
    return lathe(prof, segs)


def disk_cap(center, normal, r, segs=16) -> Mesh:
    c = np.asarray(center, float); n = np.asarray(normal, float); n /= np.linalg.norm(n)
    m = align_y(n)
    pos = [c]; nrm = [n]; uv = [(0.5, 0.5)]
    for j in range(segs + 1):
        a = 2 * math.pi * j / segs
        local = np.array([r * math.cos(a), 0, r * math.sin(a)])
        pos.append(c + m[:3, :3] @ local); nrm.append(n); uv.append((0.5 + 0.5 * math.cos(a), 0.5 + 0.5 * math.sin(a)))
    idx = [(0, j + 1, j + 2) for j in range(segs)]
    return fix_winding(Mesh(pos, nrm, uv, idx))


def rounded_path(points, radius, arc=8):
    pts = [np.asarray(p, float) for p in points]
    out = [pts[0]]
    for i in range(1, len(pts) - 1):
        P, A, B = pts[i], pts[i - 1], pts[i + 1]
        a = P - A; la = np.linalg.norm(a); a /= la
        b = B - P; lb = np.linalg.norm(b); b /= lb
        ang = math.acos(max(-1, min(1, float(np.dot(a, b)))))
        if ang < 1e-3:
            out.append(P); continue
        d = min(radius * math.tan(ang / 2), la * 0.49, lb * 0.49)
        s, e = P - a * d, P + b * d
        for k in range(arc + 1):
            t = k / arc
            out.append((1 - t) ** 2 * s + 2 * t * (1 - t) * P + t ** 2 * e)
    out.append(pts[-1])
    # remove duplicates
    clean = [out[0]]
    for p in out[1:]:
        if np.linalg.norm(p - clean[-1]) > 1e-6: clean.append(p)
    return clean


def tube(points, r, segs=12, bend=None, caps=True) -> Mesh:
    """Sweep a circle of radius r along a polyline (corners filleted with radius `bend`)."""
    pts = rounded_path(points, bend if bend is not None else r * 3) if len(points) > 2 else [np.asarray(p, float) for p in points]
    pts = np.array(pts)
    n = len(pts)
    tang = np.zeros_like(pts)
    for i in range(n):
        t = pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]
        tang[i] = t / np.linalg.norm(t)
    # parallel transport frame
    ref = np.array([0, 1.0, 0]) if abs(tang[0][1]) < 0.9 else np.array([1.0, 0, 0])
    nrm0 = np.cross(tang[0], ref); nrm0 /= np.linalg.norm(nrm0)
    frames = [nrm0]
    for i in range(1, n):
        v = frames[-1] - tang[i] * np.dot(frames[-1], tang[i])
        frames.append(v / np.linalg.norm(v))
    lengths = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(pts, axis=0), axis=1))])
    pos, nr, uv = [], [], []
    for i in range(n):
        N = frames[i]; B = np.cross(tang[i], N)
        for j in range(segs + 1):
            a = 2 * math.pi * j / segs
            d = N * math.cos(a) + B * math.sin(a)
            pos.append(pts[i] + d * r); nr.append(d); uv.append((lengths[i] / (2 * math.pi * r * 4), j / segs))
    idx = []
    for i in range(n - 1):
        for j in range(segs):
            v0 = i * (segs + 1) + j; v1 = v0 + segs + 1
            idx += [(v0, v1, v1 + 1), (v0, v1 + 1, v0 + 1)]
    m = fix_winding(Mesh(pos, nr, uv, idx))
    if caps:
        m = merge([m, disk_cap(pts[0], -tang[0], r, segs), disk_cap(pts[-1], tang[-1], r, segs)])
    return m


def place(m: Mesh, *mats) -> Mesh:
    return m.transformed(chain(*mats))


# --------------------------------------------------------------------------------------
# Materials (PBR metallic-roughness)
# --------------------------------------------------------------------------------------
MATERIALS = {
    "Copper":            ((0.72, 0.42, 0.22, 1), 1.0, 0.32),
    "CopperOxidised":    ((0.55, 0.33, 0.20, 1), 0.9, 0.45),
    "AluminiumFin":      ((0.80, 0.82, 0.85, 1), 0.9, 0.42),
    "AluminiumFinBlue":  ((0.62, 0.78, 0.86, 1), 0.7, 0.40),
    "GalvanisedSteel":   ((0.62, 0.64, 0.66, 1), 0.85, 0.50),
    "CompressorPaint":   ((0.10, 0.13, 0.20, 1), 0.25, 0.45),
    "FramePaint":        ((0.11, 0.17, 0.29, 1), 0.30, 0.55),
    "PanelPaint":        ((0.93, 0.92, 0.88, 1), 0.00, 0.80),
    "AluminiumExtrusion":((0.75, 0.77, 0.79, 1), 0.95, 0.35),
    "Insulation":        ((0.05, 0.05, 0.055, 1), 0.0, 0.95),
    "Brass":             ((0.80, 0.62, 0.28, 1), 1.0, 0.30),
    "Rubber":            ((0.04, 0.04, 0.04, 1), 0.0, 0.90),
    "PlasticDark":       ((0.15, 0.16, 0.18, 1), 0.0, 0.55),
    "FanBlade":          ((0.20, 0.22, 0.25, 1), 0.1, 0.45),
    "Chrome":            ((0.90, 0.90, 0.92, 1), 1.0, 0.12),
    "GaugeFace":         ((0.97, 0.97, 0.95, 1), 0.0, 0.60),
    "AccentRed":         ((0.80, 0.18, 0.14, 1), 0.1, 0.50),
    "AccentBlue":        ((0.10, 0.45, 0.75, 1), 0.1, 0.50),
    "DrierPaint":        ((0.12, 0.42, 0.45, 1), 0.3, 0.45),
    "Glass":             ((0.75, 0.90, 0.95, 0.35), 0.0, 0.05),
}

def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


# --------------------------------------------------------------------------------------
# Layout constants
# --------------------------------------------------------------------------------------
BASE_Y = 0.05
COMP = np.array([-0.12, BASE_Y, 0.12])     # compressor footprint centre
COND_X = (-0.53, -0.23)                     # condenser fin pack x-range
EVAP_X = (0.23, 0.53)
COIL_Y0, COIL_PITCH, COIL_ROWS = 0.575, 0.05, 6
COIL_Z = -0.12
TUBE_R = 0.0055

C_IN = (-0.19, 0.575, COIL_Z); C_OUT = (-0.19, 0.325, COIL_Z)
E_IN = (0.19, 0.325, COIL_Z); E_OUT = (0.19, 0.575, COIL_Z)
V_C = np.array([0.11, 0.42, -0.06])         # expansion valve body
V_IN = (0.075, 0.42, -0.06); V_OUT = (0.145, 0.42, -0.06)
R_C = np.array([-0.05, 0.0, -0.20])         # receiver x/z
R_IN = (-0.066, 0.345, -0.20); R_OUT = (-0.034, 0.345, -0.20)
SUCTION_STUB = (0.005, 0.24, 0.12)
DISCHARGE_STUB = (-0.15, 0.405, 0.12)


# --------------------------------------------------------------------------------------
# Component builders: each returns {material: [Mesh,...]} in WORLD space
# --------------------------------------------------------------------------------------
def parts():
    return {}


def add(d, mat, mesh):
    d.setdefault(mat, []).append(mesh)


def build_frame():
    d = parts()
    add(d, "FramePaint", place(box(1.30, 0.04, 0.70), T(0, 0.03, 0)))
    for x in (-0.6, 0.6):
        for z in (-0.3, 0.3):
            add(d, "Rubber", place(cylinder(0.028, 0.012, 20), T(x, 0.0, z)))
    # aluminium uprights + top rail
    for x in (-0.63, 0.63):
        add(d, "AluminiumExtrusion", place(box(0.04, 0.78, 0.04), T(x, BASE_Y + 0.39, -0.33)))
    add(d, "AluminiumExtrusion", place(box(1.30, 0.04, 0.04), T(0, BASE_Y + 0.80, -0.33)))
    # back panel (trainer board)
    add(d, "PanelPaint", place(box(1.22, 0.76, 0.012), T(0, BASE_Y + 0.39, -0.336)))
    # coil mounting brackets
    for x0 in (COND_X, EVAP_X):
        for x in x0:
            for y in (0.29, 0.61):
                add(d, "GalvanisedSteel", place(box(0.02, 0.012, 0.19), T(x, y, -0.235)))
    # receiver bracket
    add(d, "GalvanisedSteel", place(box(0.09, 0.02, 0.12), T(R_C[0], 0.22, -0.27)))
    add(d, "GalvanisedSteel", place(box(0.09, 0.02, 0.12), T(R_C[0], 0.14, -0.27)))
    # valve bracket
    add(d, "GalvanisedSteel", place(box(0.02, 0.02, 0.27), T(V_C[0], 0.39, -0.195)))
    # front edge trim
    add(d, "AluminiumExtrusion", place(box(1.30, 0.012, 0.012), T(0, BASE_Y + 0.006, 0.344)))
    return d


def build_compressor():
    d = parts()
    cx, cy, cz = COMP
    # mounting plate and rubber grommets
    add(d, "GalvanisedSteel", place(box(0.28, 0.012, 0.26), T(cx, cy + 0.006, cz)))
    for dx in (-0.11, 0.11):
        for dz in (-0.10, 0.10):
            add(d, "Rubber", place(cylinder(0.018, 0.022, 16), T(cx + dx, cy + 0.012, cz + dz)))
            add(d, "GalvanisedSteel", place(cylinder(0.006, 0.04, 8), T(cx + dx, cy + 0.012, cz + dz)))
    # base feet tabs
    add(d, "CompressorPaint", place(box(0.26, 0.008, 0.06), T(cx, cy + 0.04, cz - 0.10)))
    add(d, "CompressorPaint", place(box(0.26, 0.008, 0.06), T(cx, cy + 0.04, cz + 0.10)))
    # hermetic shell profile (bottom dome, body, weld seam, top dome)
    R = 0.10; y0 = cy + 0.044
    prof = [(0, y0)]
    for t in np.linspace(math.pi / 2, 0, 8)[1:]:
        prof.append((R * math.cos(t), y0 + 0.05 * (1 - math.sin(t))))
    prof += [(R, y0 + 0.17), (R + 0.006, y0 + 0.175), (R + 0.006, y0 + 0.19), (R, y0 + 0.195)]
    top = y0 + 0.26
    for t in np.linspace(0, math.pi / 2, 9)[1:]:
        prof.append((R * math.cos(t), top + 0.09 * math.sin(t)))
    prof[-1] = (0, top + 0.09)
    add(d, "CompressorPaint", place(lathe(prof, 40), T(cx, 0, cz)))
    # terminal box (front) + cover + cable gland
    tb = np.array([cx, y0 + 0.13, cz + R + 0.02])
    add(d, "PlasticDark", place(box(0.09, 0.07, 0.04), T(*tb)))
    add(d, "PlasticDark", place(box(0.10, 0.08, 0.008), T(tb[0], tb[1], tb[2] + 0.022)))
    add(d, "Rubber", place(cylinder(0.009, 0.03, 12), Rz(math.pi / 2), T(tb[0] + 0.05, tb[1] - 0.015, tb[2])))
    for k, dx in enumerate((-0.025, 0, 0.025)):  # terminal pins visible under cover edge
        add(d, "Brass", place(cylinder(0.004, 0.012, 8), Rx(math.pi / 2), T(tb[0] + dx, tb[1] - 0.04, tb[2] + 0.02)))
    # suction stub (right side), discharge stub (top), process tube
    add(d, "Copper", tube([(cx + R - 0.01, 0.24, cz), SUCTION_STUB], 0.010, 14))
    add(d, "Brass", place(cylinder(0.016, 0.016, 6), Rz(-math.pi / 2), T(cx + R + 0.004, 0.24, cz)))
    add(d, "Copper", tube([(DISCHARGE_STUB[0], top + 0.06, cz), DISCHARGE_STUB], 0.0075, 12))
    add(d, "Copper", tube([(cx + 0.04, top + 0.06, cz - 0.02), (cx + 0.04, top + 0.12, cz - 0.02)], 0.004, 8))
    add(d, "Copper", place(cylinder(0.0065, 0.015, 10, True), T(cx + 0.04, top + 0.105, cz - 0.02)))
    # nameplate (blank – labels are added by the apps)
    add(d, "Chrome", place(box(0.08, 0.04, 0.002), Ry(-0.6), T(cx - 0.065, y0 + 0.12, cz + 0.08)))
    return d


def coil(xr, fin_mat, flip):
    """Finned serpentine coil. xr = (x_min, x_max). flip => inner side at x_min (evaporator)."""
    d = parts()
    x0, x1 = xr
    inner = x0 if flip else x1
    outer = x1 if flip else x0
    sgn_in = -1 if flip else 1  # direction from fin pack towards the inner header
    ys = [COIL_Y0 - i * COIL_PITCH for i in range(COIL_ROWS)]
    # fins
    nf = 36
    for k in range(nf):
        x = x0 + 0.012 + (x1 - x0 - 0.024) * k / (nf - 1)
        add(d, fin_mat, place(box(0.0012, 0.31, 0.065), T(x, 0.45, COIL_Z)))
    # end (tube) sheets
    for x in (x0, x1):
        add(d, "GalvanisedSteel", place(box(0.003, 0.335, 0.075), T(x, 0.45, COIL_Z)))
    # top & bottom channels
    for y in (0.2975, 0.6025):
        add(d, "GalvanisedSteel", place(box(x1 - x0, 0.003, 0.075), T((x0 + x1) / 2, y, COIL_Z)))
    # straight tube runs
    for y in ys:
        add(d, "Copper", tube([(outer - sgn_in * 0.016, y, COIL_Z), (inner + sgn_in * 0.016, y, COIL_Z)], TUBE_R, 10, caps=False))
    # return bends: outer side joins rows (0,1),(2,3),(4,5); inner side joins (1,2),(3,4)
    def bend(xe, yt, yb, outward):
        yc, rb = (yt + yb) / 2, (yt - yb) / 2
        pts = [(xe + outward * rb * math.sin(t), yc + rb * math.cos(t), COIL_Z) for t in np.linspace(0, math.pi, 13)]
        add(d, "Copper", tube(pts, TUBE_R, 10, caps=False))
    for i in (0, 2, 4):
        bend(outer - sgn_in * 0.016, ys[i], ys[i + 1], -sgn_in)
    for i in (1, 3):
        bend(inner + sgn_in * 0.016, ys[i], ys[i + 1], sgn_in)
    # inlet/outlet stubs to the header connections (top + bottom rows, inner side)
    xa = inner + sgn_in * 0.016
    xb = inner + sgn_in * 0.04
    add(d, "Copper", tube([(xa, ys[0], COIL_Z), (xb, ys[0], COIL_Z)], TUBE_R, 10, caps=False))
    add(d, "Copper", tube([(xa, ys[-1], COIL_Z), (xb, ys[-1], COIL_Z)], TUBE_R, 10, caps=False))
    for y in (ys[0], ys[-1]):
        add(d, "Brass", place(cylinder(0.0105, 0.014, 6), Rz(-sgn_in * math.pi / 2), T(xb - sgn_in * 0.004, y, COIL_Z)))
    return d


def build_fan(center, axis_z_sign, R=0.125):
    """Axial fan: returns (static parts, rotor parts, rotor pivot)."""
    st, ro = parts(), parts()
    c = np.asarray(center, float)
    orient = Rx(math.pi / 2) if axis_z_sign > 0 else Rx(-math.pi / 2)  # +Y -> +/-Z
    to_world = lambda m, *ms: place(m, *ms, orient, T(*c))
    # venturi shroud ring
    prof = [(R + 0.012, -0.022), (R + 0.002, -0.018), (R, -0.01), (R, 0.012), (R + 0.006, 0.02), (R + 0.02, 0.022)]
    sh = lathe(prof, 48)
    # make a double-sided shroud by also adding the inner wall offset
    add(st, "PlasticDark", to_world(sh))
    add(st, "PlasticDark", to_world(lathe([(R + 0.02, 0.022), (R + 0.02, 0.026), (R + 0.012, -0.022)], 48)))
    # finger guard: rings + spokes on the outlet side
    for rr in (0.035, 0.07, 0.105):
        add(st, "Chrome", to_world(torus(rr, 0.0018, 40, 6), T(0, -0.03, 0)))
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        add(st, "Chrome", to_world(tube([(0, -0.03, 0), ((R + 0.012) * math.cos(a), -0.03, (R + 0.012) * math.sin(a))], 0.0018, 6)))
    # motor body + mounting arms (on inlet side)
    add(st, "PlasticDark", to_world(cylinder(0.04, 0.05, 24), T(0, 0.022, 0)))
    add(st, "GalvanisedSteel", to_world(cylinder(0.03, 0.01, 24), T(0, 0.072, 0)))
    for k in range(3):
        a = k * 2 * math.pi / 3
        add(st, "GalvanisedSteel", to_world(tube([(0.035 * math.cos(a), 0.045, 0.035 * math.sin(a)),
                                                  ((R + 0.008) * math.cos(a), 0.015, (R + 0.008) * math.sin(a))], 0.003, 6)))
    # rotor: hub + 5 twisted blades, modelled around the pivot (local origin)
    hub = lathe([(0, -0.02), (0.022, -0.016), (0.03, -0.006), (0.03, 0.012), (0.03, 0.012), (0, 0.012)], 24)
    rot_local = []
    rot_local.append(("FanBlade", hub))
    for k in range(5):
        a = k * 2 * math.pi / 5
        for s in range(4):  # 4 slices per blade with increasing twist => curved blade
            r0 = 0.03 + s * 0.022
            twist = 0.55 - s * 0.08
            blade = place(box(0.024, 0.0025, 0.05 - s * 0.004), Rz(twist), T(r0 + 0.011, 0, 0), Ry(a))
            rot_local.append(("FanBlade", blade))
    for mat, m in rot_local:
        add(ro, mat, place(m, orient))  # rotor stays local to pivot
    return st, ro, c


def build_condenser():
    return coil(COND_X, "AluminiumFin", flip=False)


def build_evaporator():
    return coil(EVAP_X, "AluminiumFinBlue", flip=True)


def build_receiver():
    d = parts()
    x, _, z = R_C
    r = 0.035; y0, y1 = 0.12, 0.31
    prof = [(0, y0)] + [(r * math.sin(t), y0 + 0.02 * (1 - math.sin(t))) for t in np.linspace(0.2, math.pi / 2, 6)]
    prof += [(r, y1 - 0.02)] + [(r * math.cos(t), y1 - 0.02 + 0.02 * math.sin(t)) for t in np.linspace(0.3, math.pi / 2 - 0.2, 5)] + [(0, y1)]
    add(d, "DrierPaint", place(lathe(prof, 32), T(x, 0, z)))
    for yy in (0.14, 0.22):  # clamp bands
        add(d, "GalvanisedSteel", place(torus(r + 0.002, 0.003, 32, 6), T(x, yy, z)))
    for p in (R_IN, R_OUT):
        add(d, "Copper", tube([(p[0], y1 - 0.01, z), p], 0.0055, 10))
        add(d, "Brass", place(cylinder(0.01, 0.014, 6), T(p[0], p[1] - 0.02, z)))
    # sight glass on top (illustrative)
    add(d, "Brass", place(cylinder(0.012, 0.012, 16), Rx(math.pi / 2), T(x, 0.27, z + r - 0.004)))
    add(d, "Glass", place(cylinder(0.008, 0.004, 16), Rx(math.pi / 2), T(x, 0.27, z + r + 0.008)))
    return d


def build_expansion_valve():
    d = parts()
    c = V_C
    # hexagonal brass body
    add(d, "Brass", place(cylinder(0.022, 0.05, 6), T(c[0], c[1] - 0.025, c[2])))
    # inlet / outlet flare fittings
    for p, sgn in ((V_IN, -1), (V_OUT, 1)):
        add(d, "Brass", place(cylinder(0.012, 0.022, 6), Rz(-sgn * math.pi / 2), T(c[0] + sgn * 0.018, c[1], c[2])))
    # power head (diaphragm) on top
    head = lathe([(0, c[1] + 0.025), (0.008, c[1] + 0.025), (0.008, c[1] + 0.035), (0.03, c[1] + 0.035),
                  (0.03, c[1] + 0.035), (0.031, c[1] + 0.04), (0.028, c[1] + 0.05), (0.012, c[1] + 0.058), (0, c[1] + 0.06)], 32)
    add(d, "Chrome", place(head, T(c[0], 0, c[2])))
    # adjustment cap (bottom)
    add(d, "Brass", place(cylinder(0.01, 0.02, 12), T(c[0], c[1] - 0.045, c[2])))
    # capillary from power head to remote sensing bulb on the suction line
    bulb = np.array([0.105, 0.575 + 0.016 + 0.009, COIL_Z])
    cap_pts = [(c[0], c[1] + 0.06, c[2]), (c[0], c[1] + 0.10, c[2]), (c[0] - 0.02, c[1] + 0.12, c[2] - 0.03),
               (c[0] - 0.03, 0.62, COIL_Z), (bulb[0] - 0.03, bulb[1], bulb[2])]
    add(d, "Copper", tube(cap_pts, 0.0016, 6, bend=0.02))
    add(d, "Copper", tube([(bulb[0] - 0.03, bulb[1], bulb[2]), (bulb[0] + 0.03, bulb[1], bulb[2])], 0.008, 12))
    for dx in (-0.015, 0.015):  # bulb strap clamps
        add(d, "GalvanisedSteel", place(torus(0.0255, 0.0018, 24, 4), Rz(math.pi / 2), T(bulb[0] + dx, 0.575, COIL_Z)))
    # valve outlet tube to evaporator inlet (two-phase feed tube)
    add(d, "Copper", tube([V_OUT, (0.165, 0.42, -0.06), (0.165, 0.325, -0.06), (0.165, 0.325, COIL_Z), E_IN], 0.0055, 10, bend=0.02))
    return d


def build_discharge_line():
    d = parts()
    pts = [DISCHARGE_STUB, (-0.15, 0.66, 0.12), (-0.15, 0.66, COIL_Z), (-0.15, 0.575, COIL_Z), C_IN]
    add(d, "Copper", tube(pts, 0.0075, 12, bend=0.035))
    # vibration-loop style service port (illustrative)
    add(d, "Brass", place(cylinder(0.008, 0.03, 6), T(-0.15, 0.52, 0.12)))
    add(d, "Copper", tube([(-0.15, 0.53, 0.12), (-0.12, 0.53, 0.12)], 0.004, 8))
    return d


def build_liquid_line():
    d = parts()
    a = [C_OUT, (-0.12, 0.325, COIL_Z), (-0.12, 0.38, COIL_Z), (-0.12, 0.38, -0.20), (R_IN[0], 0.38, -0.20), R_IN]
    b = [R_OUT, (R_OUT[0], 0.42, -0.20), (R_OUT[0], 0.42, -0.06), V_IN]
    add(d, "Copper", tube(a, 0.0055, 10, bend=0.025))
    add(d, "Copper", tube(b, 0.0055, 10, bend=0.025))
    return d


def build_suction_line():
    d = parts()
    pts = [E_OUT, (0.06, 0.575, COIL_Z), (0.06, 0.24, COIL_Z), (0.06, 0.24, 0.12), SUCTION_STUB]
    add(d, "Insulation", tube(pts, 0.016, 16, bend=0.05))
    return d


def build_gauges():
    d = parts()
    for x, accent in ((-0.08, "AccentRed"), (0.08, "AccentBlue")):
        c = (x, 0.70, -0.33)
        g = lambda m, *ms: place(m, *ms, Rx(math.pi / 2), T(*c))
        add(d, "Chrome", g(lathe([(0, 0), (0.05, 0), (0.05, 0), (0.05, 0.028), (0.046, 0.032), (0.046, 0.026), (0.046, 0.026), (0, 0.026)], 40)))
        add(d, "GaugeFace", g(cylinder(0.044, 0.002, 40), T(0, 0.0265, 0)))
        add(d, accent, g(torus(0.0395, 0.0016, 40, 4), T(0, 0.029, 0)))
        for k in range(11):  # tick marks only: no invented numbers
            a = math.radians(-135 + 27 * k)
            add(d, "PlasticDark", g(box(0.0016, 0.001, 0.008), T(0, 0.0292, -0.032), Ry(a)))
        add(d, accent, g(box(0.002, 0.001, 0.03), T(0, 0.0298, -0.012), Ry(math.radians(-40))))
        add(d, "PlasticDark", g(cylinder(0.004, 0.003, 12), T(0, 0.029, 0)))
        add(d, "Glass", g(cylinder(0.046, 0.001, 40), T(0, 0.031, 0)))
        add(d, "Brass", place(cylinder(0.006, 0.03, 6), T(x, 0.64, -0.33)))
    return d


# --------------------------------------------------------------------------------------
# Scene assembly
# --------------------------------------------------------------------------------------
COMPONENTS = [
    ("C01_Compressor", "compressor", build_compressor),
    ("C02_Condenser", "condenser", build_condenser),
    ("C03_ExpansionValve", "expansion_valve", build_expansion_valve),
    ("C04_Evaporator", "evaporator", build_evaporator),
    ("C05_ReceiverDrier", "receiver_drier", build_receiver),
    ("C06_CondenserFan", "condenser_fan", None),
    ("C07_EvaporatorFan", "evaporator_fan", None),
    ("C08_DischargeLine", "discharge_line", build_discharge_line),
    ("C09_LiquidLine", "liquid_line", build_liquid_line),
    ("C10_SuctionLine", "suction_line", build_suction_line),
    ("C11_PressureGauges", "pressure_gauges", build_gauges),
    ("C12_Frame", "frame", build_frame),
]


def flow_paths():
    """Conceptual refrigerant route (world space) for flow animation. Not a CFD result."""
    ys = [COIL_Y0 - i * COIL_PITCH for i in range(COIL_ROWS)]

    def serpentine(inner, outer, rows):
        pts = []
        for k, y in enumerate(rows):
            a, b = (inner, outer) if k % 2 == 0 else (outer, inner)
            pts += [(a, y, COIL_Z), (b, y, COIL_Z)]
        return pts

    cx, _, cz = COMP
    segs = [
        ("compressor", "compression", [SUCTION_STUB, (cx, 0.24, cz), (cx, 0.40, cz), DISCHARGE_STUB]),
        ("discharge_line", "hp_vapour", [DISCHARGE_STUB, (-0.15, 0.66, 0.12), (-0.15, 0.66, COIL_Z), (-0.15, 0.575, COIL_Z), C_IN]),
        ("condenser", "hp_condensing", [C_IN] + serpentine(-0.215, -0.545, ys) + [C_OUT]),
        ("liquid_line", "hp_liquid", [C_OUT, (-0.12, 0.325, COIL_Z), (-0.12, 0.38, COIL_Z), (-0.12, 0.38, -0.20), (R_IN[0], 0.38, -0.20), R_IN,
                                      (R_IN[0], 0.16, -0.20), (R_OUT[0], 0.16, -0.20), R_OUT, (R_OUT[0], 0.42, -0.20), (R_OUT[0], 0.42, -0.06), V_IN]),
        ("expansion_valve", "lp_mix", [V_IN, V_OUT, (0.165, 0.42, -0.06), (0.165, 0.325, -0.06), (0.165, 0.325, COIL_Z), E_IN]),
        ("evaporator", "lp_evaporating", [E_IN] + serpentine(0.215, 0.545, ys[::-1]) + [E_OUT]),
        ("suction_line", "lp_vapour", [E_OUT, (0.06, 0.575, COIL_Z), (0.06, 0.24, COIL_Z), (0.06, 0.24, 0.12), SUCTION_STUB]),
    ]
    # "flat" duplicates the points as x,y,z,x,y,z... because Unity's JsonUtility cannot read nested arrays
    return [{"component": c, "state": s, "points": [[round(float(v), 4) for v in p] for p in pts],
             "flat": [round(float(v), 4) for p in pts for v in p]} for c, s, pts in segs]


def bbox_of(groups):
    allp = np.concatenate([m.pos for g in groups for ms in g.values() for m in ms])
    return allp.min(0), allp.max(0)


def build_scene():
    nodes = []  # dicts: name, translation, mesh(dict mat->Mesh), children(list), extras
    for name, cid, fn in COMPONENTS:
        if fn is not None:
            g = fn()
            lo, hi = bbox_of([g])
            ctr = (lo + hi) / 2
            mesh = {m: merge(ms).transformed(T(*-ctr)) for m, ms in g.items()}
            nodes.append(dict(name=name, translation=ctr, mesh=mesh, children=[], extras={"componentId": cid, "selectable": True}))
        else:
            if cid == "condenser_fan":
                st, ro, pivot = build_fan((-0.38, 0.45, -0.215), -1)
            else:
                st, ro, pivot = build_fan((0.38, 0.45, -0.035), +1)
            rot_world = {m: [x.transformed(T(*pivot)) for x in ms] for m, ms in ro.items()}
            lo, hi = bbox_of([st, rot_world])
            ctr = (lo + hi) / 2
            mesh = {m: merge(ms).transformed(T(*-ctr)) for m, ms in st.items()}
            rotor = dict(name=name + "_Rotor", translation=pivot - ctr,
                         mesh={m: merge(ms) for m, ms in ro.items()}, children=[],
                         extras={"componentId": cid, "role": "rotor", "spinAxis": [0, 0, 1]})
            nodes.append(dict(name=name, translation=ctr, mesh=mesh, children=[rotor], extras={"componentId": cid, "selectable": True}))
    return nodes


# --------------------------------------------------------------------------------------
# GLB writer
# --------------------------------------------------------------------------------------
class GLBWriter:
    def __init__(self):
        self.bin = bytearray()
        self.gltf = {"asset": {"version": "2.0", "generator": "VCR-AR-Explorer generate_vcr_glb.py",
                               "copyright": "Original procedural educational model - VCR-AR-Explorer project",
                               "extras": {"disclaimer": "Educational visualisation, not CAD; not dimensionally exact."}},
                     "scene": 0, "scenes": [], "nodes": [], "meshes": [], "materials": [],
                     "accessors": [], "bufferViews": [], "buffers": []}
        self.mat_index = {}
        self.uses_transmission = False

    def material(self, name):
        if name not in self.mat_index:
            col, met, rough = MATERIALS[name]
            # palette is authored in sRGB; glTF baseColorFactor is linear
            lin = [srgb_to_linear(c) for c in col[:3]] + [col[3]]
            m = {"name": name, "pbrMetallicRoughness": {"baseColorFactor": [round(v, 5) for v in lin], "metallicFactor": met, "roughnessFactor": rough}}
            if col[3] < 1:
                m["alphaMode"] = "BLEND"
            if name in ("FanBlade", "PlasticDark", "Glass", "GaugeFace"):
                m["doubleSided"] = True
            self.mat_index[name] = len(self.gltf["materials"])
            self.gltf["materials"].append(m)
        return self.mat_index[name]

    def _view(self, data: bytes, target=None):
        while len(self.bin) % 4: self.bin.append(0)
        bv = {"buffer": 0, "byteOffset": len(self.bin), "byteLength": len(data)}
        if target: bv["target"] = target
        self.bin += data
        self.gltf["bufferViews"].append(bv)
        return len(self.gltf["bufferViews"]) - 1

    def _acc(self, arr, ctype, typ, target, minmax=False):
        v = self._view(arr.tobytes(), target)
        a = {"bufferView": v, "componentType": ctype, "count": int(arr.shape[0]), "type": typ}
        if minmax:
            a["min"] = arr.min(0).astype(float).tolist(); a["max"] = arr.max(0).astype(float).tolist()
        self.gltf["accessors"].append(a)
        return len(self.gltf["accessors"]) - 1

    def mesh(self, name, matmeshes):
        prims = []
        for mat, m in sorted(matmeshes.items()):
            if len(m.idx) == 0: continue
            pos = np.ascontiguousarray(m.pos, np.float32)
            nrm = np.ascontiguousarray(m.nrm, np.float32)
            uv = np.ascontiguousarray(m.uv, np.float32)
            big = len(pos) > 65535
            idx = np.ascontiguousarray(m.idx.reshape(-1), np.uint32 if big else np.uint16)
            prims.append({"attributes": {"POSITION": self._acc(pos, 5126, "VEC3", 34962, True),
                                         "NORMAL": self._acc(nrm, 5126, "VEC3", 34962),
                                         "TEXCOORD_0": self._acc(uv, 5126, "VEC2", 34962)},
                          "indices": self._acc(idx.reshape(-1, 1) if False else idx, 5125 if big else 5123, "SCALAR", 34963),
                          "material": self.material(mat)})
        self.gltf["meshes"].append({"name": name, "primitives": prims})
        return len(self.gltf["meshes"]) - 1

    def node(self, n):
        children = [self.node(c) for c in n["children"]]
        nd = {"name": n["name"], "translation": [float(x) for x in n["translation"]], "extras": n["extras"]}
        if n["mesh"]:
            nd["mesh"] = self.mesh(n["name"] + "_Mesh", n["mesh"])
        if children: nd["children"] = children
        self.gltf["nodes"].append(nd)
        return len(self.gltf["nodes"]) - 1

    def write(self, roots, path):
        root = {"name": "VCR_System", "translation": [0, 0, 0], "mesh": None, "children": roots,
                "extras": {"description": "Vapour compression refrigeration demonstration rig (educational)"}}
        r = self.node(root)
        self.gltf["scenes"].append({"name": "VCR_Scene", "nodes": [r]})
        while len(self.bin) % 4: self.bin.append(0)
        self.gltf["buffers"].append({"byteLength": len(self.bin)})
        js = json.dumps(self.gltf, separators=(",", ":")).encode()
        while len(js) % 4: js += b" "
        total = 12 + 8 + len(js) + 8 + len(self.bin)
        with open(path, "wb") as f:
            f.write(struct.pack("<III", 0x46546C67, 2, total))
            f.write(struct.pack("<II", len(js), 0x4E4F534A)); f.write(js)
            f.write(struct.pack("<II", len(self.bin), 0x004E4942)); f.write(self.bin)
        return total


def main(argv):
    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(here, "output", "VCR_System.glb")
    copies = []
    args = list(argv)
    while args:
        a = args.pop(0)
        if a == "--copy-to": copies.append(args.pop(0))
        else: out = a
    os.makedirs(os.path.dirname(out), exist_ok=True)
    nodes = build_scene()
    w = GLBWriter()
    size = w.write(nodes, out)
    tris = sum(len(m.idx) for n in nodes for m in list(n["mesh"].values()) + [mm for c in n["children"] for mm in c["mesh"].values()])
    report = {"file": out, "bytes": size, "triangles": int(tris),
              "components": [{"node": n["name"], "id": n["extras"]["componentId"],
                              "centre": [round(float(v), 4) for v in n["translation"]]} for n in nodes]}
    with open(os.path.join(os.path.dirname(out), "model_report.json"), "w") as f:
        json.dump(report, f, indent=2)
    # Exploded variant for AR viewers that cannot move nodes (offsets come from the shared data file;
    # refrigerant lines are left out because they would no longer connect anything).
    with open(os.path.join(here, "..", "data", "components.json"), encoding="utf-8") as f:
        cdata = {c["node"]: c for c in json.load(f)["components"]}
    exploded = []
    for n in nodes:
        c = cdata[n["name"]]
        if c.get("line"): continue
        exploded.append(dict(n, translation=np.asarray(n["translation"]) + np.asarray(c["explode"])))
    out_x = out.replace(".glb", "_Exploded.glb")
    GLBWriter().write(exploded, out_x)

    # Hotspot anchors (top-centre of each component's bounds) for both variants
    def anchors(ns):
        res = {}
        for n in ns:
            pts = [m.pos + n["translation"] for m in n["mesh"].values()]
            pts += [m.pos + n["translation"] + c["translation"] for c in n["children"] for m in c["mesh"].values()]
            p = np.concatenate(pts); lo, hi = p.min(0), p.max(0)
            res[n["extras"]["componentId"]] = {"top": [round(float((lo[0] + hi[0]) / 2), 4), round(float(hi[1]), 4), round(float((lo[2] + hi[2]) / 2), 4)],
                                               "centre": [round(float(v), 4) for v in (lo + hi) / 2]}
        return res
    with open(os.path.join(here, "..", "data", "anchors.json"), "w") as f:
        json.dump({"assembled": anchors(nodes), "exploded": anchors(exploded)}, f, indent=1)

    flow = {"note":"Conceptual refrigerant route for animation only; not a CFD or thermodynamic simulation.",
            "segments": flow_paths()}
    with open(os.path.join(here, "..", "data", "flow_paths.json"), "w") as f:
        json.dump(flow, f, indent=1)
    if not copies:  # default: refresh the copies used by the web app and the Unity project
        root = os.path.join(here, "..")
        for fn in ("components.json", "flow_paths.json", "anchors.json"):  # keep Unity's copy of the shared data in sync
            dst = os.path.join(root, "Unity", "VCR-AR-Explorer", "Assets", "Data")
            if os.path.isdir(dst): shutil.copyfile(os.path.join(root, "data", fn), os.path.join(dst, fn))
        copies = [os.path.join(root, "web", "public", "models", "VCR_System.glb"),
                  os.path.join(root, "Unity", "VCR-AR-Explorer", "Assets", "Models", "VCR_System.glb")]
    for c in copies:
        os.makedirs(os.path.dirname(c), exist_ok=True); shutil.copyfile(out, c)
        shutil.copyfile(out_x, c.replace(".glb", "_Exploded.glb"))
    print(json.dumps({k: report[k] for k in ("file", "bytes", "triangles")}))


if __name__ == "__main__":
    main(sys.argv[1:])
