# -*- coding: utf-8 -*-
"""ANALIZA PLIKU DANYCH NARRATORA (format [PN-DATA] v6-v11: luki z kroku 5, fakty z kroku 6,
styl gracza z kroku 7, brama Anomaly i lustro silnika z kroku 9, logowanie ewaluacji z etapu L) - niezmienniki
+ podsumowanie.

Uzycie:
    python analysis_v7.py                                   # domyslny PN_decyzje.log gracza
    python analysis_v7.py sciezka/PN_decyzje.log
    python analysis_v7.py plik --tryb gra|symulacja|wszystko   (domyslnie: wszystko)

Zawiera 17 niezmiennikow v6 (bez zmian znaczenia), niezmienniki lukow (18-27, dla v7-v10),
niezmienniki faktow (28-31, dla v8-v10), stylu gracza (32-41, dla v9-v10), kroku 8 (42-45) oraz
kolumn v10 (46: strona bramy Anomaly, szansa, akcje odciete lustrem sprawdzen gry) i etapu L kroku 9 (47-54:
lustroOdcina, czasMs, [PN-DZIEN], [PN-KOLONISTA], [PN-FIRED] z listami, [PN-PERF], [PN-EVAL], akcja wymuszona).
Wiersze starszego
formatu w tym samym pliku sa sprawdzane tylko regulami swojej wersji. Parametry (profile, PASS,
brama, limit lukow, katalog lukow z krawedziami i warunkiem stylu, parametry i prototypy stylu,
orientacja stylu profili) czyta z linii [PN-CONFIG] - nie z literalow.

Linie poza kontraktem:
  [PN-ARC]  krok automatu luku (otwarcie/przejscie/zamkniecie/odrzucenie); tick w linii.
  [PN-EXEC] potwierdzenie wykonania zdarzenia (lastFireTicks przed/po TryFire).
  [PN-FACT] zdarzenie ksiegi faktow (krok 6): ustawienie po potwierdzonym wykonaniu, odrzucenie
            faktow zdarzenia niewykonanego; stan faktow w chwili decyzji jest odtwarzany z tych
            linii (dzien ustawienia + czas zycia - wygasanie jest leniwe i nie ma wlasnej linii).
  [PN-LOAD] rozwidlenie: wiersze [PN-DATA] mapy z decyzjaNr >= "mapy" wypadaja (regula v6);
            stan lukow mapy jest odtwarzany z pola "luki" (uid:luk#nr:faza) i od tej chwili
            liczony z kolejnych linii [PN-ARC]. Pole "styl" (dni:..,dzien:..) jest kotwica ciaglosci
            linii [PN-GRACZ] (krok 7); [PN-RESET] akcja=skasujStyl kotwiczy dni=0.
  [PN-GRACZ] zamknieta doba obserwacji stylu gracza (krok 7): cechy, profil wzgledny, mocne strony,
            etykieta, pomiary; porzucona galaz po wczytaniu wypada po ticku jak [PN-ARC].
  [PN-DZIEN], [PN-KOLONISTA], [PN-PERF] (etap L): stan mapy na koniec doby, zmiany skladu kolonii, koszt czasu -
            kazdy narrator, tylko gra; porzucona galaz jak [PN-ARC].
  [PN-EVAL]  start gry ewaluacyjnej (etap L): nowy runId, pamiec i styl od zera (kotwica stylu dni=0).
  [PN-LIST]  nowy list gracza (etap L, kazdy narrator): tytul, nazwy do znacznikow, tresc= do konca linii.
  [PN-STAN]  stan mapy domowej dla regul RN (etap L): start, zmiana predykatu (kryzys, zagrozenie, pusta), koniec;
            stan obowiazuje do nastepnej linii mapy; porzucona galaz jak [PN-ARC].

Kod wyjscia 1, gdy jakis niezmiennik jest naruszony.
"""
import bisect, collections, io, math, re, sys

DOMYSLNY = 'C:/Users/Luis/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/PN_decyzje.log'
THREAT_BIG = {'RaidEnemy', 'ManhunterPack', 'Infestation', 'PsychicEmanatorShipPartCrash'}
FIT = {'Escalate': 0.0, 'Hold': 0.5, 'Breathe': 1.0, 'Pass': 1.0}
PROFIL_DOMYSLNY = dict(wN=1.0, wS=1.0, calm=0.30, tense=0.60, span=1.0)
ZGODNOSC = {  # decyzja autora nr 2: walencja fazy -> intencje, przy ktorych faza steruje
    'Negative': {'Escalate', 'Hold'}, 'Neutral': {'Escalate', 'Hold', 'Breathe', 'Pass'},
    'Positive': {'Hold', 'Breathe', 'Pass'}}
WYKONANE = {'wykonane', 'symulacja', 'pozno-wykonane'}
# Wersje formatu, od ktorych obowiazuja grupy regul (v11 = etap L kroku 9).
OD_V7 = ('7', '8', '9', '10', '11')
OD_V8 = ('8', '9', '10', '11')
OD_V9 = ('9', '10', '11')
OD_V10 = ('10', '11')
OD_V11 = ('11',)
KOLONISCI_ZDARZENIA = {'start', 'dolaczyl', 'zginal', 'porwany', 'uwieziony', 'zdziczal', 'odszedl', 'inne'}
N47 = '47 lustroOdcina: puste z licznikiem, lista kanoniczna, licznik <= lista'
N48 = '48 czasMs >= 0 w kazdym wierszu v11'
N49 = '49 [PN-DZIEN]: doba = tick div 60000 - 1, jedna na mape, zakresy'
N50 = '50 [PN-KOLONISTA]: lancuch licznosci od kotwicy (+-1)'
N51 = '51 [PN-FIRED] v11: listy, frakcja, pora, noc, wiek bogactwa'
N52 = '52 [PN-PERF]: agregaty spojne, decyzji = wiersze gry doby'
N53 = '53 [PN-EVAL]: nowy runId, pamiec od zera'
N54 = '54 [PN-EXEC] wymuszone=N: bez decyzji, faktow, lukow; wykonane => [PN-FIRED] po'


def wymuszona(x):
    """Akcja 'PN: wymus akcje': wymuszone = numer akcji w sesji (>= 1); 0, puste albo brak pola = zwykle zdarzenie."""
    return (x.get('wymuszone') or '0') != '0'


N55 = '55 [PN-LIST]: listy tiku = listow odpalen, incydenty zgodne, pola'
N56 = '56 [PN-STAN]: lancuch stanu, predykaty z liczb, siatka, zgodnosc z [PN-DZIEN]'
STAN_ZDARZENIA = {'start', 'zmiana', 'koniec'}
STAN_PREDYKATY = ('zagrozenie', 'kryzys', 'pusta')
POLA_LISTU = {'runId', 'narrator', 'tick', 'dzien', 'mapa', 'incydenty', 'wymuszone', 'typ', 'frakcja', 'pionki',
              'tytul', 'tresc', 'tryb'}
PERF_GRUPY = ('tickow', 'odpalen', 'styl', 'doba', 'kolonistow', 'interwalow', 'decyzji', 'potwierdzen')

# ---- styl gracza (krok 7, v9) ----
CECHY = ('Walka', 'Gospodarka', 'Ekspansja', 'Reaktywnosc')
RYTM = {'Breathe': 1.0, 'Hold': 0.0, 'Escalate': -1.0}   # StyleDirection.Rhythm; reszta (Pass) = 1
KOLUMNY_STYLU = ['stylDni', 'stylAktywny', 'stylWalka', 'stylGospodarka', 'stylEkspansja', 'stylReaktywnosc',
                 'stylMocne', 'stylEtykieta', 'stylKierunek', 'stylWartosc', 'premiaStylu', 'stylWPasmie']
# Tolerancje WYPROWADZONE z zaokraglen logu (Z do 3 miejsc, +-0.0005):
TOL_ODL = 0.0021   # roznica dwoch odleglosci do prototypow: 2 * sqrt(4) * 0.0005 = 0.002


def tol_c(skala):
    """c = (Z - srednia)/skala: blad (0.0005 + 0.0005)/skala (+ zapas). Ze SKALI z [PN-CONFIG], nie stala dla
    0.25 - skala jest pokretlem kalibracji (przeglad S8 kroku 7)."""
    return 0.001 / skala + 1e-4


def mocne_wzgledne(zs, skala, prog):
    """Mocne strony z bezwzglednych Z (None = cecha nieznana) - ta sama regula co PlayerStyleModel.Finish:
    profil wzgledny po ZNANYCH cechach, przy mniej niz 2 znanych brak mocnych; zwraca (zbior, niepewny)."""
    znane = [i for i, z in enumerate(zs) if z is not None]
    if len(znane) < 2:
        return set(), False
    sr = sum(zs[i] for i in znane) / float(len(znane))
    mocne, niepewny = set(), False
    for i in znane:
        c = max(-1.0, min(1.0, (zs[i] - sr) / skala))
        if abs(c - prog) <= tol_c(skala):
            niepewny = True
        if c >= prog:
            mocne.add(CECHY[i])
    return mocne, niepewny


def najblizszy_prototyp(zs, proto):
    """Etykieta najblizszego prototypu po znanych cechach (remis -> wczesniejszy w XML); (etykieta, niepewna)."""
    odl = []
    for et, wsp in proto:
        s = 0.0
        for i, z in enumerate(zs):
            if z is not None and i < len(wsp):
                s += (z - wsp[i]) ** 2
        odl.append(math.sqrt(s))
    if not odl or all(z is None for z in zs):
        return None, True
    k = min(range(len(odl)), key=lambda j: (odl[j], j))
    znane = [i for i, z in enumerate(zs) if z is not None]

    def rozne_na_znanych(j):
        return any(i >= len(proto[j][1]) or i >= len(proto[k][1]) or proto[j][1][i] != proto[k][1][i] for i in znane)
    # Prototypy IDENTYCZNE na znanych cechach (np. Wojownik i Dowodca przy nieznanej Reaktywnosci) maja w grze
    # DOKLADNIE rowne odleglosci - remis systematyczny, rozstrzygany kolejnoscia XML (scisle "<"), wiec
    # rozstrzygalny takze tutaj. Niepewny jest tylko bliski remis prototypow, ktore na znanych cechach sie roznia
    # (zaokraglenie Z w logu moze go odwrocic). Pierwsza wersja pomijala oba przypadki, czyli etykiete kazdej
    # kolonii z nieznana cecha - wykryl to straznik fikstury w test_analysis_v9.py.
    niepewna = any(j != k and abs(odl[j] - odl[k]) <= TOL_ODL and rozne_na_znanych(j) for j in range(len(odl)))
    return proto[k][0], niepewna


def zbior_mocnych(tekst):
    return set(x for x in (tekst or '').split('/') if x and x != '-')


def styl_z_pola(tekst):
    """Pole styl= linii [PN-LOAD]/[PN-RESET]: "dni:N,dzien:D,aktywny:..,mocne:..,etykieta:.." -> dict albo None."""
    if not tekst:
        return None
    d = {}
    for cz in tekst.split(','):
        if ':' in cz:
            k, v = cz.split(':', 1)
            d[k] = v
    try:
        return dict(dni=int(d.get('dni', '')), dzien=int(d.get('dzien', '-1')))
    except ValueError:
        return None

# Tolerancja granicy wygasania faktu (S6): pol interwalu compa = 500 tickow = 1/120 dnia, jak
# FactLedger.BoundaryToleranceDays. Liczona na TICKACH (tick wiersza - tick decyzji zrodla), a nie na
# kolumnie "dzien" zaokraglonej do 3 miejsc - ta dawala na samej granicy inny werdykt niz gra
# (ok. 43% przypadkow granicznych, falszywe naruszenie 28).
POL_INTERWALU = 500


def fakt_obowiazuje(tick_teraz, tick_ustawienia, zycie):
    """Ta sama regula co Fact.IsActive: zycie <= 0 = wieczny; inaczej wiek < zycie - tolerancja."""
    return zycie <= 0 or (tick_teraz - tick_ustawienia) < zycie * 60000.0 - POL_INTERWALU


def kv(fragment):
    wynik = collections.OrderedDict()
    for p in fragment.split(';'):
        p = p.strip()
        if '=' in p:
            k, v = p.split('=', 1)
            wynik[k.strip()] = v.strip()
    return wynik


def liczba(tekst, klucz, domyslna):
    m = re.search(r'\b' + klucz + r'=([-\d.]+)', tekst)
    return float(m.group(1)) if m else domyslna


def f(r, k):
    v = r.get(k, '')
    return float(v) if v not in ('', None) else None


def luk_z_config(t):
    """[PN-CONFIG] luk=...; fazy=id:kind:walencje,...; krawedzie=a>b,... -> opis automatu."""
    d = kv(t)
    fazy = []
    for x in d.get('fazy', '').split(','):
        czesci = x.split(':')
        if len(czesci) >= 3:
            fazy.append(dict(id=czesci[0], kind=czesci[1], walencje=set(v for v in czesci[2].split('/') if v)))
    straz = set()
    for kr in d.get('krawedzie', '').split(','):
        if '>' in kr:
            a, b = kr.split('>', 1)
            straz.add((a, b))
    return dict(fazy=fazy, idx={fz['id']: i for i, fz in enumerate(fazy)}, krawedzie=straz,
                warunekStylu=d.get('warunekStylu', '-'))


def wczytaj(path):
    cols, profile, stc, luki = None, {}, {}, {}
    rows, arcs, execs, facts, extras = [], [], [], [], collections.defaultdict(list)
    odcinek = collections.Counter()
    porzucone = 0
    # Styl gracza (krok 7): parametry z [PN-CONFIG] styl=, linie [PN-GRACZ], sekwencja do ciaglosci (40)
    # i "ostatnie dni" w porzadku pliku do niezmiennika 41 (wiersz decyzji w ticku T powstaje PRZED
    # zamknieciem doby w tym samym ticku - comp jest wolany przed GameComponentTick).
    styl_cfg = {}
    gracze = []
    sekw = collections.defaultdict(list)   # runId -> [('k', dni, dzien|None) albo ('g', linia)]
    ostatnieDni = {}                        # runId -> dni ostatniej linii [PN-GRACZ] albo kotwicy
    stylPrzedEksp = {}                      # id eksperymentu -> dni w chwili [PN-EXP] start
    ramionaStylu = {}                       # "id/ramie" -> styl z linii [PN-EXP] ramie (gry|wylaczony|prototyp)
    # Stan lukow odtwarzany ze strumienia [PN-ARC]. KLUCZ ZAWIERA RAMIE EKSPERYMENTU: kazde ramie
    # symulacji POWTARZA ten sam zakres tickow na kopii pamieci sprzed eksperymentu, wiec bez tego
    # pieciu ramion zlewaloby sie w jeden strumien (znalezione na pierwszych danych z gry 2026-09-22).
    stan = {}            # (runId, eksperyment, mapa) -> {arcId: faza}
    stanPrzedEksp = {}   # runId -> {mapa: {arcId: faza}} w chwili [PN-EXP] start

    def stan_dla(run, eksp, mapa):
        klucz = (run, eksp, mapa)
        if klucz not in stan:
            baza = stanPrzedEksp.get(run, {}).get(mapa, {}) if eksp else {}
            stan[klucz] = dict(baza)
        return stan[klucz]

    # Stan FAKTOW (krok 6) - ta sama os ramion co luki: klucz -> (wartosc, dzienUstawienia, zycie).
    fakty = {}            # (runId, eksperyment, mapa) -> {klucz: (wartosc, dzien, zycie)}
    faktyPrzedEksp = {}   # runId -> {mapa: {...}} w chwili [PN-EXP] start

    def fakty_dla(run, eksp, mapa):
        klucz = (run, eksp, mapa)
        if klucz not in fakty:
            baza = faktyPrzedEksp.get(run, {}).get(mapa, {}) if eksp else {}
            fakty[klucz] = dict(baza)
        return fakty[klucz]
    # KONFIGURACJA PER SESJA (przeglad S8 kroku 7): plik danych jest DOPISYWANY przez kolejne uruchomienia gry,
    # a parametry (krzywa, PASS, styl, katalog lukow) sa pokretlami kalibracji. Kazda linia [PN-SESSION] zaczyna
    # NOWE slowniki konfiguracji; wiersze i linie zapamietuja te, ktore obowiazywaly przy ich odczycie. Wczesniej
    # obowiazywala ostatnia konfiguracja w pliku - dla WSZYSTKICH sesji (falszywe 10/11/14/15/35-38 po kalibracji).
    ostatniWiersz = {}   # (runId, eksperyment, mapa) -> ostatni wiersz [PN-DATA] w porzadku pliku (niezmiennik 39)
    # Krok 8: sesja z linia [PN-CONFIG] srodowisko= ma obserwatora odpalen - tylko wtedy wykonanie MUSI miec
    # swoja linie [PN-FIRED] (niezmiennik 42). Stare pliki (bez tej linii) nie sa z tego rozliczane.
    sesjaObserwator = False
    # Etap L: wiersze gry od poprzedniej linii [PN-PERF] (albo [PN-LOAD]/[PN-SESSION], gdzie pomiar startuje od nowa) -
    # decyzja w ticku granicy doby jest pisana PRZED linia [PN-PERF] tego ticku i liczy sie do zamykanej doby.
    decyzjiOdPerf = 0
    # Linie dzielone TYLKO po \n (przeglad S10): str.splitlines tnie tez na U+2028/U+0085 i innych znakach,
    # a tekst listu niesie nazwe frakcji z gry. Mod czysci je w PNLog.JednaLinia - to druga linia obrony.
    for nrLinii, l in enumerate(x[:-1] if x.endswith('\r') else x
                                for x in io.open(path, encoding='utf-8', newline='').read().split('\n')):
        if l.startswith('[PN-SESSION]'):
            profile, stc, luki, styl_cfg = {}, {}, {}, {}
            sesjaObserwator = False
            decyzjiOdPerf = 0
        elif l.startswith('[PN-DATA-COLS]'):
            cols = l.split('kolumny=')[1].split(',')
        elif l.startswith('[PN-CONFIG]'):
            t = l[len('[PN-CONFIG] '):]
            if t.startswith('storyteller='):
                stc = dict(
                    wR=liczba(t, 'weightRestraint', 0.55), wB=liczba(t, 'weightBaseline', 0.20),
                    wI=liczba(t, 'weightIntentAlignment', 0.25), floor=liczba(t, 'densityFloor', 1.5),
                    sat=liczba(t, 'densitySaturation', 4.5), T=liczba(t, 'gateTemperature', 0.1),
                    rundy=liczba(t, 'maxSelectionRounds', 8), maxLukow=liczba(t, 'maxRownoczesnych', 2))
                # Krok 8: przelacznik listu gracza (DescribeEffective: "zlozonyList=tak|nie"); None = brak w linii.
                stc['zlozonyList'] = ('zlozonyList=tak' in t) if 'zlozonyList=' in t else None
            elif t.startswith('profil='):
                pid = t.split(';')[0].split('=')[1]
                profile[pid] = dict(wN=liczba(t, 'wNarr', 1), wS=liczba(t, 'wSyt', 1),
                                    calm=liczba(t, 'spokojPonizej', 0.3), tense=liczba(t, 'napieciePowyzej', 0.6),
                                    span=liczba(t, 'rozpietoscMocy', 1),
                                    # Przycieta jak w grze (NarratorProfileCatalog.Resolve) - linia konfiguracji moze
                                    # niesc wartosc sprzed przyciecia.
                                    o=max(-1.0, min(1.0, liczba(t, 'orientacjaStylu', 0.0))))
            elif t.startswith('styl='):
                dc = kv(t)
                proto = []
                for x in (dc.get('prototypy') or '').split(','):
                    if ':' in x:
                        et, wsp = x.split(':', 1)
                        try:
                            proto.append((et, [float(v) for v in wsp.split('/')]))
                        except ValueError:
                            pass
                styl_cfg = dict(warmup=int(liczba(t, 'rozgrzewkaDni', 15)), cap=int(liczba(t, 'pojemnoscDni', 60)),
                                skala=liczba(t, 'skalaWzgledna', 0.25), prog=liczba(t, 'progMocnej', 0.4),
                                wO=liczba(t, 'wagaOrientacji', 0.3), wR=liczba(t, 'wagaRytmu', 0.7), proto=proto)
            elif t.startswith('luk='):
                luki[t.split(';')[0].split('=')[1]] = luk_z_config(t)
            elif t.startswith('srodowisko='):
                sesjaObserwator = True
        elif l.startswith('[PN-LOAD]') or l.startswith('[PN-RESET]'):
            d = kv(l.split('] ', 1)[1])
            extras[l.split(']')[0] + ']'].append(d)
            run = d.get('runId', '?')
            if l.startswith('[PN-LOAD]'):
                decyzjiOdPerf = 0
            odcinek[run] += 1
            if l.startswith('[PN-LOAD]') and d.get('mapy'):
                granice = {}
                for para in d['mapy'].split(','):
                    uid, dec = para.split(':')
                    granice[uid] = int(dec)
                przed = len(rows)
                rows = [r for r in rows if not (r['tryb'] == 'gra' and r['runId'] == run
                                                and r['mapa'] in granice and int(r['decyzjaNr']) >= granice[r['mapa']])]
                porzucone += przed - len(rows)
            if l.startswith('[PN-LOAD]') and (d.get('tick') or '').isdigit():
                # PORZUCONA GALAZ TAKZE POZA [PN-DATA] (S6): linie [PN-ARC]/[PN-EXEC]/[PN-FACT] i wiersze
                # map ZALOZONYCH po zapisie (nie ma ich w "mapy", a po wczytaniu gra nadaje nowej mapie
                # ten sam uniqueID) pochodza z przyszlosci porzuconej galezi - ich tick jest pozniejszy
                # niz tick wczytania. Bez tego niezmienniki 25/26/30 i liczniki podsumowan widzialy obie galezie.
                tl = int(d['tick'])

                def z_przyszlosci(x):
                    return x.get('runId') == run and x.get('tryb') == 'gra' and int(x.get('tick') or 0) > tl

                przed = len(rows)
                rows = [r for r in rows if not (z_przyszlosci(r) and r['mapa'] not in (granice if d.get('mapy') else {}))]
                porzucone += przed - len(rows)
                arcs[:] = [x for x in arcs if not z_przyszlosci(x)]
                execs[:] = [x for x in execs if not z_przyszlosci(x)]
                facts[:] = [x for x in facts if not z_przyszlosci(x)]
                gracze[:] = [x for x in gracze if not z_przyszlosci(x)]
                extras['[PN-FIRED]'][:] = [x for x in extras['[PN-FIRED]'] if not z_przyszlosci(x)]
                extras['[PN-CACHE]'][:] = [x for x in extras['[PN-CACHE]'] if not z_przyszlosci(x)]
                for typ_ in ('[PN-DZIEN]', '[PN-KOLONISTA]', '[PN-PERF]', '[PN-LIST]', '[PN-STAN]'):
                    extras[typ_][:] = [x for x in extras[typ_] if not z_przyszlosci(x)]
                sekw[run] = [e for e in sekw[run] if not (e[0] == 'g' and z_przyszlosci(e[1]))]
                # Ostatni wiersz mapy po przycieciu - wiersz porzuconej galezi nie moze byc decyzja otwarcia luku.
                for k in [k for k in ostatniWiersz if k[0] == run]:
                    del ostatniWiersz[k]
                for r_ in rows:
                    if r_.get('runId') == run:
                        ostatniWiersz[(run, r_.get('eksperyment', ''), r_.get('mapa'))] = r_
            # KOTWICA STYLU (krok 7): stan ksiegi stylu w chwili wczytania albo recznej ingerencji.
            # skasujStyl niesie stan SPRZED skasowania - po nim kolejka jest pusta (dni=0, dzien nieznany).
            ks = styl_z_pola(d.get('styl'))
            if ks is not None:
                kot = (0, None) if d.get('akcja') == 'skasujStyl' else (ks['dni'], ks['dzien'] if ks['dzien'] >= 0 else None)
                sekw[run].append(('k', kot[0], kot[1]))
                ostatnieDni[run] = kot[0]
            if l.startswith('[PN-LOAD]') or d.get('akcja') == 'skasujPamiec':
                # Stan lukow rozgrywki odtwarzany z pola "luki" (skasowanie pamieci = puste luki).
                for k in [k for k in stan if k[0] == run]:
                    del stan[k]
                if l.startswith('[PN-LOAD]'):
                    for wpis in (d.get('luki') or '').split(','):
                        cz = wpis.split(':')
                        if len(cz) == 3:
                            # Klucz TROJELEMENTOWY jak w stan_dla (S6): dawny (run, uid) gubil stan lukow
                            # z wczytania (falszywe 23), a [PN-EXP] start wywracal analize (ValueError).
                            stan.setdefault((run, '', cz[0]), {})[cz[1].split('#')[0]] = cz[2]
                # Fakty rozgrywki (krok 6): pole "fakty" = uid:klucz=wartosc@dzien/zycie; skasowanie
                # pamieci czysci je tak samo jak luki.
                for k in [k for k in fakty if k[0] == run]:
                    del fakty[k]
                if l.startswith('[PN-LOAD]'):
                    for wpis in (d.get('fakty') or '').split(','):
                        if ':' not in wpis or '=' not in wpis:
                            continue
                        uid, reszta = wpis.split(':', 1)
                        kl, reszta = reszta.split('=', 1)
                        try:
                            wart, reszta = reszta.split('@', 1)
                            dz, zy = reszta.split('/', 1)
                            fakty.setdefault((run, '', uid), {})[kl] = (float(wart), float(dz), float(zy),
                                                                        int(round(float(dz) * 60000.0)))
                        except ValueError:
                            pass
        elif l.startswith('[PN-ARC] '):
            d = kv(l[len('[PN-ARC] '):])
            d['_odcinek'] = odcinek[d.get('runId', '?')]
            d['_luki'] = luki
            # Decyzja otwarcia (39) = ostatni wiersz tej mapy PRZED linia luku w pliku: na sciezce normalnej otwarcie
            # pada tuz po wierszu decyzji T, na spoznionej (T+1000) - w ArcTick PRZED nowa decyzja z T+1000.
            d['_wiersz'] = ostatniWiersz.get((d.get('runId'), d.get('eksperyment', ''), d.get('mapa')))
            arcs.append(d)
            s = stan_dla(d.get('runId'), d.get('eksperyment', ''), d.get('mapa'))
            if d.get('zdarzenie') in ('otwarcie', 'przejscie'):
                s[d.get('luk')] = d.get('do')
            elif d.get('zdarzenie') in ('zamkniecie', 'odrzucenie'):
                s.pop(d.get('luk'), None)
        elif l.startswith('[PN-GRACZ] '):
            d = kv(l[len('[PN-GRACZ] '):])
            d['_styl'] = styl_cfg
            gracze.append(d)
            sekw[d.get('runId', '?')].append(('g', d))
            if (d.get('dni') or '').isdigit():
                ostatnieDni[d.get('runId', '?')] = int(d['dni'])
        elif l.startswith('[PN-EXEC] '):
            tresc = l[len('[PN-EXEC] '):]
            tekst = None
            if '; tekstListu=' in tresc:
                # Krok 8: OSTATNIE pole, do konca linii - zlozony opis moze zawierac srednik.
                tresc, tekst = tresc.split('; tekstListu=', 1)
            d = kv(tresc)
            if tekst is not None:
                d['tekstListu'] = tekst
            d['_obserwator'] = sesjaObserwator
            d['_stc'] = stc
            execs.append(d)
        elif l.startswith('[PN-FIRED] '):
            # Krok 8: odpalenie incydentu (kazdy narrator). Linia tylko z gry - tryb dopisany dla filtrow i galezi.
            # Etap L: listy= to OSTATNIE pole, do konca linii (etykiety listow z gry).
            tresc = l[len('[PN-FIRED] '):]
            listy_ = None
            if '; listy=' in tresc:
                tresc, listy_ = tresc.split('; listy=', 1)
            d = kv(tresc)
            if listy_ is not None:
                d['listy'] = listy_
            d['tryb'] = 'gra'
            extras['[PN-FIRED]'].append(d)
        elif l.startswith('[PN-CACHE] '):
            extras['[PN-CACHE]'].append(kv(l[len('[PN-CACHE] '):]))
        elif l.startswith('[PN-DZIEN] ') or l.startswith('[PN-KOLONISTA] ') or l.startswith('[PN-PERF] ') \
                or l.startswith('[PN-STAN] '):
            typ_ = l.split(']')[0] + ']'
            d = kv(l[len(typ_) + 1:])
            d['tryb'] = 'gra'
            if typ_ == '[PN-PERF]':
                d['_decyzjiWPliku'] = decyzjiOdPerf
                decyzjiOdPerf = 0
            extras[typ_].append(d)
        elif l.startswith('[PN-LIST] '):
            # tresc= to OSTATNIE pole, do konca linii (tekst listu moze miec srednik).
            tresc_ = l[len('[PN-LIST] '):]
            tekst_ = None
            if '; tresc=' in tresc_:
                tresc_, tekst_ = tresc_.split('; tresc=', 1)
            d = kv(tresc_)
            if tekst_ is not None:
                d['tresc'] = tekst_
            d['tryb'] = 'gra'
            extras['[PN-LIST]'].append(d)
        elif l.startswith('[PN-EVAL] '):
            # Nowa rozgrywka pod nowym runId: pamiec i styl od zera - kotwica stylu dni=0 (niezmienniki 40-41).
            d = kv(l[len('[PN-EVAL] '):])
            d['_lin'] = nrLinii
            # Start gry ewaluacyjnej zeruje pomiar czasu (PerfMonitor.Reset) - licznik decyzji do [PN-PERF] tez.
            decyzjiOdPerf = 0
            extras['[PN-EVAL]'].append(d)
            nowy = d.get('runId', '?')
            sekw[nowy].append(('k', 0, None))
            ostatnieDni[nowy] = 0
        elif l.startswith('[PN-FACT] '):
            d = kv(l[len('[PN-FACT] '):])
            facts.append(d)
            if d.get('zdarzenie') == 'ustawienie' and d.get('wartosc', '') != '':
                fs = fakty_dla(d.get('runId'), d.get('eksperyment', ''), d.get('mapa'))
                fs[d.get('klucz')] = (float(d['wartosc']), float(d['dzien']), float(d.get('zycie') or 0),
                                      int(d.get('tickZrodla') or 0))
        elif l.startswith('[PN-DATA] '):
            d = kv(l[len('[PN-DATA] '):])
            d['_ksztalt_ok'] = cols is not None and list(k for k in d.keys()) == cols
            d['_lin'] = nrLinii
            d['_odcinek'] = odcinek[d.get('runId', '?')]
            s = stan_dla(d.get('runId'), d.get('eksperyment', ''), d.get('mapa'))
            d['_stanLukow'] = sorted('%s:%s' % (a, fz) for a, fz in s.items())
            # Liczba faktow obowiazujacych w chwili decyzji - ta sama regula co FactLedger.IsActive,
            # z tolerancja granicy, na tickach (fakt_obowiazuje).
            fs = fakty_dla(d.get('runId'), d.get('eksperyment', ''), d.get('mapa'))
            tw = int(d.get('tick') or 0)
            d['_faktow'] = sum(1 for (w, dzien, zycie, tz) in fs.values() if fakt_obowiazuje(tw, tz, zycie))
            d['_profile'], d['_stc'], d['_luki'], d['_styl'] = profile, stc, luki, styl_cfg
            ostatniWiersz[(d.get('runId'), d.get('eksperyment', ''), d.get('mapa'))] = d
            if d.get('tryb') == 'gra':
                decyzjiOdPerf += 1
            # Oczekiwane stylDni (41): gra - ostatnia linia [PN-GRACZ] albo kotwica w porzadku pliku; ramie
            # symulatora - wedlug stylu ramienia: gry = dni w chwili startu, narzucony = pojemnosc, wylaczony = pusto.
            eks = d.get('eksperyment', '')
            if d.get('tryb') == 'gra' or not eks:
                d['_stylDniOczek'] = ostatnieDni.get(d.get('runId', '?'))
            else:
                rodzaj = ramionaStylu.get(eks)
                if rodzaj is None:
                    d['_stylDniOczek'] = None
                elif rodzaj == 'wylaczony':
                    d['_stylDniOczek'] = ''
                elif rodzaj == 'gry':
                    d['_stylDniOczek'] = stylPrzedEksp.get(eks.split('/')[0])
                else:
                    d['_stylDniOczek'] = max(styl_cfg['cap'], styl_cfg['warmup']) if styl_cfg else None
            rows.append(d)
        elif l.startswith('[PN-WARN]') or l.startswith('[PN-ERR]') or l.startswith('[PN-EXP]'):
            extras[l.split(']')[0] + ']'].append(l)
            if l.startswith('[PN-EXP] start;'):
                # Ramiona startuja z KOPII pamieci gry z tej chwili - zapamietujemy ja jako baze.
                d = kv(l[len('[PN-EXP] '):])
                run = d.get('runId', '?')
                stanPrzedEksp[run] = {m: dict(fz) for (r, e, m), fz in stan.items() if r == run and not e}
                faktyPrzedEksp[run] = {m: dict(fs) for (r, e, m), fs in fakty.items() if r == run and not e}
                stylPrzedEksp[d.get('eksperyment')] = ostatnieDni.get(run)
            elif l.startswith('[PN-EXP] ramie;'):
                d = kv(l[len('[PN-EXP] '):])
                ramionaStylu[(d.get('eksperyment') or '') + '/' + (d.get('ramie') or '')] = d.get('styl')
    return cols, profile, stc, luki, rows, arcs, execs, facts, extras, porzucone, styl_cfg, gracze, sekw


def main():
    args = sys.argv[1:]
    tryb = 'wszystko'
    if '--tryb' in args:
        i = args.index('--tryb')
        tryb = args[i + 1]
        del args[i:i + 2]
    path = args[0] if args else DOMYSLNY
    cols, profile, stc, luki, rows, arcs, execs, facts, extras, porzucone, styl_cfg, gracze, sekw = wczytaj(path)
    if tryb != 'wszystko':
        rows = [r for r in rows if r.get('tryb') == tryb]
        arcs = [a for a in arcs if a.get('tryb') == tryb]
        execs = [e for e in execs if e.get('tryb') == tryb]
        facts = [x for x in facts if x.get('tryb') == tryb]
        gracze = [g for g in gracze if g.get('tryb') == tryb]
        if tryb != 'gra':
            sekw = {}
    STC_DOMYSLNE = dict(wR=0.55, wB=0.20, wI=0.25, floor=1.5, sat=4.5, T=0.1, rundy=8, maxLukow=2)
    if any(not r.get('_stc') for r in rows):
        print('UWAGA: wiersze bez linii [PN-CONFIG] storyteller w swojej sesji - parametry PASS/bramy domyslne z kodu')

    print('plik:', path)
    print('kolumn w naglowku:', len(cols) if cols else 'BRAK', '| wierszy:', len(rows),
          '| zly ksztalt:', sum(1 for r in rows if not r['_ksztalt_ok']),
          '| porzucona galaz po wczytaniu:', porzucone)
    print('wersjaLogu:', sorted(set(r.get('wersjaLogu') for r in rows)), '| tryb:', dict(collections.Counter(r.get('tryb') for r in rows)))
    print('profile z [PN-CONFIG]:', ', '.join(sorted(profile)) or 'BRAK', '| luki z [PN-CONFIG]:', ', '.join(sorted(luki)) or 'BRAK')

    grupy = collections.OrderedDict()
    for r in rows:
        klucz = ('sym ' + r.get('eksperyment', '?')) if r.get('tryb') == 'symulacja' else \
            'gra run=%s mapa=%s odc=%d' % (r.get('runId'), r.get('mapa'), r['_odcinek'])
        grupy.setdefault(klucz, []).append(r)

    narus, przyklad = collections.Counter(), {}

    def narusz(n, opis):
        narus[n] += 1
        przyklad.setdefault(n, opis)

    # ---------------- 1-17: niezmienniki v6 (bez zmian znaczenia) ----------------
    for klucz, rr in grupy.items():
        seria = None
        poprzedni = None
        for r in rr:
            nr = int(r['decyzjaNr'])
            prof = r['_profile'].get(r['profil'], PROFIL_DOMYSLNY)
            stc = r['_stc'] or STC_DOMYSLNE
            los = int(r['losowan'])
            rundy = (los - 1) / 2.0
            kryzys = r.get('kryzys') == 'true'
            if los < 3 or (los - 1) % 2:
                narusz('01 losowan = 1 + 2*rundy', (klucz, nr))
            if int(r['niedostepnych']) == 0 and rundy != 1 + int(r['odmowSilnika']) - int(r['odmowCzola']):
                narusz('02 rundy = 1 + odmowSilnika - odmowCzola (niedostepnych=0)', (klucz, nr))
            if int(r['pytanDoGry']) > stc['rundy']:
                narusz('03 pytanDoGry <= maxSelectionRounds', (klucz, nr))
            if int(r['histDecyzji']) != nr:
                narusz('04 histDecyzji == decyzjaNr', (klucz, nr))
            if poprzedni is not None and nr != poprzedni + 1:
                narusz('05 decyzjaNr ciagly w grupie', (klucz, poprzedni, nr))
            if int(r['wygenerowanych']) < int(r['kandydatow']):
                narusz('06 wygenerowanych >= kandydatow', (klucz, nr))
            pas = r['decyzja'] == 'PASS'
            if pas != (r['pRunda'] == ''):
                narusz('07 pRunda puste <=> PASS', (klucz, nr))
            if not pas and los == 3 and abs(f(r, 'pRunda') * (1 - f(r, 'pBrama')) - f(r, 'p')) > 0.0015:
                narusz('08 pRunda*(1-pBrama) ~ p (losowan=3)', (klucz, nr))
            t = f(r, 'napiecie')
            nap = (prof['wN'] * f(r, 'napiecieNarr') + prof['wS'] * f(r, 'napiecieSyt')) / (prof['wN'] + prof['wS'])
            if abs(nap - t) > 0.0015:
                narusz('09 napiecie = srednia wazona czlonow', (klucz, nr, round(nap, 4), t))
            oczek = 'Escalate' if t < prof['calm'] else ('Breathe' if t > prof['tense'] else 'Hold')
            moc = prof['span'] * (1 - 2 * min(1.0, max(0.0, t)))
            if kryzys:
                if r['intencja'] != 'Breathe':
                    narusz('12 kryzys -> intencja Breathe', (klucz, nr, r['intencja']))
                if abs(min(moc, 0.0) - f(r, 'docelowaMoc')) > 0.002 or f(r, 'docelowaMoc') > 0.0005:
                    narusz('13 kryzys -> docelowaMoc = min(moc, 0)', (klucz, nr, r['docelowaMoc']))
            else:
                if r['intencja'] != oczek:
                    narusz('10 intencja z progow profilu', (klucz, nr, t, r['intencja']))
                if abs(moc - f(r, 'docelowaMoc')) > 0.002:
                    narusz('11 docelowaMoc = span*(1-2t)', (klucz, nr, round(moc, 4), r['docelowaMoc']))
            rr_ = min(1.0, max(0.0, (f(r, 'gestosc') - stc['floor']) / (stc['sat'] - stc['floor'])))
            up = (stc['wR'] * rr_ + stc['wB'] + stc['wI'] * FIT.get(r['intencja'], 0.5)) / (stc['wR'] + stc['wB'] + stc['wI'])
            if abs(up - f(r, 'passWynik')) > 0.0015:
                narusz('14 passWynik = pelny wzor', (klucz, nr, round(up, 4), r['passWynik']))
            if r['best'] != '' and r['passStlumiony'] == 'false' and r['pBrama'] != '':
                pb = 1.0 / (1.0 + math.exp((f(r, 'best') - f(r, 'passWynik')) / stc['T']))
                # TOLERANCJA WYPROWADZONA, nie stala: best i passWynik sa w logu zaokraglone do 3 miejsc
                # (+-0.0005 kazde, roznica +-0.001), a wykladnik dzieli ja przez T, wiec pBrama moze
                # odejsc o p(1-p)*0.001/T; do tego zaokraglenie samego pBrama (+-0.0005). Stala 0.002
                # dawala falszywe naruszenia (dane z gry 2026-09-23: roznica 0.00212 przy granicy 0.00249).
                tol = pb * (1 - pb) * (0.001 / stc['T']) + 0.0005 + 1e-6
                if abs(pb - f(r, 'pBrama')) > tol:
                    narusz('15 pBrama = 1/(1+exp((best-passWynik)/T))', (klucz, nr, round(pb, 4), r['pBrama']))
            if seria is not None and poprzedni is not None and nr == poprzedni + 1 and int(r['ciszaSwiadoma']) != seria:
                narusz('16 ciszaSwiadoma z poprzedniego wiersza', (klucz, nr, seria, r['ciszaSwiadoma']))
            s0 = int(r['ciszaSwiadoma'])
            seria = (s0 + 1 if (r['powodPass'] == 'Competitive' and not kryzys) else s0) if pas else 0
            if (r.get('straznikZawieszony') == 'true') and not kryzys:
                narusz('17 straznikZawieszony tylko w kryzysie', (klucz, nr))

            # ---------------- 18-24: luki w wierszu [PN-DATA] (v7-v10) ----------------
            if r.get('wersjaLogu') in OD_V7:
                if not r['_ksztalt_ok']:
                    narusz('18 ksztalt wiersza v7 = naglowek', (klucz, nr))
                aa, pl = r.get('arcAlignment', ''), r.get('premiaLuku', '')
                if (aa == '') != (pl == ''):
                    narusz('19 premiaLuku = (best-pasmo)*arcAlignment', (klucz, nr, 'puste razem'))
                elif aa != '' and r['best'] != '' and \
                        abs((f(r, 'best') - f(r, 'pasmo')) * f(r, 'arcAlignment') - f(r, 'premiaLuku')) > 0.002:
                    narusz('19 premiaLuku = (best-pasmo)*arcAlignment', (klucz, nr, r['best'], r['pasmo'], pl))
                if aa != '' and not (r.get('lukStosowany') == 'true' and int(r.get('lukDopasowanych') or 0) >= 1):
                    narusz('20 arcAlignment => lukStosowany i dopasowanych >= 1', (klucz, nr))
                if r.get('lukiAktywne', '') != '' and int(r['lukiAktywne']) > stc.get('maxLukow', 2):
                    narusz('21 lukiAktywne <= maxRownoczesnych', (klucz, nr, r['lukiAktywne']))
                fazy = [x for x in r.get('lukFazy', '').split(',') if x]
                if r.get('lukiAktywne', '') != '':
                    if int(r['lukiAktywne']) != len(fazy):
                        narusz('21 lukiAktywne = liczba wpisow lukFazy', (klucz, nr))
                    wiersz = sorted(':'.join(x.split(':')[:2]) for x in fazy)
                    if wiersz != r['_stanLukow']:
                        narusz('23 lukFazy odtwarzalne z [PN-ARC]', (klucz, nr, wiersz, r['_stanLukow']))
                for x in fazy:
                    cz = x.split(':')
                    if len(cz) == 3 and cz[2] == 'aktywna' and cz[0] in r['_luki']:
                        luk = r['_luki'][cz[0]]
                        fz = luk['fazy'][luk['idx'][cz[1]]] if cz[1] in luk['idx'] else None
                        if fz is not None and not any(r['intencja'] in ZGODNOSC.get(v, set()) for v in fz['walencje']):
                            narusz('24 faza aktywna => walencja zgodna z intencja', (klucz, nr, x, r['intencja']))

            # ---------------- 28, 31: fakty i konsekwencja w wierszu [PN-DATA] (v8-v10) ----------------
            if r.get('wersjaLogu') in OD_V8:
                if r.get('faktow', '') != '' and int(r['faktow']) != r['_faktow']:
                    narusz('28 faktow = stan odtworzony z [PN-FACT]', (klucz, nr, r['faktow'], r['_faktow']))
                segmenty = r.get('klucz', '').split('|')
                if pas:
                    if r.get('konsekwencja', '') != '':
                        narusz('31 konsekwencja zgodna z kluczem kompozycji', (klucz, nr, 'PASS', r.get('konsekwencja')))
                elif len(segmenty) != 6 or segmenty[5] != r.get('konsekwencja'):
                    narusz('31 konsekwencja zgodna z kluczem kompozycji', (klucz, nr, r.get('klucz'), r.get('konsekwencja')))

            # ---------------- 32-38, 41: styl gracza w wierszu [PN-DATA] (v9-v10) ----------------
            if r.get('wersjaLogu') in OD_V9:
                styl_wiersza(r, klucz, nr, pas, los, prof, r['_styl'], narusz)

            # ---------------- 46: brama Anomaly i lustro silnika (v10) ----------------
            # Wyprowadzenie z kodu moda: strona to symbol enuma AnomalyGateKind z przepisu tury - w grze zawsze
            # Regular albo Anomaly (None tylko w testach bez przepisu); bez DLC (szansa pusta) AnomalyGate.Draw
            # zwraca Regular bez losowania; szansa to prawdopodobienstwo z gry (AnomalyIncidentChanceNow) w [0, 1];
            # zablokowanychSilnik to licznik akcji (>= 0) albo puste, gdy lustro nie dzialalo.
            if r.get('wersjaLogu') in OD_V10:
                tura, sz, zb = r.get('anomaliaTura', ''), r.get('anomaliaSzansa', ''), r.get('zablokowanychSilnik', '')
                n46 = '46 kolumny v10: strona bramy Anomaly, szansa, lustro silnika'
                if tura not in ('Regular', 'Anomaly'):
                    narusz(n46, (klucz, nr, 'strona', tura))
                elif sz == '' and tura != 'Regular':
                    narusz(n46, (klucz, nr, 'bez DLC strona', tura))
                if sz != '' and not (0.0 <= f(r, 'anomaliaSzansa') <= 1.0):
                    narusz(n46, (klucz, nr, 'szansa', sz))
                if zb != '' and not (zb.isdigit()):
                    narusz(n46, (klucz, nr, 'zablokowanychSilnik', zb))

            # ---------------- 47-48: kolumny v11 (etap L kroku 9) ----------------
            # Z kodu moda (EngineMirror.DataColumn): lustroOdcina puste <=> lustro nie dzialalo <=> zablokowanychSilnik
            # puste; "-" = dzialalo i nic nie odcielo (licznik 0); lista w postaci kanonicznej (ordynalnie, bez powtorzen),
            # a licznik liczy tylko akcje, ktore przeszly strone bramy i wlasne warunki - wiec nie wiecej niz lista.
            # czasMs mierzony zawsze (Stopwatch), takze w symulatorze.
            if r.get('wersjaLogu') in OD_V11:
                lo, zb = r.get('lustroOdcina', ''), r.get('zablokowanychSilnik', '')
                if (lo == '') != (zb == ''):
                    narusz(N47, (klucz, nr, 'puste razem', lo, zb))
                elif lo == '-':
                    if zb != '0':
                        narusz(N47, (klucz, nr, 'lista pusta, licznik', zb))
                elif lo != '':
                    nazwy_ = lo.split(',')
                    if len(set(nazwy_)) != len(nazwy_) or nazwy_ != sorted(nazwy_) or '' in nazwy_:
                        narusz(N47, (klucz, nr, 'lista niekanoniczna', lo))
                    if zb.isdigit() and int(zb) > len(nazwy_):
                        narusz(N47, (klucz, nr, 'licznik > lista', zb, len(nazwy_)))
                cz = r.get('czasMs', '')
                try:
                    if cz == '' or float(cz) < 0:
                        narusz(N48, (klucz, nr, cz))
                except ValueError:
                    narusz(N48, (klucz, nr, cz))
            poprzedni = nr

    # ---------------- 22: legalne krawedzie automatow ----------------
    for a in arcs:
        luk = a['_luki'].get(a.get('luk'))
        if luk is None or a.get('zdarzenie') in ('odrzucenie',):
            continue
        idx, fazy = luk['idx'], luk['fazy']
        z, do, powod, wynik = a.get('z'), a.get('do'), a.get('powod', ''), a.get('wynik')
        ok = True
        if a.get('zdarzenie') == 'otwarcie':
            ok = z == fazy[0]['id'] and (do == fazy[1]['id'] if len(fazy) > 1 else False)
        elif a.get('zdarzenie') == 'przejscie':
            if powod == 'wykonanie':
                ok = z in idx and do in idx and idx[do] == idx[z] + 1
            elif powod == 'limitCzasu':
                ok = z in idx and do == fazy[-1]['id'] and fazy[idx[z]]['kind'] in ('Escalation', 'Climax')
            elif powod.startswith('straznik:'):
                ok = (z, do) in luk['krawedzie']
            else:
                ok = False
        elif a.get('zdarzenie') == 'zamkniecie':
            if powod == 'wykonanie':
                ok = z == fazy[-1]['id'] and wynik == 'rozwiazany'
            elif powod == 'limitCzasu':
                ok = z == fazy[-1]['id'] and wynik == 'wygaszony'
            elif powod.startswith('straznik:'):
                ok = True   # zamkniecie calego luku mozliwe w kazdej fazie (np. pojednanie)
            else:
                ok = False
        if not ok:
            narusz('22 krawedz [PN-ARC] legalna w katalogu', (a.get('luk'), a.get('zdarzenie'), z, do, powod, wynik))

    # ---------------- 25-27: [PN-EXEC] ----------------
    # KLUCZ Z RAMIENIEM: ten sam tick wystepuje w KAZDYM ramieniu eksperymentu.
    def klucz_zdarzenia(d):
        return d.get('runId'), d.get('eksperyment', ''), d.get('mapa'), d.get('tick')

    # S6: [PN-EXEC] niesie tickDecyzji - na sciezce spoznionej potwierdzenie pada w T+1000, a dotyczy
    # decyzji z T. Stare pliki (bez pola) maja tylko sciezke normalna, gdzie tick == tickDecyzji.
    def klucz_decyzji(e):
        return e.get('runId'), e.get('eksperyment', ''), e.get('mapa'), e.get('tickDecyzji') or e.get('tick')

    # Etap L: [PN-EXEC] akcji wymuszonej (akcja debugowa) nie ma wiersza decyzji ani lukow i faktow.
    execs_dec = [e for e in execs if not wymuszona(e)]
    klucz_exec = collections.Counter(klucz_decyzji(e) for e in execs_dec)
    for r in rows:
        if r.get('wersjaLogu') in OD_V7 and r['decyzja'] != 'PASS':
            if klucz_exec[klucz_zdarzenia(r)] != 1:
                narusz('25 kazde zdarzenie <-> dokladnie jedno [PN-EXEC]',
                       (r.get('eksperyment'), r.get('mapa'), r.get('tick'), klucz_exec[klucz_zdarzenia(r)]))
    # 26 wiaze krok luku z potwierdzeniem po ticku EMISJI (krok spozniony i jego [PN-EXEC] padaja w T+1000);
    # 30 wiaze fakt z decyzja po ticku DECYZJI (tickZrodla).
    exec_status = {klucz_zdarzenia(e): e.get('status') for e in execs_dec}
    exec_status_decyzji = {klucz_decyzji(e): e.get('status') for e in execs_dec}
    for a in arcs:
        if a.get('powod') == 'wykonanie':
            st = exec_status.get(klucz_zdarzenia(a))
            if st not in WYKONANE:
                narusz('26 krok "wykonanie" => wykonanie potwierdzone w tym ticku', (a.get('luk'), a.get('tick'), st))
    for e in execs:
        if (e.get('tryb') == 'symulacja') != (e.get('status') == 'symulacja'):
            if e.get('tryb') == 'symulacja' or e.get('status') == 'symulacja':
                narusz('27 symulator nigdy "wykonane", gra nigdy "symulacja"', (e.get('tryb'), e.get('status')))

    # ---------------- 29-30: [PN-FACT] ----------------
    # 29: fakt trafia do pamieci dopiero PO ticku decyzji zdarzenia, ktore go zostawilo (warunki
    #     widza swiat z poczatku tury), z dniem DECYZJI - nie dniem zastosowania.
    # 30: ustawienie tylko po wykonaniu, odrzucenie tylko po braku wykonania - sprawdzane wobec
    #     [PN-EXEC] sciezki normalnej (ten sam tick co decyzja); sciezka spozniona niesie swoj
    #     werdykt w polu "powod" i nie ma linii [PN-EXEC] w ticku decyzji.
    # 29 WEDLUG POWODU (S6): odrzucenie na sciezce NORMALNEJ pada w ticku decyzji (ConfirmPending po
    # yield), a spoznione - w nastepnym wywolaniu. Dawna regula "zawsze tick > tickZrodla" dawala falszywe
    # naruszenie przy kazdym niewykonanym zdarzeniu w zwyklej grze. "niejednoznaczne" bywa na obu
    # sciezkach; "niepoprawny" (zly klucz) bywa i przy kolejkowaniu, i przy stosowaniu - pomijany.
    for x in facts:
        zd = x.get('zdarzenie')
        if zd not in ('ustawienie', 'odrzucenie'):
            continue
        tz = int(x.get('tickZrodla') or 0)
        tf = int(x.get('tick') or 0)
        powod = x.get('powod') or ''
        if zd == 'ustawienie':
            dobry_tick = tf > tz
        elif powod == 'niewykonane':
            dobry_tick = tf == tz
        elif powod == 'pozno-niewykonane':
            dobry_tick = tf > tz
        else:
            dobry_tick = tf >= tz
        if not dobry_tick:
            narusz('29 fakt stosowany po ticku zdarzenia, z dniem decyzji', (x.get('klucz'), zd, powod, x.get('tick'), tz))
        elif abs(float(x.get('dzien') or 0) - tz / 60000.0) > 0.001:
            narusz('29 fakt stosowany po ticku zdarzenia, z dniem decyzji', (x.get('klucz'), x.get('dzien'), tz))
        st = exec_status_decyzji.get((x.get('runId'), x.get('eksperyment', ''), x.get('mapa'), str(tz)))
        if st is not None:
            if (x.get('zdarzenie') == 'ustawienie') != (st in WYKONANE):
                narusz('30 fakt ustawiony <=> zdarzenie wykonane', (x.get('klucz'), x.get('zdarzenie'), st))

    # ---------------- 33-35, 37-38: linie [PN-GRACZ] ----------------
    for g in gracze:
        styl_gracza(g, g['_styl'], narusz)

    # ---------------- 39: otwarcie luku z warunkiem stylu ----------------
    # Wiersz decyzji po ticku OTWARCIA (sciezka normalna: otwarcie w ticku decyzji), a gdy go nie ma - przez
    # [PN-EXEC] emitowany w tym ticku i jego tickDecyzji (sciezka spozniona: otwarcie w T+1000).
    # Wiersz decyzji = ostatni wiersz tej mapy PRZED linia otwarcia w pliku (przeglad S8: dawne laczenie po ticku
    # otwarcia wybieralo na sciezce spoznionej wiersz NOWEJ decyzji z T+1000, jesli ta tez zapadla).
    for a in arcs:
        if a.get('zdarzenie') != 'otwarcie':
            continue
        war = (a['_luki'].get(a.get('luk')) or {}).get('warunekStylu', '-')
        if war in ('-', '', None):
            continue
        r = a.get('_wiersz')
        if r is None or r.get('wersjaLogu') not in OD_V9:
            continue
        mocne = zbior_mocnych(r.get('stylMocne'))
        for w in war.split('/'):
            if (w[1:] in mocne) if w.startswith('!') else (w not in mocne):
                narusz('39 luk z warunkiem stylu => cecha w stylMocne decyzji',
                       (a.get('luk'), a.get('eksperyment', ''), a.get('tick'), war, r.get('stylMocne')))

    # ---------------- 40: ciaglosc [PN-GRACZ] ----------------
    # Po kotwicy (wczytanie, reset) albo poprzedniej linii: dni = min(poprzednie + 1, pojemnosc), a zamknieta
    # doba rosnie scisle (kotwica z dniem w toku D pozwala zamknac D).
    for run, zd in sekw.items():
        oczek, dzien0 = None, None
        for e in zd:
            if e[0] == 'k':
                oczek, dzien0 = e[1], (e[2] - 1 if e[2] is not None else None)
                continue
            g = e[1]
            if not (g.get('dni') or '').isdigit() or not (g.get('dzien') or '').lstrip('-').isdigit():
                narusz('40 ciaglosc [PN-GRACZ] (dni +1, dzien rosnie)', (run, 'zle pola', g.get('dni'), g.get('dzien')))
                continue
            dni, dz = int(g['dni']), int(g['dzien'])
            pojemnosc = g['_styl'].get('cap', 60) if g.get('_styl') else 60
            if oczek is not None and dni != min(oczek + 1, pojemnosc):
                narusz('40 ciaglosc [PN-GRACZ] (dni +1, dzien rosnie)', (run, dz, 'dni', oczek, dni))
            if dzien0 is not None and dz <= dzien0:
                narusz('40 ciaglosc [PN-GRACZ] (dni +1, dzien rosnie)', (run, 'dzien', dzien0, dz))
            oczek, dzien0 = dni, dz

    # ---------------- 42-45: krok 8 - log odpalen, list gracza, cache CanFireNow ----------------
    fired = extras.get('[PN-FIRED]', []) if tryb in ('wszystko', 'gra') else []
    cachel = [c for c in extras.get('[PN-CACHE]', []) if tryb == 'wszystko' or c.get('tryb') == tryb]

    # 42: wykonanie na sciezce normalnej <-> dokladnie jedna linia [PN-FIRED] pn=1 (ten tick, mapa, incydent);
    # kazde pn=1 ma potwierdzone wykonanie (takze spoznione albo niejednoznaczne - rejestracja przed yield).
    n42 = '42 wykonanie <-> [PN-FIRED] pn=1 (gra, obserwator w sesji)'
    pn1 = collections.Counter((x.get('runId'), x.get('mapa'), x.get('incydent'), x.get('tick'))
                              for x in fired if x.get('pn') == '1')
    for e in execs:
        # Akcje wymuszone paruje regula 54 (po numerze) - kilka w jednej pauzie ma ten sam tick i incydent.
        if e.get('tryb') != 'gra' or not e.get('_obserwator') or wymuszona(e):
            continue
        # Sciezka normalna (wykonane) i spozniona (pozno-wykonane): rejestracja idzie przed yield, wiec odpalenie
        # ma pn=1 w ticku DECYZJI takze wtedy, gdy potwierdzenie padlo w T+1000 (przeglad S10).
        normalna = (e.get('tickDecyzji') or e.get('tick')) == e.get('tick')
        if not ((e.get('status') == 'wykonane' and normalna) or (e.get('status') == 'pozno-wykonane' and not normalna)):
            continue
        k = (e.get('runId'), e.get('mapa'), e.get('incydent'), e.get('tickDecyzji') or e.get('tick'))
        if pn1[k] != 1:
            narusz(n42, ('wykonane, a linii pn=1', pn1[k], k))
    potw = set((e.get('runId'), e.get('mapa'), e.get('incydent'), e.get('tickDecyzji') or e.get('tick'))
               for e in execs
               if e.get('tryb') == 'gra' and e.get('status') in ('wykonane', 'pozno-wykonane', 'niejednoznaczne'))
    for x in fired:
        if x.get('pn') == '1' and (x.get('runId'), x.get('mapa'), x.get('incydent'), x.get('tick')) not in potw:
            narusz(n42, ('pn=1 bez potwierdzonego wykonania', x.get('runId'), x.get('mapa'), x.get('incydent'), x.get('tick')))

    # 43: forma linii [PN-FIRED].
    n43 = '43 forma [PN-FIRED] (kontekst, zakresy, pn=1 tylko nasz narrator)'
    for x in fired:
        t_ = int(x.get('tick') or -1)
        kon = x.get('kontekst')
        if kon not in ('przed', 'po', '-'):
            narusz(n43, ('kontekst', kon, t_))
        if kon == 'przed' and t_ % 1000 != 0:
            narusz(n43, ('kontekst=przed poza tickiem interwalu', t_))
        if (kon == '-') != (x.get('mapa') == '-1'):
            narusz(n43, ('kontekst a cel', kon, x.get('cel')))
        if x.get('powaleni', '') != '' and x.get('kolonisciNaMapie', '') != '' \
                and int(x['powaleni']) > int(x['kolonisciNaMapie']):
            narusz(n43, ('powaleni > kolonisci na mapie', x['powaleni'], x['kolonisciNaMapie'], t_))
        if x.get('zagrozenie', '') not in ('', '0', '1'):
            narusz(n43, ('zagrozenie', x.get('zagrozenie'), t_))
        # Obserwator przeglada co tick, wiec prawdziwe opoznienie jest zawsze 0 (przeglad S10): inna wartosc to
        # przerwa w obserwacji albo wpis podrzucony (kopia StoryState, wpis z przyszlosci).
        if (x.get('opoznienie') or '0') != '0':
            narusz(n43, ('opoznienie != 0', x.get('opoznienie'), t_))
        # Cel i mapa spojne; poza mapa (swiat, karawana) nie ma domu.
        if x.get('mapa') == '-1':
            if x.get('dom') != 'false' or (x.get('cel') or '').startswith('map:'):
                narusz(n43, ('cel poza mapa, a dom albo cel mapy', x.get('cel'), x.get('dom'), t_))
        elif x.get('cel') != 'map:' + (x.get('mapa') or ''):
            narusz(n43, ('cel != map:mapa', x.get('cel'), x.get('mapa'), t_))
        if x.get('pn') == '1' and (x.get('narrator') != 'PN_GenerativeNarrator' or x.get('dom') != 'true'):
            narusz(n43, ('pn=1 poza naszym narratorem albo poza domem', x.get('narrator'), x.get('dom'), t_))
        if abs(float(x.get('dzien') or 0) - t_ / 60000.0) > 0.0006:
            narusz(n43, ('dzien != tick/60000', x.get('dzien'), t_))

    # 44: [PN-EXEC] list= zgodny ze statusem, sciezka (spozniona) i konfiguracja; warianty liczone dokladnie
    # dla zdarzen wykonanych i symulowanych na sciezce normalnej.
    n44 = '44 [PN-EXEC] list= zgodny ze statusem, sciezka i konfiguracja'
    for e in execs:
        if 'list' not in e:
            continue
        st, li, war = e.get('status'), e.get('list'), e.get('warianty', '-')
        pozno = (e.get('tickDecyzji') or e.get('tick')) != e.get('tick')
        if pozno:
            ok = li == 'pozno'
        elif st == 'wykonane':
            # 'ukryty' (krok 9, K2-b): zdarzenie ukryte - gra nie wysyla listu, tekst zostaje tylko w logu.
            ok = li in ('dopisany', 'odroczony', 'brak', 'wylaczony', 'pustyOpis', 'blad', 'ukryty')
        elif st == 'symulacja':
            ok = li in ('symulacja', 'blad')
        else:
            ok = li == 'niewykonane'
        if not ok:
            narusz(n44, ('list', st, li, 'spozniona' if pozno else 'normalna', e.get('tick')))
        if li in ('dopisany', 'odroczony') and (int(e.get('nowychListow') or 0) < 1 or not e.get('tekstListu')):
            narusz(n44, ('dopisany bez nowego listu albo bez tekstu', e.get('nowychListow'), e.get('tick')))
        # Zdarzenie ukryte: comp nie szuka listu (nowy list w tym ticku bylby cudzy), a tekst ma trafic do logu.
        if li == 'ukryty' and (int(e.get('nowychListow') or 0) != 0 or not e.get('tekstListu')):
            narusz(n44, ('ukryty z liczonym listem albo bez tekstu w logu', e.get('nowychListow'), e.get('tick')))
        liczony = st in ('wykonane', 'symulacja') and not pozno and li != 'blad'
        if liczony != (war not in ('-', '')):
            narusz(n44, ('warianty', st, li, war, e.get('tick')))
        if war not in ('-', '') and any(w.count(':') != 1 for w in war.split(',')):
            narusz(n44, ('format warianty= (klocek:wariant)', war))
        zl = (e.get('_stc') or {}).get('zlozonyList')
        if st == 'wykonane' and not pozno and li not in ('blad', 'ukryty') and zl is not None and (li == 'wylaczony') != (zl is False):
            narusz(n44, ('wylaczony a zlozonyList w konfiguracji', li, zl, e.get('tick')))

    # 45: [PN-CACHE] - pole rozny spojne, kolizja tylko w ticku NASZEJ decyzji na tej mapie.
    n45 = '45 [PN-CACHE] spojna i w ticku naszej decyzji'
    ticki_decyzji = set((r.get('runId'), r.get('eksperyment', ''), r.get('mapa'), r.get('tick')) for r in rows)
    for c in cachel:
        if (c.get('werdyktGry') != c.get('nasz')) != (c.get('rozny') == 'true'):
            narusz(n45, ('rozny', c.get('werdyktGry'), c.get('nasz'), c.get('rozny')))
        if (c.get('runId'), c.get('eksperyment', ''), c.get('mapa'), c.get('tick')) not in ticki_decyzji:
            narusz(n45, ('kolizja poza tickiem decyzji', c.get('mapa'), c.get('tick')))

    # ---------------- 49-54: linie etapu L kroku 9 (tylko gra) ----------------
    dni_ = extras.get('[PN-DZIEN]', []) if tryb in ('wszystko', 'gra') else []
    kol_ = extras.get('[PN-KOLONISTA]', []) if tryb in ('wszystko', 'gra') else []
    perf_ = extras.get('[PN-PERF]', []) if tryb in ('wszystko', 'gra') else []
    eval_ = extras.get('[PN-EVAL]', [])

    def liczba_(x, k):
        v = x.get(k, '')
        return None if v in ('', None) else float(v)

    # 49: doba zamknieta = tick div 60000 - 1 (linia w pierwszym ticku nowej doby), jedna linia na (runId, mapa, dzien),
    # relacje licznosci jak w [PN-FIRED], liczniki gry (StatsRecord) nie maleja w obrebie runId.
    wid = collections.Counter((x.get('runId'), x.get('mapa'), x.get('dzien')) for x in dni_)
    ostatnieLiczniki = {}
    for x in dni_:
        t_ = int(x.get('tick') or 0)
        if str(t_ // 60000 - 1) != x.get('dzien'):
            narusz(N49, ('dzien', x.get('dzien'), t_))
        if wid[(x.get('runId'), x.get('mapa'), x.get('dzien'))] != 1:
            narusz(N49, ('powtorzona doba', x.get('runId'), x.get('mapa'), x.get('dzien')))
        k_, nm, pw = liczba_(x, 'kolonisci'), liczba_(x, 'kolonisciNaMapie'), liczba_(x, 'powaleni')
        if None not in (nm, pw) and pw > nm:
            narusz(N49, ('powaleni > na mapie', pw, nm, t_))
        if None not in (k_, nm) and nm > k_:
            narusz(N49, ('na mapie > kolonisci', nm, k_, t_))
        if x.get('zagrozenie') not in ('0', '1') or x.get('pora') not in ('0', '1', '2', '3'):
            narusz(N49, ('zagrozenie/pora', x.get('zagrozenie'), x.get('pora'), t_))
        ns = liczba_(x, 'nastroj')
        if ns is not None and not (0.0 <= ns <= 1.0):
            narusz(N49, ('nastroj', ns, t_))
        if (x.get('bogactwo', '') == '') != (x.get('bogactwoWiek', '') == '') or (liczba_(x, 'bogactwoWiek') or 0) < 0:
            narusz(N49, ('bogactwo i wiek', x.get('bogactwo'), x.get('bogactwoWiek'), t_))
        licz = tuple(liczba_(x, k) for k in ('napadow', 'threatBig', 'poleglych'))
        pop = ostatnieLiczniki.get(x.get('runId'))
        if pop is not None and any(a is not None and b is not None and b < a for a, b in zip(pop, licz)):
            narusz(N49, ('licznik gry maleje', pop, licz, t_))
        ostatnieLiczniki[x.get('runId')] = licz

    # 50: sklad kolonii - kotwica "start" (pionek -1) zaczyna lancuch, kazda zmiana to +-1 (dolaczyl +1, ubytek -1).
    stanKol = {}
    for x in kol_:
        run, zd = x.get('runId'), x.get('zdarzenie')
        n_ = int(x.get('liczebnosc') or -1)
        if zd not in KOLONISCI_ZDARZENIA or n_ < 0:
            narusz(N50, ('zdarzenie/liczebnosc', zd, x.get('liczebnosc'), x.get('tick')))
            continue
        if zd == 'start':
            if x.get('pionek') != '-1':
                narusz(N50, ('kotwica z pionkiem', x.get('pionek'), x.get('tick')))
            stanKol[run] = n_
            continue
        if run not in stanKol:
            narusz(N50, ('zmiana bez kotwicy', run, zd, x.get('tick')))
        elif n_ != stanKol[run] + (1 if zd == 'dolaczyl' else -1):
            narusz(N50, ('skok licznosci', stanKol[run], zd, n_, x.get('tick')))
        stanKol[run] = n_

    # 51: [PN-FIRED] z polami etapu L - listow = liczba etykiet, frakcja razem z jej Defem, pora i noc tylko na mapie.
    for x in fired:
        if 'listow' not in x:
            continue
        t_ = x.get('tick')
        et = [] if (x.get('listy') or '') == '' else x['listy'].split(' | ')
        if str(len(et)) != x.get('listow'):
            narusz(N51, ('listow', x.get('listow'), len(et), t_))
        if x.get('listyWspolne') not in ('tak', 'nie') or (x.get('listyWspolne') == 'tak' and not et):
            narusz(N51, ('listyWspolne', x.get('listyWspolne'), t_))
        if not (x.get('wymuszone', '0') or '').isdigit() or (wymuszona(x) and
                                                         (x.get('pn') != '1' or x.get('kontekst') != 'po')):
            narusz(N51, ('wymuszone', x.get('wymuszone'), x.get('pn'), x.get('kontekst'), t_))
        if (x.get('frakcja') == '-') != (x.get('frakcjaDef') == '-'):
            narusz(N51, ('frakcja bez Defa', x.get('frakcja'), x.get('frakcjaDef'), t_))
        mapa_ = x.get('mapa') != '-1'
        if mapa_ != (x.get('pora', '') != '') or x.get('pora', '') not in ('', '0', '1', '2', '3') \
                or x.get('noc', '') not in ('', '0', '1') or (x.get('pora', '') == '') != (x.get('noc', '') == ''):
            narusz(N51, ('pora/noc', x.get('mapa'), x.get('pora'), x.get('noc'), t_))
        if (x.get('bogactwo', '') == '') != (x.get('bogactwoWiek', '') == '') or (liczba_(x, 'bogactwoWiek') or 0) < 0:
            narusz(N51, ('bogactwo i wiek', x.get('bogactwo'), x.get('bogactwoWiek'), t_))

    # 52: [PN-PERF] - agregaty spojne (0 <= max <= suma, kubelki sumuja sie do liczby, obserwatory co tick), doba
    # = tick div 60000 - 1, a decyzji = wiersze gry od poprzedniej linii [PN-PERF] w pliku (albo od [PN-LOAD]).
    wper = collections.Counter((x.get('runId'), x.get('dzien')) for x in perf_)
    for x in perf_:
        t_ = int(x.get('tick') or 0)
        if str(t_ // 60000 - 1) != x.get('dzien'):
            narusz(N52, ('dzien', x.get('dzien'), t_))
        if wper[(x.get('runId'), x.get('dzien'))] != 1:
            narusz(N52, ('powtorzona doba', x.get('runId'), x.get('dzien')))
        for g in PERF_GRUPY:
            n_ = x.get(g, '')
            su = [v for k_, v in x.items() if k_.startswith(g + 'Suma')]
            mx = [v for k_, v in x.items() if k_.startswith(g + 'Max')]
            if not n_.isdigit() or len(su) != 1 or len(mx) != 1:
                narusz(N52, ('pola', g, t_))
                continue
            s_, m_ = float(su[0]), float(mx[0])
            if not (0.0 <= m_ <= s_ + 0.002) or (n_ == '0' and (s_ != 0 or m_ != 0)):
                narusz(N52, ('max/suma', g, n_, s_, m_, t_))
            if g + 'Kubelki' in x:
                kub = [int(v) for v in x[g + 'Kubelki'].split('/')]
                gr = x.get(g + 'Granice', '').split('/')
                if sum(kub) != int(n_) or len(kub) != len(gr) + 1:
                    narusz(N52, ('kubelki', g, x[g + 'Kubelki'], n_, t_))
        if any(x.get(g) != x.get('tickow') for g in ('odpalen', 'styl', 'doba', 'kolonistow')) \
                or int(x.get('tickow') or 0) > 60000:
            narusz(N52, ('obserwatory != tickow', x.get('tickow'), t_))
        if x.get('decyzji', '').isdigit() and int(x['decyzji']) != x['_decyzjiWPliku']:
            narusz(N52, ('decyzji != wiersze gry doby', x['decyzji'], x['_decyzjiWPliku'], t_))

    # 53: [PN-EVAL] - nowy runId "etykieta-narrator-...", zadnego wiersza z nim wczesniej w pliku, a pierwsza decyzja
    # kazdej mapy pod nim zaczyna od pustej pamieci (decyzjaNr 0, historia 0).
    for x in eval_:
        run = x.get('runId')
        if run == x.get('runIdPoprzedni') or x.get('etykieta') not in ('L2', 'KAL', 'G0') \
                or not (run or '').startswith('%s-%s-' % (x.get('etykieta'), x.get('narrator'))):
            narusz(N53, ('runId', run, x.get('runIdPoprzedni'), x.get('etykieta')))
        if any(r_.get('runId') == run and r_['_lin'] < x['_lin'] for r_ in rows):
            narusz(N53, ('wiersze z nowym runId przed linia', run))
        pierwsze = {}
        for r_ in rows:
            if r_['_lin'] > x['_lin'] and r_.get('runId') == run and r_.get('tryb') == 'gra':
                pierwsze.setdefault(r_.get('mapa'), r_)
        for mapa_, r_ in pierwsze.items():
            if r_.get('decyzjaNr') != '0' or r_.get('histWpisow') != '0':
                narusz(N53, ('pierwsza decyzja nie od zera', run, mapa_, r_.get('decyzjaNr'), r_.get('histWpisow')))

    # 54: [PN-EXEC] akcji wymuszonej (wymuszone = numer) - tylko gra, sciezka normalna, bez faktow i krokow lukow z tego
    # ticku; wykonane => linia [PN-FIRED] pn=1 z kontekstem "po" i tym samym numerem (odpalenie z UI po przegladzie
    # ticku, logowane wprost); w druga strone kazde odpalenie wymuszone ma swoj [PN-EXEC].
    for e in execs:
        if not wymuszona(e):
            continue
        k_ = (e.get('runId'), e.get('mapa'), e.get('tick'))
        if e.get('tryb') != 'gra' or (e.get('tickDecyzji') or e.get('tick')) != e.get('tick'):
            narusz(N54, ('tryb/sciezka', e.get('tryb'), e.get('tick'), e.get('tickDecyzji')))
        if any((x.get('runId'), x.get('mapa'), x.get('tickZrodla')) == k_ for x in facts) \
                or any((a.get('runId'), a.get('mapa'), a.get('tick')) == k_ and a.get('powod') == 'wykonanie' for a in arcs):
            narusz(N54, ('fakt albo krok luku z akcji wymuszonej', k_))
        if e.get('status') == 'wykonane' and e.get('_obserwator') and not any(
                x.get('pn') == '1' and x.get('kontekst') == 'po' and x.get('wymuszone') == e.get('wymuszone')
                and (x.get('runId'), x.get('mapa'), x.get('tick')) == k_
                and x.get('incydent') == e.get('incydent') for x in fired):
            narusz(N54, ('wykonane bez [PN-FIRED] pn=1 kontekst=po', k_, e.get('incydent')))

    wym_exec = set((e.get('runId'), e.get('mapa'), e.get('tick'), e.get('incydent'), e.get('wymuszone'))
                   for e in execs if wymuszona(e))
    for x in fired:
        if wymuszona(x) and (x.get('runId'), x.get('mapa'), x.get('tick'), x.get('incydent'),
                             x.get('wymuszone')) not in wym_exec:
            narusz(N54, ('[PN-FIRED] wymuszone bez [PN-EXEC] z tym numerem', x.get('incydent'), x.get('tick')))

    # 55: [PN-LIST] - kazde odpalenie w ticku dostaje wszystkie nowe listy tego ticku (listow = liczba linii [PN-LIST]
    # tego ticku), incydenty= to zbior incydentow odpalonych w ticku ('-' = zadnego); akcja wymuszona osobno
    # (wymuszone = numer akcji - osobna grupa kazdej akcji). Pola z ustalonej listy - tresc= czytana do konca linii.
    listy_ = extras.get('[PN-LIST]', []) if tryb in ('wszystko', 'gra') else []
    lin_ = collections.defaultdict(list)
    for x in listy_:
        lin_[(x.get('runId'), x.get('tick'), x.get('wymuszone'))].append(x)
        if not set(x.keys()) <= POLA_LISTU or 'tresc' not in x or not (x.get('wymuszone') or '').isdigit():
            narusz(N55, ('pola', sorted(set(x.keys()) - POLA_LISTU), x.get('tick')))
    odp_ = collections.defaultdict(list)
    for x in fired:
        if 'listow' in x:
            odp_[(x.get('runId'), x.get('tick'), x.get('wymuszone', '0'))].append(x)
    for k_, oo in odp_.items():
        ll = lin_.get(k_, [])
        incydenty_ = set(o.get('incydent') for o in oo)
        for o in oo:
            if int(o.get('listow') or 0) != len(ll):
                narusz(N55, ('listow != linie [PN-LIST] ticku', o.get('incydent'), o.get('listow'), len(ll), k_[1]))
        for x in ll:
            if set((x.get('incydenty') or '').split(',')) != incydenty_:
                narusz(N55, ('incydenty listu != odpalenia ticku', x.get('incydenty'), sorted(incydenty_), k_[1]))
    for k_, ll in lin_.items():
        if k_ not in odp_ and any(x.get('incydenty') != '-' for x in ll):
            narusz(N55, ('list z incydentem bez odpalenia w ticku', k_[1], ll[0].get('incydenty')))

    # 56: [PN-STAN] - ekspozycja na warunki regul RN (PLAN_EWALUACJI.md 5). Na (runId, mapa): start zaczyna lancuch (takze
    # po wczytaniu i po [PN-EVAL], moze byc poza siatka), zmiana zmienia co najmniej jeden z predykatow kryzys/zagrozenie/
    # pusta, koniec zamyka mape (pola puste); zmiana i koniec na siatce 250 tickow; predykaty z liczb (kryzys = powaleni >= 1
    # i 2*powaleni >= na mapie, pusta = 0 na mapie); tick nie cofa sie po przycieciu galezi; stan obowiazujacy w ticku
    # [PN-DZIEN] na siatce zgodny z jej liczbami (ten sam odczyt gry w tym samym ticku).
    stan_ = extras.get('[PN-STAN]', []) if tryb in ('wszystko', 'gra') else []
    obowStan = {}
    histStan = collections.defaultdict(list)
    for x in stan_:
        run, mapa_, zd = x.get('runId'), x.get('mapa'), x.get('zdarzenie')
        t_ = int(x.get('tick') or 0)
        k_ = (run, mapa_)
        if zd not in STAN_ZDARZENIA:
            narusz(N56, ('zdarzenie', zd, t_))
            continue
        if histStan[k_] and t_ < histStan[k_][-1][0]:
            narusz(N56, ('tick cofa sie', run, mapa_, histStan[k_][-1][0], t_))
        if zd != 'start' and t_ % 250 != 0:
            narusz(N56, ('poza siatka 250', zd, t_))
        if zd == 'koniec':
            if any(x.get(p) not in ('', None) for p in ('kolonisciNaMapie', 'powaleni') + STAN_PREDYKATY):
                narusz(N56, ('koniec z polami stanu', run, mapa_, t_))
            if k_ not in obowStan:
                narusz(N56, ('koniec bez stanu', run, mapa_, t_))
            obowStan.pop(k_, None)
            histStan[k_].append((t_, None))
            continue
        nm, pw = liczba_(x, 'kolonisciNaMapie'), liczba_(x, 'powaleni')
        s_ = tuple(x.get(p) for p in STAN_PREDYKATY)
        if nm is None or pw is None or pw < 0 or pw > nm or any(v not in ('0', '1') for v in s_):
            narusz(N56, ('pola', nm, pw, s_, t_))
            continue
        kryzys_ = pw >= 1 and 2 * pw >= nm
        if s_[1] != ('1' if kryzys_ else '0') or s_[2] != ('1' if nm == 0 else '0'):
            narusz(N56, ('predykat niezgodny z liczbami', nm, pw, s_, t_))
        if zd == 'zmiana':
            if k_ not in obowStan:
                narusz(N56, ('zmiana bez startu', run, mapa_, t_))
            elif obowStan[k_] == s_:
                narusz(N56, ('zmiana bez zmiany predykatu', s_, t_))
        obowStan[k_] = s_
        histStan[k_].append((t_, s_))
    for x in dni_:
        t_ = int(x.get('tick') or 0)
        h_ = histStan.get((x.get('runId'), x.get('mapa')))
        nm, pw = liczba_(x, 'kolonisciNaMapie'), liczba_(x, 'powaleni')
        if not h_ or t_ % 250 != 0 or None in (nm, pw):
            continue
        i_ = bisect.bisect_right([e[0] for e in h_], t_) - 1
        if i_ < 0 or h_[i_][1] is None:
            continue
        oczek = (x.get('zagrozenie'), '1' if pw >= 1 and 2 * pw >= nm else '0', '1' if nm == 0 else '0')
        if h_[i_][1] != oczek:
            narusz(N56, ('stan niezgodny z [PN-DZIEN]', x.get('runId'), x.get('mapa'), t_, h_[i_][1], oczek))

    print('\nNIEZMIENNIKI (%d wierszy, %d grup, %d linii [PN-ARC], %d [PN-EXEC], %d [PN-FACT], %d [PN-GRACZ]):' % (
        len(rows), len(grupy), len(arcs), len(execs), len(facts), len(gracze)))
    nazwy = sorted(set(list(narus.keys()) + [
        '01 losowan = 1 + 2*rundy', '02 rundy = 1 + odmowSilnika - odmowCzola (niedostepnych=0)',
        '03 pytanDoGry <= maxSelectionRounds', '04 histDecyzji == decyzjaNr', '05 decyzjaNr ciagly w grupie',
        '06 wygenerowanych >= kandydatow', '07 pRunda puste <=> PASS', '08 pRunda*(1-pBrama) ~ p (losowan=3)',
        '09 napiecie = srednia wazona czlonow', '10 intencja z progow profilu', '11 docelowaMoc = span*(1-2t)',
        '12 kryzys -> intencja Breathe', '13 kryzys -> docelowaMoc = min(moc, 0)', '14 passWynik = pelny wzor',
        '15 pBrama = 1/(1+exp((best-passWynik)/T))', '16 ciszaSwiadoma z poprzedniego wiersza',
        '17 straznikZawieszony tylko w kryzysie', '18 ksztalt wiersza v7 = naglowek',
        '19 premiaLuku = (best-pasmo)*arcAlignment', '20 arcAlignment => lukStosowany i dopasowanych >= 1',
        '21 lukiAktywne <= maxRownoczesnych', '22 krawedz [PN-ARC] legalna w katalogu',
        '23 lukFazy odtwarzalne z [PN-ARC]', '24 faza aktywna => walencja zgodna z intencja',
        '25 kazde zdarzenie <-> dokladnie jedno [PN-EXEC]', '26 krok "wykonanie" => wykonanie potwierdzone w tym ticku',
        '27 symulator nigdy "wykonane", gra nigdy "symulacja"', '28 faktow = stan odtworzony z [PN-FACT]',
        '29 fakt stosowany po ticku zdarzenia, z dniem decyzji', '30 fakt ustawiony <=> zdarzenie wykonane',
        '31 konsekwencja zgodna z kluczem kompozycji', '32 premiaStylu = (best-pasmo)*stylWartosc',
        '33 zakresy i relacje kolumn stylu', '34 wypelnienie kolumn stylu: warstwa, rozgrzewka, zdarzenie',
        '35 stylAktywny <=> stylDni >= rozgrzewka', '36 stylKierunek = clamp(wO*o + wR*rytm)',
        '37 stylMocne = profil wzgledny z Z', '38 stylEtykieta = najblizszy prototyp',
        '39 luk z warunkiem stylu => cecha w stylMocne decyzji', '40 ciaglosc [PN-GRACZ] (dni +1, dzien rosnie)',
        '41 stylDni = ostatni [PN-GRACZ] / styl= / ramie',
        '42 wykonanie <-> [PN-FIRED] pn=1 (gra, obserwator w sesji)',
        '43 forma [PN-FIRED] (kontekst, zakresy, pn=1 tylko nasz narrator)',
        '44 [PN-EXEC] list= zgodny ze statusem, sciezka i konfiguracja',
        '45 [PN-CACHE] spojna i w ticku naszej decyzji',
        '46 kolumny v10: strona bramy Anomaly, szansa, lustro silnika',
        N47, N48, N49, N50, N51, N52, N53, N54, N55, N56]))
    for n in nazwy:
        print('  %-66s %s' % (n, 'OK' if narus[n] == 0 else 'NARUSZEN %d, np. %s' % (narus[n], przyklad[n])))

    print('\nGRUPY:')
    for klucz, rr in grupy.items():
        zd = [r for r in rr if r['decyzja'] != 'PASS']
        pas = [r for r in rr if r['decyzja'] == 'PASS']
        dni = [f(r, 'dzien') for r in rr]
        nap = [f(r, 'napiecie') for r in rr]
        pb = [f(r, 'pBrama') for r in rr if r['pBrama'] != '']
        okres = max(dni) - min(dni) if len(dni) > 1 else 0.0
        print(' %s  [profil %s]' % (klucz, sorted(set(r['profil'] for r in rr))))
        print('   decyzji %d (dni %.1f-%.1f), zdarzen %d, PASS %d %s' % (
            len(rr), min(dni), max(dni), len(zd), len(pas), dict(collections.Counter(r['powodPass'] for r in pas))))
        print('   intencje %s | napiecie sr. %.3f | kryzys w %d turach | sr. pBrama %.3f' % (
            dict(collections.Counter(r['intencja'] for r in rr)), sum(nap) / len(nap),
            sum(1 for r in rr if r.get('kryzys') == 'true'), sum(pb) / len(pb) if pb else float('nan')))
        if zd:
            print('   incydenty %s | unikalnych kompozycji %d/%d' % (
                dict(collections.Counter(r['wybor'] for r in zd).most_common()), len(set(r['klucz'] for r in zd)), len(zd)))
        if okres > 0:
            print('   tempo zdarzen %.3f/dzien' % (len(zd) / okres))
        v7 = [r for r in rr if r.get('wersjaLogu') in OD_V7]
        v8 = [r for r in rr if r.get('wersjaLogu') in OD_V8]
        v9 = [r for r in rr if r.get('wersjaLogu') in OD_V9]
        v10 = [r for r in rr if r.get('wersjaLogu') in OD_V10]
        v11 = [r for r in rr if r.get('wersjaLogu') in OD_V11]
        if v11:
            cz = sorted(f(r, 'czasMs') for r in v11 if r.get('czasMs', '') != '')
            if cz:
                print('   czas decyzji (czasMs): p50 %.3f, p95 %.3f, max %.3f ms (n=%d)' % (
                    cz[len(cz) // 2], cz[min(len(cz) - 1, int(math.ceil(0.95 * len(cz))) - 1)], cz[-1], len(cz)))
        if v10:
            # Czestosc strony Anomaly wobec sredniej szansy - kanarek losowania bramy (nie niezmiennik: to proba).
            zdlc = [r for r in v10 if r.get('anomaliaSzansa', '') != '']
            zb = [int(r['zablokowanychSilnik']) for r in v10 if (r.get('zablokowanychSilnik') or '').isdigit()]
            print('   brama Anomaly: tur z DLC %d/%d, strona Anomaly %d (sr. szansa %s) | lustro: pomiar w %d turach, '
                  'sr. zablokowanych %.2f, max %d' % (
                      len(zdlc), len(v10), sum(1 for r in v10 if r.get('anomaliaTura') == 'Anomaly'),
                      ('%.3f' % (sum(f(r, 'anomaliaSzansa') for r in zdlc) / len(zdlc))) if zdlc else '-',
                      len(zb), sum(zb) / float(len(zb)) if zb else 0.0, max(zb) if zb else 0))
        if v9:
            warstwa = [r for r in v9 if r.get('stylDni', '') != '']
            akt = [r for r in warstwa if r.get('stylAktywny') == 'true']
            zst = [r for r in zd if r.get('stylWartosc', '') != '']
            pr = [abs(f(r, 'premiaStylu')) for r in zst if r.get('premiaStylu', '') != '']
            print('   styl: warstwa w %d/%d turach, aktywny w %d | mocne %s | etykiety %s' % (
                len(warstwa), len(v9), len(akt), dict(collections.Counter(r.get('stylMocne') for r in akt).most_common()),
                dict(collections.Counter(r.get('stylEtykieta') for r in akt).most_common())))
            print('   styl u zwyciezcow: ze stylem %d, v != 0 %d, sr. |premiaStylu| %.3f, sr. kierunek %s' % (
                len(zst), sum(1 for r in zst if f(r, 'stylWartosc') != 0), sum(pr) / len(pr) if pr else 0.0,
                ('%.3f' % (sum(f(r, 'stylKierunek') for r in warstwa) / len(warstwa))) if warstwa else '-'))
        if v8:
            zd8 = [r for r in v8 if r['decyzja'] != 'PASS']
            print('   fakty: sr. obowiazujacych w decyzji %.2f | konsekwencje %s' % (
                sum(int(r.get('faktow') or 0) for r in v8) / float(len(v8)),
                dict(collections.Counter(r.get('konsekwencja') for r in zd8).most_common())))
        if v7:
            stos = [r for r in v7 if r.get('lukStosowany') == 'true']
            wyg = [r for r in zd if r.get('arcAlignment', '') not in ('', None) and f(r, 'arcAlignment') > 0]
            print('   luki: tur z otwartym lukiem %d, luk stosowany %d, zwyciezca lukowy %d/%d zdarzen' % (
                sum(1 for r in v7 if (r.get('lukiAktywne') or '0') not in ('', '0')), len(stos), len(wyg), len(zd)))

    print('\nLUKI ([PN-ARC]):')
    for tryb_ in sorted(set(a.get('tryb') for a in arcs)):
        aa = [a for a in arcs if a.get('tryb') == tryb_]
        print('  tryb=%s: %s' % (tryb_, dict(collections.Counter(a.get('zdarzenie') for a in aa))))
        print('    otwarcia: %s' % dict(collections.Counter(a.get('luk') for a in aa if a.get('zdarzenie') == 'otwarcie')))
        print('    zamkniecia: %s' % dict(collections.Counter((a.get('luk'), a.get('wynik')) for a in aa if a.get('zdarzenie') == 'zamkniecie')))
        print('    powody przejsc: %s' % dict(collections.Counter(a.get('powod', '').split('(')[0] for a in aa if a.get('zdarzenie') == 'przejscie')))
    print('\nWYKONANIA ([PN-EXEC]): %s' % dict(collections.Counter((e.get('tryb'), e.get('status')) for e in execs)))
    # Krok 8: list gracza, log odpalen i kolizje cache'u.
    listy = collections.Counter(e.get('list') for e in execs if 'list' in e)
    if listy:
        print('\nLISTY GRACZA ([PN-EXEC] list=): %s' % dict(listy))
        przyklad = next((e.get('tekstListu') for e in execs
                         if e.get('list') in ('dopisany', 'odroczony') and e.get('tekstListu')), None)
        if przyklad:
            print('  przyklad tekstu: ' + przyklad)
    if fired:
        print('\nODPALENIA ([PN-FIRED]): %d linii; narrator/pn: %s; kontekst: %s' % (
            len(fired), dict(collections.Counter((x.get('narrator'), x.get('pn')) for x in fired)),
            dict(collections.Counter(x.get('kontekst') for x in fired))))
    if cachel:
        print('\nKOLIZJE CACHE ([PN-CACHE]): %d, w tym z innym werdyktem: %d' % (
            len(cachel), sum(1 for c in cachel if c.get('rozny') == 'true')))
    # Etap L: dzienny stan kolonii, sklad, koszt czasu, gry ewaluacyjne.
    if dni_:
        print('\nDOBY ([PN-DZIEN]): %d linii; narrator: %s; ostatnia: %s' % (
            len(dni_), dict(collections.Counter(x.get('narrator') for x in dni_)),
            ' '.join('%s=%s' % (k, dni_[-1].get(k)) for k in ('dzien', 'kolonisci', 'bogactwo', 'bogactwoWiek', 'pora',
                                                                 'warunki', 'monolit', 'nastroj', 'napadow'))))
    if kol_:
        print('\nSKLAD KOLONII ([PN-KOLONISTA]): %s' % dict(collections.Counter(x.get('zdarzenie') for x in kol_)))
    if stan_:
        print('\nSTAN REGUL RN ([PN-STAN]): %d linii; %s' % (
            len(stan_), dict(collections.Counter(x.get('zdarzenie') for x in stan_))))
    if perf_:
        dec = sum(int(x.get('decyzji') or 0) for x in perf_)
        sumaMs = sum(float(x.get('decyzjiSumaMs') or 0) for x in perf_)
        tik = sum(int(x.get('tickow') or 0) for x in perf_)
        sumaUs = sum(float(x.get('tickowSumaUs') or 0) for x in perf_)
        print('\nCZAS ([PN-PERF]): %d dob; decyzji %d, sr. %.3f ms, max %.3f ms; obserwatory sr. %.2f us/tick, max %.1f us' % (
            len(perf_), dec, sumaMs / dec if dec else 0.0, max(float(x.get('decyzjiMaxMs') or 0) for x in perf_),
            sumaUs / tik if tik else 0.0, max(float(x.get('tickowMaxUs') or 0) for x in perf_)))
    if listy_:
        print('\nLISTY ([PN-LIST]): %d; z odpaleniem w ticku %d, bez %d, z akcji wymuszonej %d; typy: %s' % (
            len(listy_), sum(1 for x in listy_ if x.get('incydenty') != '-'), sum(1 for x in listy_ if x.get('incydenty') == '-'),
            sum(1 for x in listy_ if wymuszona(x)), dict(collections.Counter(x.get('typ') for x in listy_))))
    if eval_:
        print('\nGRY EWALUACYJNE ([PN-EVAL]): %s' % ', '.join(
            '%s (profil %s)' % (x.get('runId'), x.get('profil')) for x in eval_))
    wym = [e for e in execs if wymuszona(e)]
    if wym:
        print('\nAKCJE WYMUSZONE ([PN-EXEC] wymuszone=N): %s' % dict(collections.Counter(
            (e.get('incydent'), e.get('status')) for e in wym)))
    print('\nSTYL GRACZA ([PN-GRACZ]): %d linii' % len(gracze))
    for run in sorted(set(g.get('runId') for g in gracze)):
        gg = [g for g in gracze if g.get('runId') == run]
        o = gg[-1]
        print('  run=%s: %d dob (narrator %s), ostatnia: dzien %s, dni %s, aktywny %s, mocne %s, etykieta %s, '
              'epizodow %s, ofert %s, schwytanych %s' % (
                  run, len(gg), sorted(set(g.get('narrator') for g in gg)), o.get('dzien'), o.get('dni'), o.get('aktywny'),
                  o.get('mocne') or '-', o.get('etykieta') or '-', o.get('epizody'), o.get('oferty'), o.get('schwytani')))
    print('\nFAKTY ([PN-FACT]): %s' % dict(collections.Counter((x.get('tryb'), x.get('zdarzenie')) for x in facts)))
    for tryb_ in sorted(set(x.get('tryb') for x in facts)):
        print('  tryb=%s, ustawienia: %s' % (tryb_, dict(collections.Counter(
            x.get('klucz') for x in facts if x.get('tryb') == tryb_ and x.get('zdarzenie') == 'ustawienie'))))

    print('\nLINIE POZA [PN-DATA]:')
    for typ in ['[PN-LOAD]', '[PN-RESET]', '[PN-EVAL]', '[PN-EXP]', '[PN-WARN]', '[PN-ERR]']:
        wpisy = extras.get(typ, [])
        print('  %-11s %d' % (typ, len(wpisy)))
        if typ in ('[PN-WARN]', '[PN-ERR]'):
            for tekst, n in collections.Counter(
                    (w.split('tekst=', 1)[1] if 'tekst=' in w else w)[:90] for w in wpisy).most_common(6):
                print('      %3dx %s' % (n, tekst))

    sys.exit(1 if sum(narus.values()) else 0)


def styl_wiersza(r, klucz, nr, pas, los, prof, cfg, narusz):
    """Niezmienniki 32-38 i 41 dla wiersza [PN-DATA] v9."""
    dni = r.get('stylDni', '')
    aktywny = r.get('stylAktywny') == 'true'
    # 32: premia rundy zwyciezcy = szerokosc pasma * wartosc stylu (jedna runda); PASS bez wartosci i premii.
    # W KAZDEJ turze ze zdarzeniem (przeglad S8): czolo H zweryfikowane w fazie 0 zostaje dostepne we wszystkich
    # rundach, wiec best i pasmo rundy zwyciezcy == wartosci z wiersza (runda pierwsza). Wyjatek - blad okablowania
    # tury - ma wlasna linie [PN-ERR] i tez powinien tu wyjsc.
    v, pr = r.get('stylWartosc', ''), r.get('premiaStylu', '')
    if (v == '') != (pr == '') or (pas and v != ''):
        narusz('32 premiaStylu = (best-pasmo)*stylWartosc', (klucz, nr, 'puste razem', v, pr))
    elif v != '' and r['best'] != '' and r['pasmo'] != '':
        # Luk ma pierwszenstwo (decyzja autora po przegladzie S8): zwyciezca z faza luku (arcAlignment > 0) nie dostaje
        # UJEMNEJ premii stylu - premia 0, wartosc v zostaje w kolumnie.
        tlumiona = (f(r, 'arcAlignment') or 0.0) > 0.0 and f(r, 'stylWartosc') < 0.0
        oczek = 0.0 if tlumiona else (f(r, 'best') - f(r, 'pasmo')) * f(r, 'stylWartosc')
        if abs(oczek - f(r, 'premiaStylu')) > 0.002:
            narusz('32 premiaStylu = (best-pasmo)*stylWartosc', (klucz, nr, r['best'], r['pasmo'], v, pr, 'tlumiona' if tlumiona else ''))
    # 33: zakresy.
    zle = []
    for c in CECHY:
        z = f(r, 'styl' + c)
        if z is not None and not (0.0 <= z <= 1.0):
            zle.append('styl' + c)
    for k in ('stylKierunek', 'stylWartosc'):
        x = f(r, k)
        if x is not None and not (-1.0 <= x <= 1.0):
            zle.append(k)
    if r.get('stylWPasmie', '') != '' and not r['stylWPasmie'].isdigit():
        zle.append('stylWPasmie')
    if pr != '' and r['best'] != '' and r['pasmo'] != '' and abs(f(r, 'premiaStylu')) > max(0.0, f(r, 'best') - f(r, 'pasmo')) + 0.002:
        zle.append('|premiaStylu| > best-pasmo')
    # Relacje (przeglad S8): v = clamp(d*a), |a| <= 1, wiec |v| <= |d| (z tym d = 0 => v = 0); liczniki pasma
    # z tej samej puli pierwszej rundy co wSoftmaksie.
    if v != '' and r.get('stylKierunek', '') != '' and abs(f(r, 'stylWartosc')) > abs(f(r, 'stylKierunek')) + 0.001:
        zle.append('|stylWartosc| > |stylKierunek|')
    for k in ('stylWPasmie', 'lukWPasmie'):
        if (r.get(k) or '').isdigit() and (r.get('wSoftmaksie') or '').isdigit() and int(r[k]) > int(r['wSoftmaksie']):
            zle.append(k + ' > wSoftmaksie')
    if zle:
        narusz('33 zakresy i relacje kolumn stylu', (klucz, nr, zle))
    # 34: brak warstwy = wszystko puste; rozgrzewka = puste cechy, mocne, etykieta, wartosc, premia, stylWPasmie,
    #     a kierunek jest.
    if dni == '':
        pelne = [k for k in KOLUMNY_STYLU if r.get(k, '') != '']
        if pelne:
            narusz('34 wypelnienie kolumn stylu: warstwa, rozgrzewka, zdarzenie', (klucz, nr, 'warstwa nieobecna', pelne))
    elif not aktywny:
        pelne = [k for k in KOLUMNY_STYLU[2:8] + KOLUMNY_STYLU[9:] if r.get(k, '') != '']
        if pelne or r.get('stylKierunek', '') == '':
            narusz('34 wypelnienie kolumn stylu: warstwa, rozgrzewka, zdarzenie', (klucz, nr, 'rozgrzewka', pelne))
    elif not pas:
        # Aktywny styl i zdarzenie (przeglad S8): zwyciezca pochodzi z puli pasma, wiec nie jest zawetowany i ma styl;
        # przy v != 0 sam jest w pasmie z niezerowym stylem.
        braki = [k for k in ('stylWartosc', 'premiaStylu', 'stylWPasmie') if r.get(k, '') == '']
        if braki or (f(r, 'stylWartosc') not in (None, 0.0) and (r.get('stylWPasmie') or '0') == '0'):
            narusz('34 wypelnienie kolumn stylu: warstwa, rozgrzewka, zdarzenie', (klucz, nr, 'zdarzenie', braki, r.get('stylWPasmie')))
    # 35: aktywnosc z liczby dni.
    if dni != '' and cfg and (not dni.isdigit() or aktywny != (int(dni) >= cfg['warmup'])):
        narusz('35 stylAktywny <=> stylDni >= rozgrzewka', (klucz, nr, dni, r.get('stylAktywny')))
    # 36: kierunek z orientacji profilu i rytmu intencji (po regule kryzysu - kolumna intencja).
    if r.get('stylKierunek', '') != '' and cfg:
        d = max(-1.0, min(1.0, cfg['wO'] * prof.get('o', 0.0) + cfg['wR'] * RYTM.get(r['intencja'], 1.0)))
        if abs(d - f(r, 'stylKierunek')) > 0.0006:
            narusz('36 stylKierunek = clamp(wO*o + wR*rytm)', (klucz, nr, r['profil'], r['intencja'], round(d, 4), r['stylKierunek']))
    # 37, 38: mocne strony i etykieta przeliczone NIEZALEZNIE z Z (przypadki na granicy zaokraglen pomijane).
    if aktywny and cfg:
        zs = [f(r, 'styl' + c) for c in CECHY]
        mocne, niepewny = mocne_wzgledne(zs, cfg['skala'], cfg['prog'])
        if not niepewny and mocne != zbior_mocnych(r.get('stylMocne')):
            narusz('37 stylMocne = profil wzgledny z Z', (klucz, nr, r.get('stylMocne'), sorted(mocne)))
        if r.get('stylEtykieta', '') != '' and cfg.get('proto'):
            et, niepewna = najblizszy_prototyp(zs, cfg['proto'])
            if not niepewna and et != r['stylEtykieta']:
                narusz('38 stylEtykieta = najblizszy prototyp', (klucz, nr, r['stylEtykieta'], et))
    # 41: liczba dni zgodna z ostatnia linia [PN-GRACZ] / kotwica (gra) albo ze stylem ramienia (symulacja).
    oczek = r.get('_stylDniOczek')
    if oczek == '' and dni != '':
        narusz('41 stylDni = ostatni [PN-GRACZ] / styl= / ramie', (klucz, nr, 'ramie bez stylu', dni))
    elif oczek not in (None, '') and dni != '' and (not dni.isdigit() or int(dni) != oczek):
        narusz('41 stylDni = ostatni [PN-GRACZ] / styl= / ramie', (klucz, nr, oczek, dni))


def styl_gracza(g, cfg, narusz):
    """Niezmienniki 33-35 i 37-38 dla linii [PN-GRACZ]."""
    ident = ('[PN-GRACZ]', g.get('runId'), g.get('dzien'))
    aktywny = g.get('aktywny') == 'true'
    zs = [f(g, 'styl' + c) for c in CECHY]
    cs = [f(g, 'c' + c) for c in CECHY]
    if any(z is not None and not (0.0 <= z <= 1.0) for z in zs) or any(c is not None and not (-1.0 <= c <= 1.0) for c in cs):
        narusz('33 zakresy i relacje kolumn stylu', ident)
    if not aktywny and (any(z is not None for z in zs) or g.get('mocne') or g.get('etykieta')):
        narusz('34 wypelnienie kolumn stylu: warstwa, rozgrzewka, zdarzenie', ident + ('rozgrzewka',))
    if cfg and (not (g.get('dni') or '').isdigit() or aktywny != (int(g['dni']) >= cfg['warmup'])):
        narusz('35 stylAktywny <=> stylDni >= rozgrzewka', ident + (g.get('dni'), g.get('aktywny')))
    if aktywny and cfg:
        mocne, niepewny = mocne_wzgledne(zs, cfg['skala'], cfg['prog'])
        if not niepewny and mocne != zbior_mocnych(g.get('mocne')):
            narusz('37 stylMocne = profil wzgledny z Z', ident + (g.get('mocne'), sorted(mocne)))
        if g.get('etykieta') and cfg.get('proto'):
            et, niepewna = najblizszy_prototyp(zs, cfg['proto'])
            if not niepewna and et != g['etykieta']:
                narusz('38 stylEtykieta = najblizszy prototyp', ident + (g['etykieta'], et))


if __name__ == '__main__':
    main()
