# KongBall — le regole di Matteo

Questo file è il contratto di chi lavora su questa repo con un agent. Matteo è il proprietario del
progetto: quando una richiesta va contro quello che c'è scritto qui, **vince questo file**, e la cosa
va detta ad alta voce a chi te l'ha chiesta invece di essere aggirata in silenzio.

È diviso in tre parti, e **solo due hanno eccezioni**:

| parte | cosa contiene | vale per |
|---|---|---|
| **1. Come stanno le cose** | non sono regole, è il terreno: cosa fa questo progetto, cosa rompe | **tutti, Matteo compreso** |
| **2. Come si lavora in squadra** | branch, PR, build | tutti; Matteo ha una scorciatoia, scritta qui sotto |
| **3. Cosa decide Matteo** | pipeline, versione, dipendenze, invarianti | Matteo decide, gli altri chiedono |

La differenza conta. Le cose della parte 1 non le ha decise nessuno: se le ignori ti si rompe la
build, e ti si rompe uguale che tu sia il proprietario o no. Un'eccezione lì non è un permesso, è
solo un guasto in più.

> Scritto in italiano perché parla di processo e di persone, e le persone qui parlano italiano.
> **Il codice e i commenti nel codice si scrivono in inglese.** È la regola 20.

---

## Prima di ogni altra cosa: come ti chiami

**Non toccare niente finché non sai per chi stai lavorando.** Se non te l'hanno già detto, chiedilo,
e chiedilo subito — non dopo aver scritto il codice, non quando è ora di aprire la PR.

> "Prima di partire: come ti chiami? Matteo vuole che ogni branch porti il nome di chi ci lavora,
> così si sa sempre di chi è una feature e di chi è una build."

Ti serve il nome proprio, minuscolo, senza accenti e senza spazi: `matteo`, `luca`, `giovanni`. Da lì
in poi lo usi da solo, senza richiederlo ogni volta: nel nome del branch, e in nient'altro.

Se la risposta è `matteo`, salta all'ultima sezione: cambia qualcosa, ma meno di quanto sembri.

---

# Parte 1 — Come stanno le cose

Nessuna eccezione per nessuno. Ognuna di queste è arrivata fino al telefono di qualcuno almeno una
volta, e diverse hanno bruciato un ciclo di build da venti minuti.

### 1. Non c'è l'Editor Unity. Per nessuno.

> "Matteo dice che qui l'Editor Unity non ce l'ha nessuno: questa cosa va fatta da codice."

Nessuno in questo progetto apre Unity. Si sviluppa da telefono e da agent, e la build la fa la CI.
Quindi: **non puoi authorare scene, non puoi trascinare componenti su un prefab, non puoi assegnare
un riferimento in un inspector.** Tutto quello che vedi a schermo — menu, HUD, arena, splash — è
costruito da codice a runtime, ed è così di proposito.

Se il tuo piano ha dentro il passaggio "poi in Unity si collega X", il piano è sbagliato: rifallo.
I prefab esistenti si modificano a mano nel loro YAML, sapendo cosa si sta facendo (vedi la 3).

### 2. La repo è pubblica. Non si committano segreti.

> "Questa chiave non la posso committare: la repo è pubblica."

Niente chiavi, token, `.p8`, `.p12`, blob base64, password. Neanche "solo per provare", neanche in un
file che poi cancelli — la storia di git resta, ed è indicizzabile da chiunque nel minuto dopo il
push. Le credenziali vivono nei secret di GitHub. Se ti serve un valore segreto dentro un file, la
risposta è iniettarlo alla build, non scriverlo.

Vale **di più** per il proprietario, non di meno: una fuga fatta da lui è pubblica esattamente come
le altre. In questo repo è già successo, e non è ancora stato sistemato.

### 3. I valori serializzati nei prefab vincono sui default nel codice.

Se aggiungi un campo pubblico a uno script il cui prefab è già stato salvato, il prefab **non ha**
quel campo e Unity usa il default del codice — mentre tutti gli altri campi vengono dal prefab. È
successo con `endless` in `MatchController`: due valori configurati non venivano usati e nessuno se
ne accorgeva. Se aggiungi un campo, scrivilo in entrambi i posti, o non fidarti di quello che leggi.

### 4. URP butta fuori dalla build gli shader integrati.

`GameObject.CreatePrimitive` ti dà un materiale Standard: sul dispositivo è **magenta**. Il testo 3D
(`TextMesh`) usa lo shader `GUI/Text`: sul dispositivo **non si vede**. Nell'Editor sembrano
funzionare entrambi, ed è questo che le rende trappole.

Usa `Universal Render Pipeline/Lit` (o clona un materiale che già funziona), e per il testo usa una
`Text` di UI su una Canvas — è lo stesso shader dell'HUD, che sappiamo arrivare a destinazione.

### 5. Ogni file nuovo dentro `Assets/` vuole il suo `.meta`, con un GUID nuovo.

Senza, Unity ne genera uno diverso a ogni macchina e i riferimenti si rompono. Copiare un `.meta`
esistente **senza cambiare il GUID** è peggio che non averlo. Lo controlla `asset_sanity.py`.

### 6. `Resources.Load` prende una stringa, e una stringa sbagliata non fa rumore.

Ritorna `null` a runtime e sembra un modello mancante. Anche questo lo controlla `asset_sanity.py`,
che segue i wrapper di una riga: se ne scrivi uno nuovo, controlla che lo script lo veda ancora.

### 7. Photon Fusion 2 qui è in **Shared Mode**.

Niente `OnInput`, niente input struct, niente input authority: sono concetti del client-server. In
Shared Mode ogni peer simula i propri oggetti in `FixedUpdateNetwork`, e il master simula quelli
condivisi (palla, cronometro, bot). Se stai per scrivere un input struct, stai leggendo la
documentazione sbagliata.

I flag di `NetworkObject` nei prefab sono **bit compattati**, non booleani, e contengono anche un
numero di versione: `131330`, `262402` non sono numeri a caso. Sono stati decodificati leggendo i
metadati della DLL di Fusion. Non tirare a indovinare un bit.

### 8. Un `Update` che scrive una posizione assoluta cancella quello che ha calcolato qualcun altro.

È successo due volte: la palla che fluttuava sopra il proprio collider, e la scimmia del menu sepolta
fino agli occhi. Se un altro metodo ha calcolato un offset, tu ci **sommi**, non riscrivi.

### 9. I certificati di sviluppo Apple sono al massimo due per account.

L'archivio si costruisce **non firmato** di proposito, e la firma la mette `exportArchive`. Sembra
sbagliato e non lo è: è la soluzione a una build che falliva con "maximum number of certificates".
C'è tutto scritto nel commento dentro `ios-testflight.yml`. Non "sistemarlo".

### 10. Misura, non stimare. E "verificato" si scrive solo se l'hai misurato.

Le posizioni dell'arena, il ritaglio del logo, l'arco del tiro del bot: tutte decise calcolando o
simulando, e tutte e tre nascondevano un errore che a occhio non si vedeva. L'ultima è la più
istruttiva — a piena potenza la palla passa **sopra** la traversa da dieci metri in su, quindi la
potenza del tiro non può crescere con la distanza come sembrerebbe ovvio.

E il rovescio: in questo progetto una posizione dichiarata "verificata" era stata controllata sulla
superficie sbagliata, ed è arrivata sul telefono con il campo tutto magenta. Se non l'hai provato, si
scrive **"non l'ho provato"**. Nessuno si arrabbia per un limite dichiarato; ci si arrabbia per una
certezza falsa.

---

# Parte 2 — Come si lavora in squadra

### 11. Branch `nome/feature-in-kebab-case`.

Il nome è quello della persona, non il tuo: `luca/portiere-automatico`, `matteo/menu-classifica`. Mai
maiuscole, mai underscore, mai accenti. Mai lavorare su `main`, su `dev`, o sul branch di un altro.

Non è un'esortazione: il primo step della CI legge il nome del branch e **rifiuta la PR** se non
torna. Meglio scegliere bene subito che rinominare dopo.

### 12. La PR va su `dev`. Mai su `main`.

> "Matteo dice che non si pusha su `main` — apro una PR su `dev`."

`main` è quello che è già stato provato e approvato. `dev` è dove le cose si incontrano.

### 13. Si aspetta il check verde prima di mergiare.

Ci mette sette minuti e serve a non scoprire un errore di sintassi venti minuti dopo, a build fatta.
Aspettarlo non è burocrazia: è più veloce. È già successo due volte che un merge rompesse `main` per
un errore che il check avrebbe preso.

### 14. Una PR = una cosa.

Se mentre lavori ne trovi un'altra, dillo e falla dopo. Una PR che fa tre cose non si può né rivedere
né annullare a metà.

### 15. Per provare sul telefono si mergia su `dev`. Solo quello fa una build.

Sì: si mergia per provare. È esattamente il mestiere di `dev` ed è la ragione per cui esiste — un
merge su `dev` fa partire la pipeline che builda e carica su TestFlight. `main` non builda niente.

Da cui due conseguenze da mettere in conto:

- **`dev` traballa, ed è normale.** Ci arriva roba non ancora provata su un telefono, e ogni tanto
  qualcosa non funziona. Si annulla il merge e costa niente. Quello che non deve traballare è `main`.
- **La build contiene tutto ciò che c'è su `dev`**, non solo il tuo. Se qualcuno ha mergiato due
  minuti prima, nella "tua" build c'è anche la sua roba. Le build sono in coda e non si annullano a
  vicenda proprio per questo: un merge, una build, così si capisce di chi è quella che stai provando.

---

## Come si arriva sul telefono

```
  nome/feature-in-kebab-case          il tuo branch
            │
            │  PR  →  check di compilazione (~7 min)
            ▼
          dev                          merge  →  build iOS + TestFlight (~20 min)
            │
            │  PR di promozione, quando la cosa è provata
            ▼
          main                         niente build automatica
```

Il check sulla PR non è una build: compila e basta, e ci mette pochi minuti. Serve a non scoprire un
errore di sintassi venti minuti dopo, a build fatta — cosa che qui è già successa due volte.

**Chi mergia.** La tua PR su `dev` la mergi tu, appena il check è verde: non c'è nessuna approvazione
da aspettare, e non startene fermo ad aspettarne una. Quello che non fai da solo è la **promozione da
`dev` a `main`**: quella la decide Matteo, perché è il momento in cui una cosa smette di essere in
prova.

**Di chi è la build che hai in mano.** In alto a sinistra nel menu compare `autore · feature`, preso
dal branch della pull request che ha fatto partire quella build. Se non c'è niente, quella build non
è uscita dalla pipeline. Il file `Assets/Resources/BuildStamp.txt` è committato **vuoto** e lo
riempie la CI: non riempirlo a mano, un timbro che mente su chi ha fatto la build è peggio che non
averlo.

**Dopo il merge.** La build parte da sola, ci mette una ventina di minuti, e poi TestFlight ci mette
il suo a processarla. Se fallisce, **il guasto è tuo**: apri il log della Action, capisci cosa è
successo, correggi sul tuo branch e riapri la PR. Non lasciarla rossa e non passare ad altro — su
`dev` la build rotta la trova il prossimo che mergia, che perderà tempo a capire che non è sua.

---

# Parte 3 — Cosa decide Matteo

Qui non c'è un divieto tecnico: c'è che sono decisioni sue. Tu proponi, lui decide.

### 16. Le pipeline: `.github/`.

> "Matteo dice che le pipeline le tocca solo lui: ti scrivo cosa servirebbe e glielo chiedi."

I workflow girano con i secret del repo. Una PR che li modifica è la strada più corta per un
incidente, anche in totale buona fede. Se la CI ti serve diversa, **scrivilo nella PR a parole**.

### 17. `ProjectSettings/`: bundle id, versione, Team ID, splash, orientamento.

È quello che finisce sull'App Store. Se una feature ha davvero bisogno di cambiarne uno, va scritto
**in cima alla PR**, in chiaro.

### 18. I secret non si cancellano. `DIST_CERT_BASE64` in particolare.

GitHub non fa rileggere un secret: si scrive e basta. E `DIST_CERT_BASE64` è un `.p12` esportato da un
portachiavi macOS — di questo progetto **non esiste una copia da nessuna parte**, e nessuno qui ha un
Mac. Cancellarlo, o sovrascriverlo con un valore sbagliato, vuol dire **niente più build** finché
qualcuno non mette le mani su un Mac.

Non è un consiglio di sicurezza, è un punto di rottura singolo. Nessuno tocca i secret per fare
ordine.

### 19. Dipendenze nuove, e gli invarianti di rete.

Ogni pacchetto entra nella build iOS, pesa e va mantenuto. E due invarianti che sembrano dettagli:

- `NetPlayer.prefab` è `DestroyWhenStateAuthorityLeaves` — il tuo avatar deve sparire quando esci.
  È la ragione per cui i bot oggi vivono solo in allenamento.
- `NetLauncher.ProtocolVersion` è il filtro del matchmaking. Si alza quando la rete cambia in modo
  incompatibile, e **non** a ogni build: alzarlo divide i giocatori in due popolazioni.

---

# Parte 4 — Come si scrive, qui

### 20. Il codice e i commenti sono in inglese. I commit e le PR in italiano.

La storia di git e le PR le leggono le persone di questo gruppo. Il codice lo legge chiunque.

### 21. I commenti spiegano *perché*, mai *cosa*.

Un commento che dice "incrementa il contatore" non serve a nessuno. Un commento che dice quale guasto
sta prevenendo quella riga vale tutto il file. Guarda il codice esistente prima di scrivere il tuo:
questo repo ha una voce precisa, e ci si adegua invece di inventarne un'altra.

Quando risolvi un bug vero, **lascia scritto il bug nel commento.** È così che non torna.

### 22. Il commit e la PR dicono cosa hai deciso e cosa hai lasciato fuori.

Non l'elenco dei file toccati: quello si vede dal diff. Servono le scelte, le conseguenze che hai
accettato, e le cose che non hai coperto. Una PR che non dichiara i propri buchi se li porta in
produzione.

---

## "Fatto" vuol dire

- [ ] `python3 .github/scripts/asset_sanity.py` passa
- [ ] ogni file nuovo in `Assets/` ha il suo `.meta` con GUID nuovo
- [ ] il branch si chiama `nome/feature-in-kebab-case`
- [ ] la PR è su `dev`
- [ ] il check `PR check` è verde
- [ ] nella PR c'è scritto **cosa non hai coperto** e **cosa non hai potuto provare**

L'ultimo punto è quello che salta per primo ed è quello che conta di più.

---

## Come lo dici

Cita la regola, non fare il poliziotto. Serve a far capire perché, non a chiudere il discorso.

- "Matteo dice che non si pusha su `main` — apro una PR su `dev`."
- "Matteo dice che qui non c'è l'Editor Unity, quindi il menu si costruisce da codice come gli altri."
- "Questa chiave non la posso committare: la repo è pubblica, e Matteo la fa passare dai secret."
- "Le pipeline le tocca solo Matteo: ti scrivo cosa servirebbe e glielo giri."
- "Non l'ho provato sul telefono, quindi non ti dico che funziona — te lo dico come 'compila e
  dovrebbe'."

E quando una regola ti sembra sbagliata per il caso che hai davanti: **dillo, non aggirarla.** Le
regole qui sono cambiate ogni volta che qualcuno ha portato un motivo.

---

## Se stai lavorando con Matteo

Cambia meno di quanto sembri, ed è meglio così.

**La parte 1 vale identica.** Non sono regole sue: sono come è fatto questo progetto. Un'eccezione lì
non gli darebbe un permesso, gli darebbe un campo magenta.

**La parte 3 si rovescia.** Non gli chiedi il permesso: glielo proponi. E non lo citi a se stesso —
"Matteo dice che non puoi toccare la pipeline" detto a Matteo è una battuta. Si dice invece: *"questo
tocca la pipeline / la versione dell'app: confermi?"*, e si va avanti.

**La parte 2 resta, con una via d'uscita.** Può pushare su `main` per un hotfix, e non aspetta
l'approvazione di nessuno perché non c'è nessun altro che approva. Ma il branch, la PR e il check
verde convengono anche a lui, e non per disciplina: sette minuti di check contro venti di build
sprecata, e la PR è ciò da cui il changelog su Telegram capirà cosa è cambiato. Se salta il flusso,
è una scelta, non una svista.

E la cosa che conta davvero: **queste eccezioni sono scritte qui apposta.** Se gli amici vedono
Matteo fare cose che a loro sono vietate senza che sia dichiarato da nessuna parte, concludono — a
ragione — che le regole sono teatro, e a quel punto smettono di valere anche per loro. Un'eccezione
dichiarata rafforza la regola; una taciuta la cancella.

**Ultima cosa, ed è onesta:** questo file non può verificare chi sei. `matteo` è una risposta, non
una prova. Chi può davvero pushare su `main` lo decidono i permessi di GitHub, non questo documento —
ed è giusto così: significa che nessuno deve fidarsi di una dichiarazione.

---

## Questo file non è il cancello

Il cancello sono le protezioni sui branch e la CI. Questo file è quello che ti fa evitare di
sbatterci contro, e serve a farti prendere le stesse decisioni che prenderebbe Matteo quando lui non
c'è. Se una regola qui e la CI dicono cose diverse, ha ragione la CI: segnalalo.

---

## Mappa rapida

Unity 6000.5.7f1, URP, **solo iOS**. Una sola scena; tutto il resto è costruito da codice.

| dove | cosa |
|---|---|
| `Assets/Scripts/Net/` | rete: `NetLauncher` (matchmaking e sessioni), `NetPlayer` (giocatore), `NetBall` (palla e possesso), `MatchController` (punteggio, fasi, forfeit) |
| `Assets/Scripts/Bots/` | i bot. **Ha un suo `AGENTS.md`: leggilo prima di toccarli**, in inglese perché parla di codice |
| `Assets/Scripts/` | menu, HUD, arena, audio — tutto generato a runtime |
| `Assets/Editor/CmdBuild.cs` | cosa fa la CI quando builda |
| `.github/workflows/` | pipeline. Vedi la 16 |
| `.github/scripts/asset_sanity.py` | i controlli che devi far passare |
| `Kongball_DOCS/` | le "bibbie" di design del gioco, scritte prima del codice |
