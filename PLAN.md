# Rocket Slam: plán hry pro session Clauda

> **Stav:** revize 3 (2026-11-18). Nástroje nainstalované, repozitář a Unity projekt založené. Otevřené otázky jsou na konci (sekce 8).
> **Účel:** ukázka, co Claude zvládne sám: napíše hru v Unity, vygeneruje 3D modely přes
> Blender skripty, stáhne volné (CC0) assety, otestuje a sestaví hru.
> Člověk jen připraví nástroje a účty, pak hraje a dává zpětnou vazbu.
>
> Repozitář: `~/Documents/GIT/hhvitek/hhvitek-rocket-slam` (jedno repo pro Unity, Blender i další aplikace).
> Nová session Clauda otevřená v kořeni repa dostane pokyn: *„Přečti PLAN.md a začni milníkem M0.“*
> Hlavní session pracuje jako **orchestrátor se subagenty** (sekce 4) a **hlídá limity
> předplatného** s automatickým zastavením a obnovením (sekce 4B).

---

## 1. Co máš udělat ty (checklist před startem)

### 1a. Účty
| Účet | Proč | Stav |
|---|---|---|
| **Unity ID** + licence **Unity Personal** | Unity Hub a editor | ✅ hotovo (headless licence ověřena) |
| **GitHub** (`hhvitek`) | vzdálený repozitář | existuje, repo ještě založit (1c) |
| Claude | práce na hře | stávající předplatné |

Další účty nejsou potřeba. Kenney, Poly Haven a ambientCG se stahují bez registrace.

### 1b. Instalace
| Co | Kde | Stav |
|---|---|---|
| **Unity Hub 4.0.1** | apt repozitář Unity | ✅ |
| **Unity Editor 6000.6.9f1** (vydání *Supported*) | `~/Unity/Hub/Editor/6000.6.9f1/`, moduly Linux, Windows (Mono), WebGL | ✅ ověřeno i z příkazové řádky |
| **Blender 5.3.0** | `~/Documents/Blender/`, příkaz `blender` (symlink v `~/.local/bin`) | ✅ ověřeno i headless (`bpy`) |

### 1c. Založení projektu
1. ✅ Repo `~/Documents/GIT/hhvitek/hhvitek-rocket-slam` s `git init`, `.gitignore`, `.gitattributes`, README, `CLAUDE.md` (pravidla pro session) a strukturou složek (níže).
2. ✅ Unity projekt v `unity/` ze šablony **Universal 3D** (URP), název produktu *Rocket Slam*.
3. **Na GitHubu vytvoř prázdný soukromý repozitář `hhvitek-rocket-slam`** (bez README a .gitignore) a přidej ho:
   `git remote add origin git@github.com:hhvitek/hhvitek-rocket-slam.git && git push -u origin master`
   Dokud `origin` neexistuje, session commituje jen lokálně a push přeskočí.
4. V **desktopové aplikaci Claude** (záložka Code) otevři novou session v kořeni repa a napiš:
   *„Přečti PLAN.md a začni milníkem M0.“* Sledovat ji můžeš i přes remote session z mobilu.
5. Při prvním spuštění schval oprávnění pro práci bez dozoru (Unity, Blender, git, python, stahování z webů v 4C.5).
   Bez nich by se práce v noci nebo po obnovení zastavila na schvalování.

### 1d. Během vývoje
- **Unity Editor s projektem `unity/` nech zavřený, dokud Claude pracuje.** Unity nedovolí otevřít
  jeden projekt dvakrát a Claude ho spouští z příkazové řádky. Editor potřebovat nebudeš:
- **Hru hraješ ze sestavené verze:** každý milník končí buildem `builds/Linux/RocketSlam.x86_64`. Ten jde spustit kdykoli, i když Claude zrovna pracuje.
- Po **M1** (hratelný prototyp) se Claude zastaví a počká na tvoje „je to zábavné / tohle uprav“. Teprve pak pokračuje dál. Další milníky jdou bez čekání a připomínky můžeš psát kdykoli.
- Session po zastavení kvůli limitům **nech otevřenou** (nearchivuj ji). Obnovení po resetu 5hodinového okna běží uvnitř ní. Po zastavení týdenním prahem (20 %) session čeká na tvoje další pokyny.

---

## 2. Zadání hry

### 2a. Koncept
**Rocket Slam** je 3D simulace přistání vratného prvního stupně rakety. Hráč převezme řízení
stupně padajícího z výšky ~3 km a musí ho měkce posadit na pozemní rampu nebo na plovoucí
přistávací loď. Výsledkem je **úspěch** (dosednutí) nebo **neúspěch** (výbuch, převrácení, minutí).
Důraz je na **jednoduché ovládání a poloreálnou fyziku**: snadné pochopit, těžké zvládnout.

Raketa je inspirovaná skutečnými vratnými nosiči, ale **bez log, názvů a vzhledu konkrétních
firem**: vlastní design, vlastní jméno. Manévr „hoverslam“ (zážeh na poslední chvíli) je jádro hratelnosti.

**Nezávislost:** hra vzniká od nuly. Z uživatelovy webové hry „Přistání“ (projekt `hhvitek-games`)
nic nečti ani nepřebírej: kód, návrh ani ladění obtížnosti.
**Inspirace z internetu je vítaná:** skutečné záběry přistání raket, popisy fyziky a techniky
(hoverslam, gimbal, mřížková kormidla), herní návrh podobných her a tutoriály k Unity a Blenderu.
Platí jen, že se nekopíruje konkrétní existující hra (její jméno, grafika, úrovně) ani loga a názvy firem (2a).

### 2b. Fyzika (poloreálná)
| Parametr | Hodnota | Poznámka |
|---|---|---|
| Suchá hmotnost stupně | 22 t | |
| Palivo pro přistání | 6–10 t podle úrovně | hmotnost se spotřebou klesá |
| Tah motoru | 850 kN při 100 % | |
| **Minimální tah** | **40 %** (níž nejde; motor je buď vypnutý, nebo 40–100 %) | i na minimu je tah > váha, takže raketa **nemůže viset** a musí se trefit okamžik zážehu („hoverslam“) |
| Spotřeba | ~280 kg/s při 100 % | úměrná tahu |
| Natočení motoru (gimbal) | ±5° ve dvou osách | hlavní způsob řízení náklonu |
| Odpor vzduchu | `½ρv²·Cd·A`, ρ konstantní | |
| Startovní stav | výška 3 km, svislá rychlost −200 až −250 m/s, boční odchylka podle úrovně | |
| Vítr | stálý + nárazy, podle úrovně | |
| Mřížková kormidla | ve vyšší rychlosti pomáhají řídit směr | funkce od M5; model už v M2 |

Kontrola, že to jde: při plném tahu brzdí raketa zhruba 18 m/s², takže zastavit 250 m/s trvá ~14 s
na ~1,7 km a stojí ~4 t paliva. 6 t na nejtěžší úrovni je tedy těsné, ale zvládnutelné.

Fyzika běží ve `FixedUpdate` na `Rigidbody` s vlastními silami (tah v místě motoru → točivý moment z gimbalu).
Hodnoty jsou v `ScriptableObject` (`RocketConfig`), aby šly ladit bez zásahu do kódu.

### 2c. Ovládání
| Akce | Klávesnice | Gamepad |
|---|---|---|
| Zážeh / vypnutí motoru | Mezerník | A |
| Tah +/− | W / S | pravý / levý trigger |
| Gimbal (náklon ve 2 osách) | šipky | levá páčka |
| Vysunout nohy | G | Y |
| Přepnout kameru | C | pravá páčka (stisk) |
| Pauza | Esc | Start |
| Restart pokusu | R | Back |

Volitelný **asistent** (pro začátečníky): automaticky drží raketu svisle, hráč řídí jen tah.

### 2d. Podmínky úspěchu a neúspěchu
**Úspěch:** všechny nohy na ploše a zároveň:
- svislá rychlost při dosednutí ≤ 2,5 m/s,
- vodorovná rychlost ≤ 1,5 m/s,
- náklon ≤ 6°,
- raketa se do 3 s nepřevrátí.

**Neúspěch** (s důvodem na obrazovce výsledků):
- tvrdý dopad, tedy výbuch,
- převrácení,
- dosednutí bez vysunutých nohou,
- dopad mimo rampu nebo loď,
- došlo palivo.

### 2e. Úrovně
| # | Místo | Podmínky | Palivo |
|---|---|---|---|
| 1 | pozemní rampa | bezvětří, den, malá odchylka | 10 t |
| 2 | pozemní rampa | boční vítr | 9 t |
| 3 | loď na moři | klidné moře | 8 t |
| 4 | loď na moři | vlny (paluba se houpe a naklání), vítr | 7 t |
| 5 | loď na moři | noc, mlha, nárazy větru | 6 t |

### 2f. Hodnocení
Body se počítají ze zbylého paliva, přesnosti (vzdálenost od středu terče) a měkkosti dosednutí.
K tomu 1–3 hvězdičky za úroveň. Nejlepší výsledky se ukládají lokálně (JSON v `Application.persistentDataPath`).

### 2g. Obrazovky
- **Hlavní menu:** Hrát, Výběr úrovně, Nastavení, Konec.
- **HUD:** výška, svislá a vodorovná rychlost, tah (%), palivo, náklon, stav nohou, šipka nebo vzdálenost k terči.
- **Výsledky:** úspěch nebo neúspěch a důvod, body, hvězdičky, **graf telemetrie** (výška, rychlost a tah v čase), tlačítka Znovu a Další.
- **Nastavení:** hlasitost, kvalita grafiky, asistent zapnuto/vypnuto.

### 2h. Grafika a zvuk
- **Styl:** low-poly s plochým stínováním, jedna sdílená **paletová textura** (atlas barev) a jeden materiál (URP Lit) pro většinu modelů. Vypadá to konzistentně a je to výkonově levné (cíl: integrovaná grafika Radeon).
- **Kamery:** sledovací (za raketou) a kamera z rampy nebo lodi (pohled zespodu).
- **Moře:** low-poly síť, jejíž vlny počítá **jedna funkce v C#**. Ta samá funkce hýbe i lodí, takže paluba se houpe přesně s viditelnými vlnami. Vlastní shader není potřeba.
- **Efekty:** plamen motoru (velikost podle tahu), kouř a pára u země, výbuch, otřes kamery.
- **Zvuk:** hukot motoru (hlasitost a výška tónu podle tahu), vítr, cvaknutí nohou, výbuch, UI zvuky. Vše CC0.

### 2i. Mimo rozsah (vědomě vynecháno)
Start a let na orbitu, více motorů, multiplayer, mobil, VR, editor úrovní, vlastní shadery.

---

## 3. Technologie

| Oblast | Volba | Proč |
|---|---|---|
| Engine | **Unity 6 (6000.6.9f1)**, **URP** (šablona Universal 3D, projekt v `unity/`) | moderní, zdarma, dobře běží na integrované grafice |
| Jazyk | **C#** | |
| Vstup | **Input System**, akce v `.inputactions` | klávesnice i gamepad |
| UI | **UI Toolkit** (UXML + USS) | textové soubory, které Claude dobře píše a verzuje |
| Fyzika | vestavěná PhysX, vlastní síly | |
| Moře | síť generovaná v C#, materiál URP Lit | bez vlastních shaderů |
| Efekty | Particle System (nastavený skriptem) | |
| Scény | generované **editorovými skripty** (`unity/Assets/Editor/SceneBuilder.cs`, spouštěné `-executeMethod`) | reprodukovatelné, bez ručního klikání |
| Testy | **Unity Test Framework**: EditMode (fyzika, hodnocení přistání) + PlayMode (krátký automatický let) | spouštěné z příkazové řádky (`-runTests`) |
| Build | `unity/Assets/Editor/BuildScript.cs` → `builds/Linux/`, `builds/Windows/`; Linux od M1, Windows v M6 | |
| 3D modely | **Blender + Python (bpy)**, skripty v `blender/scripts/`, export **FBX** do `unity/Assets/Models/`, `.blend` do `blender/blend/` | procedurální low-poly, opakovatelné |
| Textury | paletový atlas generovaný skriptem | |
| Volné assety | **CC0**: Kenney (zvuky, UI prvky, textury částic), Poly Haven (HDRI obloha, rozlišení 1k–2k), ambientCG (textura betonu) | evidované v `THIRD_PARTY.md` |
| Verzování | Git, **jedno repo** (monorepo) pro Unity, Blender i další aplikace; `.gitignore` v kořeni (bez `unity/Library/`, `builds/`, `screenshots/`) | assety jsou malé, Git LFS není potřeba. Zavést ho, až by nějaký soubor přesáhl ~10 MB. |

---

## 4. Jak má Claude pracovat (pokyny pro session)

### 4A. Hlavní session je ORCHESTRÁTOR
**Hlavní session sama kód, modely ani assety nepíše. Jen řídí práci a deleguje ji na subagenty.**

Orchestrátor:
- **jako úplně první krok zkontroluje limity a spustí hlídače** (sekce 4B), pak čte `PLAN.md` a `PROGRESS.md`,
- rozdělí milník na úkoly s jasným zadáním a kritériem hotovosti a přidělí je subagentům,
- skládá výsledky dohromady, rozhoduje, komunikuje s uživatelem, commituje a po milníku pushuje,
- sám upravuje jen `CLAUDE.md` a `PROGRESS.md`,
- nečte velké soubory a logy; ty čtou subagenti a vracejí shrnutí (šetří kontext i kvótu).

**Role subagentů:** nespouští se jako zvláštní soubory agentů, ale jako běžní subagenti (nástroj `Agent`).
Roli jim dá zadání: orchestrátor vloží popis role z `CLAUDE.md` a konkrétní úkol. Model se volí při spuštění.

| Role | Dělá | Zapisuje do | Model |
|---|---|---|---|
| **implementer** | C# kód, UI (UXML/USS), editorové skripty, scény, testy; **sám kompiluje a spouští testy, snímky a build** v Unity z příkazové řádky | `unity/Assets/` (kromě `Models`, `Textures`, `ThirdParty`), `unity/Packages/`, `unity/ProjectSettings/`, `screenshots/`, `builds/` | silnější |
| **modeler** | Python skripty pro Blender, export FBX, náhledy PNG | `blender/`, `unity/Assets/Models/`, `unity/Assets/Textures/` | silnější |
| **asset-scout** | dohledání a stažení CC0 assetů, evidence licencí | `unity/Assets/ThirdParty/`, `THIRD_PARTY.md` | levnější (Sonnet) |
| **reviewer** | nezávislá revize | nic, jen čte | silnější |

**Pravidla souběhu:**
- Najednou běží nejvýš **3 subagenti** a každý zapisuje jen do svých složek.
- **Unity smí v jednu chvíli spouštět jen jeden subagent** (projekt má zámek). Unity spouští jen implementer, takže najednou běží vždy jen jeden implementer. Modeler a asset-scout můžou běžet souběžně s ním.
- Když Unity hlásí, že je projekt otevřený jinde (editor), implementer to ohlásí, orchestrátor požádá uživatele o zavření editoru a počká.
- Git worktrees nepoužívat: každý by si znovu importoval celou knihovnu Unity (GB dat a minuty času).
- Commituje jen orchestrátor. Věci mimo hru (web, nástroje) patří do `apps/<název>/` nebo `tools/`.

**Nezávislá revize:**
- **Jednou na konci každého milníku** spustí orchestrátor čerstvého subagenta **reviewer**.
- Reviewer dostane jen diff milníku, kritéria hotovosti z `PLAN.md` a snímky nebo náhledy. **Nedostane** úvahy ani shrnutí implementujícího agenta, aby nebyl ovlivněný.
- Hledá chyby (fyzika, logika přistání, výjimky, výkon, licence assetů, rozpor se zadáním) a vrací seznam nálezů se závažností. Sám nic neopravuje.
- Orchestrátor pošle nálezy k opravě. Byly-li mezi nimi **závažné**, spustí na opravy **nového** reviewera, nejvýš 2 kola. Méně závažné nálezy, které zbudou, zapíše do `PROGRESS.md`.

### 4B. Hlídání limitů (usage guard)

Funguje v **desktopové aplikaci Claude** i přes remote session (mechanismus je v praxi ověřený).

**Zdroj dat:** nástroj aplikace `mcp__ccd_session_mgmt__get_usage` → `plan.windows`. Vrací procenta
a čas resetu pro **5hodinové okno** („5-hour limit“, uživatel mu říká „denní“) a týdenní okna
(„Weekly · all models“ a týdenní okna jednotlivých modelů).
`unavailable` znamená nespouštět nic nového a zkusit to při další kontrole. `not_applicable` znamená, že limity neplatí a guard se nespouští.

**Prahy** (nastavil uživatel 2026-11-18; změní je jen uživatel, aktuální hodnoty drž v `CLAUDE.md`):
| Okno | Graceful stop | Hard stop (kill) | Po zastavení |
|---|---|---|---|
| 5hodinové („denní“) | **≥ 95 %** | **≥ 98 %** | automatické obnovení po resetu okna |
| Každé týdenní okno | **≥ 20 %** | **≥ 23 %** | **žádné obnovení: počkat na další pokyny uživatele** |

Týdenní práh je absolutní procento okna, ne přírůstek od startu (při sepsání plánu bylo týdenní okno na 9 %).

**Kdy kontrolovat:**
1. **Na začátku session**, ještě před čímkoli jiným. Pokud je už nad graceful prahem, nezačínat a napsat uživateli čísla a čas resetu. Obnovení naplánovat jen u 5hodinového okna.
2. Před spuštěním každého subagenta.
3. Pokaždé, když se subagent ozve s výsledkem.
4. **Hlídač:** opakovaná úloha `CronCreate` v rámci session, každých 10 minut v „lichou“ minutu (`7,17,27,37,47,57 * * * *`). Kontroluje limity a uplatní tuhle sekci. Spouští se jen tehdy, když je session nečinná, tedy právě když orchestrátor čeká na subagenty. Ostatní chvíle pokrývají body 1–3.

**Graceful stop** (5h ≥ 95 % nebo kterékoli týdenní okno ≥ 20 %):
1. Nespouštět žádného nového subagenta.
2. Běžícím subagentům poslat (`SendMessage`), ať dokončí aktuální krok tak, aby se projekt kompiloval a nic nebylo napůl.
3. Hlídat dál: když mezitím padne kill práh, má přednost.
4. Commitnout (rozpracovanou práci s prefixem `WIP:`) a zapsat do `PROGRESS.md`: co je hotovo, co zbývá, přesný další krok, který limit práci zastavil a čas resetu.
5. Zrušit hlídače. **Zastavilo-li práci týdenní okno: obnovení neplánovat**, napsat uživateli čísla a čekat na jeho pokyny. Zastavilo-li ji jen 5hodinové okno: naplánovat obnovení (níže). Ukončit tah s čísly a časem resetu.

**Hard stop / kill** (5h ≥ 98 % nebo kterékoli týdenní okno ≥ 23 %):
1. Okamžitě `TaskStop` na všechny běžící subagenty a úlohy na pozadí, zrušit hlídače, nic dalšího nespouštět.
2. Do `PROGRESS.md` zapsat krátkou poznámku: kde práce skončila a co může být napůl hotové (podle `git status`). Bez commitu.
3. Obnovení naplánovat jen tehdy, když práci zastavilo samo 5hodinové okno (stejně jako u graceful). Ukončit tah s čísly.

**Automatické obnovení po resetu** (jen po zastavení 5hodinovým oknem):
- Jednorázová úloha `CronCreate` na čas resetu 5hodinového okna +5 minut.
- Prompt úlohy: *„Zkontroluj limity podle sekce 4B. Pokud jsou pod graceful prahy, spusť hlídače a pokračuj podle PROGRESS.md. Je-li nad prahem týdenní okno, nepokračuj a napiš uživateli; je-li nad prahem jen 5hodinové, přeplánuj obnovení.“*
- Úloha žije jen v téhle session: aplikace musí běžet, session nesmí být archivovaná a notebook nesmí spát. Notebook se teď sám neuspává a ani zavření víka ho neuspí.
- Když session mezitím skončí, pokračuje se ručně stejnou větou v nové session. `PROGRESS.md` na to vždy stačí.

### 4C. Obecné pokyny
1. **Postupuj po milnících** (sekce 5). Na konci každého milníku: testy zelené, Linux build, revize, commit `M<n>: <popis>`, push (pokud existuje `origin`), zápis do `PROGRESS.md` a krátké shrnutí pro uživatele, co má vyzkoušet. Po M1 počkej na zpětnou vazbu uživatele (1d).
2. **Ověřuj, ne naslepo:**
   - kód: kompilace a testy z příkazové řádky (`Unity -batchmode -projectPath unity -runTests …`), oprava chyb z logu,
   - **vizuální kontrola:** editorový skript vyrenderuje snímky z kamer do `screenshots/*.png` (mimo git; vybrané se kopírují do `docs/`) a subagent si je prohlédne,
   - modely: Blender vyrenderuje náhled každého modelu do `blender/renders/*.png` ke kontrole tvaru, měřítka a os.
3. **Modely jen ze skriptů.** Žádné ruční úpravy `.fbx`, vždy upravit skript a znovu vygenerovat. Měřítko 1 jednotka = 1 m, správné osy při exportu FBX pro Unity.
4. **`CLAUDE.md`** obsahuje pravidla (sekce 4A–4C, popisy rolí, konvence). **`PROGRESS.md`** obsahuje stav (hotovo, rozpracováno, další krok, nevyřešené nálezy revize). Stav se do `CLAUDE.md` nepíše.
5. **Assety třetích stran** stahuj jen z oficiálních webů (kenney.nl, polyhaven.com, ambientcg.com), jen s licencí CC0. Každý zapiš do `THIRD_PARTY.md` (zdroj, URL, licence) a ulož do `unity/Assets/ThirdParty/<zdroj>/`.
6. Nic neinstaluj do systému (sudo) a nezakládej účty. Když něco chybí, napiš uživateli, co má udělat.
7. Výkonový cíl: 60 FPS při 1080p na Ryzen 5 5500U s integrovanou grafikou (střední kvalita URP).

### Struktura repozitáře
```
hhvitek-rocket-slam/
├─ PLAN.md, CLAUDE.md, PROGRESS.md, README.md, THIRD_PARTY.md
├─ .gitignore, .gitattributes
├─ unity/                      Unity projekt (otevírá se v Unity Hubu)
│  ├─ Assets/
│  │  ├─ Scripts/ (Rocket/, Gameplay/, UI/, Camera/, Audio/, Environment/)
│  │  ├─ Editor/ (SceneBuilder.cs, BuildScript.cs, ScreenshotTool.cs)
│  │  ├─ Models/ (FBX z blender/)
│  │  ├─ Materials/, Textures/, Audio/, VFX/, UI/
│  │  ├─ Settings/ (URP asset ze šablony, RocketConfig, LevelConfigs)
│  │  ├─ Scenes/ (MainMenu, Level01–05)
│  │  ├─ ThirdParty/ (Kenney/, PolyHaven/, ambientCG/)
│  │  └─ Tests/ (EditMode/, PlayMode/)
│  ├─ Packages/, ProjectSettings/
│  └─ Library/, Logs/, UserSettings/   (mimo git)
├─ blender/
│  ├─ scripts/  (common.py, palette.py, booster.py, landing_pad.py, droneship.py)
│  ├─ blend/    (.blend uložené generátory, k prohlédnutí v Blenderu)
│  └─ renders/  (náhledy modelů PNG)
├─ tools/      pomocné skripty (stahování assetů, build wrappery)
├─ apps/       cokoli mimo hru, jedna podsložka na aplikaci
├─ docs/       snímky a poznámky pro README
├─ builds/     (mimo git) Linux/, Windows/
└─ screenshots/ (mimo git) pracovní snímky z kontrol
```

---

## 5. Milníky

| # | Milník | Hotovo, když… |
|---|---|---|
| **M0** | **Příprava:** kontrola limitů a hlídač (4B), přečíst `CLAUDE.md` (pravidla už sepsaná, jen doplnit, co chybí), `PROGRESS.md`, `THIRD_PARTY.md`, úklid šablony (`Readme.asset`, `TutorialInfo/`), balíček Test Framework (Input System už je), složky v `unity/Assets/`, `BuildScript`, zkušební jednorázová úloha obnovení za 2 min, commit a push (pokud je `origin`) | projekt se zkompiluje z příkazové řádky, prázdný test projde, zkušební obnovení proběhlo |
| **M1** | **Šedý prototyp:** raketa jako válec, rampa jako plocha, fyzika (tah, gimbal, palivo, odpor), ovládání, základní HUD, detekce úspěchu a neúspěchu, restart. **Pak čekat na zpětnou vazbu.** | Linux build jde hrát: přistát i havarovat; testy fyziky a hodnocení zelené |
| **M2** | **Modely z Blenderu:** stupeň (válec, mezistupeň, tryska, 4 mřížková kormidla), 4 přistávací nohy jako samostatné díly s pivotem, rampa s terčem, přistávací loď, paletový atlas; import do Unity, animace vysunutí nohou | náhledy modelů a snímky scény odpovídají návrhu |
| **M3** | **Efekty a zvuk:** plamen podle tahu, kouř, výbuch, otřes kamery, zvuky z Kenney (CC0), 2 kamery | snímky a automatický testovací let bez chyb |
| **M4** | **Prostředí:** HDRI obloha (Poly Haven), terén, moře s vlnami a houpáním lodi, vítr, noc a mlha | úrovně 1–5 sestavené skriptem, snímky ze všech úrovní |
| **M5** | **Herní smyčka:** menu, výběr úrovně, výsledky s grafem telemetrie, hodnocení a hvězdičky, ukládání, nastavení, asistent, funkce mřížkových kormidel | celou hru jde projít od menu po výsledky |
| **M6** | **Doladění:** gamepad, vyvážení obtížnosti, výkon, Windows build, README se snímky | builds pro Linux i Windows, ověřené uživatelem |

Odhad: zhruba **1 delší sezení na milník** (M0 a M1 se dají stihnout v jednom). Spotřebovává se jen kvóta stávajícího předplatného.

---

## 6. Co Claude ukáže (smysl ukázky)
- vývoj hry v Unity bez klikání v editoru (kód, scény ze skriptů, testy, build),
- návrh herní fyziky a její ověření automatickými testy,
- **procedurální 3D modely** psané jako kód v Blenderu, včetně náhledů pro vlastní kontrolu,
- dohledání a zapojení volných CC0 assetů s evidencí licencí,
- **vizuální sebekontrolu** přes vyrenderované snímky,
- orchestraci subagentů s nezávislou revizí a hlídáním limitů.

Co **nedělá dobře**, a proto to není v zadání: detailní organické modely, postavy a animace,
ladění pocitu ze hry bez lidského hráče (proto zastávka po M1).

---

## 7. Rizika
| Riziko | Opatření |
|---|---|
| Unity z příkazové řádky nejde spustit (licence, chybějící knihovny) | ověřeno 2026-11-18: projekt `unity/` založen headless z příkazové řádky; jinak to M0 ohlásí a uživatel to opraví |
| Editor je otevřený, když Claude spouští Unity | pravidlo v 1d; implementer kolizi ohlásí a orchestrátor počká (4A) |
| Integrovaná grafika nestíhá | low-poly, jeden materiál, URP střední kvalita, bez náročných efektů |
| Generované modely vypadají „programátorsky“ | paletový atlas, zkosení hran (bevel) ve skriptech, iterace přes náhledy |
| `get_usage` dočasně nevrací data | nic nového nespouštět, zkusit při další kontrole |
| Paralelní subagenti spálí kvótu rychleji, než guard zareaguje | rezerva 3 % mezi graceful a kill v obou oknech, kontrola před každým subagentem a po každém hlášení, max. 3 souběžní, jedna revize na milník |
| Automatické obnovení se nespustí (session zavřená, aplikace neběží, notebook spí) | `PROGRESS.md` umožní ruční pokračování jednou větou |
| Týdenní limit je blízko prahu už na startu | kontrola v bodě 1 (4B); případně začít po týdenním resetu |

---

## 8. Otevřené otázky (doplň, výchozí volba je tučně)
1. **Název:** **Rocket Slam** (podle názvu repa), nebo jiný?
2. **Jazyk hry:** **angličtina**, čeština, nebo obojí?
3. **Platformy buildu:** **Linux + Windows**, nebo jen Linux?
4. **Realismus:** **poloreálný** (viz 2b), nebo víc arkádový (víc paliva, větší tolerance)?
5. **Repozitář na GitHubu:** **soukromý**, nebo veřejný?
6. **Asistent pro začátečníky** (automatické držení svislé polohy): **ano**, nebo ne?
7. **Zastávka po M1** na tvou zpětnou vazbu: **ano**, nebo jet bez zastávek?
