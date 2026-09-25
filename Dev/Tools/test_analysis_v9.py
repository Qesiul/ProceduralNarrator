# -*- coding: utf-8 -*-
# Uzycie: python test_analysis_v9.py  (kod wyjscia 1 przy bledzie)
# Test niezmiennikow STYLU GRACZA (32-41) narzedzia analysis_v7.py na SYNTETYCZNYM pliku v9 w TRYBIE GRA,
# zbudowanym z pliku v8 (test_analysis_v8 -> do_gry): kotwica [PN-LOAD] styl= na poczatku, linia [PN-GRACZ]
# na kazdej granicy doby (PO wierszach decyzji z tego samego ticku - comp jest wolany przed GameComponentTick),
# kolumny stylu w wierszach z ostatniej zamknietej doby, otwarcie luku pod styl (Slawa twierdzy) i na koncu
# eksperyment z trzema ramionami: styl gry, styl narzucony (Wojownik), bez stylu.
# WZORCE LICZY NIEZALEZNA REPLIKA modelu (profil wzgledny, prototypy, kierunek) - nie funkcje analizatora,
# inaczej mutacja reguly w analizatorze bylaby niewidoczna (lekcja z S6 kroku 6).
# Plik poprawny -> 0 naruszen; kazde uszkodzenie -> DOKLADNIE oczekiwany zbior; do tego trzy uszkodzenia
# starych niezmiennikow (19, 25, 28) na wierszach v9 - dowod, ze bramki wersji objely v9.
# Od przegladu S8 (2026-09-24): fikstura zgodna z kodem (premia = (best - pasmo)*v w KAZDEJ rundzie, bo czolo
# zweryfikowane w fazie 0 zostaje dostepne; v = d*a, wiec |v| <= |d|), reset "PN: skasuj styl gracza" w srodku gry,
# wczytanie z porzucona galezia (linie [PN-GRACZ] z przyszlosci), luk z warunkiem !Walka, otwarcie luku na sciezce
# spoznionej (T+1000), druga sesja procesu z INNA konfiguracja na koncu pliku (konfiguracja per sesja).
import collections, math, re, sys

sys.path.insert(0, 'D:/Games/RimWorld/Mods/ProceduralNarrator/Dev/Tools')
import test_analysis_v7 as t7
import test_analysis_v8 as t8

ROZGRZEWKA, POJEMNOSC, SKALA, PROG, WO, WR = 15, 60, 0.25, 0.4, 0.3, 0.7
CECHY = ['Walka', 'Gospodarka', 'Ekspansja', 'Reaktywnosc']
SYGNALY = ['Obrona', 'Inicjatywa', 'Praca', 'Produkcja', 'Przyjecia', 'Werbunek', 'Poborowi', 'Szybkosc']
PROTOTYPY = [('Wszechstronny', (0.5, 0.5, 0.5, 0.5)), ('Wojownik', (0.8, 0.5, 0.5, 0.5)),
             ('Gospodarz', (0.5, 0.8, 0.5, 0.5)), ('Osadnik', (0.5, 0.5, 0.8, 0.5)), ('Czujny', (0.5, 0.5, 0.5, 0.8)),
             ('Kasztelan', (0.8, 0.8, 0.5, 0.5)), ('Zdobywca', (0.8, 0.5, 0.8, 0.5)), ('Dowodca', (0.8, 0.5, 0.5, 0.8)),
             ('Zalozyciel', (0.5, 0.8, 0.8, 0.5)), ('Zaradny', (0.5, 0.8, 0.5, 0.8)), ('Opiekun', (0.5, 0.5, 0.8, 0.8)),
             ('Zaangazowany', (0.75, 0.75, 0.75, 0.75)), ('Bierny', (0.25, 0.25, 0.25, 0.25))]
ORIENTACJA = {'PN_Profil_Powsciagliwy': 1.0, 'PN_Profil_Zrownowazony': 0.0, 'PN_Profil_Napastliwy': -1.0}
RYTM = {'Breathe': 1.0, 'Hold': 0.0, 'Escalate': -1.0}
PRE9 = ['stylDni', 'stylAktywny', 'stylWalka', 'stylGospodarka', 'stylEkspansja', 'stylReaktywnosc',
        'stylMocne', 'stylEtykieta', 'stylKierunek']
POST9 = ['stylWartosc', 'premiaStylu', 'stylWPasmie']
SLAWA = ('luk=PN_Luk_SlawaTwierdzy; priorytet=12; grupa=frakcja; cooldownDni=30; nastepca=-; wiazeFrakcje=nie; '
         'fazy=Zasiew:Seed:Negative,Eskalacja:Escalation:Negative,Kulminacja:Climax:Negative,Rozwiazanie:Resolution:Positive; '
         'krawedzie=Zasiew>Eskalacja,Eskalacja>Kulminacja,Eskalacja>Rozwiazanie,Kulminacja>Rozwiazanie,Kulminacja>Rozwiazanie; '
         'warunekStylu=Walka')
# Wektory cech zamknietych dob (cyklicznie po numerze dnia); None = cecha nieznana. Dobrane tak, zeby zaden
# nie lezal na granicy progu ani remisu prototypow - pilnuje tego straznik w main().
WEKTORY = [(0.800, 0.500, 0.500, 0.500),     # Wojownik, mocna Walka
           (0.500, 0.750, 0.720, 0.300),     # dwie mocne (Gospodarka, Ekspansja)
           (0.700, 0.620, 0.380, None),      # Reaktywnosc nieznana, mocna Walka
           (0.550, 0.520, None, None)]       # brak mocnych ("-")
START_DNI = 5          # kotwica: 5 dni w kolejce na poczatku pliku - rozgrzewka konczy sie po 10 dobach
WIDELEC_PO = 25        # zapis tuz po tej linii [PN-GRACZ], dwie doby gry, wczytanie zapisu (porzucona galaz)
RESET_PO = 60          # "PN: skasuj styl gracza" tuz po tej linii [PN-GRACZ] (pelna kolejka osiagnieta po 55)
ZAKAZ = ('luk=PN_Luk_TestZakazu; priorytet=1; grupa=test; cooldownDni=30; nastepca=-; wiazeFrakcje=nie; '
         'fazy=Zasiew:Seed:Negative,Eskalacja:Escalation:Negative,Kulminacja:Climax:Negative,Rozwiazanie:Resolution:Positive; '
         'krawedzie=Zasiew>Eskalacja,Eskalacja>Kulminacja,Kulminacja>Rozwiazanie; warunekStylu=!Walka')
RAMIONA = [('1-ramie', 'gry'), ('W-ramie', 'Wojownik(0.80/0.50/0.50/0.50)'), ('S-ramie', 'wylaczony')]


def replika(zs):
    """NIEZALEZNA replika PlayerStyleModel.Finish: (c, mocne, etykieta, margines_c, margines_etykiety)."""
    znane = [i for i in range(4) if zs[i] is not None]
    c, mocne, marg_c = [None] * 4, [], 9.0
    if len(znane) >= 2:
        sr = sum(zs[i] for i in znane) / len(znane)
        for i in znane:
            c[i] = max(-1.0, min(1.0, (zs[i] - sr) / SKALA))
            marg_c = min(marg_c, abs(c[i] - PROG))
            if c[i] >= PROG:
                mocne.append(CECHY[i])
    else:
        for i in znane:
            c[i] = 0.0
    odl = [(math.sqrt(sum((zs[i] - p[i]) ** 2 for i in znane)), et) for et, p in PROTOTYPY]
    best = 0
    for k in range(1, len(odl)):
        if odl[k][0] < odl[best][0]:
            best = k
    # Margines tylko wobec prototypow ROZNYCH na znanych cechach - identyczne daja remis dokladny,
    # rozstrzygany kolejnoscia (gra: scisle "<"), a nie przez zaokraglenie.
    inne = [k for k in range(len(odl)) if k != best and any(PROTOTYPY[k][1][i] != PROTOTYPY[best][1][i] for i in znane)]
    marg_e = min(abs(odl[k][0] - odl[best][0]) for k in inne) if inne else 9.0
    return c, mocne, odl[best][1] if znane else '', marg_c, marg_e


def f3(x):
    return '' if x is None else '%.3f' % x


def kierunek(profil, intencja):
    return max(-1.0, min(1.0, WO * ORIENTACJA.get(profil, 0.0) + WR * RYTM.get(intencja, 1.0)))


def linia_gracza(run, tick, dzien, dni, zs):
    aktywny = dni >= ROZGRZEWKA
    c, mocne, et, _, _ = replika(zs)
    pola = [('runId', run), ('tryb', 'gra'), ('narrator', 'PN_GenerativeNarrator'), ('tick', str(tick)),
            ('dzien', str(dzien)), ('dni', str(dni)), ('aktywny', 'true' if aktywny else 'false')]
    for i, n in enumerate(CECHY):
        pola.append(('styl' + n, f3(zs[i]) if aktywny else ''))
    for i, n in enumerate(CECHY):
        pola.append(('c' + n, f3(c[i]) if aktywny else ''))
    pola += [('mocne', ('/'.join(mocne) or '-') if aktywny else ''), ('etykieta', et if aktywny else ''),
             ('druga', ''), ('margines', ''), ('epizody', '2'), ('oferty', '1'), ('schwytani', '0')]
    for s in SYGNALY:
        pola += [('x' + s, ''), ('z' + s, ''), ('n' + s, '0'), ('d' + s, '0')]
    return '[PN-GRACZ] ' + '; '.join('%s=%s' % p for p in pola)


def kolumny_stylu(w, stan):
    """Kolumny v9 wiersza w stanie stylu 'stan' (dni, zs, rodzaj) - rodzaj 'wylaczony' = warstwa nieobecna."""
    if stan['rodzaj'] == 'wylaczony':
        return dict((k, '') for k in PRE9 + POST9)
    dni, zs = stan['dni'], stan['zs']
    aktywny = dni >= ROZGRZEWKA
    _, mocne, et, _, _ = replika(zs)
    k = {'stylDni': str(dni), 'stylAktywny': 'true' if aktywny else 'false',
         'stylMocne': ('/'.join(mocne) or '-') if aktywny else '', 'stylEtykieta': et if aktywny else '',
         'stylKierunek': f3(kierunek(w['profil'], w['intencja']))}
    for i, n in enumerate(CECHY):
        k['styl' + n] = f3(zs[i]) if aktywny else ''
    zd = w['decyzja'] != 'PASS'
    if aktywny and zd:
        # v = d*a jak w StyleFocus.ValueFor: dopasowanie a w [-1, 1] (rozne w wierszach), wiec |v| <= |d|.
        a = (int(w['decyzjaNr']) * 7 % 21 - 10) / 10.0
        # Zwyciezca z faza luku: dopasowanie dodatnie, wiec w eskalacji (d < 0) v < 0 - przypadek premii tlumionej
        # przez pierwszenstwo luku (decyzja S8) jest w pliku na pewno.
        if (w.get('arcAlignment') or '') not in ('', '0.000') and float(w['arcAlignment']) > 0:
            a = 0.5
        v = round(float(k['stylKierunek']) * a, 3)
        szer = float(w['best']) - float(w['pasmo'])
        k['stylWartosc'] = f3(v)
        # W KAZDEJ rundzie (przeglad S8): best i pasmo rundy zwyciezcy == wartosci z wiersza. Luk ma pierwszenstwo
        # (decyzja S8): przy fazie luku (arcAlignment > 0) ujemna premia stylu jest tlumiona do 0.
        tlumiona = (w.get('arcAlignment') or '') not in ('', '0.000') and float(w['arcAlignment']) > 0 and v < 0
        k['premiaStylu'] = f3(0.0 if tlumiona else szer * v)
    else:
        k['stylWartosc'] = k['premiaStylu'] = ''
    # Zwyciezca jest w pasmie; przy v != 0 sam jest kandydatem z niezerowym stylem.
    k['stylWPasmie'] = ('1' if zd else '0') if aktywny else ''
    return k


def na_v9(w_linii, stan):
    w = t7.kv(w_linii[len('[PN-DATA] '):])
    k = kolumny_stylu(w, stan)
    w2 = collections.OrderedDict()
    for a, b in w.items():
        w2[a] = '9' if a == 'wersjaLogu' else b
        if a == 'faktow':
            for x in PRE9:
                w2[x] = k[x]
    for x in POST9:
        w2[x] = k[x]
    return '[PN-DATA] ' + '; '.join('%s=%s' % p for p in w2.items())


def linie_luku(run, t, mapa, nr, luk, inst):
    pre = 'runId=%s; tryb=gra; eksperyment=; tick=%d; dzien=%.3f; mapa=%s; decyzjaNr=%s' % (run, t, t / 60000.0, mapa, nr)
    return ['[PN-ARC] %s; luk=%s; instancja=%d; zdarzenie=otwarcie; z=Zasiew; do=Eskalacja; powod=wykonanie; wynik=-; '
            'frakcja=-; komunikat=tak' % (pre, luk, inst),
            '[PN-ARC] %s; luk=%s; instancja=%d; zdarzenie=zamkniecie; z=Eskalacja; do=-; powod=straznik:test; '
            'wynik=przerwany; frakcja=-; komunikat=nie' % (pre, luk, inst)]


def tick_linii(l):
    return int(t8.pole(l, 'tick')) if t8.jest(l, '[PN-DATA] ', '[PN-EXEC] ', '[PN-FACT] ', '[PN-ARC] ', '[PN-GRACZ] ') else None


def zbuduj(slawa_bez_walki=False, zakaz_przy_walce=False, pozna_bez_walki=False):
    """Zwraca (linie, straznicy)."""
    baza = t8.do_gry(t8.zbuduj())
    run = t8.pole(next(l for l in baza if l.startswith('[PN-DATA] ')), 'runId')
    ticki = [int(t8.pole(l, 'tick')) for l in baza if t8.jest(l, '[PN-DATA] ', '[PN-EXEC] ', '[PN-FACT] ', '[PN-ARC] ')]
    t_pierwszy = min(ticki)
    dzien0 = t_pierwszy // 60000
    s = collections.Counter()

    wynik, cialo = [], []
    for l in baza:
        if l.startswith('[PN-DATA-COLS]'):
            cols = l.split('kolumny=')[1].split(',')
            i = cols.index('faktow') + 1
            wynik.append('[PN-DATA-COLS] wersja=9; kolumny=' + ','.join(cols[:i] + PRE9 + cols[i:] + POST9))
        elif l.startswith('[PN-CONFIG] profil='):
            pid = l.split('profil=')[1].split(';')[0]
            wynik.append(l + ' | orientacjaStylu=%.1f' % ORIENTACJA.get(pid, 0.0))
        elif l.startswith('[PN-CONFIG] luk='):
            wynik.append(l + '; warunekStylu=-')
        elif l.startswith('[PN-CONFIG]') or l.startswith('[PN-DATA-COLS]') or not t8.jest(l, '[PN-DATA] ', '[PN-EXEC] ', '[PN-FACT] ', '[PN-ARC] '):
            wynik.append(l)
        else:
            cialo.append(l)
    wynik.append('[PN-CONFIG] ' + SLAWA)
    wynik.append('[PN-CONFIG] ' + ZAKAZ)
    wynik.append('[PN-CONFIG] styl=wlaczony; rozgrzewkaDni=%d; pojemnoscDni=%d; skalaWzgledna=%s; progMocnej=%s; '
                 'wagaOrientacji=%s; wagaRytmu=%s; prototypy=%s'
                 % (ROZGRZEWKA, POJEMNOSC, SKALA, PROG, WO, WR,
                    ','.join('%s:%s' % (et, '/'.join('%g' % v for v in p)) for et, p in PROTOTYPY)))
    wynik.append('[PN-LOAD] runId=%s; zrodlo=zapis; profil=PN_Profil_Powsciagliwy; map=0; wpisow=0; decyzji=0; odrzuconych=0; '
                 'odrzuconychHistorii=0; odrzuconychLukow=0; odrzuconychFaktow=0; odrzuconychStylu=0; wersjaPamieci=4; '
                 'profilZapisany=PN_Profil_Powsciagliwy; narrator=PN_GenerativeNarrator; tick=%d; dzien=%.3f; mapy=; luki=; '
                 'fakty=; styl=dni:%d,dzien:%d,aktywny:0,mocne:,etykieta:'
                 % (run, t_pierwszy - 1, (t_pierwszy - 1) / 60000.0, START_DNI, dzien0))

    stan = dict(dni=START_DNI, zs=WEKTORY[(dzien0 - 1) % 4], rodzaj='gry')
    granica = (dzien0 + 1) * 60000
    # Otwarcia na sciezce normalnej: luk -> (warunek na mocnych stronach wiersza decyzji, numer instancji).
    otwarcia = [('PN_Luk_SlawaTwierdzy', (lambda m: 'Walka' not in m) if slawa_bez_walki else (lambda m: 'Walka' in m), 9),
                ('PN_Luk_TestZakazu', (lambda m: 'Walka' in m) if zakaz_przy_walce else (lambda m: 'Walka' not in m), 11)]
    wybrane = {}          # tick decyzji -> (luk, instancja)
    n_gracz = 0
    for l in cialo:
        t = int(t8.pole(l, 'tick'))
        while granica < t:                       # scisle: wiersze z ticku granicy ida PRZED linia [PN-GRACZ]
            dz = granica // 60000 - 1
            stan['dni'] = min(stan['dni'] + 1, POJEMNOSC)
            stan['zs'] = WEKTORY[dz % 4]
            wynik.append(linia_gracza(run, granica, dz, stan['dni'], stan['zs']))
            n_gracz += 1
            s['linii [PN-GRACZ]'] += 1
            s['dob z pelna kolejka'] += stan['dni'] == POJEMNOSC
            if n_gracz == RESET_PO:
                # "PN: skasuj styl gracza": linia niesie stan SPRZED skasowania; kolejka pusta, obserwator inicjuje
                # ksiege w nastepnym ticku, a pierwsza zamknieta doba (dzien w toku resetu) ma dni = 1.
                _, m0, e0, _, _ = replika(stan['zs'])
                akt = stan['dni'] >= ROZGRZEWKA
                wynik.append('[PN-RESET] runId=%s; tick=%d; dzien=%.3f; akcja=skasujStyl; styl=dni:%d,dzien:%d,aktywny:%d,'
                             'mocne:%s,etykieta:%s' % (run, granica + 100, (granica + 100) / 60000.0, stan['dni'],
                                                        granica // 60000, 1 if akt else 0,
                                                        ('/'.join(m0) or '-') if akt else '', e0 if akt else ''))
                stan['dni'] = 0
                s['resetow stylu (skasujStyl)'] += 1
            granica += 60000
        if l.startswith('[PN-DATA] '):
            nowa = na_v9(l, stan)
            wynik.append(nowa)
            aktywny = stan['dni'] >= ROZGRZEWKA
            s['wierszy w rozgrzewce' if not aktywny else 'wierszy z aktywnym stylem'] += 1
            _, mocne, et, _, _ = replika(stan['zs'])
            if aktywny:
                s['etykiet: ' + et] += 1
                s['wierszy bez mocnych (-)'] += not mocne
                s['aktywnych wierszy PASS'] += t8.pole(l, 'decyzja') == 'PASS'
                s['wierszy z tlumiona premia stylu (luk)'] += t8.pole(nowa, 'stylWartosc').startswith('-') \
                    and t8.pole(nowa, 'premiaStylu') == '0.000' and t8.pole(nowa, 'stylWartosc') != '-0.000'
            if aktywny and t8.pole(l, 'decyzja') != 'PASS' and t not in wybrane:
                for luk, war, inst in otwarcia:
                    if luk not in [x[0] for x in wybrane.values()] and war(mocne):
                        wybrane[t] = (luk, inst)
                        break
        else:
            wynik.append(l)
        if l.startswith('[PN-EXEC] ') and t in wybrane and wybrane[t][1] > 0:
            luk, inst = wybrane[t]
            wynik.extend(linie_luku(run, t, t8.pole(l, 'mapa'), t8.pole(l, 'decyzjaNr'), luk, inst))
            s['otwarc luku pod styl' if luk == 'PN_Luk_SlawaTwierdzy' else 'otwarc z warunkiem zakazu (!Walka)'] += 1
            wybrane[t] = (luk, -1)

    # SCIEZKA SPOZNIONA: [PN-EXEC] decyzji T wedruje do T+1000 (status pozno-wykonane, tickDecyzji=T), a otwarcie
    # Slawy pada w ArcTick T+1000 - PRZED stosowaniem faktow tego zdarzenia i przed ewentualna nowa decyzja.
    # Warunek stylu czyta mocne strony Z DECYZJI (linia P), wiec wlasciwym wierszem jest T.
    ticki_wierszy = set(int(t8.pole(l, 'tick')) for l in wynik if l.startswith('[PN-DATA] '))
    kandydaci = []
    for i, l in enumerate(wynik):
        if not wiersz_gry(l) or t8.pole(l, 'stylAktywny') != 'true' or t8.pole(l, 'decyzja') == 'PASS':
            continue
        t = int(t8.pole(l, 'tick'))
        walka = 'Walka' in (t8.pole(l, 'stylMocne') or '')
        if t in wybrane or (t + 1000) in ticki_wierszy or (t + 1000) % 60000 == 0 or walka == pozna_bez_walki:
            continue
        if any(x.startswith('[PN-ARC] ') and int(t8.pole(x, 'tick')) == t for x in wynik):
            continue
        kandydaci.append((i, t))
    i, t = kandydaci[-1]                         # koniec gry - daleko od wczytania z porzucona galezia
    e = next(k for k in range(i + 1, len(wynik)) if wynik[k].startswith('[PN-EXEC] ') and int(t8.pole(wynik[k], 'tick')) == t)
    ex = wynik.pop(e)
    ex = t8.ustaw(t8.ustaw(t8.ustaw(ex, 'tick', str(t + 1000)), 'status', 'pozno-wykonane'), 'tickDecyzji', str(t))
    j = next((k for k in range(e, len(wynik)) if tick_linii(wynik[k]) is not None and tick_linii(wynik[k]) >= t + 1000),
             len(wynik))
    wynik[j:j] = [ex] + linie_luku(run, t + 1000, t8.pole(ex, 'mapa'), t8.pole(ex, 'decyzjaNr'), 'PN_Luk_SlawaTwierdzy', 13)
    s['otwarc na sciezce spoznionej'] += 1

    # WCZYTANIE Z PORZUCONA GALEZIA: zapis tuz po linii [PN-GRACZ] nr WIDELEC_PO, dwie doby gry, wczytanie, powtorka.
    # Linie [PN-GRACZ] z porzuconej galezi (tick > tick wczytania) analiza ma PRZYCIAC - pilnuje tego liczba linii
    # w raporcie (niezmienniki ich nie widza: kotwica wczytania i tak zaczyna ciaglosc od nowa).
    gr = [k for k, l in enumerate(wynik) if l.startswith('[PN-GRACZ] ')]
    # Porzucona galaz: co najmniej dwie doby i dwie decyzje (decyzje padaja srednio co 2,5 dnia).
    p = gr[WIDELEC_PO - 1]
    e2 = next(gr[k] for k in range(WIDELEC_PO + 1, len(gr))
              if sum(1 for l in wynik[p + 1:gr[k] + 1] if l.startswith('[PN-DATA] ')) >= 2)
    odcinek = wynik[p + 1:e2 + 1]
    ts = int(t8.pole(wynik[p], 'tick')) + 500
    fk, lk = t8.stan_do(wynik, p + 1)
    nr = int(t8.pole(next(l for l in odcinek if l.startswith('[PN-DATA] ')), 'decyzjaNr'))
    load = t8.linia_load(run, ts, {'0': nr}, fk, lk)
    load = t8.ustaw(t8.ustaw(t8.ustaw(load, 'wersjaPamieci', '4'), 'odrzuconychStylu', '0'), 'styl',
                    'dni:%s,dzien:%d,aktywny:1,mocne:,etykieta:' % (t8.pole(wynik[p], 'dni'), int(t8.pole(wynik[p], 'tick')) // 60000))
    wynik = wynik[:p + 1] + odcinek + [load] + odcinek + wynik[e2 + 1:]
    s['_porzuconych_gracz'] = sum(1 for l in odcinek if l.startswith('[PN-GRACZ] '))
    s['wczytan z porzucona galezia [PN-GRACZ]'] = 1 if s['_porzuconych_gracz'] else 0

    # EKSPERYMENT na koncu: okno pierwszych decyzji (bez lukow), przesuniete za ostatni tick gry.
    ostatni = max(int(t8.pole(l, 'tick')) for l in wynik if t8.jest(l, '[PN-DATA] ', '[PN-EXEC] ', '[PN-FACT] ', '[PN-ARC] ', '[PN-GRACZ] '))
    okno = []
    for l in t8.do_gry(t8.zbuduj()):
        if l.startswith('[PN-DATA] ') and int(t8.pole(l, 'decyzjaNr')) >= 4:
            break
        if t8.jest(l, '[PN-DATA] ', '[PN-EXEC] '):
            okno.append(l)
    przes = ostatni + 1000 - min(int(t8.pole(l, 'tick')) for l in okno)
    wynik.append('[PN-EXP] start; eksperyment=expST; runId=%s' % run)
    for ramie, rodzaj in RAMIONA:
        wynik.append('[PN-EXP] ramie; eksperyment=expST; ramie=%s; profil=PN_Profil_Powsciagliwy; styl=%s' % (ramie, rodzaj))
        if rodzaj == 'gry':
            st = dict(stan)
        elif rodzaj == 'wylaczony':
            st = dict(dni=0, zs=(None,) * 4, rodzaj='wylaczony')
        else:
            st = dict(dni=POJEMNOSC, zs=(0.8, 0.5, 0.5, 0.5), rodzaj='narzucony')
        for l in okno:
            l = t8.ustaw(t8.ustaw(l, 'tryb', 'symulacja'), 'eksperyment', 'expST/' + ramie)
            nt = int(t8.pole(l, 'tick')) + przes
            l = t8.ustaw(l, 'tick', str(nt))
            if l.startswith('[PN-DATA] '):
                l = na_v9(t8.ustaw(l, 'dzien', '%.3f' % (nt / 60000.0)), st)
                s['wierszy ramienia ' + st['rodzaj']] += 1
            else:
                l = t8.ustaw(l, 'status', 'symulacja')
            wynik.append(l)
    wynik.append('[PN-EXP] end; eksperyment=expST; status=kompletny; kontrola=brak; stylGryAktywny=true; stylS=nd')

    # DRUGA SESJA PROCESU (plik jest dopisywany): INNA konfiguracja - rozgrzewka, skala, orientacja profili,
    # nasycenie gestosci. Wiersze sesji pierwszej maja byc liczone z SWOJA konfiguracja.
    druga = ['[PN-SESSION] start; wersjaLogu=9; wersjaGry=1.5.4063']
    for l in wynik:
        if l.startswith('[PN-DATA-COLS]'):
            druga.append(l)
        elif l.startswith('[PN-CONFIG] storyteller='):
            druga.append(re.sub(r'densitySaturation=[-\d.]+', 'densitySaturation=9.5', l))
        elif l.startswith('[PN-CONFIG] profil='):
            druga.append(re.sub(r'orientacjaStylu=[-\d.]+', 'orientacjaStylu=0.0', l))
        elif l.startswith('[PN-CONFIG] styl='):
            druga.append(l.replace('rozgrzewkaDni=%d' % ROZGRZEWKA, 'rozgrzewkaDni=30').replace('skalaWzgledna=%s' % SKALA, 'skalaWzgledna=0.2'))
    s['sesji z inna konfiguracja'] = 1 if len(druga) >= 5 else 0
    wynik += druga
    return t8.przelicz_faktow(wynik), s


def idx(L, warunek, od_konca=False):
    zakres = range(len(L) - 1, -1, -1) if od_konca else range(len(L))
    return next(i for i in zakres if warunek(L[i]))


def wiersz_gry(l):
    return l.startswith('[PN-DATA] ') and t8.pole(l, 'tryb') == 'gra'


def main():
    ok = True
    # STRAZNIK FIKSTURY: wektory nie leza na granicy progu ani remisu (inaczej analizator je pominie).
    for zs in WEKTORY + [(0.8, 0.5, 0.5, 0.5)]:
        _, mocne, et, mc, me = replika(zs)
        dobry = mc > 0.02 and me > 0.01
        print('STRAZNIK wektora %-30s mocne %-22s etykieta %-12s margines c %.3f, odl %.3f %s'
              % (zs, mocne, et, mc, me, 'OK' if dobry else '*** NA GRANICY ***'))
        ok = ok and dobry

    wzorzec, s = zbuduj()
    for nazwa in ['linii [PN-GRACZ]', 'dob z pelna kolejka', 'wierszy w rozgrzewce', 'wierszy z aktywnym stylem',
                  'wierszy bez mocnych (-)', 'aktywnych wierszy PASS', 'otwarc luku pod styl',
                  'otwarc z warunkiem zakazu (!Walka)', 'otwarc na sciezce spoznionej', 'resetow stylu (skasujStyl)',
                  'wierszy z tlumiona premia stylu (luk)',
                  'wczytan z porzucona galezia [PN-GRACZ]', 'sesji z inna konfiguracja', 'wierszy ramienia gry',
                  'wierszy ramienia narzucony', 'wierszy ramienia wylaczony']:
        print('STRAZNIK: %-40s %d' % (nazwa, s[nazwa]))
        ok = ok and s[nazwa] > 0
    etykiet = [k for k in s if k.startswith('etykiet: ')]
    print('STRAZNIK: rozne etykiety w wierszach: %s' % sorted(etykiet))
    ok = ok and len(etykiet) >= 3

    kod, nar, out = t7.uruchom('v9_poprawny', wzorzec)
    print('POPRAWNY v9: kod %d, naruszone %s' % (kod, nar))
    if kod != 0:
        print(out[:4000])
    ok = ok and kod == 0 and not nar
    # Linie [PN-GRACZ] porzuconej galezi przyciete: raport analizy == linie w pliku - porzucone.
    raport = [l for l in out.splitlines() if l.startswith('STYL GRACZA ([PN-GRACZ]):')]
    policzone = int(raport[0].split(':')[1].split()[0]) if raport else -1
    oczek = sum(1 for l in wzorzec if l.startswith('[PN-GRACZ] ')) - s['_porzuconych_gracz']
    print('LINIE [PN-GRACZ] PO PRZYCIECIU: raport %d, oczekiwane %d' % (policzone, oczek))
    ok = ok and policzone == oczek

    def w(L, i, k, v):
        L[i] = t8.ustaw(L[i], k, v)
        return L

    aktywne = lambda l: wiersz_gry(l) and t8.pole(l, 'stylAktywny') == 'true'
    rozgrzewka = lambda l: wiersz_gry(l) and t8.pole(l, 'stylAktywny') == 'false'
    zdarzenie = lambda l: aktywne(l) and t8.pole(l, 'decyzja') != 'PASS'
    gracz_akt = lambda l: l.startswith('[PN-GRACZ] ') and t8.pole(l, 'aktywny') == 'true'

    def psuj_premie(L):
        i = idx(L, lambda l: aktywne(l) and t8.pole(l, 'losowan') == '3' and t8.pole(l, 'stylWartosc') not in ('', '0.000')
                and abs(float(t8.pole(l, 'stylWartosc'))) <= 0.5)
        szer = float(t8.pole(L[i], 'best')) - float(t8.pole(L[i], 'pasmo'))
        v = float(t8.pole(L[i], 'stylWartosc'))
        return w(L, i, 'premiaStylu', '%.3f' % (szer * (v + (0.3 if v >= 0 else -0.3))))

    def psuj_kierunek(L):
        i = idx(L, lambda l: wiersz_gry(l) and t8.pole(l, 'stylKierunek') and abs(float(t8.pole(l, 'stylKierunek'))) <= 0.8)
        return w(L, i, 'stylKierunek', '%.3f' % (float(t8.pole(L[i], 'stylKierunek')) + 0.1))

    def psuj_mocne(L):
        # Walka zostaje, wiec otwarcie luku pod styl (39) nie zmienia werdyktu, nawet gdyby to byl ten wiersz.
        i = idx(L, lambda l: aktywne(l) and t8.pole(l, 'stylMocne') == 'Walka')
        return w(L, i, 'stylMocne', 'Walka/Ekspansja')

    def ramie_s_z_kolumnami(L):
        # Wiersz ramienia BEZ STYLU dostaje komplet spojnych kolumn z ramienia narzuconego (ta sama decyzja) -
        # jedyne, co go zdradza, to ze ramie S nie ma prawa miec warstwy stylu (41).
        s_i = idx(L, lambda l: l.startswith('[PN-DATA] ') and t8.pole(l, 'eksperyment') == 'expST/S-ramie')
        w_i = idx(L, lambda l: l.startswith('[PN-DATA] ') and t8.pole(l, 'eksperyment') == 'expST/W-ramie'
                  and t8.pole(l, 'decyzjaNr') == t8.pole(L[s_i], 'decyzjaNr'))
        for k in PRE9 + POST9:
            L = w(L, s_i, k, t8.pole(L[w_i], k))
        return L

    def psuj_etykiete(L):
        i = idx(L, lambda l: aktywne(l) and t8.pole(l, 'stylEtykieta') == 'Wojownik')
        return w(L, i, 'stylEtykieta', 'Gospodarz')

    def psuj_ciaglosc(L):
        # Para kolejnych dob BEZ kotwicy pomiedzy (wczytanie i reset zaczynaja ciaglosc od nowa).
        gr = [i for i, l in enumerate(L) if l.startswith('[PN-GRACZ] ')]
        k = next(k for k in range(len(gr) // 2, len(gr))
                 if not any(L[x].startswith(('[PN-LOAD]', '[PN-RESET]')) for x in range(gr[k - 1], gr[k])))
        return w(L, gr[k], 'dzien', t8.pole(L[gr[k - 1]], 'dzien'))

    def psuj_v_ponad_d(L):
        i = idx(L, lambda l: zdarzenie(l) and t8.pole(l, 'stylKierunek') == '-0.400')
        szer = float(t8.pole(L[i], 'best')) - float(t8.pole(L[i], 'pasmo'))
        return w(w(L, i, 'stylWartosc', '-0.900'), i, 'premiaStylu', '%.3f' % (szer * -0.9))

    def psuj_tlumiona(L):
        i = idx(L, lambda l: zdarzenie(l) and t8.pole(l, 'premiaStylu') == '0.000' and t8.pole(l, 'stylWartosc').startswith('-')
                and t8.pole(l, 'stylWartosc') != '-0.000', True)
        szer = float(t8.pole(L[i], 'best')) - float(t8.pole(L[i], 'pasmo'))
        return w(L, i, 'premiaStylu', '%.3f' % (szer * float(t8.pole(L[i], 'stylWartosc'))))

    def ramie_s_kierunek(L):
        i = idx(L, lambda l: l.startswith('[PN-DATA] ') and t8.pole(l, 'eksperyment') == 'expST/S-ramie')
        return w(L, i, 'stylKierunek', f3(kierunek(t8.pole(L[i], 'profil'), t8.pole(L[i], 'intencja'))))

    def psuj_dni(L):
        i = idx(L, lambda l: aktywne(l) and ROZGRZEWKA < int(t8.pole(l, 'stylDni')) < POJEMNOSC)
        return w(L, i, 'stylDni', str(int(t8.pole(L[i], 'stylDni')) + 1))

    przypadki = [
        ({'32'}, 'premia stylu inna niz pasmo*v', psuj_premie),
        # Tura WIELORUNDOWA (losowan > 3): wzor obowiazuje i tu (przeglad S8 - czolo zweryfikowane w fazie 0 zostaje
        # dostepne, wiec best i pasmo rundy zwyciezcy == wartosci z wiersza).
        ({'32'}, 'premia stylu zla w turze wielorundowej', lambda L: w(
            L, idx(L, lambda l: zdarzenie(l) and t8.pole(l, 'losowan') != '3' and t8.pole(l, 'stylWartosc') not in ('0.000', '-0.000')
                   and abs(float(t8.pole(l, 'stylWartosc'))) <= 0.5),
            'premiaStylu', '%.3f' % (0.0 - float(t8.pole(L[idx(L, lambda l: zdarzenie(l) and t8.pole(l, 'losowan') != '3'
                                                              and t8.pole(l, 'stylWartosc') not in ('0.000', '-0.000')
                                                              and abs(float(t8.pole(l, 'stylWartosc'))) <= 0.5)], 'premiaStylu'))))),
        # Luk ma pierwszenstwo (decyzja S8): tlumiona premia (0) podmieniona na (best - pasmo)*v. OSTATNIE wystapienie -
        # pierwsze moze lezec w porzuconej galezi (przycinanej przy wczytaniu).
        ({'32'}, 'premia stylu nietlumiona mimo fazy luku', psuj_tlumiona),
        ({'32'}, 'PASS z wartoscia i premia stylu', lambda L: w(w(
            L, idx(L, lambda l: aktywne(l) and t8.pole(l, 'decyzja') == 'PASS', True), 'stylWartosc', '0.000'),
            idx(L, lambda l: aktywne(l) and t8.pole(l, 'decyzja') == 'PASS', True), 'premiaStylu', '0.000')),
        ({'33'}, 'stylWPasmie ujemne', lambda L: w(L, idx(L, aktywne), 'stylWPasmie', '-1')),
        ({'33'}, 'cecha poza [0, 1]', lambda L: w(
            L, idx(L, lambda l: aktywne(l) and t8.pole(l, 'stylWalka') == '0.800' and t8.pole(l, 'stylEtykieta') == 'Wojownik'
                   and t8.pole(l, 'stylReaktywnosc') == '0.500'), 'stylWalka', '1.200')),
        ({'33'}, '|stylWartosc| > |stylKierunek|', psuj_v_ponad_d),
        ({'33'}, 'stylWPasmie > wSoftmaksie', lambda L: w(
            L, idx(L, zdarzenie), 'stylWPasmie', str(int(t8.pole(L[idx(L, zdarzenie)], 'wSoftmaksie')) + 1))),
        ({'34'}, 'ramie bez stylu z kierunkiem', ramie_s_kierunek),
        ({'34'}, 'zdarzenie przy aktywnym stylu bez stylWPasmie', lambda L: w(L, idx(L, zdarzenie), 'stylWPasmie', '')),
        ({'34'}, 'zdarzenie z v != 0 poza pasmem stylu', lambda L: w(
            L, idx(L, lambda l: zdarzenie(l) and t8.pole(l, 'stylWartosc') not in ('0.000', '-0.000')), 'stylWPasmie', '0')),
        ({'35'}, 'linia [PN-GRACZ] aktywna w rozgrzewce', lambda L: w(
            L, idx(L, lambda l: l.startswith('[PN-GRACZ] ') and t8.pole(l, 'aktywny') == 'false'), 'aktywny', 'true')),
        ({'37'}, 'linia [PN-GRACZ] z mocna strona dopisana', lambda L: w(
            L, idx(L, lambda l: gracz_akt(l) and t8.pole(l, 'mocne') == 'Walka'), 'mocne', 'Walka/Gospodarka')),
        ({'38'}, 'linia [PN-GRACZ] z etykieta nie najblizsza', lambda L: w(
            L, idx(L, lambda l: gracz_akt(l) and t8.pole(l, 'etykieta') == 'Wojownik'), 'etykieta', 'Czujny')),
        ({'39'}, 'luk z warunkiem !Walka przy mocnej Walce', lambda L: zbuduj(zakaz_przy_walce=True)[0]),
        ({'39'}, 'luk pod styl na sciezce spoznionej bez Walki', lambda L: zbuduj(pozna_bez_walki=True)[0]),
        ({'41'}, 'ramie gry z innym stylDni', lambda L: w(
            L, idx(L, lambda l: l.startswith('[PN-DATA] ') and t8.pole(l, 'eksperyment') == 'expST/1-ramie'),
            'stylDni', str(int(t8.pole(L[idx(L, lambda l: l.startswith('[PN-DATA] ') and t8.pole(l, 'eksperyment') == 'expST/1-ramie')],
                                      'stylDni')) + 1))),
        ({'34'}, 'mocne strony w rozgrzewce', lambda L: w(L, idx(L, rozgrzewka), 'stylMocne', '-')),
        ({'34'}, 'rozgrzewka bez kierunku', lambda L: w(L, idx(L, rozgrzewka), 'stylKierunek', '')),
        # Wiersz rozgrzewki to zdarzenie bez wartosci stylu: po przelaczeniu na aktywny lamie takze 34 (przeglad S8:
        # aktywny styl i zdarzenie => wartosc, premia i stylWPasmie wypelnione).
        ({'34', '35'}, 'aktywny przed koncem rozgrzewki', lambda L: w(L, idx(L, rozgrzewka), 'stylAktywny', 'true')),
        # Kotwica [PN-LOAD] styl= mowi 4 dni, a wiersze sprzed pierwszej doby i pierwsza linia [PN-GRACZ] - 5 i 6:
        # ciaglosc liczona OD KOTWICY (40) i wiersze przed pierwsza doba (41).
        ({'40', '41'}, 'kotwica wczytania rozjechana', lambda L: w(
            L, idx(L, lambda l: l.startswith('[PN-LOAD]')), 'styl', 'dni:%d,dzien:%s,aktywny:0,mocne:,etykieta:'
            % (START_DNI - 1, t8.pole(L[idx(L, lambda l: l.startswith('[PN-LOAD]'))], 'styl').split('dzien:')[1].split(',')[0]))),
        ({'36'}, 'kierunek rozjechany z profilem', psuj_kierunek),
        ({'37'}, 'mocna strona dopisana', psuj_mocne),
        ({'38'}, 'etykieta nie najblizsza', psuj_etykiete),
        # Remis DOKLADNY przy nieznanej Reaktywnosci (Wojownik = Dowodca na W/G/E): wygrywa wczesniejszy w XML.
        # Pierwsza wersja analizatora uznawala taki remis za niepewny i nie sprawdzala etykiety wcale.
        ({'38'}, 'remis dokladny rozstrzygniety wbrew XML', lambda L: w(
            L, idx(L, lambda l: aktywne(l) and t8.pole(l, 'stylReaktywnosc') == '' and t8.pole(l, 'stylEtykieta') == 'Wojownik'),
            'stylEtykieta', 'Dowodca')),
        ({'39'}, 'luk pod styl bez mocnej Walki', lambda L: zbuduj(slawa_bez_walki=True)[0]),
        ({'40'}, 'doba [PN-GRACZ] powtorzona', psuj_ciaglosc),
        ({'41'}, 'stylDni inne niz ostatni [PN-GRACZ]', psuj_dni),
        ({'41'}, 'ramie narzucone z innym stylDni',
         lambda L: w(L, idx(L, lambda l: l.startswith('[PN-DATA] ') and t8.pole(l, 'eksperyment') == 'expST/W-ramie'),
                     'stylDni', str(POJEMNOSC - 1))),
        ({'41'}, 'ramie bez stylu z kolumnami stylu', ramie_s_z_kolumnami),
        # STARE niezmienniki na wierszach v9 - bramki wersji ('7','8') -> ('7','8','9').
        ({'19'}, 'v9: premia luku zla', lambda L: t7.zamien_pierwsza(
            L, lambda l: l.startswith('[PN-DATA]') and ('; arcAlignment=0' in l or '; arcAlignment=1' in l) and 'losowan=3' in l,
            lambda l: l.replace('premiaLuku=', 'premiaLuku=9'))),
        ({'19'}, 'v9: premia luku zla w turze wielorundowej', lambda L: t7.zamien_pierwsza(
            L, lambda l: l.startswith('[PN-DATA]') and ('; arcAlignment=0' in l or '; arcAlignment=1' in l) and 'losowan=3;' not in l,
            lambda l: l.replace('premiaLuku=', 'premiaLuku=9'))),
        ({'25'}, 'v9: brak [PN-EXEC]', lambda L: L[:idx(L, lambda l: l.startswith('[PN-EXEC]'))]
         + L[idx(L, lambda l: l.startswith('[PN-EXEC]')) + 1:]),
        ({'28'}, 'v9: faktow rozjechane ze stanem', lambda L: t7.zamien_pierwsza(
            L, lambda l: l.startswith('[PN-DATA]') and '; faktow=1;' in l, lambda l: l.replace('; faktow=1;', '; faktow=5;'))),
    ]
    for oczek, opis, psuj in przypadki:
        zepsuty = psuj(list(wzorzec))
        if zepsuty == wzorzec:
            print('%-40s *** USZKODZENIE NIE ZMIENILO PLIKU ***' % opis)
            ok = False
            continue
        kod, nar, out = t7.uruchom('v9_zly_' + '_'.join(sorted(oczek)), zepsuty)
        trafiony = kod == 1 and set(nar) == oczek
        ok = ok and trafiony
        print('%-40s oczekiwane %s -> kod %d, naruszone %s %s' % (opis, sorted(oczek), kod, nar, 'OK' if trafiony else '*** NIE ***'))
    print('WYNIK:', 'OK' if ok else 'BLAD')
    sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main()
