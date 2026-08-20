#!/usr/bin/env python3
"""Gli invarianti di questo repo: cose che devono essere vere, e che si rompono in silenzio.

Non e' un linter di stile. Ogni controllo qui dentro corrisponde a un guasto che in questo progetto
e' costato una build, una serata, o una versione rotta sul telefono di qualcuno — e ognuno e' scritto
per passare sull'albero attuale, cosi' se diventa rosso e' perche' qualcosa e' cambiato davvero.

Gira in pochi secondi e senza Unity, quindi sta nel check delle pull request accanto ad
asset_sanity.py: uno guarda l'integrita' degli asset, questo guarda le regole.
"""

import os
import re
import subprocess
import sys

# --- 1. segreti -------------------------------------------------------------------------------
#
# La repo e' PUBBLICA. La push protection di GitHub prende i formati dei provider noti, ma non
# l'AppId di Photon ne' un token di bot Telegram: quelli li conosciamo solo noi.
#
# Solo formati inequivocabili. Niente "blob base64 lungo": in un progetto Unity ce ne sono a
# decine di legittimi, e un controllo che grida al lupo viene disattivato dopo due giorni.
SEGRETI = [
    (re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----"), "una chiave privata"),
    (re.compile(r"\bgh[pousr]_[A-Za-z0-9]{16,}"), "un token GitHub"),
    (re.compile(r"\bgithub_pat_[A-Za-z0-9_]{20,}"), "un personal access token GitHub"),
    (re.compile(r"\b\d{8,12}:[A-Za-z0-9_-]{30,}"), "un token di bot Telegram"),
]

# File che non devono stare nell'indice di git. .gitignore li copre gia', ma `git add -f` lo scavalca
# e un agent frettoloso lo fa.
VIETATI = re.compile(r"(?:^|/)(?:Library|Temp|Builds?|Logs)/|\.(?:csproj|sln|p8|p12|mobileprovision)$|(?:^|/)\.DS_Store$")

# --- 2. flag dei NetworkObject ------------------------------------------------------------------
#
# Sono BIT COMPATTATI, non booleani, e contengono anche un numero di versione: sono stati ricavati
# leggendo i metadati della DLL di Fusion, non indovinati. Ognuno di questi numeri dice una cosa che
# se cambia rompe la partita in un modo difficile da collegare alla causa:
#
#   NetPlayer  262402  DestroyWhenStateAuthorityLeaves -> il tuo avatar sparisce quando esci.
#                      Togliendolo, restano in campo i fantasmi di chi ha abbandonato.
#   NetBall    131330  MasterClientObject -> la palla migra quando il master lascia la sessione.
#                      Prima non c'era, e il master che usciva portava con se' punteggio e cronometro.
#   NetMatch   131074  come sopra, per punteggio, timer e fasi della partita.
FLAG_ATTESI = {
    "Assets/Prefabs/NetPlayer.prefab": 262402,
    "Assets/Prefabs/NetBall.prefab": 131330,
    "Assets/Prefabs/NetMatchController.prefab": 131074,
}

# --- 3. shader che URP non mette nella build ----------------------------------------------------
#
# Nell'editor funzionano, sul telefono no: il campo magenta e il testo 3D invisibile sono arrivati
# entrambi cosi'. Si cercano per NOME, perche' e' il nome la cosa che sbaglia.
SHADER_STRIPPATI = re.compile(r'Shader\.Find\(\s*"(Standard|Diffuse|Legacy Shaders/[^"]*|GUI/Text|Unlit/Texture)"')
COMPONENTI_VIETATI = re.compile(r"(?:Add|Get)Component<\s*TextMesh\s*>")

BINARI = {".glb", ".fbx", ".png", ".jpg", ".jpeg", ".tga", ".psd", ".dll", ".wav", ".mp3",
          ".ogg", ".ttf", ".otf", ".icon", ".p8", ".p12", ".zip", ".pdf", ".mp4", ".exr"}

# Questo file contiene i modelli che cerca: senza escluderlo troverebbe se stesso e sarebbe
# sempre rosso.
IO_STESSO = ".github/scripts/repo_lint.py"


def tracciati():
    out = subprocess.run(["git", "ls-files", "-z"], capture_output=True, text=True, check=True)
    return [p for p in out.stdout.split("\0") if p]


def testo(path):
    if os.path.splitext(path)[1].lower() in BINARI:
        return None
    try:
        with open(path, "rb") as f:
            grezzo = f.read()
    except OSError:
        return None
    if b"\0" in grezzo[:8000]:
        return None
    return grezzo.decode("utf-8", "replace")


def main():
    problemi = []
    files = tracciati()

    for p in files:
        if VIETATI.search(p):
            problemi.append("%s non deve stare in git: e' generato o e' una credenziale." % p)

    for p in files:
        if p == IO_STESSO:
            continue
        contenuto = testo(p)
        if contenuto is None:
            continue

        for modello, cosa in SEGRETI:
            m = modello.search(contenuto)
            if m:
                riga = contenuto[:m.start()].count("\n") + 1
                problemi.append("%s:%d sembra contenere %s. La repo e' pubblica: il valore va in un "
                                "secret e iniettato alla build (vedi photon_appid.py)." % (p, riga, cosa))

        if p.startswith("Assets/") and p.endswith(".cs"):
            m = SHADER_STRIPPATI.search(contenuto)
            if m:
                riga = contenuto[:m.start()].count("\n") + 1
                problemi.append("%s:%d usa lo shader integrato \"%s\": URP non lo mette nella build. "
                                "Nell'editor si vede, sul telefono e' magenta." % (p, riga, m.group(1)))
            m = COMPONENTI_VIETATI.search(contenuto)
            if m:
                riga = contenuto[:m.start()].count("\n") + 1
                problemi.append("%s:%d usa TextMesh: disegna con lo shader GUI/Text, che URP butta "
                                "fuori dalla build. Usa una Text di UI su una Canvas." % (p, riga))

    # L'AppId Photon deve restare vuoto: lo mette la pipeline dal secret.
    appsettings = "Assets/Photon/Fusion/Resources/PhotonAppSettings.asset"
    contenuto = testo(appsettings) or ""
    m = re.search(r"(?m)^\s*AppIdFusion:[ \t]*(\S+)\s*$", contenuto)
    if m:
        problemi.append("%s ha di nuovo un AppId scritto dentro (%s...). Deve restare VUOTO: lo mette "
                        "la pipeline dal secret PHOTON_APP_ID." % (appsettings, m.group(1)[:8]))

    # Il timbro della build lo scrive la pipeline; committato deve essere vuoto, altrimenti una build
    # fatta fuori dalla CI mentirebbe su chi l'ha fatta.
    stamp = "Assets/Resources/BuildStamp.txt"
    if os.path.exists(stamp) and (testo(stamp) or "").strip():
        problemi.append("%s deve essere vuoto in repo: lo riempie la pipeline. Un timbro che mente "
                        "su chi ha fatto la build e' peggio di nessun timbro." % stamp)

    # Stessa regola, motivo diverso: un'impronta committata resterebbe ferma mentre il netcode cambia,
    # e due build incompatibili tornerebbero a incontrarsi — che e' esattamente cio' che previene.
    netid = "Assets/Resources/NetcodeId.txt"
    if os.path.exists(netid) and (testo(netid) or "").strip():
        problemi.append("%s deve essere vuoto in repo: lo calcola la pipeline a ogni build. "
                        "Committato, resterebbe fermo mentre il netcode cambia." % netid)

    for path, atteso in FLAG_ATTESI.items():
        contenuto = testo(path)
        if contenuto is None:
            problemi.append("%s non trovato: i flag di rete non sono verificabili." % path)
            continue
        m = re.search(r"(?m)^\s*Flags:\s*(\d+)\s*$", contenuto)
        if not m:
            problemi.append("%s non ha un campo Flags leggibile." % path)
        elif int(m.group(1)) != atteso:
            problemi.append("%s ha Flags %s invece di %d. Sono bit compattati, non booleani, e sono "
                            "stati ricavati dai metadati della DLL di Fusion: se il cambio e' voluto "
                            "va spiegato nella PR e aggiornato qui." % (path, m.group(1), atteso))

    if problemi:
        for x in problemi:
            print("::error::" + x)
        print("\n%d %s. Le regole stanno in AGENTS.md."
              % (len(problemi), "problema" if len(problemi) == 1 else "problemi"))
        return 1

    print("invarianti ok: %d file tracciati, 3 prefab di rete, AppId e timbro vuoti" % len(files))
    return 0


if __name__ == "__main__":
    sys.exit(main())
