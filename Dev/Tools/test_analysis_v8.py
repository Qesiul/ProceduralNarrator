# -*- coding: utf-8 -*-
# Uzycie: python test_analysis_v8.py  (kod wyjscia 1 przy bledzie)
# Test niezmiennikow FAKTOW (28-31) narzedzia analysis_v7.py na SYNTETYCZNYM pliku v8, zbudowanym
# z syntetycznego pliku v7 (test_analysis_v7.zbuduj): kazdy napad zostawia konsekwencje
# PN_Kons_SladWalki z faktem walka.byla, zastosowanym w NASTEPNYM wywolaniu compa (tick + 1000)
# z dniem DECYZJI. Plik poprawny -> 0 naruszen (jedno i dwa ramiona); kazde z 4 uszkodzen ->
# DOKLADNIE oczekiwany zbior niezmiennikow. Od S6 (2026-09-23) takze scenariusz TRYBU GRA - patrz
# scenariusz_gry().
import collections, sys

sys.path.insert(0, 'D:/Games/RimWorld/Mods/ProceduralNarrator/Dev/Tools')
import test_analysis_v7 as t7

KONS = 'PN_Kons_SladWalki'
KLUCZ = 'walka.byla'
ZYCIE = 20.0


def na_v8(linie):
    wynik = []
    fakty = collections.defaultdict(dict)   # eksperyment -> {klucz: (wartosc, dzien, zycie)}
    czekajace = {}                           # (eksperyment, tick) -> (dzien, decyzjaNr, mapa, runId)
    for l in linie:
        if l.startswith('[PN-DATA-COLS]'):
            cols = l.split('kolumny=')[1].split(',')
            i = cols.index('lukDopasowanych') + 1
            cols = cols[:i] + ['faktow'] + cols[i:] + ['konsekwencja']
            wynik.append('[PN-DATA-COLS] wersja=8; kolumny=' + ','.join(cols))
        elif l.startswith('[PN-DATA] '):
            w = t7.kv(l[len('[PN-DATA] '):])
            eksp = w.get('eksperyment', '')
            dz = float(w['dzien'])
            obow = sum(1 for (v, d, z) in fakty[eksp].values() if z <= 0 or dz - d < z)
            zd = w['decyzja'] != 'PASS'
            kons = (KONS if w['wybor'] == 'RaidEnemy' else '-') if zd else ''
            w2 = collections.OrderedDict()
            for k, v in w.items():
                if k == 'wersjaLogu':
                    v = '8'
                if k == 'klucz' and zd:
                    v = v + '|' + kons
                w2[k] = v
                if k == 'lukDopasowanych':
                    w2['faktow'] = str(obow)
            w2['konsekwencja'] = kons
            wynik.append('[PN-DATA] ' + '; '.join('%s=%s' % (k, v) for k, v in w2.items()))
            if kons == KONS:
                czekajace[(eksp, w['tick'])] = (int(w['tick']) / 60000.0, w['decyzjaNr'], w['mapa'], w['runId'])
        elif l.startswith('[PN-EXEC] '):
            wynik.append(l)
            d = t7.kv(l[len('[PN-EXEC] '):])
            eksp, tick = d.get('eksperyment', ''), d.get('tick')
            if (eksp, tick) in czekajace:
                dzien, nr, mapa, run = czekajace.pop((eksp, tick))
                stary = fakty[eksp].get(KLUCZ)
                wart = stary[0] + 1 if stary is not None and dzien - stary[1] < stary[2] else 1.0
                fakty[eksp][KLUCZ] = (wart, dzien, ZYCIE)
                wynik.append('[PN-FACT] runId=%s; tryb=symulacja; eksperyment=%s; tick=%d; mapa=%s; zdarzenie=ustawienie; '
                             'klucz=%s; wartosc=%s; dzien=%.6f; zycie=%s; tickZrodla=%s; decyzjaZrodla=%s; powod=potwierdzone'
                             % (run, eksp, int(tick) + 1000, mapa, KLUCZ, repr(wart), dzien, repr(ZYCIE), tick, nr))
        else:
            wynik.append(l)
    return wynik


def zbuduj(psuj=None, ramiona=1):
    linie = na_v8(t7.zbuduj(ramiona=ramiona))
    return psuj(linie) if psuj else linie


# =====================================================================================================
#  SCENARIUSZ TRYBU GRA (przeglad S6, 2026-09-23). Dawny test mial tylko tryb symulacji z jedna mapa,
#  bez [PN-LOAD], bez odrzucen i bez sciezki spoznionej - dlatego przepuscil dwa bledy analizatora
#  (zly klucz stanu lukow z luki=, falszywe 29 przy odrzuceniu na sciezce normalnej).
#  WZORZEC "faktow" liczy tu NIEZALEZNA replika reguly (przelicz_faktow, na tickach, z tolerancja
#  pol interwalu) - nie funkcja analizatora, inaczej mutacja reguly w analizatorze bylaby niewidoczna.
# =====================================================================================================
MAPA2 = '7'
TOL = 500          # tolerancja granicy w tickach (1/120 dnia) - FactLedger.BoundaryToleranceDays


def pole(l, k):
    return t7.kv(l.split('] ', 1)[1]).get(k)


def ustaw(l, k, v):
    glowa, reszta = l.split('] ', 1)
    d = t7.kv(reszta)
    if k in d:
        d[k] = v
    else:
        d[k] = v
    return glowa + '] ' + '; '.join('%s=%s' % (a, b) for a, b in d.items())


def jest(l, *prefiksy):
    return any(l.startswith(p) for p in prefiksy)


def do_gry(linie):
    """Plik symulacji -> plik zwyklej gry (bez eksperymentu, status wykonane)."""
    wynik = []
    for l in linie:
        if l.startswith('[PN-EXP]'):
            continue
        if jest(l, '[PN-DATA] ', '[PN-EXEC] ', '[PN-FACT] ', '[PN-ARC] '):
            l = ustaw(ustaw(l, 'tryb', 'gra'), 'eksperyment', '')
            if l.startswith('[PN-EXEC] ') and pole(l, 'status') == 'symulacja':
                l = ustaw(l, 'status', 'wykonane')
        wynik.append(l)
    return wynik


def fakt_aktywny(tick_teraz, tick_ust, zycie):
    # NIEZALEZNA replika: calkowite ticki, tolerancja pol interwalu (nie importujemy analizatora).
    return zycie <= 0 or (tick_teraz - tick_ust) < zycie * 60000.0 - TOL


def stan_do(linie, koniec):
    """Replay stanu PRZED linia 'koniec' dla gry (bez ramion): fakty i luki per mapa."""
    fakty, luki = {}, {}
    for l in linie[:koniec]:
        if l.startswith('[PN-LOAD]'):
            fakty, luki = {}, {}
            for w in (pole(l, 'fakty') or '').split(','):
                if ':' in w and '=' in w:
                    uid, r = w.split(':', 1)
                    kl, r = r.split('=', 1)
                    wart, r = r.split('@', 1)
                    dz, zy = r.split('/', 1)
                    fakty.setdefault(uid, {})[kl] = (float(wart), float(dz), float(zy), int(round(float(dz) * 60000)))
            for w in (pole(l, 'luki') or '').split(','):
                cz = w.split(':')
                if len(cz) == 3:
                    luki.setdefault(cz[0], {})[cz[1].split('#')[0]] = cz[2]
        elif l.startswith('[PN-FACT] ') and pole(l, 'tryb') == 'gra' and pole(l, 'zdarzenie') == 'ustawienie':
            fakty.setdefault(pole(l, 'mapa'), {})[pole(l, 'klucz')] = (
                float(pole(l, 'wartosc')), float(pole(l, 'dzien')), float(pole(l, 'zycie')), int(pole(l, 'tickZrodla')))
        elif l.startswith('[PN-ARC] ') and pole(l, 'tryb') == 'gra':
            s = luki.setdefault(pole(l, 'mapa'), {})
            if pole(l, 'zdarzenie') in ('otwarcie', 'przejscie'):
                s[pole(l, 'luk')] = pole(l, 'do')
            elif pole(l, 'zdarzenie') in ('zamkniecie', 'odrzucenie'):
                s.pop(pole(l, 'luk'), None)
    return fakty, luki


def linia_load(run, tick, mapy, fakty, luki, kolejka=None):
    f = ','.join('%s:%s=%r@%r/%r' % (uid, k, v[0], v[1], v[2]) for uid in sorted(fakty) for k, v in sorted(fakty[uid].items()))
    if kolejka:
        f = (f + ',' if f else '') + kolejka     # "uid:kolejka@tick" - parser ma go pominac
    lk = ','.join('%s:%s#1:%s' % (uid, a, fz) for uid in sorted(luki) for a, fz in sorted(luki[uid].items()))
    return ('[PN-LOAD] runId=%s; zrodlo=zapis; profil=PN_Profil_Powsciagliwy; map=%d; wpisow=0; decyzji=0; odrzuconych=0; '
            'odrzuconychHistorii=0; odrzuconychLukow=0; odrzuconychFaktow=0; wersjaPamieci=3; profilZapisany=PN_Profil_Powsciagliwy; '
            'narrator=PN_GenerativeNarrator; tick=%d; dzien=%.3f; mapy=%s; luki=%s; fakty=%s'
            % (run, len(mapy), tick, tick / 60000.0, ','.join('%s:%d' % (m, n) for m, n in sorted(mapy.items())), lk, f))


def przelicz_faktow(linie):
    """NIEZALEZNY wzorzec kolumny faktow: replay [PN-LOAD]/[PN-EXP] start/[PN-FACT] z osia ramion."""
    stan, baza, wynik = {}, {}, []
    for l in linie:
        if l.startswith('[PN-LOAD]'):
            run = pole(l, 'runId')
            for k in [k for k in stan if k[0] == run]:
                del stan[k]
            for w in (pole(l, 'fakty') or '').split(','):
                if ':' in w and '=' in w:
                    uid, r = w.split(':', 1)
                    kl, r = r.split('=', 1)
                    wart, r = r.split('@', 1)
                    dz, zy = r.split('/', 1)
                    stan.setdefault((run, '', uid), {})[kl] = (int(round(float(dz) * 60000)), float(zy))
        elif l.startswith('[PN-EXP] start'):
            run = pole(l, 'runId')
            baza[run] = {m: dict(s) for (r, e, m), s in stan.items() if r == run and not e}
        elif l.startswith('[PN-FACT] ') and pole(l, 'zdarzenie') == 'ustawienie':
            k = (pole(l, 'runId'), pole(l, 'eksperyment') or '', pole(l, 'mapa'))
            if k not in stan:
                stan[k] = dict(baza.get(k[0], {}).get(k[2], {})) if k[1] else {}
            stan[k][pole(l, 'klucz')] = (int(pole(l, 'tickZrodla')), float(pole(l, 'zycie')))
        elif l.startswith('[PN-DATA] '):
            k = (pole(l, 'runId'), pole(l, 'eksperyment') or '', pole(l, 'mapa'))
            if k not in stan:
                stan[k] = dict(baza.get(k[0], {}).get(k[2], {})) if k[1] else {}
            t = int(pole(l, 'tick'))
            l = ustaw(l, 'faktow', str(sum(1 for (tu, z) in stan[k].values() if fakt_aktywny(t, tu, z))))
        wynik.append(l)
    return wynik


def scenariusz_gry():
    """Zwraca (linie, straznicy). Kazdy straznik > 0 znaczy, ze dany przypadek NAPRAWDE jest w pliku."""
    G = do_gry(zbuduj())
    run = pole(next(l for l in G if l.startswith('[PN-DATA] ')), 'runId')
    s = {}

    def idx_wiersza(tick, mapa='0'):
        return next(i for i, l in enumerate(G) if l.startswith('[PN-DATA] ') and pole(l, 'tick') == tick and pole(l, 'mapa') == mapa)

    def linie_tick(prefiks, tick, mapa='0'):
        return [i for i, l in enumerate(G) if l.startswith(prefiks) and pole(l, 'tick') == tick and pole(l, 'mapa') == mapa]

    def fakt_zrodla(tick):
        return [i for i, l in enumerate(G) if l.startswith('[PN-FACT] ') and pole(l, 'tickZrodla') == tick]

    napady = [pole(l, 'tick') for l in G if l.startswith('[PN-DATA] ') and pole(l, 'konsekwencja') == KONS]
    ticki_wierszy = set(pole(l, 'tick') for l in G if l.startswith('[PN-DATA] '))
    z_lukiem = [t for t in napady if any(pole(G[i], 'powod') == 'wykonanie' for i in linie_tick('[PN-ARC] ', t))]
    bez_luku = [t for t in napady if not linie_tick('[PN-ARC] ', t) and str(int(t) + 1000) not in ticki_wierszy]
    assert z_lukiem and len(bez_luku) >= 3, (z_lukiem, bez_luku)

    # (a) odrzucenie na sciezce NORMALNEJ: niewykonane i niejednoznaczne, tick == tickZrodla.
    for t, st in ((bez_luku[0], 'niewykonane'), (bez_luku[1], 'niejednoznaczne')):
        e = linie_tick('[PN-EXEC] ', t)[0]
        G[e] = ustaw(G[e], 'status', st)
        f = fakt_zrodla(t)[0]
        G[f] = ustaw(ustaw(ustaw(ustaw(ustaw(G[f], 'zdarzenie', 'odrzucenie'), 'klucz', '-'), 'wartosc', ''),
                           'tick', t), 'powod', st)
        s['odrzucen normalnych'] = s.get('odrzucen normalnych', 0) + 1

    # (b) sciezka SPOZNIONA z krokiem luku: [PN-EXEC] i [PN-ARC] w T+1000, tickDecyzji = T.
    t = z_lukiem[0]
    nast = str(int(t) + 1000)
    e = linie_tick('[PN-EXEC] ', t)[0]
    G[e] = ustaw(ustaw(ustaw(G[e], 'tick', nast), 'status', 'pozno-wykonane'), 'tickDecyzji', t)
    for a in linie_tick('[PN-ARC] ', t):
        if pole(G[a], 'powod') == 'wykonanie':
            G[a] = ustaw(G[a], 'tick', nast)
    s['wykonan spoznionych z krokiem luku'] = 1

    # (c) spoznione niejednoznaczne: odrzucenie w T+1000.
    t = bez_luku[2]
    nast = str(int(t) + 1000)
    e = linie_tick('[PN-EXEC] ', t)[0]
    G[e] = ustaw(ustaw(ustaw(G[e], 'tick', nast), 'status', 'niejednoznaczne'), 'tickDecyzji', t)
    f = fakt_zrodla(t)[0]
    G[f] = ustaw(ustaw(ustaw(ustaw(G[f], 'zdarzenie', 'odrzucenie'), 'klucz', '-'), 'wartosc', ''), 'powod', 'niejednoznaczne')
    s['odrzucen spoznionych'] = 1

    # (d) GRANICA: czas zycia ostatniego ustawienia dobrany tak, zeby jakis wiersz padl DOKLADNIE
    # w ticku wygasniecia (roznica tickow wielokrotnoscia 3000 -> zycie dokladne w zapisie dziesietnym).
    ustawienia = [i for i, l in enumerate(G) if l.startswith('[PN-FACT] ') and pole(l, 'zdarzenie') == 'ustawienie']
    granica = None
    for i in ustawienia:
        tz = int(pole(G[i], 'tickZrodla'))
        nastepne = [int(pole(G[j], 'tickZrodla')) for j in ustawienia if int(pole(G[j], 'tickZrodla')) > tz]
        do = min(nastepne) if nastepne else 10 ** 9
        for l in G:
            if l.startswith('[PN-DATA] ') and pole(l, 'mapa') == '0':
                d = int(pole(l, 'tick')) - tz
                if 60000 <= d and int(pole(l, 'tick')) <= do and d % 3000 == 0:
                    granica = (i, d)
                    break
        if granica:
            break
    assert granica, 'brak pary (fakt, wiersz) z roznica tickow podzielna przez 3000'
    G[granica[0]] = ustaw(G[granica[0]], 'zycie', '%.2f' % (granica[1] / 60000.0))
    s['wierszy dokladnie w ticku wygasniecia'] = 1

    # (e) DRUGA MAPA od poczatku: kopia wierszy, [PN-EXEC] i [PN-ARC] (bez faktow - fakty sa per mapa).
    G2 = []
    for l in G:
        G2.append(l)
        if jest(l, '[PN-DATA] ', '[PN-EXEC] ', '[PN-ARC] ') and pole(l, 'mapa') == '0':
            G2.append(ustaw(l, 'mapa', MAPA2))
    G = G2
    s['wierszy drugiej mapy'] = sum(1 for l in G if l.startswith('[PN-DATA] ') and pole(l, 'mapa') == MAPA2)

    # (f) WCZYTANIE BEZSTRATNE w polowie: luki= i fakty= NIEPUSTE, z elementem kolejki.
    wiersze0 = [i for i, l in enumerate(G) if l.startswith('[PN-DATA] ') and pole(l, 'mapa') == '0']
    p = None
    for i in wiersze0[1:]:
        fk, lk = stan_do(G, i)
        if lk.get('0') and fk.get('0'):
            p = i
            break
    assert p is not None
    ts = int(pole(G[p], 'tick'))
    # Wczytanie tuz PRZED ostatnia linia z tickiem < ts (fakt stosowany w ts zostaje w kolejce).
    while p > 0 and not (jest(G[p - 1], '[PN-DATA] ', '[PN-EXEC] ', '[PN-FACT] ', '[PN-ARC] ') and int(pole(G[p - 1], 'tick')) < ts):
        p -= 1
    fk, lk = stan_do(G, p)
    nr = int(pole(G[next(i for i in range(p, len(G)) if G[i].startswith('[PN-DATA] '))], 'decyzjaNr'))
    G.insert(p, linia_load(run, ts - 1, {'0': nr, MAPA2: nr}, fk, lk, kolejka='0:kolejka@%d' % ts))
    s['wczytan z niepustymi luki= i fakty='] = 1 if lk and fk else 0

    # (g) PORZUCONA GALAZ: zapis w polowie pozniejszego odcinka, trzy decyzje "przyszlosci", wczytanie, powtorka.
    # Odcinek bez otwartych lukow (mapa 8 kopiuje wiersze mapy 0 bez linii [PN-ARC]).
    wiersze0 = [i for i, l in enumerate(G) if l.startswith('[PN-DATA] ') and pole(l, 'mapa') == '0']
    q = next(w for k, w in enumerate(wiersze0) if k >= len(wiersze0) * 2 // 3 and k + 3 < len(wiersze0)
             and all(not pole(G[x], 'lukFazy') for x in wiersze0[k:k + 3]))
    tq = int(pole(G[q], 'tick'))
    while q > 0 and not (jest(G[q - 1], '[PN-DATA] ', '[PN-EXEC] ', '[PN-FACT] ', '[PN-ARC] ') and int(pole(G[q - 1], 'tick')) < tq):
        q -= 1
    q2 = [i for i in wiersze0 if i >= q][3]
    fk, lk = stan_do(G, q)
    nr = int(pole(G[next(i for i in range(q, len(G)) if G[i].startswith('[PN-DATA] '))], 'decyzjaNr'))
    kol = next(('0:kolejka@%d' % tq for l in G[q:q2] if l.startswith('[PN-FACT] ') and int(pole(l, 'tick')) == tq), None)
    # MAPA 8 ZALOZONA PO ZAPISIE: kopia wierszy i [PN-EXEC] mapy 0 z odcinka, numeracja decyzji od zera.
    # Nie ma jej w "mapy=", a powtorka po wczytaniu nadaje jej TE SAME numery - bez przyciecia wierszy
    # map spoza "mapy" grupa mapy 8 mialaby 0,1,2,0,1,2 (naruszenie 05).
    odcinek, nr8 = [], {}
    for l in G[q:q2]:
        odcinek.append(l)
        if jest(l, '[PN-DATA] ', '[PN-EXEC] ') and pole(l, 'mapa') == '0':
            t = pole(l, 'tick')
            if t not in nr8:
                nr8[t] = len(nr8)
            k8 = ustaw(ustaw(l, 'mapa', '8'), 'decyzjaNr', str(nr8[t]))
            if l.startswith('[PN-DATA] '):
                k8 = ustaw(k8, 'histDecyzji', str(nr8[t]))
            odcinek.append(k8)
    # Porzucona galaz zostawia fakt NOWEGO klucza - wczytanie ma go zapomniec (stan faktow z pola fakty=).
    t_porz = next(pole(l, 'tick') for l in odcinek if l.startswith('[PN-EXEC] ') and pole(l, 'mapa') == '0')
    porzucony = ('[PN-FACT] runId=%s; tryb=gra; eksperyment=; tick=%d; mapa=0; zdarzenie=ustawienie; klucz=porzucony.slad; '
                 'wartosc=1.0; dzien=%.6f; zycie=30.0; tickZrodla=%s; decyzjaZrodla=0; powod=wykonane'
                 % (run, int(t_porz) + 1000, int(t_porz) / 60000.0, t_porz))
    s['faktow nowego klucza tylko w porzuconej galezi'] = 1
    G = G[:q] + odcinek + [porzucony] + [linia_load(run, tq - 1, {'0': nr, MAPA2: nr}, fk, lk, kolejka=kol)] + odcinek + G[q2:]
    # Oczekiwana liczba porzuconych wierszy: wszystkie wiersze odcinka (mapy 0, 7 i 8).
    s['_porzuconych_wierszy'] = sum(1 for l in odcinek if l.startswith('[PN-DATA] '))
    s['linii porzuconej galezi z [PN-EXEC]'] = sum(1 for l in odcinek if l.startswith('[PN-EXEC] '))
    s['wierszy mapy zalozonej po zapisie'] = sum(1 for l in odcinek if l.startswith('[PN-DATA] ') and pole(l, 'mapa') == '8')

    # (h) EKSPERYMENT z FAKTEM SPRZED NIEGO: ramie startuje z migawki (fakt gry, zadnego otwartego luku).
    wiersze0 = [i for i, l in enumerate(G) if l.startswith('[PN-DATA] ') and pole(l, 'mapa') == '0']
    P = None
    for i in wiersze0:
        j = i + 1
        while j < len(G) and not G[j].startswith('[PN-DATA] '):
            j += 1
        fk, lk = stan_do(G, j)
        if fk.get('0') and not any(lk.values()) and not any(G[k].startswith('[PN-LOAD]') for k in range(i, j)):
            P = j
            break
    assert P is not None
    tP = max(int(pole(l, 'tick')) for l in G[:P] if jest(l, '[PN-DATA] ', '[PN-EXEC] ', '[PN-FACT] ', '[PN-ARC] '))
    okno = []
    for l in do_gry(zbuduj()):
        if l.startswith('[PN-DATA] ') and int(pole(l, 'decyzjaNr')) >= 4:
            break
        if jest(l, '[PN-DATA] ', '[PN-EXEC] '):
            okno.append(l)
    t0 = min(int(pole(l, 'tick')) for l in okno)
    przes = tP + 1000 - t0
    ramie = []
    for l in okno:
        l = ustaw(ustaw(l, 'tryb', 'symulacja'), 'eksperyment', 'expS6/1-ramie')
        nt = int(pole(l, 'tick')) + przes
        l = ustaw(l, 'tick', str(nt))
        if l.startswith('[PN-DATA] '):
            l = ustaw(l, 'dzien', '%.3f' % (nt / 60000.0))
        else:
            l = ustaw(l, 'status', 'symulacja')
        ramie.append(l)
    G = G[:P] + ['[PN-EXP] start; eksperyment=expS6; runId=%s' % run,
                 '[PN-EXP] ramie; eksperyment=expS6; ramie=1-ramie'] + ramie + \
        ['[PN-EXP] end; eksperyment=expS6; status=kompletny'] + G[P:]
    s['wierszy ramienia'] = sum(1 for l in ramie if l.startswith('[PN-DATA] '))

    # (i) [PN-EXP] start PO wczytaniu z luki= - dawny klucz 2-elementowy wywracal tu analize.
    G.append('[PN-EXP] start; eksperyment=expS6b; runId=%s' % run)

    G = przelicz_faktow(G)
    s['wierszy ramienia widzacych fakt sprzed eksperymentu'] = sum(
        1 for l in G if l.startswith('[PN-DATA] ') and pole(l, 'eksperyment') == 'expS6/1-ramie' and pole(l, 'faktow') != '0')
    return G, s


def main():
    ok = True
    wzorzec = zbuduj()
    faktowNiezero = sum(1 for l in wzorzec if l.startswith('[PN-DATA]') and '; faktow=0;' not in l)
    liniiFaktow = sum(1 for l in wzorzec if l.startswith('[PN-FACT]'))
    # STRAZNIK: plik musi naprawde niesc fakty i wiersze, ktore je widza - inaczej 0 naruszen nic nie mowi.
    straznik = liniiFaktow > 0 and faktowNiezero > 0
    print('STRAZNIK: linii [PN-FACT] %d, wierszy z faktow > 0: %d -> %s' % (liniiFaktow, faktowNiezero, straznik))
    ok = ok and straznik

    kod, nar, out = t7.uruchom('v8_poprawny', wzorzec)
    print('POPRAWNY v8: kod %d, naruszone %s' % (kod, nar))
    if kod != 0:
        print(out[:3000])
    ok = ok and kod == 0 and not nar

    # Dwa ramiona: stan faktow kazdego ramienia startuje od migawki z [PN-EXP] start.
    kod2, nar2, out2 = t7.uruchom('v8_dwa_ramiona', zbuduj(ramiona=2))
    print('DWA RAMIONA v8: kod %d, naruszone %s' % (kod2, nar2))
    if kod2 != 0:
        print(out2[:2000])
    ok = ok and kod2 == 0 and not nar2

    # DOKLADNE ZBIORY naruszen (S6): dawne "oczek in nar" przepuszczalo naruszenia dodatkowe. Kazdy
    # dodatkowy niezmiennik ma uzasadnienie: zmiana ustawienia na odrzucenie usuwa fakt ze stanu, wiec
    # rozjezdza tez kolumne faktow (28).
    przypadki = [
        ({'28'}, 'faktow rozjechane ze stanem', lambda L: t7.zamien_pierwsza(
            L, lambda l: l.startswith('[PN-DATA]') and '; faktow=1;' in l, lambda l: l.replace('; faktow=1;', '; faktow=5;'))),
        ({'29'}, 'fakt w ticku decyzji', lambda L: t7.zamien_pierwsza(
            L, lambda l: l.startswith('[PN-FACT]'),
            lambda l: l.replace('; tick=%s;' % l.split('; tick=')[1].split(';')[0],
                                '; tick=%s;' % l.split('tickZrodla=')[1].split(';')[0]))),
        ({'28', '30'}, 'odrzucenie po wykonaniu', lambda L: t7.zamien_pierwsza(
            L, lambda l: l.startswith('[PN-FACT]'), lambda l: l.replace('zdarzenie=ustawienie', 'zdarzenie=odrzucenie'))),
        ({'31'}, 'konsekwencja inna niz w kluczu', lambda L: t7.zamien_pierwsza(
            L, lambda l: l.startswith('[PN-DATA]') and 'konsekwencja=' + KONS in l,
            lambda l: l.replace('konsekwencja=' + KONS, 'konsekwencja=PN_Kons_Trofea'))),
    ]
    for oczek, opis, psuj in przypadki:
        zepsuty = zbuduj(psuj)
        if zepsuty == wzorzec:
            print('%-34s *** USZKODZENIE NIE ZMIENILO PLIKU ***' % opis)
            ok = False
            continue
        kod, nar, out = t7.uruchom('v8_zly_' + '_'.join(sorted(oczek)), zepsuty)
        trafiony = kod == 1 and set(nar) == oczek
        ok = ok and trafiony
        print('%-34s oczekiwane %s -> kod %d, naruszone %s %s' % (opis, sorted(oczek), kod, nar, 'OK' if trafiony else '*** NIE ***'))

    # --- SCENARIUSZ TRYBU GRA (S6) ---
    gra, straznicy = scenariusz_gry()
    oczekPorzuconych = straznicy.pop('_porzuconych_wierszy')
    for nazwa, ile in sorted(straznicy.items()):
        print('STRAZNIK scenariusza: %-52s %d' % (nazwa, ile))
        ok = ok and ile > 0
    kodG, narG, outG = t7.uruchom('v8_gra_scenariusz', gra)
    print('SCENARIUSZ GRY: kod %d, naruszone %s' % (kodG, narG))
    if kodG != 0:
        print(outG[:3000])
    ok = ok and kodG == 0 and not narG
    # Liczba porzuconych wierszy: 05 grupuje wiersze po odcinkach miedzy wczytaniami, wiec wiersze porzuconej
    # galezi mapy zalozonej po zapisie nie daja naruszenia - zawyzylyby tylko liczniki (krok 8). Pilnujemy
    # wprost liczby raportowanej przez analize.
    raport = [l for l in outG.splitlines() if 'porzucona galaz po wczytaniu:' in l]
    porzuconych = int(raport[0].rsplit(':', 1)[1]) if raport else -1
    print('PORZUCONE WIERSZE: raport %d, oczekiwane %d' % (porzuconych, oczekPorzuconych))
    ok = ok and porzuconych == oczekPorzuconych and oczekPorzuconych > 0

    load = next(i for i, l in enumerate(gra) if l.startswith('[PN-LOAD]'))
    odrz = next(i for i, l in enumerate(gra) if l.startswith('[PN-FACT]') and 'powod=niewykonane' in l)
    # Ostatnie ustawienie mapy 0 (PO wszystkich wczytaniach - wczytanie odtwarza fakty z pola fakty=,
    # wiec fakt sprzed niego nie zmienilby niczego po nim). Kopia z innym kluczem dla DRUGIEJ mapy.
    fakt0 = max(i for i, l in enumerate(gra) if l.startswith('[PN-FACT]') and 'zdarzenie=ustawienie' in l and '; mapa=0;' in l)
    przypadkiGry = [
        ({'23'}, 'wczytanie gubi luki=', lambda L: L[:load] + [ustaw(L[load], 'luki', '')] + L[load + 1:]),
        ({'29'}, 'odrzucenie normalne w ticku pozniejszym',
         lambda L: L[:odrz] + [ustaw(L[odrz], 'tick', str(int(pole(L[odrz], 'tickZrodla')) + 1000))] + L[odrz + 1:]),
        ({'26', '30'}, 'spoznione niewykonanie przy ustawionym fakcie',
         lambda L: [ustaw(l, 'status', 'pozno-niewykonane') if l.startswith('[PN-EXEC]') and pole(l, 'status') == 'pozno-wykonane'
                    else l for l in L]),
        ({'28'}, 'fakt drugiej mapy bez odbicia w jej wierszach',
         lambda L: L[:fakt0 + 1] + [ustaw(ustaw(L[fakt0], 'mapa', MAPA2), 'klucz', 'drugi.klucz')] + L[fakt0 + 1:]),
    ]
    for oczek, opis, psuj in przypadkiGry:
        zepsuty = psuj(list(gra))
        if zepsuty == gra:
            print('%-40s *** USZKODZENIE NIE ZMIENILO PLIKU ***' % opis)
            ok = False
            continue
        kod, nar, out = t7.uruchom('v8_gra_zly_' + '_'.join(sorted(oczek)), zepsuty)
        trafiony = kod == 1 and set(nar) == oczek
        ok = ok and trafiony
        print('%-40s oczekiwane %s -> kod %d, naruszone %s %s' % (opis, sorted(oczek), kod, nar, 'OK' if trafiony else '*** NIE ***'))
    print('WYNIK:', 'OK' if ok else 'BLAD')
    sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main()
