"""
Trains the rune classifier and exports weights as C# source.

The runtime feature extraction (RuneFeatures.cs) must mirror this script
exactly — see `extract_features`.

Usage:
    python tools/train_rune_model.py

Outputs:
    scripts/RuneModelWeights.g.cs  (committed; the game compiles it in)

Feature vector (100 dims), invariant to position, scale and rotation:
    - combine all strokes, resample to 48 points (arc-length proportional)
    - smooth (2 moving-average passes)
    - normalize: centroid at origin, RMS radius = 1, rotate onto the PCA axis
      (axis sign resolved by radius-weighted x skew); emit two mirror variants
    - append 4 structural features: eigenvalue ratio (thinness),
      path length / RMS, corner fraction, sharpest turn / pi
"""

from __future__ import annotations

import math
import random

import numpy as np

TOTAL_POINTS = 48
INPUT_SIZE = TOTAL_POINTS * 2 + 6
HIDDEN_SIZE = 160
OUTPUT_NAMES = ["unknown", "fire", "water", "density"]

SEED = 1234
SAMPLES_PER_CLASS = 8000
EPOCHS = 120
BATCH_SIZE = 64
LEARNING_RATE = 0.01


# ---------------------------------------------------------------- geometry

def resample_strokes(strokes: list[list[tuple[float, float]]], total_points: int) -> list[list[tuple[float, float]]]:
    """Mirror of RuneRecognizer.ResampleStrokes / RuneFeatures.ResampleStrokes."""
    lengths = []
    total_length = 0.0
    for stroke in strokes:
        length = sum(math.dist(stroke[i], stroke[i - 1]) for i in range(1, len(stroke)))
        lengths.append(length)
        total_length += length

    if not strokes:
        return []
    if total_length <= 0:
        return [list(s) for s in strokes]

    resampled = []
    assigned = 0
    for s, stroke in enumerate(strokes):
        if s == len(strokes) - 1:
            count = total_points - assigned
        else:
            count = max(2, int(round(lengths[s] / total_length * total_points)))
        count = min(count, total_points - assigned)
        if count < 1:
            break
        assigned += count
        resampled.append(resample_polyline(stroke, count))
    return resampled


def resample_polyline(stroke: list[tuple[float, float]], count: int) -> list[tuple[float, float]]:
    if count <= 1 or len(stroke) == 1:
        return [stroke[0]]

    cumulative = [0.0]
    for i in range(1, len(stroke)):
        cumulative.append(cumulative[-1] + math.dist(stroke[i], stroke[i - 1]))
    total = cumulative[-1]
    if total <= 0:
        return [stroke[0]] * count

    result = []
    segment = 0
    for k in range(count):
        target = total * k / (count - 1)
        while segment < len(stroke) - 2 and cumulative[segment + 1] < target:
            segment += 1
        seg_len = cumulative[segment + 1] - cumulative[segment]
        t = 0.0 if seg_len <= 0 else (target - cumulative[segment]) / seg_len
        t = min(max(t, 0.0), 1.0)
        x = stroke[segment][0] + (stroke[segment + 1][0] - stroke[segment][0]) * t
        y = stroke[segment][1] + (stroke[segment + 1][1] - stroke[segment][1]) * t
        result.append((x, y))
    result[-1] = stroke[-1]
    return result


def smooth_strokes(strokes: list[list[tuple[float, float]]], passes: int = 2):
    current = [list(s) for s in strokes]
    for _ in range(passes):
        nxt = []
        for stroke in current:
            if len(stroke) < 3:
                nxt.append(stroke)
                continue
            smoothed = [((stroke[0][0] * 2 + stroke[1][0]) / 3, (stroke[0][1] * 2 + stroke[1][1]) / 3)]
            for i in range(1, len(stroke) - 1):
                x = (stroke[i - 1][0] + stroke[i][0] * 2 + stroke[i + 1][0]) * 0.25
                y = (stroke[i - 1][1] + stroke[i][1] * 2 + stroke[i + 1][1]) * 0.25
                smoothed.append((x, y))
            smoothed.append(((stroke[-2][0] + stroke[-1][0] * 2) / 3, (stroke[-2][1] + stroke[-1][1] * 2) / 3))
            nxt.append(smoothed)
        current = nxt
    return current


def canonicalize(points):
    """Rotates points so the principal (PCA) axis is horizontal; returns the
    two mirror variants (axis sign is ambiguous)."""
    n = len(points)
    cx = sum(p[0] for p in points) / n
    cy = sum(p[1] for p in points) / n
    sq = sum((p[0] - cx) ** 2 + (p[1] - cy) ** 2 for p in points)
    rms = math.sqrt(sq / n)
    if rms < 1e-4:
        return [np.zeros((n, 2), dtype=np.float32)] * 2

    centered = [((p[0] - cx) / rms, (p[1] - cy) / rms) for p in points]
    cov_xx = sum(x * x for x, _ in centered) / n
    cov_xy = sum(x * y for x, y in centered) / n
    cov_yy = sum(y * y for _, y in centered) / n
    theta = 0.5 * math.atan2(2 * cov_xy, cov_xx - cov_yy)
    cos, sin = math.cos(-theta), math.sin(-theta)

    rotated = [(x * cos - y * sin, x * sin + y * cos) for x, y in centered]

    # The PCA axis has no sign: resolve the 180° ambiguity deterministically so
    # features are stable under rotation. Use radius-weighted x skew.
    skew = sum(x * math.hypot(x, y) for x, y in rotated)
    if skew < 0:
        rotated = [(-x, -y) for x, y in rotated]

    plus = np.array(rotated, dtype=np.float32)
    minus = plus * np.array([-1.0, 1.0], dtype=np.float32)
    return [plus, minus]


def structural_features(points):
    """Shape statistics, all invariant to position/scale/rotation:
    eigenvalue ratio (thinness), path length / RMS, corner fraction, sharpest turn."""
    n = len(points)
    cx = sum(p[0] for p in points) / n
    cy = sum(p[1] for p in points) / n
    sq = sum((p[0] - cx) ** 2 + (p[1] - cy) ** 2 for p in points)
    rms = math.sqrt(sq / n) if n else 0.0
    path = sum(math.dist(points[i], points[i - 1]) for i in range(1, n))

    if rms < 1e-4:
        return 0.0, 0.0, 0.0, 0.0

    cov_xx = sum((p[0] - cx) ** 2 for p in points) / n
    cov_xy = sum((p[0] - cx) * (p[1] - cy) for p in points) / n
    cov_yy = sum((p[1] - cy) ** 2 for p in points) / n
    trace = cov_xx + cov_yy
    det = cov_xx * cov_yy - cov_xy * cov_xy
    disc = math.sqrt(max(trace * trace / 4 - det, 0.0))
    major = trace / 2 + disc
    minor = max(trace / 2 - disc, 0.0)
    thinness = minor / major if major > 1e-9 else 0.0

    segments = []
    for i in range(1, n):
        dx = points[i][0] - points[i - 1][0]
        dy = points[i][1] - points[i - 1][1]
        if math.hypot(dx, dy) > 1e-6:
            segments.append((dx, dy))

    corners = 0
    max_turn = 0.0
    for i in range(1, len(segments)):
        ax, ay = segments[i - 1]
        bx, by = segments[i]
        turn = abs(math.atan2(ax * by - ay * bx, ax * bx + ay * by))
        max_turn = max(max_turn, turn)
        if turn > math.radians(60):
            corners += 1

    corner_frac = corners / len(segments) if segments else 0.0
    return thinness, path / rms, corner_frac, max_turn / math.pi


def stroke_shape_features(strokes, points):
    """Stroke topology, invariant to position/scale/rotation:
    stroke count (normalized) and mean gap between consecutive strokes."""
    n = len(strokes)
    if len(points) == 0:
        return 0.0, 0.0

    cx = sum(p[0] for p in points) / len(points)
    cy = sum(p[1] for p in points) / len(points)
    rms = math.sqrt(sum((p[0] - cx) ** 2 + (p[1] - cy) ** 2 for p in points) / len(points))
    if rms < 1e-4:
        return 0.0, 0.0

    gaps = [math.dist(strokes[i - 1][-1], strokes[i][0]) for i in range(1, n)]
    mean_gap = sum(gaps) / len(gaps) if gaps else 0.0
    return n / 5.0, mean_gap / rms


def extract_features(strokes: list[list[tuple[float, float]]]) -> tuple[np.ndarray, np.ndarray]:
    """Mirror of RuneFeatures.Extract in C#. Returns both mirror variants."""
    resampled = resample_strokes(strokes, TOTAL_POINTS)
    smoothed = smooth_strokes(resampled, 2)
    points = [p for s in smoothed for p in s]
    if not points:
        zero = np.zeros(INPUT_SIZE, dtype=np.float32)
        return zero, zero
    variants = canonicalize(points)
    extra = np.array(structural_features(points) + stroke_shape_features(resampled, points), dtype=np.float32)

    out = []
    for v in variants:
        flat = v.reshape(-1).copy()
        out.append(np.concatenate([flat, extra]))
    return out[0], out[1]


# ---------------------------------------------------------------- shapes

FIRE = [
    [(-0.75, 0.85), (-0.60, 0.45), (-0.40, -0.05), (-0.15, -0.60), (0.02, -0.98),
     (0.25, -0.45), (0.45, 0.10), (0.62, 0.50), (0.80, 0.90)],
]

WATER = [
    [(-1.00, 0.15), (-0.85, -0.10), (-0.65, -0.32), (-0.45, -0.42), (-0.25, -0.35),
     (-0.05, -0.12), (0.15, 0.08), (0.35, 0.15), (0.55, 0.08), (0.75, -0.12), (1.00, -0.42)],
    [(-0.95, 0.85), (-0.80, 0.60), (-0.60, 0.40), (-0.42, 0.32), (-0.22, 0.38),
     (0.00, 0.60), (0.20, 0.78), (0.42, 0.82), (0.62, 0.72), (0.82, 0.50), (1.02, 0.22)],
]

DENSITY = [
    [(-0.75, -0.20), (-0.35, -0.85), (-0.05, -0.35), (0.25, -0.90), (0.55, -0.20)],
    [(-0.85, 0.06), (0.00, 0.02), (0.85, 0.06)],
    [(-0.35, 0.55), (0.05, 1.05), (0.50, 0.50)],
]

# label index -> rune template
RUNES = {1: FIRE, 2: WATER, 3: DENSITY}


def circle():
    top = [(math.cos(a) * 100, math.sin(a) * 100) for a in np.linspace(math.pi, 2 * math.pi, 13)]
    bottom = [(math.cos(a) * 100, math.sin(a) * 100) for a in np.linspace(0, math.pi, 13)]
    return [top, bottom]


def square():
    pts = [(-100, -100), (100, -100), (100, 100), (-100, 100), (-100, -100)]
    return [pts]


def triangle():
    return [[(0, -120), (110, 90), (-110, 90), (0, -120)]]


def line():
    return [[(-120, -80), (120, 80)]]


def sine():
    return [[(x, math.sin(x / 120 * 2 * math.pi) * 60) for x in np.linspace(-120, 120, 30)]]


def zigzag():
    return [[(-120, 60), (-60, -60), (0, 60), (60, -60), (120, 60)]]


def a_letter():
    return [[(-90, 90), (0, -100), (90, 90)], [(-45, 20), (45, 20)]]


def m_letter():
    return [[(-110, 90), (-100, -100), (0, 20), (100, -100), (110, 90)]]


def random_walk(rng):
    """Continuous, human-like scribble: smooth wandering with small steps."""
    heading = rng.uniform(0, 2 * math.pi)
    x, y = 0.0, 0.0
    pts = [(x, y)]
    for _ in range(rng.randint(40, 90)):
        heading += rng.gauss(0, 0.25)
        x += math.cos(heading) * rng.uniform(6, 14)
        y += math.sin(heading) * rng.uniform(6, 14)
        pts.append((x, y))
    return [pts]


def spiral(_rng=None):
    turns = 1.5
    pts = []
    for i in range(60):
        a = i / 59 * turns * 2 * math.pi
        r = 10 + i / 59 * 110
        pts.append((math.cos(a) * r, math.sin(a) * r))
    return [pts]


def s_curve():
    return [[(x, 70 * math.sin(x / 120 * math.pi)) for x in np.linspace(-130, 130, 28)]]


def loopy():
    return [[(math.cos(a) * (60 + 40 * math.sin(3 * a)), math.sin(a) * (60 + 40 * math.sin(3 * a)))
             for a in np.linspace(0, 2 * math.pi, 34)]]


UNKNOWN_SHAPES = [circle, square, triangle, line, sine, zigzag, a_letter, m_letter, spiral, s_curve, loopy]


# ---------------------------------------------------------------- augmentation

def densify(stroke, max_seg=20.0):
    """Add intermediate points like real mouse input."""
    out = [stroke[0]]
    for i in range(1, len(stroke)):
        a, b = stroke[i - 1], stroke[i]
        dist = math.dist(a, b)
        steps = max(1, int(dist / max_seg))
        for t in range(1, steps + 1):
            out.append((a[0] + (b[0] - a[0]) * t / steps, a[1] + (b[1] - a[1]) * t / steps))
    return out


def augment(strokes, rng):
    angle = rng.uniform(0, 2 * math.pi)
    scale = rng.uniform(0.4, 2.5)
    ox, oy = rng.uniform(-500, 500), rng.uniform(-500, 500)
    cos, sin = math.cos(angle), math.sin(angle)
    jitter = rng.uniform(0.0, 0.05) * scale * 100  # px, relative to shape size

    out = []
    for stroke in strokes:
        dense = densify(stroke)
        transformed = []
        for x, y in dense:
            rx, ry = x * cos - y * sin, x * sin + y * cos
            transformed.append((
                rx * scale + ox + rng.gauss(0, jitter),
                ry * scale + oy + rng.gauss(0, jitter),
            ))
        out.append(transformed)
    return out


# ---------------------------------------------------------------- model

def make_dataset(rng):
    xs, ys, groups = [], [], []

    def add(strokes, label, group):
        v0, v1 = extract_features(augment(strokes, rng))
        xs.append(v0)
        ys.append(label)
        groups.append(group)
        xs.append(v1)  # mirror variant (PCA axis sign is ambiguous)
        ys.append(label)
        groups.append(group)

    for label, rune in RUNES.items():
        for _ in range(SAMPLES_PER_CLASS):
            add(rune, label, OUTPUT_NAMES[label])
    for _ in range(SAMPLES_PER_CLASS):
        if rng.random() < 0.15:
            add(random_walk(rng), 0, "random_walk")
        else:
            shape_fn = rng.choice(UNKNOWN_SHAPES)
            add(shape_fn(), 0, shape_fn.__name__)
    return np.array(xs), np.array(ys), groups


def train():
    rng = random.Random(SEED)
    np_rng = np.random.default_rng(SEED)
    x, y, groups = make_dataset(rng)
    groups = np.array(groups)

    perm = np_rng.permutation(len(x))
    x, y, groups = x[perm], y[perm], groups[perm]
    split = int(len(x) * 0.85)
    xtr, ytr = x[:split], y[:split]
    xte, yte = x[split:], y[split:]
    gte = groups[split:]

    # Standardize features (z-score) so no single dimension dominates.
    mean = xtr.mean(axis=0).astype(np.float32)
    std = (xtr.std(axis=0) + 1e-6).astype(np.float32)
    xtr = (xtr - mean) / std
    xte = (xte - mean) / std

    w1 = np_rng.normal(0, 1 / math.sqrt(INPUT_SIZE), (HIDDEN_SIZE, INPUT_SIZE)).astype(np.float32)
    b1 = np.zeros(HIDDEN_SIZE, dtype=np.float32)
    w2 = np_rng.normal(0, 1 / math.sqrt(HIDDEN_SIZE), (len(OUTPUT_NAMES), HIDDEN_SIZE)).astype(np.float32)
    b2 = np.zeros(len(OUTPUT_NAMES), dtype=np.float32)
    params = [w1, b1, w2, b2]
    m = [np.zeros_like(p) for p in params]
    v = [np.zeros_like(p) for p in params]
    beta1, beta2, eps = 0.9, 0.999, 1e-8
    step = 0

    for epoch in range(EPOCHS):
        order = np_rng.permutation(len(xtr))
        for start in range(0, len(order), BATCH_SIZE):
            idx = order[start:start + BATCH_SIZE]
            xb, yb = xtr[idx], ytr[idx]

            h = np.maximum(0, xb @ w1.T + b1)
            logits = h @ w2.T + b2
            exp = np.exp(logits - logits.max(axis=1, keepdims=True))
            probs = exp / exp.sum(axis=1, keepdims=True)
            probs[np.arange(len(idx)), yb] -= 1.0
            probs /= len(idx)

            g_w2 = probs.T @ h
            g_b2 = probs.sum(axis=0)
            g_h = (probs @ w2) * (h > 0)
            g_w1 = g_h.T @ xb
            g_b1 = g_h.sum(axis=0)
            grads = [g_w1, g_b1, g_w2, g_b2]

            step += 1
            for p, g, mm, vv in zip(params, grads, m, v):
                mm *= beta1
                mm += (1 - beta1) * g
                vv *= beta2
                vv += (1 - beta2) * g * g
                mhat = mm / (1 - beta1 ** step)
                vhat = vv / (1 - beta2 ** step)
                p -= LEARNING_RATE * mhat / (np.sqrt(vhat) + eps)

        def predict(xs):
            h = np.maximum(0, xs @ w1.T + b1)
            return np.argmax(h @ w2.T + b2, axis=1)

        def accuracy(xs, ys):
            return float((predict(xs) == ys).mean())

        if epoch % 10 == 0 or epoch == EPOCHS - 1:
            print(f"epoch {epoch:3d}  train={accuracy(xtr, ytr):.4f}  test={accuracy(xte, yte):.4f}")

    pred = predict(xte)
    for cls, name in enumerate(OUTPUT_NAMES):
        tp = int(((pred == cls) & (yte == cls)).sum())
        total = int((yte == cls).sum())
        print(f"{name:8s}: recall {tp}/{total} = {tp / max(total, 1):.3f}")

    print("misclassified unknown samples by shape:")
    for group in sorted(set(gte.tolist())):
        mask = (gte == group) & (yte == 0)
        if not mask.any():
            continue
        wrong = int((pred[mask] != 0).sum())
        total = int(mask.sum())
        if wrong:
            print(f"  {group:12s}: {wrong}/{total} called a rune")

    print("confusion for rune samples (wrong -> predicted):")
    for cls, name in enumerate(OUTPUT_NAMES):
        if cls == 0:
            continue
        mask = yte == cls
        if not mask.any():
            continue
        wrong = pred[mask] != cls
        if wrong.any():
            counts = np.bincount(pred[mask][wrong], minlength=len(OUTPUT_NAMES))
            detail = ", ".join(
                f"{OUTPUT_NAMES[i]}:{c}" for i, c in enumerate(counts) if c
            )
            print(f"  {name:6s}: {int(wrong.sum())}/{int(mask.sum())} wrong ({detail})")

    return w1, b1, w2, b2, accuracy(xte, yte), mean, std


def export_weights(w1, b1, w2, b2, accuracy, mean, std):
    """Writes the model to CSV files under resources/rune_model/.

    Files are embedded into the assembly at build time (see the csproj), so the
    game and the tests can read them without Godot's res:// file access.
    """
    import os

    out_dir = "resources/rune_model"
    os.makedirs(out_dir, exist_ok=True)

    def write_matrix(path, data):
        arr = np.atleast_2d(data)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            for row in arr:
                f.write(",".join(f"{float(v):.8g}" for v in row) + "\n")

    def write_column(path, values):
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            for v in values:
                f.write(f"{float(v):.8g}\n")

    parity_points = [p for s in FIRE for p in s]
    parity_stroke_lengths = [len(s) for s in FIRE]
    parity_features = extract_features(FIRE)[0]

    write_matrix(os.path.join(out_dir, "w1.csv"), w1)
    write_column(os.path.join(out_dir, "b1.csv"), b1)
    write_matrix(os.path.join(out_dir, "w2.csv"), w2)
    write_column(os.path.join(out_dir, "b2.csv"), b2)
    write_matrix(os.path.join(out_dir, "feature_mean.csv"), mean.reshape(1, -1))
    write_matrix(os.path.join(out_dir, "feature_std.csv"), std.reshape(1, -1))

    # Parity sample: reference drawing + expected features (guards C# drift).
    write_matrix(os.path.join(out_dir, "parity_points.csv"), parity_points)
    write_matrix(os.path.join(out_dir, "parity_features.csv"), parity_features.reshape(1, -1))

    with open(os.path.join(out_dir, "meta.csv"), "w", encoding="utf-8", newline="\n") as f:
        f.write("key,value\n")
        f.write(f"input_size,{INPUT_SIZE}\n")
        f.write(f"hidden_size,{HIDDEN_SIZE}\n")
        f.write(f"output_size,{len(OUTPUT_NAMES)}\n")
        f.write(f"output_names,{';'.join(OUTPUT_NAMES)}\n")
        f.write(f"parity_stroke_lengths,{';'.join(str(n) for n in parity_stroke_lengths)}\n")
        f.write(f"trained_accuracy,{accuracy:.6f}\n")

    print(f"wrote model CSVs to {out_dir}/")


if __name__ == "__main__":
    w1, b1, w2, b2, acc, mean, std = train()
    np.savez("tools/last_weights.npz", w1=w1, b1=b1, w2=w2, b2=b2, mean=mean, std=std)
    export_weights(w1, b1, w2, b2, acc, mean, std)
