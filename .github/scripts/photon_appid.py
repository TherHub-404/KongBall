#!/usr/bin/env python3
"""Scrive l'AppId Photon dentro PhotonAppSettings prima che Unity apra il progetto.

Perche' esiste: l'AppId e' committato in chiaro in una repo PUBBLICA. Non da' accesso al codice ne'
agli account, ma chi lo legge puo' collegarsi al posto tuo e bruciare la quota di giocatori
connessi — cioe' far sembrare che il gioco sia rotto. Va tenuto fuori dal repo.

Perche' qui e non in C#: il player deve trovare il valore GIA' importato. Riscrivendo l'asset prima
che Unity si apra, l'import lo prende come qualsiasi altra modifica e non c'e' nessuna asset
pipeline da convincere a salvare al momento giusto.

Il valore nel repo ora e' VUOTO, quindi questo script e' l'unica fonte dell'AppId e senza il secret
non c'e' niente da spedire. Percio' e' bloccante: una build senza AppId compila, si installa, si
apre e non si connette a niente — un fallimento in CI, che si legge, e' molto meglio di un gioco
muto sul telefono di qualcuno.
"""

import os
import re
import sys

ASSET = "Assets/Photon/Fusion/Resources/PhotonAppSettings.asset"
FIELD = re.compile(r"(?m)^(\s*AppIdFusion:).*$")


def main():
    app_id = os.environ.get("PHOTON_APP_ID", "").strip()
    if not app_id:
        print("::error::PHOTON_APP_ID non configurato. Il valore nel repo e' vuoto di proposito, "
              "quindi questa build non saprebbe a quale app Photon collegarsi: si fermerebbe qui "
              "invece di finire sul telefono di qualcuno senza rete. "
              "Settings > Secrets and variables > Actions > PHOTON_APP_ID.")
        return 1

    with open(ASSET, encoding="utf-8") as f:
        text = f.read()

    # count=1 e il controllo sotto: se un giorno l'asset cambiasse forma, meglio fermarsi che
    # scrivere silenziosamente nel posto sbagliato e spedire una build che non si connette.
    text, hits = FIELD.subn(lambda m: m.group(1) + " " + app_id, text, count=1)
    if hits != 1:
        print("::error::AppIdFusion non trovato in " + ASSET)
        return 1

    with open(ASSET, "w", encoding="utf-8") as f:
        f.write(text)

    # Mai il valore: i log di una repo pubblica li legge chiunque.
    print("AppId Photon preso dal secret (%d caratteri)." % len(app_id))
    return 0


if __name__ == "__main__":
    sys.exit(main())
