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

Manda a Telegram e a Discord, a seconda di quali secret esistono: i FATTI si calcolano una volta
sola — chi ha fatto la build, cosa e' cambiato, com'e' andata — e poi si RENDONO due volte, perche'
Telegram vuole HTML e Discord vuole Markdown. Calcolarli due volte costerebbe due giri di chiamate a
GitHub e potrebbe produrre due messaggi che non dicono la stessa cosa.

Non fa MAI fallire una build. Una notifica persa non deve costare venti minuti di runner: qualunque
cosa vada storta qui diventa un avviso e l'uscita e' 0. E se una delle due destinazioni non e'
configurata non si lamenta: avvisa solo se non ce n'e' nessuna.
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


def dettaglio(errore, chiave):
    """Il messaggio d'errore del servizio, che e' l'unica cosa che dice cosa non va.

    Preso dal CORPO della risposta e non dall'eccezione: str(e) e e.url contengono l'URL, e l'URL
    contiene il token — sia per Telegram sia per il webhook di Discord.
    """
    try:
        return " — " + str(json.load(errore).get(chiave, ""))
    except Exception:
        return ""


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


def chi_html(stamp):
    """Il timbro reso in HTML, per Telegram. Vuoto se non c'e'."""
    stamp = (stamp or "").strip()
    if not stamp:
        return None
    if "/" in stamp:
        autore, _, feature = stamp.partition("/")
        return "<b>%s</b> · %s" % (html.escape(autore), html.escape(feature))
    return "<b>%s</b>" % html.escape(stamp)


# Discord interpreta il Markdown anche dentro gli embed, quindi un titolo di PR con un asterisco o un
# underscore mangerebbe la formattazione del messaggio. Non e' cosmetico: un carattere sbagliato puo'
# far sparire mezza riga.
#
# Solo quelli speciali A META' RIGA. Trattino, maggiore e cancelletto contano solo a INIZIO riga (lista,
# citazione, titolo) e le nostre righe cominciano sempre con "• " o con "di ": scapparli produrrebbe
# "sfondo\\-menu" a schermo, cioe' sporcizia in ogni messaggio per proteggere da un caso impossibile.
# Il backslash va per primo, altrimenti raddoppierebbe quelli aggiunti dopo.
MD_SPECIALI = "\\*_`~|"


def md(testo):
    for c in MD_SPECIALI:
        testo = testo.replace(c, "\\" + c)
    return testo


def chi_md(stamp):
    stamp = (stamp or "").strip()
    if not stamp:
        return None
    if "/" in stamp:
        autore, _, feature = stamp.partition("/")
        return "**%s** · %s" % (md(autore), md(feature))
    return "**%s**" % md(stamp)


def messaggio_telegram(esito, stamp, righe, run_url):
    firma = chi_html(stamp)
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


# Il bordo colorato dell'embed: verde o rosso, leggibile in un canale affollato senza leggere niente.
VERDE = 0x2ECC71
ROSSO = 0xE74C3C


def messaggio_discord(esito, stamp, righe, run_url):
    """Un embed, non testo semplice: il bordo colorato dice l'esito prima delle parole."""
    firma = chi_md(stamp)
    if esito != "success":
        corpo = ["di " + firma] if firma else []
        corpo.append("[apri il log](%s)" % run_url)
        return {"embeds": [{"title": "\U0001F534 Build rotta su dev",
                            "description": "\n".join(corpo),
                            "color": ROSSO}]}

    corpo = ["di " + firma] if firma else []
    if righe:
        corpo.append("")
        for r in righe[:MAX_RIGHE]:
            corpo.append("• " + md(r))
        se_altri = len(righe) - MAX_RIGHE
        if se_altri > 0:
            corpo.append("… e altri %d" % se_altri)
    corpo.append("")
    corpo.append("[log della build](%s)" % run_url)
    descrizione = "\n".join(corpo)
    # L'embed regge 4096 caratteri; con quindici righe non ci arriviamo, ma un titolo lunghissimo
    # non deve far rifiutare il messaggio.
    return {"embeds": [{"title": "\U0001F3C1 Nuova build su TestFlight",
                        "description": descrizione[:4000],
                        "color": VERDE}]}


def invia_telegram(token, chat_id, testo):
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


def invia_discord(webhook, payload):
    """Discord risponde 204 senza corpo quando ha accettato il messaggio."""
    corpo = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(webhook, data=corpo,
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=TIMEOUT) as r:
        return r.status


def main():
    tg_token = os.environ.get("TELEGRAM_BOT_TOKEN", "").strip()
    tg_chat = os.environ.get("TELEGRAM_CHAT_ID", "").strip()
    discord = os.environ.get("DISCORD_WEBHOOK_URL", "").strip()

    esito = os.environ.get("BUILD_OUTCOME", "success").strip()
    run_url = os.environ.get("RUN_URL", "").strip()
    repo = os.environ.get("GITHUB_REPOSITORY", "").strip()
    workflow = os.environ.get("WORKFLOW_FILE", "ios-testflight.yml").strip()
    run_id = os.environ.get("GITHUB_RUN_ID", "").strip()
    sha = os.environ.get("GITHUB_SHA", "").strip()
    secco = os.environ.get("NOTIFY_DRY_RUN", "") == "1"

    verso_telegram = bool(tg_token and tg_chat)
    verso_discord = bool(discord)

    # Avvisa solo se non c'e' NESSUNA destinazione. Lamentarsi di quella non configurata a ogni build
    # trasformerebbe un avviso utile in rumore che si impara a ignorare.
    if not secco and not verso_telegram and not verso_discord:
        warn("Nessuna destinazione configurata: servono TELEGRAM_BOT_TOKEN piu' TELEGRAM_CHAT_ID, "
             "oppure DISCORD_WEBHOOK_URL. Nessuna notifica inviata.")
        return 0

    stamp = ""
    try:
        with open("Assets/Resources/BuildStamp.txt", encoding="utf-8") as f:
            stamp = f.read()
    except OSError:
        pass   # il timbro e' un extra, non un requisito

    # I FATTI, una volta sola: due destinazioni non devono costare due giri di chiamate a GitHub, ne'
    # rischiare di raccontare due cose diverse.
    righe = []
    if esito == "success":
        try:
            base = commit_build_precedente(repo, workflow, run_id)
            righe = changelog(repo, base, sha)
        except Exception as e:
            warn("changelog non ricostruito (%s): mando il messaggio senza." % type(e).__name__)

    if secco:
        print("--- Telegram ---")
        print(messaggio_telegram(esito, stamp, righe, run_url))
        print("\n--- Discord ---")
        print(json.dumps(messaggio_discord(esito, stamp, righe, run_url),
                         ensure_ascii=False, indent=2))
        return 0

    if verso_telegram:
        try:
            dove = invia_telegram(tg_token, tg_chat, messaggio_telegram(esito, stamp, righe, run_url))
            print("Telegram: inviata a '%s' (%d righe di changelog)." % (dove, len(righe)))
        except urllib.error.HTTPError as e:
            # Codice, motivo e la `description` di Telegram — l'unica cosa che dice DAVVERO cosa non
            # va. Ma mai str(e) ne' e.url: contengono l'URL, e l'URL contiene il TOKEN. In una repo
            # pubblica quel log lo legge chiunque.
            warn("Telegram ha risposto %s %s%s." % (e.code, e.reason, dettaglio(e, "description")))
        except Exception as e:
            warn("Telegram: non inviata (%s)." % type(e).__name__)

    if verso_discord:
        try:
            invia_discord(discord, messaggio_discord(esito, stamp, righe, run_url))
            print("Discord: inviata (%d righe di changelog)." % len(righe))
        except urllib.error.HTTPError as e:
            # Stessa cautela, per la stessa ragione: l'URL del webhook Discord CONTIENE il suo token.
            warn("Discord ha risposto %s %s%s." % (e.code, e.reason, dettaglio(e, "message")))
        except Exception as e:
            warn("Discord: non inviata (%s)." % type(e).__name__)

    return 0


if __name__ == "__main__":
    sys.exit(main())
