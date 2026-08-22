#!/usr/bin/env python3
"""Ridimensiona le texture incastrate dentro un .glb.

Perche' esiste: glTFast importa le immagini di un .glb come Texture2D non
compresse. Una texture 2048x2048 con le mipmap sono 21 MB di build, sempre,
qualunque sia il peso del JPEG dentro il file. Su dieci modelli fanno 212 MB,
cioe' il 73% degli asset del gioco. Ridurre i pixel e' l'unica leva che
funziona senza aprire Unity: 2048 -> 512 sono 21 MB -> 1,4 MB.

  python3 .github/scripts/glb_textures.py --max 512 Assets/Resources/Arena/*.glb
  python3 .github/scripts/glb_textures.py --max 1024 --dry-run Assets/Models/GoalModel.glb

Il .glb viene riscritto da zero: il chunk BIN e' ricostruito concatenando le
bufferView nell'ordine originale, riallineate a 4 byte, e tutti gli offset sono
riscritti. Le view che non sono immagini passano identiche byte per byte.
"""

import argparse
import io
import json
import os
import struct
import sys

JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942


def leggi_glb(percorso):
    dati = open(percorso, "rb").read()
    magic, versione, _ = struct.unpack("<III", dati[:12])
    if magic != 0x46546C67:
        raise ValueError("non e' un .glb (magic sbagliato)")
    if versione != 2:
        raise ValueError("glTF versione %d, questo script gestisce la 2" % versione)
    testo = None
    binario = b""
    off = 12
    while off + 8 <= len(dati):
        lung, tipo = struct.unpack("<II", dati[off:off + 8])
        corpo = dati[off + 8:off + 8 + lung]
        if tipo == JSON_CHUNK:
            testo = json.loads(corpo.decode("utf-8"))
        elif tipo == BIN_CHUNK:
            binario = corpo
        off += 8 + lung + ((-lung) % 4)
    if testo is None:
        raise ValueError("chunk JSON mancante")
    return testo, binario


def scrivi_glb(percorso, testo, binario):
    js = json.dumps(testo, separators=(",", ":")).encode("utf-8")
    js += b" " * ((-len(js)) % 4)
    bi = binario + b"\x00" * ((-len(binario)) % 4)
    totale = 12 + 8 + len(js) + (8 + len(bi) if bi else 0)
    with open(percorso, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, totale))
        f.write(struct.pack("<II", len(js), JSON_CHUNK))
        f.write(js)
        if bi:
            f.write(struct.pack("<II", len(bi), BIN_CHUNK))
            f.write(bi)


def ridimensiona(blob, mime, lato_max):
    """Restituisce (nuovo_blob, larghezza_prima, larghezza_dopo) o None se e' gia' piccola."""
    from PIL import Image

    img = Image.open(io.BytesIO(blob))
    larghezza, altezza = img.size
    if max(larghezza, altezza) <= lato_max:
        return None
    scala = lato_max / float(max(larghezza, altezza))
    nuova = (max(1, int(round(larghezza * scala))), max(1, int(round(altezza * scala))))
    img = img.convert("RGBA" if mime == "image/png" else "RGB")
    img = img.resize(nuova, Image.LANCZOS)
    fuori = io.BytesIO()
    if mime == "image/png":
        img.save(fuori, format="PNG", optimize=True)
    else:
        img.save(fuori, format="JPEG", quality=90, optimize=True, subsampling=1)
    return fuori.getvalue(), (larghezza, altezza), nuova


def lavora(percorso, lato_max, prova):
    testo, binario = leggi_glb(percorso)
    immagini = testo.get("images", [])
    viste = testo.get("bufferViews", [])
    if not immagini:
        print("  %s: nessuna texture incastrata, lasciato stare" % percorso)
        return False

    buffer_glb = [i for i, b in enumerate(testo.get("buffers", [])) if "uri" not in b]
    if len(buffer_glb) > 1:
        raise ValueError("piu' di un buffer interno, caso non gestito")

    # Nuovo contenuto per le view che contengono immagini da rimpicciolire.
    sostituzioni = {}
    for numero, im in enumerate(immagini):
        vista = im.get("bufferView")
        if vista is None:
            print("  img %-2d: URI esterno, salto" % numero)
            continue
        v = viste[vista]
        if v.get("buffer", 0) not in buffer_glb:
            continue
        inizio = v.get("byteOffset", 0)
        blob = binario[inizio:inizio + v["byteLength"]]
        mime = im.get("mimeType", "image/png")
        esito = ridimensiona(blob, mime, lato_max)
        if esito is None:
            print("  img %-2d: gia' entro %d px, lasciata" % (numero, lato_max))
            continue
        nuovo, prima, dopo = esito
        sostituzioni[vista] = nuovo
        print("  img %-2d: %dx%d -> %dx%d   %.2f MB -> %.2f MB   (in build %.1f MB -> %.1f MB)"
              % (numero, prima[0], prima[1], dopo[0], dopo[1],
                 len(blob) / 1048576.0, len(nuovo) / 1048576.0,
                 prima[0] * prima[1] * 4 * 1.33 / 1048576.0,
                 dopo[0] * dopo[1] * 4 * 1.33 / 1048576.0))

    if not sostituzioni:
        return False
    if prova:
        return True

    # Ricostruisce il BIN: ogni view nell'ordine in cui compare, allineata a 4.
    fuori = bytearray()
    for numero, v in enumerate(viste):
        if v.get("buffer", 0) not in buffer_glb:
            continue
        if numero in sostituzioni:
            corpo = sostituzioni[numero]
        else:
            inizio = v.get("byteOffset", 0)
            corpo = binario[inizio:inizio + v["byteLength"]]
        fuori += b"\x00" * ((-len(fuori)) % 4)
        v["byteOffset"] = len(fuori)
        v["byteLength"] = len(corpo)
        fuori += corpo

    for indice in buffer_glb:
        testo["buffers"][indice]["byteLength"] = len(fuori)

    scrivi_glb(percorso, testo, bytes(fuori))
    return True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--max", type=int, required=True, help="lato massimo in pixel")
    ap.add_argument("--dry-run", action="store_true", help="dice cosa farebbe e non tocca nulla")
    ap.add_argument("file", nargs="+")
    args = ap.parse_args()

    toccati = 0
    for percorso in args.file:
        prima = os.path.getsize(percorso)
        print("%s  (%.1f MB)" % (percorso, prima / 1048576.0))
        if lavora(percorso, args.max, args.dry_run):
            toccati += 1
            if not args.dry_run:
                print("  --> %.1f MB" % (os.path.getsize(percorso) / 1048576.0))
    print("\n%d file %s" % (toccati, "da riscrivere" if args.dry_run else "riscritti"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
