# KongBall — le regole di Matteo

Questo file è il contratto di chi lavora su questa repo con un agent. Matteo è il proprietario del
progetto: quando una richiesta va contro quello che c'è scritto qui, **vince questo file**, e la cosa
va detta ad alta voce a chi te l'ha chiesta invece di essere aggirata in silenzio.

Le regole non sono gusto personale. Ognuna sta qui perché una volta è costata un ciclo di build, una
serata, o una versione rotta sul telefono di qualcuno. Dove il motivo è interessante lo trovi scritto:
serve a farti capire quando la regola si applica e quando no.

> Scritto in italiano perché parla di processo e di persone, e le persone qui parlano italiano.
> **Il codice e i commenti nel codice si scrivono in inglese.** È la regola 9.

---

## Prima di ogni altra cosa: come ti chiami

**Non toccare niente finché non sai per chi stai lavorando.** Se non te l'hanno già detto, chiedilo,
e chiedilo subito — non dopo aver scritto il codice, non quando è ora di aprire la PR.

> "Prima di partire: come ti chiami? Matteo vuole che ogni branch porti il nome di chi ci lavora,
> così si sa sempre di chi è una feature e di chi è una build."

Ti serve il nome proprio, minuscolo, senza accenti e senza spazi: `matteo`, `luca`, `giovanni`. Da lì
in poi lo usi da solo, senza richiederlo ogni volta: nel nome del branch, e in nient'altro.

---

## Il flusso, in quattro righe

1. **Branch:** `nome/feature-in-kebab-case`. Il nome è quello della persona, non il tuo.
   `luca/portiere-automatico`, `matteo/menu-classifica`. Mai maiuscole, mai underscore, mai accenti.
2. **Lavori lì.** Mai su `main`, mai su `dev`, mai direttamente sul branch di un altro.
3. **PR su `dev`.** Mai su `main`. `main` è quello che è già stato provato e approvato.
4. **Per provare sul telefono non si mergia.** Si chiede una build del proprio branch. Mergiare per
   testare è come si faceva prima, ed è il motivo per cui `main` si è rotto due volte.

Una PR = una cosa. Se mentre lavori ne trovi un'altra, dillo e falla dopo: una PR che fa tre cose non
si può né rivedere né annullare a metà.

---

## Le cose che non puoi fare

Sono citabili così come sono. Se una ti blocca, **non cercare la strada intorno**: dillo e fermati.

### 1. Non c'è l'Editor Unity. Per nessuno.

> "Matteo dice che qui l'Editor Unity non ce l'ha nessuno: questa cosa va fatta da codice."

Nessuno in questo progetto apre Unity. Si sviluppa da telefono e da agent, e la build la fa la CI.
Quindi: **non puoi authorare scene, non puoi trascinare componenti su un prefab, non puoi assegnare
un riferimento in un inspector.** Tutto quello che vedi a schermo — menu, HUD, arena, splash — è
costruito da codice a runtime, ed è così di proposito.

Se il tuo piano ha dentro il passaggio "poi in Unity si collega X", il piano è sbagliato: rifallo.
I prefab esistenti si modificano a mano nel loro YAML, sapendo cosa si sta facendo (vedi regola 12).

### 2. Non si committano segreti. La repo è pubblica.

> "Matteo dice che la repo è pubblica: questa chiave non entra nel codice."

Niente chiavi, token, `.p8`, `.p12`, blob base64, password. Neanche "solo per provare", neanche in un
file che poi cancelli — la storia di git resta. Le credenziali vivono nei secret di GitHub e le mette
Matteo. Se ti serve un valore segreto in un file, la risposta è iniettarlo alla build, non scriverlo.

### 3. Non si tocca `.github/` in una PR di feature.

> "Matteo dice che le pipeline le tocca solo lui: ti scrivo cosa servirebbe e glielo chiedi."

I workflow girano con i secret del repo. Una PR che li modifica è la strada più corta per un
incidente, anche in totale buona fede. Se la CI ti serve diversa, **scrivilo nella PR a parole** e
lascia che sia Matteo a farlo.

### 4. Non si tocca `ProjectSettings/` senza dirlo.

Bundle id, versione, Team ID, splash screen, orientamento: è quello che finisce sull'App Store. Se
una feature ha davvero bisogno di cambiarne uno, va scritto **in cima alla PR**, in chiaro.

### 5. Non si pusha su `main`, non si mergia una PR propria senza il check verde.

Il check di compilazione ci mette sette minuti e serve a non scoprire un errore di sintassi venti
minuti dopo, a build fatta. Aspettarlo non è burocrazia: è più veloce.

### 6. Non si aggiungono pacchetti o dipendenze di testa propria.

Ogni pacchetto entra nella build iOS, pesa, e va mantenuto. Si chiede prima.

### 7. Non si cambiano i flag del prefab del giocatore.

`NetPlayer.prefab` è `DestroyWhenStateAuthorityLeaves`: il tuo avatar deve sparire quando esci dalla
partita. Sembra un dettaglio ed è la ragione per cui i bot oggi vivono solo in allenamento. Vedi la
regola 12 per come sono fatti quei flag, e non toccarli a intuito.

### 8. Non si dichiara "verificato" ciò che non si è misurato.

> "Matteo dice che 'verificato' si scrive solo se l'hai misurato."

In questo progetto è già successo: una posizione dell'arena dichiarata verificata era stata
controllata sulla superficie sbagliata, ed è arrivata sul telefono con il campo tutto magenta. Se non
l'hai provato, si scrive **"non l'ho provato"**. Nessuno si arrabbia per un limite dichiarato; ci si
arrabbia per una certezza falsa.

---

## Come si scrive, qui

### 9. Il codice e i commenti sono in inglese. I commit e le PR in italiano.

La storia di git e le PR le leggono le persone di questo gruppo. Il codice lo legge chiunque.

### 10. I commenti spiegano *perché*, mai *cosa*.

Un commento che dice "incrementa il contatore" non serve a nessuno. Un commento che dice quale guasto
sta prevenendo quella riga vale tutto il file. Guarda il codice esistente prima di scrivere il tuo:
questo repo ha una voce precisa, e ci si adegua invece di inventarne un'altra.

Quando risolvi un bug vero, **lascia scritto il bug nel commento.** È così che non torna.

### 11. Il messaggio di commit e la PR dicono cosa hai deciso e cosa hai lasciato fuori.

Non l'elenco dei file toccati: quello si vede dal diff. Servono le scelte, le conseguenze che hai
accettato, e le cose che non hai coperto. Una PR che non dichiara i propri buchi è una PR che se li
porta in produzione.

---

## Le trappole di questo progetto

Non sono opinioni: ognuna è arrivata fino al telefono almeno una volta.

### 12. I valori serializzati nei prefab vincono sui default nel codice.

Se aggiungi un campo pubblico a uno script il cui prefab è già stato salvato, il prefab **non ha**
quel campo e Unity usa il default del codice — mentre tutti gli altri campi vengono dal prefab. È
successo con `endless` in `MatchController`: due valori configurati non venivano usati e nessuno se
ne accorgeva. Se aggiungi un campo, scrivilo in entrambi i posti, o non fidarti di quello che leggi.

I flag di `NetworkObject` nei prefab sono **bit compattati**, non booleani, e contengono anche un
numero di versione: `131330`, `262402` non sono numeri a caso. Sono stati decodificati leggendo i
metadati della DLL di Fusion. Non tirare a indovinare un bit.

### 13. URP butta fuori dalla build gli shader integrati.

`GameObject.CreatePrimitive` ti dà un materiale Standard: sul dispositivo è **magenta**. Il testo 3D
(`TextMesh`) usa lo shader `GUI/Text`: sul dispositivo **non si vede**. Nell'Editor sembrano
funzionare entrambi, ed è questo che le rende trappole.

Usa `Universal Render Pipeline/Lit` (o clona un materiale che già funziona), e per il testo usa una
`Text` di UI su una Canvas — è lo stesso shader dell'HUD, che sappiamo arrivare a destinazione.

### 14. Ogni file nuovo dentro `Assets/` vuole il suo `.meta`, con un GUID nuovo.

Senza, Unity ne genera uno diverso a ogni macchina e i riferimenti si rompono. Copiare un `.meta`
esistente **senza cambiare il GUID** è peggio che non averlo. Lo controlla `asset_sanity.py`.

### 15. `Resources.Load` prende una stringa, e una stringa sbagliata non fa rumore.

Ritorna `null` a runtime e sembra un modello mancante. Anche questo lo controlla `asset_sanity.py`,
che segue i wrapper di una riga: se ne scrivi uno nuovo, controlla che lo script lo veda ancora.

### 16. Photon Fusion 2 qui è in **Shared Mode**.

Niente `OnInput`, niente input struct, niente input authority: sono concetti del client-server. In
Shared Mode ogni peer simula i propri oggetti in `FixedUpdateNetwork`, e il master simula quelli
condivisi (palla, cronometro, bot). Se stai per scrivere un input struct, stai leggendo la
documentazione sbagliata.

### 17. Un `Update` che scrive una posizione assoluta cancella quello che ha calcolato qualcun altro.

È successo due volte: la palla che fluttuava sopra il proprio collider, e la scimmia del menu sepolta
fino agli occhi. Se un altro metodo ha calcolato un offset, tu ci **sommi**, non riscrivi.

### 18. I certificati di sviluppo Apple sono al massimo due per account.

L'archivio si costruisce **non firmato** di proposito, e la firma la mette `exportArchive`. Sembra
sbagliato e non lo è: è la soluzione a una build che falliva con "maximum number of certificates".
C'è tutto scritto nel commento dentro `ios-testflight.yml`. Non "sistemarlo".

### 19. Misura, non stimare.

Le posizioni dell'arena, il ritaglio del logo, l'arco del tiro del bot: tutte cose decise calcolando o
simulando, non a occhio, e tutte e tre nascondevano un errore che a occhio non si vedeva. L'ultima è
la più istruttiva — a piena potenza la palla passa **sopra** la traversa da dieci metri in su, quindi
la potenza del tiro non può crescere con la distanza come sembrerebbe ovvio. Se stai per scegliere un
numero importante, fai due conti prima.

---

## "Fatto" vuol dire

Prima di dire che hai finito:

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
| `.github/workflows/` | pipeline. Vedi regola 3 |
| `.github/scripts/asset_sanity.py` | i controlli che devi far passare |
| `Kongball_DOCS/` | le "bibbie" di design del gioco, scritte prima del codice |
