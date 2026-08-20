#!/usr/bin/env python3
"""Scrive l'AppId Photon dentro PhotonAppSettings prima che Unity apra il progetto.

Perche' esiste: l'AppId e' committato in chiaro in una repo PUBBLICA. Non da' accesso al codice ne'
agli account, ma chi lo legge puo' collegarsi al posto tuo e bruciare la quota di giocatori
connessi — cioe' far sembrare che il gioco sia rotto. Va tenuto fuori dal repo.

Perche' qui e non in C#: il player deve trovare il valore GIA' importato. Riscrivendo l'asset prima
che Unity si apra, l'import lo prende come qualsiasi altra modifica e non c'e' nessuna asset
pipeline da convincere a salvare al momento giusto.

Stato attuale (fase 1): se il secret non c'e', avvisa e lascia il valore committato, cosi' la
pipeline continua a funzionare esattamente come prima. Quando l'AppId sara' stato ruotato e il
secret configurato, il valore nel repo va svuotato e questo script deve diventare BLOCCANTE — una
build senza AppId non si connette a niente, e un fallimento chiaro e' meglio di un gioco muto.
"""

import os
import re
import sys

ASSET = "Assets/Photon/Fusion/Resources/PhotonAppSettings.asset"
FIELD = re.compile(r"(?m)^(\s*AppIdFusion:).*$")


def main():
    app_id = os.environ.get("PHOTON_APP_ID", "").strip()
    if not app_id:
        # ::warning:: e non ::error:: finche' il valore committato e' ancora quello buono.
        print("::warning::PHOTON_APP_ID non configurato: resta il valore committato, "
              "che e' leggibile da chiunque perche' la repo e' pubblica.")
        return 0

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
