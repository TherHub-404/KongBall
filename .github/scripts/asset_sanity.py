#!/usr/bin/env python3
"""Checks that cost seconds and catch mistakes that otherwise reach a device.

Every one of these is a failure that actually happened in this project:
  - a script committed without its .meta, so its GUID regenerated on every clone;
  - two assets sharing a GUID, which makes Unity resolve references at random;
  - a Resources.Load name with no asset behind it, which fails silently at runtime
    and shows up as a missing model or a menu with no logo.

Run from the repository root. Exits non-zero with a list of what is wrong.
"""
import os
import re
import sys
from collections import defaultdict

ASSETS = "Assets"

# Third-party trees carry their own metas and are not ours to police. The Apple icon bundle is a
# directory of resources that Unity treats as one asset, so its contents have no metas of their own.
SKIP_DIRS = ("Assets/Photon", "Assets/TextMesh Pro", "Assets/AppIcon")

# Extensions Unity imports as assets, and so must have a .meta beside them.
NEEDS_META = (".cs", ".png", ".jpg", ".glb", ".gltf", ".prefab", ".unity",
              ".asset", ".mat", ".shader", ".ttf", ".wav", ".mp3", ".anim", ".controller")

problems = []


def walk(root):
    for base, dirs, files in os.walk(root):
        norm = base.replace(os.sep, "/")
        if any(norm == s or norm.startswith(s + "/") for s in SKIP_DIRS):
            dirs[:] = []
            continue
        for f in files:
            yield norm + "/" + f


paths = list(walk(ASSETS))

# 1. every asset has its .meta
for p in paths:
    if p.endswith(".meta"):
        continue
    if p.lower().endswith(NEEDS_META) and not os.path.exists(p + ".meta"):
        problems.append("manca il .meta: " + p)

# 2. no two metas share a GUID
guids = defaultdict(list)
for p in paths:
    if not p.endswith(".meta"):
        continue
    with open(p, "r", errors="replace") as fh:
        for line in fh:
            if line.startswith("guid: "):
                guids[line.strip()[6:]].append(p)
                break
for guid, owners in guids.items():
    if len(owners) > 1:
        problems.append("GUID " + guid + " condiviso da: " + ", ".join(owners))

# 3. every Resources.Load name resolves to a file under a Resources folder
#
# Two call shapes are used in this project and both are resolved exactly rather than guessed:
#   Resources.Load<Sprite>("Menu/Logo")        a bare literal
#   Resources.Load<GameObject>(Root + name)    a const prefix plus a literal, as ArenaDressing does
# Anything genuinely dynamic is skipped: a wrong guess here would be a false failure.
stems = set()
for base, _, files in os.walk(ASSETS):
    parts = base.replace(os.sep, "/").split("/")
    if "Resources" not in parts:
        continue
    after = base.replace(os.sep, "/").split("/Resources/", 1)
    prefix = after[1] + "/" if len(after) > 1 else ""
    for f in files:
        if not f.endswith(".meta"):
            stems.add(prefix + os.path.splitext(f)[0])

# Some call sites go through a one-line wrapper: ArenaDressing has Load(string n) returning
# Resources.Load<GameObject>(Root + n), so the literals live at the wrapper's call sites and not at
# the Resources.Load itself. Wrappers of exactly that shape are followed; anything else is left
# alone, because a guess here would mean a false failure.
WRAPPER = re.compile(
    r'(?:static\s+)?\w[\w<>\[\]]*\s+(\w+)\s*\(\s*string\s+(\w+)\s*\)\s*\{'
    r'(?:[^{}]|\{[^{}]*\})*?Resources\.Load[^(]*\(\s*(\w+)\s*\+\s*\2\s*\)')

CALL = re.compile(r'Resources\.Load[^(]*\(([^()]*)\)')
BARE = re.compile(r'^\s*"([^"]+)"\s*$')
JOINED = re.compile(r'^\s*(\w+)\s*\+\s*"([^"]+)"\s*$')

checked = 0
for p in paths:
    if not p.endswith(".cs"):
        continue
    with open(p, "r", errors="replace") as fh:
        src = fh.read()
    consts = dict(re.findall(r'const\s+string\s+(\w+)\s*=\s*"([^"]*)"', src))
    for w in WRAPPER.finditer(src):
        method, const = w.group(1), w.group(3)
        if const not in consts:
            continue
        for c in re.finditer(re.escape(method) + r'\(\s*"([^"]+)"\s*\)', src):
            checked += 1
            name = consts[const] + c.group(1)
            if name not in stems:
                problems.append('Resources.Load("' + name + '") non ha un asset corrispondente  [' + p + ']')

    for call in CALL.finditer(src):
        arg = call.group(1)
        m = BARE.match(arg)
        if m:
            name = m.group(1)
        else:
            m = JOINED.match(arg)
            if not m or m.group(1) not in consts:
                continue                      # dynamic: nothing to verify
            name = consts[m.group(1)] + m.group(2)
        checked += 1
        if name not in stems:
            problems.append('Resources.Load("' + name + '") non ha un asset corrispondente  [' + p + ']')

if problems:
    print("CONTROLLI ASSET FALLITI:\n")
    for p in problems:
        print("  - " + p)
    sys.exit(1)

print(f"controlli asset ok: {len(paths)} file, {len(guids)} GUID, {checked} nomi Resources verificati")
