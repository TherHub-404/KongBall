#!/usr/bin/env python3
"""Dice in gruppo cosa e' cambiato nella build appena caricata su TestFlight.

Cosa racconta, e perche' proprio quello: non il commit del merge che ha fatto partire la build, ma
il DELTA rispetto all'ultima build riuscita. E' la differenza che conta per chi installa — se una
build fallisce, o se qualcuno mergia solo documentazione (che non fa partire nulla), il messaggio
successivo racconta comunque tutto cio' che e' cambiato rispetto a quello che avete sul telefono.

Il commit di partenza lo chiede a GitHub: l'ultima run riuscita di questo stesso workflow. Cosi' non
serve la storia git completa nel checkout, e nemmeno il permesso di scrivere tag nel repo.

Manda anche i FALLIMENTI, ed e' meta' del valore: su un `dev` condiviso, AGENTS.md dice che una build
rotta e' di chi ha mergiato e va sistemata subito. Un messaggio in gruppo lo rende immediato, e
TestFlight non puo' farlo.

Non fa MAI fallire una build. Una notifica persa non deve costare venti minuti di runner: qualunque
cosa vada storta qui diventa un avviso e l'uscita e' 0.
"""

import html
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

GITHUB_API = "https://api.github.com"
MAX_RIGHE = 15
TIMEOUT = 30


def warn(msg):
    print("::warning::" + msg)


def gh(path):
    req = urllib.request.Request(
        GITHUB_API + path,
        headers={
            "Authorization": "Bearer " + os.environ.get("GITHUB_TOKEN", ""),
            "Accept": "application/vnd.github+json",
            "User-Agent": "kongball-ci",
        },
    )
    with urllib.request.urlopen(req, timeout=TIMEOUT) as r:
        return json.load(r)


def commit_build_precedente(repo, workflow, run_id):
    """L'head_sha dell'ultima build riuscita che non sia questa."""
    runs = gh("/repos/%s/actions/workflows/%s/runs?status=success&per_page=10"
              % (repo, workflow)).get("workflow_runs", [])
    for r in runs:
        if str(r.get("id")) != str(run_id):
            return r.get("head_sha")
    return None


def changelog(repo, base, head):
    """I titoli dei commit tra due punti. Via API e non via git: il checkout e' superficiale."""
    if not base or base == head:
        return []
    data = gh("/repos/%s/compare/%s...%s" % (repo, base, head))
    righe = []
    for c in data.get("commits", []):
        titolo = c.get("commit", {}).get("message", "").split("\n", 1)[0].strip()
        if titolo:
            righe.append(titolo)
    return righe


def chi(stamp):
    """Il timbro della build, "nome/feature", reso leggibile. Vuoto se non c'e'."""
    stamp = (stamp or "").strip()
    if not stamp:
        return None
    if "/" in stamp:
        autore, _, feature = stamp.partition("/")
        return "<b>%s</b> · %s" % (html.escape(autore), html.escape(feature))
    return "<b>%s</b>" % html.escape(stamp)


def messaggio(esito, stamp, righe, run_url):
    firma = chi(stamp)
    if esito != "success":
        testo = ["\U0001F534 <b>Build rotta su dev</b>"]
        if firma:
            testo.append("di " + firma)
        testo.append('<a href="%s">apri il log</a>' % html.escape(run_url))
        # Non l'elenco delle modifiche: quando e' rossa serve sapere DI CHI e' e dove guardare.
        return "\n".join(testo)

    testo = ["\U0001F3C1 <b>Nuova build su TestFlight</b>"]
    if firma:
        testo.append("di " + firma)
    if righe:
        testo.append("")
        for r in righe[:MAX_RIGHE]:
            testo.append("• " + html.escape(r))
        se_altri = len(righe) - MAX_RIGHE
        if se_altri > 0:
            testo.append("… e altri %d" % se_altri)
    testo.append("")
    testo.append('<a href="%s">log della build</a>' % html.escape(run_url))
    return "\n".join(testo)


def invia(token, chat_id, testo):
    """Ritorna il titolo della chat in cui e' finito il messaggio, per confermarlo nel log."""
    corpo = urllib.parse.urlencode({
        "chat_id": chat_id,
        "text": testo,
        "parse_mode": "HTML",
        "disable_web_page_preview": "true",
    }).encode("utf-8")
    url = "https://api.telegram.org/bot%s/sendMessage" % token
    req = urllib.request.Request(url, data=corpo)
    with urllib.request.urlopen(req, timeout=TIMEOUT) as r:
        risposta = json.load(r)
    return risposta.get("result", {}).get("chat", {}).get("title") or "?" 


def main():
    token = os.environ.get("TELEGRAM_BOT_TOKEN", "").strip()
    chat_id = os.environ.get("TELEGRAM_CHAT_ID", "").strip()
    esito = os.environ.get("BUILD_OUTCOME", "success").strip()
    run_url = os.environ.get("RUN_URL", "").strip()
    repo = os.environ.get("GITHUB_REPOSITORY", "").strip()
    workflow = os.environ.get("WORKFLOW_FILE", "ios-testflight.yml").strip()
    run_id = os.environ.get("GITHUB_RUN_ID", "").strip()
    sha = os.environ.get("GITHUB_SHA", "").strip()
    secco = os.environ.get("TELEGRAM_DRY_RUN", "") == "1"

    if not secco and (not token or not chat_id):
        warn("TELEGRAM_BOT_TOKEN o TELEGRAM_CHAT_ID non configurati: nessuna notifica.")
        return 0

    stamp = ""
    try:
        with open("Assets/Resources/BuildStamp.txt", encoding="utf-8") as f:
            stamp = f.read()
    except OSError:
        pass   # il timbro e' un extra, non un requisito

    righe = []
    if esito == "success":
        try:
            base = commit_build_precedente(repo, workflow, run_id)
            righe = changelog(repo, base, sha)
        except Exception as e:
            warn("changelog non ricostruito (%s): mando il messaggio senza." % type(e).__name__)

    testo = messaggio(esito, stamp, righe, run_url)

    if secco:
        print(testo)
        return 0

    try:
        dove = invia(token, chat_id, testo)
        print("Notifica Telegram inviata a '%s' (%d righe di changelog)." % (dove, len(righe)))
    except urllib.error.HTTPError as e:
        # Codice, motivo e la `description` di Telegram — che e' l'unica cosa che dice DAVVERO cosa
        # non va ("chat not found", "can't parse entities", "bot was kicked..."). Senza, un errore
        # costa una seconda build per capirlo.
        #
        # Ma mai str(e) ne' e.url: contengono l'URL, e l'URL contiene il TOKEN. In una repo pubblica
        # quel log lo legge chiunque.
        motivo = ""
        try:
            motivo = " — " + str(json.load(e).get("description", ""))
        except Exception:
            pass
        warn("Telegram ha risposto %s %s%s." % (e.code, e.reason, motivo))
    except Exception as e:
        warn("Notifica Telegram non inviata (%s)." % type(e).__name__)
    return 0


if __name__ == "__main__":
    sys.exit(main())
