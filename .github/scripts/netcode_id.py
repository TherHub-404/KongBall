#!/usr/bin/env python3
"""L'impronta del netcode di questa build, per non far incrociare build incompatibili.

Il problema che risolve: `NetLauncher.ProtocolVersion` e' un intero che si alza A MANO quando la rete
cambia in modo incompatibile. Con una persona sola funzionava; con quattro che buildano piu' volte al
giorno nessuno se lo ricordera', e due build con netcode diverso si troverebbero nella stessa stanza
a vedere cose incoerenti — un guasto che sembra un bug del gioco e non un disallineamento.

Cosa fa: prende i file che DECIDONO la compatibilita' di rete, li riduce a otto caratteri, e la
pipeline li scrive in un asset che il gioco legge. Due build si incontrano solo se quell'impronta
combacia, cioe' solo se il loro netcode e' identico byte per byte.

Perche' l'impronta e non il commit: due build che differiscono solo per il menu o per un colore DEVONO
potersi incontrare. Prendere il commit spaccherebbe la popolazione a ogni merge, anche quando la rete
non e' cambiata di una riga, e la gente si troverebbe a non potersi vedere per un motivo che non c'e'.

L'elenco e' esplicito di proposito: e' una decisione da poter rileggere, non un glob da indovinare.
"""

import hashlib
import os
import sys

# Cosa decide se due peer possono giocare insieme.
PERCORSI = [
    "Assets/Scripts/Net",          # il netcode: launcher, giocatore, palla, controller di partita
    "Assets/Scripts/Bots",         # il master li simula: cambiano cio' che fa l'autorita'
    "Assets/Scripts/GameEnums.cs", # MatchMode viaggia come filtro di matchmaking
    "Assets/Prefabs",              # prefab di rete: flag e ordine dei NetworkBehaviour
    "Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion",  # tick rate, PlayerCount, feature
]

USCITA = "Assets/Resources/NetcodeId.txt"
CIFRE = 8


def files():
    trovati = []
    for p in PERCORSI:
        if os.path.isfile(p):
            trovati.append(p)
        elif os.path.isdir(p):
            for radice, _, nomi in os.walk(p):
                for n in nomi:
                    trovati.append(os.path.join(radice, n))
        else:
            print("::error::%s non esiste: l'impronta del netcode sarebbe incompleta." % p)
            return None
    # Ordinati: l'impronta deve dipendere dal contenuto, non dall'ordine in cui il filesystem
    # restituisce i nomi.
    return sorted(trovati)


def main():
    elenco = files()
    if elenco is None:
        return 1

    h = hashlib.sha256()
    for p in elenco:
        # Anche il PERCORSO entra nell'impronta: spostare un file cambia la compatibilita' tanto
        # quanto cambiarne il contenuto.
        h.update(p.replace(os.sep, "/").encode("utf-8"))
        h.update(b"\0")
        with open(p, "rb") as f:
            h.update(f.read())
        h.update(b"\0")

    impronta = h.hexdigest()[:CIFRE]
    with open(USCITA, "w", encoding="utf-8") as f:
        f.write(impronta)

    print("Impronta netcode: %s (%d file)" % (impronta, len(elenco)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
