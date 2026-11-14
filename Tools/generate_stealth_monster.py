import math
from pathlib import Path


OUT = Path(__file__).resolve().parents[1] / "Assets" / "3.Model" / "StealthMonster_Prototype.obj"
MTL = OUT.with_suffix(".mtl")

verts = []
faces = []
materials = []
parts = []


def ellipsoid(name, center, scale, material, rings=20, sides=28, lean=0.0):
    start = len(verts) + 1
    face_start = len(faces)
    cx, cy, cz = center
    sx, sy, sz = scale
    for r in range(rings + 1):
        phi = math.pi * r / rings
        y = math.cos(phi)
        radius = math.sin(phi)
        for s in range(sides):
            theta = 2 * math.pi * s / sides
            x = radius * math.cos(theta)
            z = radius * math.sin(theta)
            verts.append((cx + sx * x + lean * (cy + sy * y), cy + sy * y, cz + sz * z))
    materials.append((name, material))
    for r in range(rings):
        for s in range(sides):
            a = start + r * sides + s
            b = start + r * sides + (s + 1) % sides
            c = start + (r + 1) * sides + (s + 1) % sides
            d = start + (r + 1) * sides + s
            faces.extend(((a, b, c), (a, c, d)))
    parts.append((name, material, face_start, len(faces)))


def tapered_limb(name, a, b, ra, rb, material, sides=14):
    start = len(verts) + 1
    face_start = len(faces)
    ax, ay, az = a
    bx, by, bz = b
    direction = (bx - ax, by - ay, bz - az)
    length = math.sqrt(sum(v * v for v in direction))
    direction = tuple(v / length for v in direction)
    ref = (0, 0, 1) if abs(direction[1]) > 0.9 else (0, 1, 0)
    u = (direction[1] * ref[2] - direction[2] * ref[1], direction[2] * ref[0] - direction[0] * ref[2], direction[0] * ref[1] - direction[1] * ref[0])
    ul = math.sqrt(sum(v * v for v in u))
    u = tuple(v / ul for v in u)
    v = (direction[1] * u[2] - direction[2] * u[1], direction[2] * u[0] - direction[0] * u[2], direction[0] * u[1] - direction[1] * u[0])
    for point, radius in ((a, ra), (b, rb)):
        for i in range(sides):
            angle = 2 * math.pi * i / sides
            verts.append(tuple(point[k] + radius * (math.cos(angle) * u[k] + math.sin(angle) * v[k]) for k in range(3)))
    materials.append((name, material))
    for i in range(sides):
        p = start + i
        q = start + (i + 1) % sides
        faces.append((p, q, q + sides))
        faces.append((p, q + sides, p + sides))
    faces.append(tuple(start + i for i in reversed(range(sides))))
    faces.append(tuple(start + sides + i for i in range(sides)))
    parts.append((name, material, face_start, len(faces)))


# Model height: 2.35 m. The long arms, hunched thorax, and digitigrade stance
# keep a readable stealth-predator silhouette at gameplay distance.
ellipsoid("Torso", (0, 1.30, 0.02), (0.31, 0.57, 0.235), "VoidFlesh", lean=-0.11)
ellipsoid("AbdominalCore", (0, 1.27, 0.207), (0.205, 0.39, 0.055), "ColdArmor", lean=-0.08)
ellipsoid("ChestMantle", (0, 1.58, 0.025), (0.43, 0.33, 0.30), "VoidFlesh", lean=-0.08)
ellipsoid("BreastPlate", (0, 1.56, 0.270), (0.28, 0.205, 0.065), "ColdArmor", lean=-0.06)
ellipsoid("Pelvis", (0, 0.88, 0.02), (0.27, 0.27, 0.21), "VoidFlesh")
ellipsoid("Neck", (0, 1.86, -0.015), (0.155, 0.25, 0.16), "VoidFlesh", lean=-0.13)
ellipsoid("Head", (0, 2.08, -0.065), (0.155, 0.30, 0.16), "VoidFlesh", lean=0.24)
ellipsoid("CranialCrest", (0, 2.27, -0.10), (0.12, 0.18, 0.13), "ColdArmor", lean=0.16)
ellipsoid("FaceSlit", (0, 2.10, 0.087), (0.014, 0.15, 0.012), "ColdGlow", rings=14, sides=12)
ellipsoid("SpineGlow", (0, 1.48, -0.213), (0.024, 0.48, 0.013), "ColdGlow", rings=16, sides=12)

# Asymmetric shoulders and long arms.
for side, shoulder, elbow, wrist in (
    (-1, (-0.31, 1.72, 0.00), (-0.48, 1.25, 0.02), (-0.56, 0.66, 0.13)),
    (1, (0.31, 1.72, 0.00), (0.49, 1.28, -0.02), (0.62, 0.72, 0.10)),
):
    ellipsoid("Shoulder", shoulder, (0.22, 0.25, 0.205), "VoidFlesh")
    ellipsoid("ShoulderShell", (shoulder[0], shoulder[1] + 0.08, shoulder[2] - 0.075), (0.22, 0.14, 0.18), "ColdArmor", lean=side * 0.18)
    tapered_limb("UpperArm", shoulder, elbow, 0.15, 0.105, "VoidFlesh")
    ellipsoid("Elbow", elbow, (0.125, 0.13, 0.12), "VoidFlesh")
    tapered_limb("Forearm", elbow, wrist, 0.12, 0.072, "VoidFlesh", sides=18)
    ellipsoid("ForearmRidge", (elbow[0] * 0.45 + wrist[0] * 0.55, elbow[1] * 0.45 + wrist[1] * 0.55, 0.105), (0.035, 0.27, 0.025), "ColdGlow", rings=16, sides=10)
    for finger in range(3):
        spread = (finger - 1) * 0.075
        base = (wrist[0] + spread, wrist[1] - 0.02, wrist[2] + 0.03)
        tip = (base[0] + spread * 0.45, base[1] - 0.20 - (0.025 if finger == 1 else 0), base[2] + 0.02)
        tapered_limb("HookedClaw", base, tip, 0.034, 0.003, "ObsidianClaw", sides=10)
    thumb = (wrist[0] - side * 0.07, wrist[1] + 0.01, wrist[2] + 0.02)
    tapered_limb("ThumbClaw", thumb, (thumb[0] - side * 0.11, thumb[1] - 0.12, thumb[2] + 0.025), 0.03, 0.003, "ObsidianClaw", sides=10)

# Digitigrade legs and wide, gripping feet.
for side in (-1, 1):
    hip = (side * 0.16, 0.86, 0.01)
    knee = (side * 0.23, 0.47, -0.025)
    hock = (side * 0.20, 0.20, -0.06)
    ankle = (side * 0.22, 0.12, 0.055)
    ellipsoid("Hip", hip, (0.19, 0.24, 0.18), "VoidFlesh")
    tapered_limb("Thigh", hip, knee, 0.17, 0.11, "VoidFlesh", sides=18)
    ellipsoid("Knee", knee, (0.115, 0.125, 0.12), "VoidFlesh")
    ellipsoid("KneeShell", (side * 0.23, 0.49, 0.075), (0.09, 0.095, 0.045), "ColdArmor")
    tapered_limb("Shin", knee, hock, 0.10, 0.06, "VoidFlesh", sides=16)
    tapered_limb("Hock", hock, ankle, 0.062, 0.067, "VoidFlesh", sides=16)
    ellipsoid("Foot", (side * 0.22, 0.075, 0.18), (0.115, 0.085, 0.23), "VoidFlesh")
    for toe in range(3):
        spread = (toe - 1) * 0.07
        base = (side * 0.22 + spread, 0.055, 0.28)
        tapered_limb("FootClaw", base, (base[0] + spread * 0.3, 0.025, 0.42), 0.032, 0.002, "ObsidianClaw", sides=9)

# Short swept dorsal spines, staggered to make its profile less humanoid.
for i, y in enumerate((1.42, 1.60, 1.77, 1.91, 2.02)):
    side = -1 if i % 2 == 0 else 1
    base = (0.0, y, -0.18)
    tip = (side * (0.17 + 0.028 * i), y + 0.15, -0.43)
    tapered_limb("DorsalSpine", base, tip, 0.09, 0.001, "ObsidianClaw", sides=10)

OUT.parent.mkdir(parents=True, exist_ok=True)
with OUT.open("w", encoding="utf-8", newline="\n") as obj:
    obj.write("mtllib StealthMonster_Prototype.mtl\n")
    obj.write("o StealthMonster_Prototype\n")
    for x, y, z in verts:
        obj.write(f"v {x:.6f} {y:.6f} {z:.6f}\n")
    for name, material, first, last in parts:
        obj.write(f"g {name}\nusemtl {material}\ns 1\n")
        for face in faces[first:last]:
            obj.write("f " + " ".join(str(n) for n in face) + "\n")

MTL.write_text("""newmtl VoidFlesh\nKd 0.055 0.075 0.082\nKs 0.16 0.20 0.22\nNs 42\n\nnewmtl ColdGlow\nKd 0.04 0.62 0.72\nKa 0.01 0.08 0.10\nKs 0.35 0.8 0.9\nNs 90\n\nnewmtl ColdArmor\nKd 0.075 0.16 0.19\nKs 0.28 0.42 0.46\nNs 58\n\nnewmtl ObsidianClaw\nKd 0.018 0.022 0.028\nKs 0.25 0.30 0.34\nNs 70\n""", encoding="utf-8")
print(f"Wrote {OUT} ({len(verts)} vertices, {len(faces)} triangles)")
