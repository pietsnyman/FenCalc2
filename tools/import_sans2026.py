#!/usr/bin/env python3
"""Generate Data/seed/xa2026.sql from docs/sans.txt (local SANS 10400-XA:2026 extract).

SOURCE: docs/sans.txt — a local, GITIGNORED extract of the registered standard
(licensed to the employer; never commit, never upload). OUTPUT: Data/seed/xa2026.sql —
committed, containing ONLY numeric reference data (table 4 bands, table 3 shading
multipliers, annex C town rows), no standard prose.

Usage:  python tools/import_sans2026.py
Regenerate after topping up docs/sans.txt; the script fails loudly on parse or
range problems rather than shipping junk.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "docs" / "sans.txt"
OUT = ROOT / "Data" / "seed" / "xa2026.sql"

PROVINCES = [
    "Gauteng", "Western Cape", "Eastern Cape", "KwaZulu-Natal", "Limpopo",
    "Mpumalanga", "North West", "Northern Cape", "Free State",
]
PROV_ALT = "|".join(re.escape(p) for p in sorted(PROVINCES, key=len, reverse=True))
ROW_RE = re.compile(
    r"^(?P<name>[A-Za-z][A-Za-z'. -]*?)\s+(?P<prov>" + PROV_ALT + r")"
    r"\s+(?P<lon>\d+\.\d+)\s+(?P<lat>\d+\.\d+)\s+(?P<zone>5H|[1-7])\s+(?P<sccp>Yes|No)\s*$"
)
# A wrapped town name ("King William's" + newline + "Town Eastern Cape ...").
MERGEABLE_RE = re.compile(r"^[A-Za-z][A-Za-z' -]*$")
T3_RE = re.compile(r"^(?:≤|>)\s*(\d+)[º°]\s*([\d.,]+)\s*$")
T4_RE = re.compile(r"^(?:≤|>)\s*(\d+)\s*%\s+(.+)$")

# Forced OCR repairs (each breaks the range check below and is unambiguous against the
# real town coordinates). Anything else suspect is KEPT as-is and flagged in
# docs/plans/xa-2026.md instead of guessed (longitude is display-only; only the
# latitude feeds the shading multiplier).
FIXES = {
    ("Dordrecht", "Eastern Cape"): ("lat", "31.377", "OCR 313.377 -> 31.377"),
    ("Langebaan", "Western Cape"): ("lon", "18.037", "OCR 8.037 -> 18.037"),
}
LAT_RANGE = (22.0, 35.5)
LON_RANGE = (16.0, 33.5)
ALLOWED_ZONES = {"1", "2", "3", "4", "5", "5H", "6", "7"}

INS_PRESETS = "25,30,40,50,75,100,150,200,250"
LEAF_PRESETS = "102,110,140,190,220,230"

# Wall material library (XA:2026 cl. 5.5 support). Numeric values are emitted VERBATIM.
# Provenance — never change a value without changing its source note (also in
# docs/plans/walls.md):
#   films/cavity .... SANS 204:2011 table F.2 (7 m/s outside0,03 · still-air vertical
#                     inside0,12 · non-reflective unventilated vertical cavity0,16)
#   clay brick/plaster Clay Brick Association SA TN01 (ASTM C1363; NBRI/ASHRAE:
#                     clay brickwork0,82 W/mK,1826 kg/m3, c800 J/kgK; cement plaster
#                     0,72 W/mK,1860 kg/m3, c840 J/kgK — brickwork values are AS-BUILT
#                     incl. joints, which is the user-approved nominal-leaf model; the
#                     MAXI format rows reuse the same clay-body values — TN01 notes
#                     conductivity is independent of thickness/format)
#   cement maxi ..... national: bricktileshop "Cement Maxi Brick"290×140×90,6 kg →
#                     ρ1650 DERIVED from the supplier's own mass; KZN: Corobrik
#                     "Cement Maxi 90"222×110×90,3.4 kg → ρ1550 (Corobrik concrete
#                     bricks/blocks are KZN-only). λ1.0 is a GENERIC solid sand-cement
#                     masonry value — no certified SA figure was found, so enter the
#                     manufacturer's λ per layer where you have it.
#   insulation ....... XA:2021 table 10 (generic λ/ρ; density ranges mid-pointed)
#   concrete/steel/timber/mortar generic fallback tables — editable per layer anyway
# Columns: (Name, Category, Lambda, Density, CSpec, FixedR, PlasterFree, Presets)
WALL_MATERIALS = [
    ("Outside air film (7 m/s)", "Film", None, None, None, "0.03", 0, ""),
    ("Inside air film (still air)", "Film", None, None, None, "0.12", 0, ""),
    ("Air cavity (unventilated, non-reflective)", "Cavity", None, None, None, "0.16", 0, "25,50,75,100"),
    ("Cement plaster / render", "Plaster", "0.72", "1860", "840", None, 0, "10,12,15,20"),
    ("Cement mortar (joint)", "Other", "1.4", "2000", "840", None, 0, "10"),
    ("Clay brick masonry (incl. joints)", "Masonry", "0.82", "1826", "800", None, 0, LEAF_PRESETS),
    ("Clay face brick FBX/FBS/FBA (incl. joints)", "Masonry", "0.82", "1826", "800", None, 1, LEAF_PRESETS),
    ("Clay maxi brick masonry (incl. joints)", "Masonry", "0.82", "1826", "800", None, 0, "140,290"),
    ("Clay face maxi FBX/FBS/FBA (incl. joints)", "Masonry", "0.82", "1826", "800", None, 1, "140,290"),
    ("Cement maxi brick (incl. joints)", "Masonry", "1.0", "1650", "840", None, 0, "140,290"),
    ("Cement maxi brick KZN (incl. joints)", "Masonry", "1.0", "1550", "840", None, 0, "110,230"),
    ("High-density concrete face brick/block", "Masonry", "1.4", "2100", "840", None, 1, LEAF_PRESETS),
    ("Concrete block (medium)", "Masonry", "0.51", "1400", "1000", None, 0, LEAF_PRESETS),
    ("Aerated concrete block", "Masonry", "0.24", "750", "1000", None, 0, "140,190,220,230"),
    ("Expanded polystyrene (EPS)", "Insulation", "0.035", "15", "1300", None, 0, INS_PRESETS),
    ("Extruded polystyrene (XPS)", "Insulation", "0.028", "32", "1300", None, 0, INS_PRESETS),
    ("Glass wool blanket", "Insulation", "0.040", "14", "840", None, 0, INS_PRESETS),
    ("Mineral / rock wool", "Insulation", "0.033", "90", "840", None, 0, INS_PRESETS),
    ("Fibre glass board", "Insulation", "0.033", "47.5", "840", None, 0, INS_PRESETS),
    ("Polyurethane / PIR board", "Insulation", "0.025", "32", "1300", None, 0, INS_PRESETS),
    ("Steel / metal sheet or studs", "Metal", "45", "7800", "480", None, 0, ""),
    ("Timber (softwood)", "Other", "0.14", "600", "1600", None, 0, ""),
    ("Plywood / OSB board", "Other", "0.13", "650", "1700", None, 0, ""),
]


def read_source() -> list[str]:
    for enc in ("utf-8-sig", "cp1252"):
        try:
            return SRC.read_text(encoding=enc).splitlines()
        except UnicodeDecodeError:
            continue
    sys.exit(f"ERROR: cannot decode {SRC}")


def sql_str(v: str) -> str:
    return "'" + v.replace("'", "''") + "'"


def main() -> None:
    lines = read_source()
    errors: list[str] = []

    # ---- table 3 (shading multipliers) -------------------------------------
    multipliers: list[tuple[str, str]] = []
    for ln in lines:
        m = T3_RE.match(ln.strip())
        if m:
            lat_max = "999" if m.group(1) == "32" and ln.lstrip().startswith(">") else m.group(1)
            if ln.lstrip().startswith(">"):
                lat_max = "999"  # ">32º" row = catch-all
            multipliers.append((lat_max, m.group(2).replace(",", ".")))
    if len(multipliers) != 7:
        errors.append(f"table 3: expected 7 rows, got {len(multipliers)}: {multipliers}")

    # ---- table 4 (fenestration bands) — section-scoped: Table 5 (rooflights) also
    # starts with ">" and "%" but must NOT be parsed here --------------------
    bands: list[tuple[str, list[str | None]]] = []
    in_table4 = False
    for ln in lines:
        s = ln.strip()
        if s.startswith("Table 4 —"):
            in_table4 = True
            continue
        if in_table4 and re.match(r"^5\.\d+\.\d+ [A-Za-z]", s):   # 5.3.7 … ends the block;
            in_table4 = False                                      # the header's bare "5.2.2"
        if not in_table4:                                          # caption must NOT end it
            continue
        m = T4_RE.match(s)
        if not m:
            continue
        tokens = re.findall(r"Any solution|\d+,\d+", m.group(2))
        if len(tokens) != 4:
            errors.append(f"table 4: row '{s}' -> {len(tokens)} tokens, want 4")
            continue
        vals: list[str | None] = [
            None if t == "Any solution" else t.replace(",", ".") for t in tokens
        ]
        ratio_max = "100" if s.startswith(">") else m.group(1)
        bands.append((ratio_max, vals))
    if len(bands) != 10:
        errors.append(f"table 4: expected 10 rows, got {len(bands)}")
    if bands and (bands[0][0] != "20" or bands[0][1] != [None] * 4):
        errors.append(f"table 4: first row should be the <=20% all-Any row, got {bands[0]}")
    if bands and bands[-1][1] != ["2.00", "0.28", "0.22", None]:
        errors.append(f"table 4: last row should be >60% 2,00/0,28/0,22/Any, got {bands[-1]}")

    # ---- annex C (towns) ----------------------------------------------------
    # Merge wrapped names first ("King William's" / "Town Eastern Cape ...").
    merged: list[str] = []
    merges: list[str] = []
    i = 0
    while i < len(lines):
        cur = lines[i].strip()
        if MERGEABLE_RE.match(cur) and i + 1 < len(lines) and ROW_RE.match(lines[i + 1].strip()):
            joined = f"{cur} {lines[i + 1].strip()}"
            merges.append(cur)
            merged.append(joined)
            i += 2
            continue
        merged.append(lines[i])
        i += 1

    towns: dict[tuple[str, str], tuple[str, str, str, str, str]] = {}
    dupes: list[str] = []
    conflicts: list[str] = []
    for raw in merged:
        ln = raw.strip()
        m = ROW_RE.match(ln)
        if not m:
            continue
        name, prov = m.group("name").strip(), m.group("prov")
        lon, lat, zone, sccp = m.group("lon"), m.group("lat"), m.group("zone"), m.group("sccp")
        fix = FIXES.get((name, prov))
        if fix:
            field, new_val, why = fix
            if field == "lat":
                lat = new_val
            else:
                lon = new_val
            print(f"  fix: {name} ({prov}) {why}")
        if not (LAT_RANGE[0] <= float(lat) <= LAT_RANGE[1]):
            errors.append(f"lat out of range: {ln}")
        if not (LON_RANGE[0] <= float(lon) <= LON_RANGE[1]):
            errors.append(f"lon out of range: {ln}")
        if zone not in ALLOWED_ZONES:
            errors.append(f"bad zone: {ln}")
        row = (lon, lat, zone, sccp, None)
        key = (name, prov)
        if key in towns:
            dupes.append(f"{name} ({prov})")
            if towns[key][:4] != row[:4]:
                conflicts.append(f"{name} ({prov}): {towns[key][:4]} vs {row[:4]}")
            continue
        towns[key] = row

    for c in conflicts:
        print(f"  WARNING duplicate differs: {c}")
    seen_zones = {v[2] for v in towns.values()}
    if not seen_zones <= ALLOWED_ZONES or not ALLOWED_ZONES <= seen_zones:
        errors.append(f"zone set wrong: {sorted(seen_zones)}")

    # ---- wall tables (cl. 5.5, appended after the town list) -------------------
    # Table 6 rows: zone, R, construction note; Table 7 rows: zone, R, CR.
    # Section-anchored so Table 5 (rooflights) and the header junk can't leak in.
    in6 = in7 = False
    wall6: dict[str, tuple[str, str]] = {}
    wall7: dict[str, tuple[str, str]] = {}
    row6 = re.compile(r"^(5H|[1-7])\s+(\d,\d)\s+(50 mm cavity wall|Collar jointed wall)$")
    row7 = re.compile(r"^(5H|[1-7])\s+(\d,\d)\s+(\d+)$")
    for ln in lines:
        s = ln.strip()
        if s.startswith("Table 6 —"):
            in6, in7 = True, False
            continue
        if s.startswith("Table 7 —"):
            in6, in7 = False, True
            continue
        if s.startswith("5.5.4"):
            in7 = False
        if in6:
            m = row6.match(s)
            if m:
                wall6[m.group(1)] = (m.group(2).replace(",", "."), m.group(3))
        elif in7:
            m = row7.match(s)
            if m:
                wall7[m.group(1)] = (m.group(2).replace(",", "."), m.group(3))
    if set(wall6) != ALLOWED_ZONES or set(wall7) != ALLOWED_ZONES:
        errors.append(
            f"wall tables: table 6 zones {sorted(wall6)} / table 7 zones {sorted(wall7)}"
            f" != expected {sorted(ALLOWED_ZONES)}")
    if len(WALL_MATERIALS) != 23 or len({m[0] for m in WALL_MATERIALS}) != 23:
        errors.append("WALL_MATERIALS must hold 23 unique rows")

    if errors:
        print("\n".join("ERROR: " + e for e in errors), file=sys.stderr)
        sys.exit(1)

    # ---- emit ---------------------------------------------------------------
    out: list[str] = []
    out.append("-- GENERATED by tools/import_sans2026.py from docs/sans.txt (local licensed")
    out.append("-- extract of SANS 10400-XA:2026 — source is gitignored, never commit it).")
    out.append("-- Numeric reference data only; DO NOT EDIT BY HAND — regenerate instead.")
    out.append("-- Idempotent UPSERTs: Xa2026Seeder applies this on every startup so data")
    out.append("-- corrections and topped-up town rows reach existing databases.")
    out.append("")
    out.append("-- Table 4 (cl. 5.3.4/5.3.5): per-storey limits. RatioMax 100 = '>60 %' row;")
    out.append("-- NULL = 'Any solution'.")
    out.append("INSERT INTO FenestrationBands (RatioMax, MaxU, ShadedShgc, UnshadedShgc, SouthernShgc) VALUES")
    rows = [f"  ({r}, {', '.join('NULL' if v is None else v for v in vals)})" for r, vals in bands]
    out.append(",\n".join(rows) + "")
    out.append("ON CONFLICT(RatioMax) DO UPDATE SET MaxU=excluded.MaxU, ShadedShgc=excluded.ShadedShgc, UnshadedShgc=excluded.UnshadedShgc, SouthernShgc=excluded.SouthernShgc;")
    out.append("")
    out.append("-- Table 3 (cl. 5.2.2): shading projection multiplier by latitude (degrees South).")
    out.append("-- LatitudeMax 999 = '>32º' catch-all row.")
    out.append("INSERT INTO ShadingMultipliers (LatitudeMax, Multiplier) VALUES")
    out.append(",\n".join(f"  ({lm}, {mult})" for lm, mult in multipliers) + "")
    out.append("ON CONFLICT(LatitudeMax) DO UPDATE SET Multiplier=excluded.Multiplier;")
    out.append("")
    out.append("-- Annex C table C.1: towns (deduplicated by town+province; OCR fixes per script).")
    out.append("INSERT INTO EnergyZoneTowns (Town, Province, Longitude, Latitude, EnergyZone, Sccp) VALUES")
    vals = []
    for (name, prov), (lon, lat, zone, sccp, _) in sorted(towns.items(), key=lambda kv: (kv[0][0].lower(), kv[0][1])):
        vals.append(f"  ({sql_str(name)}, {sql_str(prov)}, {lon}, {lat}, {sql_str(zone)}, {1 if sccp == 'Yes' else 0})")
    out.append(",\n".join(vals) + "")
    out.append("ON CONFLICT(Town, Province) DO UPDATE SET Longitude=excluded.Longitude, Latitude=excluded.Latitude, EnergyZone=excluded.EnergyZone, Sccp=excluded.Sccp;")
    out.append("")

    # ---- wall tables + materials (cl. 5.5) ------------------------------------
    def zkey(z: str) -> float:
        return 5.5 if z == "5H" else int(z)

    def num(v: str | None) -> str:
        return "NULL" if v is None else v

    out.append("-- Table 6 + Table 7 (cl. 5.5): per-zone wall requirements. Heavy = surface")
    out.append("-- density >= 270 kg/m2 (R + deemed-to-satisfy construction); light = < 270")
    out.append("-- (pass on R OR CR).")
    out.append("INSERT INTO WallZoneRequirements (EnergyZone, HeavyMinR, HeavyConstruction, LightMinR, LightMinCR) VALUES")
    zrows = []
    for z in sorted(ALLOWED_ZONES, key=zkey):
        h_r, h_con = wall6[z]
        l_r, l_cr = wall7[z]
        zrows.append(f"  ({sql_str(z)}, {h_r}, {sql_str(h_con)}, {l_r}, {l_cr})")
    out.append(",\n".join(zrows) + "")
    out.append("ON CONFLICT(EnergyZone) DO UPDATE SET HeavyMinR=excluded.HeavyMinR, HeavyConstruction=excluded.HeavyConstruction, LightMinR=excluded.LightMinR, LightMinCR=excluded.LightMinCR;")
    out.append("")
    out.append("-- Wall material library — provenance in tools/import_sans2026.py and")
    out.append("-- docs/plans/walls.md (CBA SA TN01 / XA table 10 / SANS 204 table F.2 /")
    out.append("-- generic fallbacks). Presets = standard thickness sets (typeable anyway).")
    out.append("INSERT INTO WallMaterials (Name, Category, Lambda, Density, CSpec, FixedR, PlasterFree, Presets) VALUES")
    mrows = [
        f"  ({sql_str(n)}, {sql_str(cat)}, {num(lam)}, {num(den)}, {num(cs)}, {num(fr)}, {pf}, {sql_str(pr)})"
        for (n, cat, lam, den, cs, fr, pf, pr) in WALL_MATERIALS
    ]
    out.append(",\n".join(mrows) + "")
    out.append("ON CONFLICT(Name) DO UPDATE SET Category=excluded.Category, Lambda=excluded.Lambda, Density=excluded.Density, CSpec=excluded.CSpec, FixedR=excluded.FixedR, PlasterFree=excluded.PlasterFree, Presets=excluded.Presets;")
    out.append("")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(out), encoding="utf-8")
    print(f"table 3 rows: {len(multipliers)}")
    print(f"table 4 rows: {len(bands)}")
    print(f"towns: {len(towns)} unique, {len(dupes)} duplicate rows skipped, merges: {merges}")
    print(f"wall zones: {len(wall6)}, wall materials: {len(WALL_MATERIALS)}")
    print(f"wrote {OUT.relative_to(ROOT)} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
