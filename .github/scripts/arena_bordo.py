#!/usr/bin/env python3
"""Misura il bordo interno dell'arena e stampa la tabella per Arena.cs.

`Arena.cs` dice che la sua tabella e' misurata e non scelta. Questo e' lo strumento che la
misura: senza, quella frase e' una promessa che nessuno puo' verificare.

  python3 .github/scripts/arena_bordo.py Assets/Models/Arena.glb
  python3 .github/scripts/arena_bordo.py nuovo.glb --scala 41 --cerca-spostamento

Serve ogni volta che il modello dell'arena cambia. Se lo cambi e NON rilanci questo, il muro
e la vernice restano dove erano e il gioco si comporta come se l'arena vecchia ci fosse
ancora: la palla rimbalza contro l'aria e si vede terreno dove non si puo' andare.

Come funziona, in ordine:

  1. Legge le posizioni dei vertici dal .glb, scartando tutto quello che sta sopra QUOTA: un
     ramo a otto metri non e' un muro.
  2. Per ogni grado, la distanza MINIMA dal centro. Il minimo, non la media: il muro deve
     stare dentro la mesh, non in mezzo.
  3. Minimo mobile su +-3 gradi, cosi' un sasso isolato non diventa una punta.
  4. Specchiatura sui due assi tenendo il piu' piccolo dei quattro. L'arena e' storta; un
     campo con una fascia piu' larga dell'altra non e' un campo.
  5. Meno il franco per il muro (mezzo spessore piu' aria).
  6. Tetto alla variazione per grado, tagliando sempre verso l'interno: un muro che gira
     troppo stretto fa sembrare un rimbalzo un bug.

Tutti i passaggi tagliano verso l'interno. Nessuno puo' spingere il bordo fuori dalla mesh.
"""

import argparse
import importlib.util
import json
import math
import os
import struct
import sys

QUOTA = 3.0        # sopra questa altezza la geometria non e' un ostacolo
FRANCO = 0.8       # mezzo spessore del muro (0,3) piu' aria
PENDENZA = 0.35    # metri di variazione massima per grado
GRADI = 360        # risoluzione della misura
VOCI = 19          # voci della tabella per quadrante (ogni 5 gradi, estremi compresi)


def leggi_glb(percorso):
    """Riusa il lettore GLB di glb_textures.py invece di averne due che divergono."""
    qui = os.path.dirname(os.path.abspath(__file__))
    spec = importlib.util.spec_from_file_location("gt", os.path.join(qui, "glb_textures.py"))
    gt = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(gt)
    return gt.leggi_glb(percorso)


def posizioni(percorso):
    """Vertici in coordinate del modello. Onora byteStride: i file passati per meshoptimizer
    interlacciano gli attributi, e leggerli a passo fisso 12 restituisce spazzatura."""
    js, bi = leggi_glb(percorso)
    out = []
    for mesh in js.get('meshes', []):
        for prim in mesh['primitives']:
            acc = js['accessors'][prim['attributes']['POSITION']]
            vista = js['bufferViews'][acc['bufferView']]
            passo = vista.get('byteStride') or 12
            base = vista.get('byteOffset', 0) + acc.get('byteOffset', 0)
            for i in range(acc['count']):
                out.append(struct.unpack_from('<fff', bi, base + i * passo))
    return out


def piano_terreno(punti):
    """La quota del terreno del modello: il picco di densita' in basso, non il minimo assoluto.
    Il minimo e' spesso la punta di una radice, e appoggiare quella a zero solleva tutto il
    resto su un gradino."""
    ys = [p[1] for p in punti]
    mn, mx = min(ys), max(ys)
    if mx - mn < 1e-6:
        return mn
    NB = 200
    conta = [0] * NB
    for y in ys:
        conta[min(NB - 1, int((y - mn) / (mx - mn) * (NB - 1)))] += 1
    basso = conta[:NB // 4]
    return mn + (basso.index(max(basso)) + 0.5) / NB * (mx - mn)


def indice(gradi):
    return int(((gradi + 180) % 360) / 360 * GRADI) % GRADI


def bordo(punti, scala, y0, dx, dz):
    r = [1e9] * GRADI
    for x, y, z in punti:
        if y * scala + y0 > QUOTA:
            continue
        wx, wz = x * scala + dx, z * scala + dz
        d = math.hypot(wx, wz)
        if d < 1e-6:
            continue
        s = int((math.atan2(wz, wx) + math.pi) / (2 * math.pi) * GRADI) % GRADI
        if d < r[s]:
            r[s] = d
    # settori vuoti: prendono il vicino piu' stretto
    for i in range(GRADI):
        if r[i] > 1e8:
            for k in range(1, GRADI):
                a, b = r[(i - k) % GRADI], r[(i + k) % GRADI]
                if a < 1e8 or b < 1e8:
                    r[i] = min(a, b)
                    break
    r = [min(r[(i + k) % GRADI] for k in range(-3, 4)) for i in range(GRADI)]
    sim = []
    for i in range(GRADI):
        g = -180 + (i + 0.5) / GRADI * 360
        sim.append(min(r[indice(g)], r[indice(180 - g)], r[indice(-g)], r[indice(180 + g)]))
    r = [v - FRANCO for v in sim]
    for _ in range(2):
        for i in range(GRADI):
            r[i] = min(r[i], r[(i - 1) % GRADI] + PENDENZA)
        for i in range(GRADI - 1, -1, -1):
            r[i] = min(r[i], r[(i + 1) % GRADI] + PENDENZA)
    return r


def area(r):
    return sum(0.5 * v * v * (2 * math.pi / len(r)) for v in r)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("modello")
    ap.add_argument("--scala", type=float, default=41.0)
    ap.add_argument("--dx", type=float, default=0.0)
    ap.add_argument("--dz", type=float, default=-2.5)
    ap.add_argument("--cerca-spostamento", action="store_true",
                    help="cerca dx/dz che massimizzano l'area giocabile")
    args = ap.parse_args()

    punti = posizioni(args.modello)
    yt = piano_terreno(punti)
    y0 = -yt * args.scala
    print("%s: %d vertici" % (args.modello, len(punti)))
    print("piano del terreno a y=%.4f nel modello  ->  ModelY = %.2f" % (yt, y0))

    dx, dz = args.dx, args.dz
    if args.cerca_spostamento:
        migliore = None
        for i in range(-40, 41, 5):
            for j in range(-70, 21, 5):
                r = bordo(punti, args.scala, y0, i / 10, j / 10)
                a = area(r)
                if migliore is None or a > migliore[0]:
                    migliore = (a, i / 10, j / 10)
        _, dx, dz = migliore
        print("spostamento migliore: dx %+.1f  dz %+.1f" % (dx, dz))

    r = bordo(punti, args.scala, y0, dx, dz)
    print("area giocabile %.0f m2   raggio min %.2f  max %.2f" % (area(r), min(r), max(r)))

    passo = GRADI // 4 // (VOCI - 1)
    quadrante = [min(r[(indice(0) + i * passo + k) % GRADI] for k in range(passo))
                 for i in range(VOCI)]
    quadrante[-1] = r[indice(90)]
    print("verso le porte %.2f   verso le fasce %.2f" % (quadrante[0], quadrante[-1]))
    print()
    print("--- da incollare in Arena.cs -------------------------------------------------")
    print("        public const float ModelScale = %gf;" % args.scala)
    print("        public const float ModelY = %.2ff;" % y0)
    print("        public const float ModelX = %gf;" % dx)
    print("        public const float ModelZ = %gf;" % dz)
    print("        static readonly float[] Quadrant =")
    print("        {")
    for i in range(0, VOCI, 10):
        print("            " + ", ".join("%.2ff" % v for v in quadrante[i:i + 10]) + ",")
    print("        };")
    print("------------------------------------------------------------------------------")
    return 0


if __name__ == "__main__":
    sys.exit(main())
