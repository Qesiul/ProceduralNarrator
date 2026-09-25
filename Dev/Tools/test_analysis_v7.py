# -*- coding: utf-8 -*-
# Uzycie: python test_analysis_v7.py  (kod wyjscia 1 przy bledzie)
# Test narzedzia analysis_v7.py na SYNTETYCZNYM pliku v7 zbudowanym z referencyjnych wierszy v6:
# plik poprawny -> 0 naruszen; kazde celowe uszkodzenie -> DOKLADNIE oczekiwany ZBIOR niezmiennikow
# (od S6; wczesniej sprawdzano tylko "oczekiwany jest wsrod naruszonych").
import collections, io, os, subprocess, sys, tempfile

ROOT = 'D:/Games/RimWorld/Mods/ProceduralNarrator'
V6 = ROOT + '/Dev/Data/fixtures/2026-09-22_simulator_v6_PN_decyzje.log'
OUT = tempfile.gettempdir()   # pliki syntetyczne NIE laduja w repozytorium
PRE = ['bogactwo', 'bogactwoWzgl', 'punkty', 'lukiAktywne', 'lukFazy', 'frakcjaLuku', 'lukStosowany', 'lukDopasowanych']
POST = ['intensywnosc', 'arcAlignment', 'premiaLuku', 'lukWPasmie']
LUK = ('luk=PN_Luk_Wendeta; priorytet=10; grupa=frakcja; cooldownDni=30; nastepca=-; wiazeFrakcje=tak; '
       'fazy=Zasiew:Seed:Negative,Eskalacja:Escalation:Negative,Kulminacja:Climax:Negative,Rozwiazanie:Resolution:Positive; '
       'krawedzie=Zasiew>Eskalacja,Eskalacja>Kulminacja,Eskalacja>Rozwiazanie,Kulminacja>Rozwiazanie,Kulminacja>Rozwiazanie')


def kv(fr):
    d = collections.OrderedDict()
    for p in fr.split(';'):
        p = p.strip()
        if '=' in p:
            k, v = p.split('=', 1)
            d[k.strip()] = v.strip()
    return d


def zbuduj(psuj=None, ramiona=1):
    linie = io.open(V6, encoding='utf-8').read().splitlines()
    cols6 = next(l for l in linie if l.startswith('[PN-DATA-COLS]')).split('kolumny=')[1].split(',')
    i = cols6.index('pytanDoGry') + 1
    cols7 = cols6[:i] + PRE + cols6[i:] + POST
    wynik = ['[PN-DATA-COLS] wersja=7; kolumny=' + ','.join(cols7)]
    for l in linie:
        if l.startswith('[PN-CONFIG] storyteller='):
            wynik.append(l + ' | luki: wlaczone maxRownoczesnych=2')
        elif l.startswith('[PN-CONFIG] '):
            wynik.append(l)
    wynik.append('[PN-CONFIG] ' + LUK)

    wiersze = [kv(l[len('[PN-DATA] '):]) for l in linie if l.startswith('[PN-DATA] ')]
    ramie = wiersze[0]['eksperyment']
    wiersze = [w for w in wiersze if w['eksperyment'] == ramie]
    napady = [j for j, w in enumerate(wiersze) if w['decyzja'] != 'PASS' and w['wybor'] == 'RaidEnemy']
    j0 = napady[0]                            # napad otwierajacy Wendete
    jT = min(j0 + 6, len(wiersze) - 3)        # limit czasu Eskalacji po kilku turach
    jZ = jT + 2                               # wygaszenie Rozwiazania
    faza = None
    for j, w in enumerate(wiersze):
        # stan lukow W CHWILI decyzji (przed krokami po tej decyzji)
        w2 = collections.OrderedDict()
        for k, v in w.items():
            if k == 'wersjaLogu':
                v = '7'
            w2[k] = v
            if k == 'pytanDoGry':
                w2['bogactwo'] = '40000.000'
                w2['bogactwoWzgl'] = '1.500'
                w2['punkty'] = '600.000'
                aktywna = faza is not None
                status = ''
                if aktywna:
                    status = 'aktywna' if faza == 'Eskalacja' else 'czeka'
                w2['lukiAktywne'] = '1' if aktywna else '0'
                w2['lukFazy'] = ('PN_Luk_Wendeta:%s:%s' % (faza, status)) if aktywna else ''
                w2['frakcjaLuku'] = 'F7' if aktywna else ''
                stos = aktywna and faza == 'Eskalacja'
                w2['lukStosowany'] = 'true' if stos else 'false'
                w2['lukDopasowanych'] = '1' if stos else '0'
        zd = w['decyzja'] != 'PASS'
        stos = faza == 'Eskalacja'
        w2['intensywnosc'] = '0' if zd else ''
        if zd and stos:
            aa = 1.0 if w['wybor'] == 'RaidEnemy' else 0.0
            w2['arcAlignment'] = '%.3f' % aa
            premia = (float(w['best']) - float(w['pasmo'])) * aa if w['best'] else 0.0
            w2['premiaLuku'] = '%.3f' % premia
        else:
            w2['arcAlignment'] = ''
            w2['premiaLuku'] = ''
        w2['lukWPasmie'] = '1' if stos else ''
        wynik.append('[PN-DATA] ' + '; '.join('%s=%s' % (k, v) for k, v in w2.items()))
        pre = 'runId=%s; tryb=symulacja; eksperyment=%s; tick=%s' % (w['runId'], ramie, w['tick'])
        if zd:
            wynik.append('[PN-EXEC] %s; mapa=%s; decyzjaNr=%s; incydent=%s; klucz=%s; status=symulacja; '
                         'ostatniPrzed=-1; ostatniPo=%s; frakcja=-; frakcjaZwiazana=-'
                         % (pre, w['mapa'], w['decyzjaNr'], w['wybor'], w['klucz'], w['tick']))
        if j == j0:
            wynik.append('[PN-ARC] %s; dzien=%s; mapa=%s; decyzjaNr=%d; luk=PN_Luk_Wendeta; instancja=1; '
                         'zdarzenie=otwarcie; z=Zasiew; do=Eskalacja; powod=wykonanie; wynik=-; frakcja=F7; komunikat=tak'
                         % (pre, w['dzien'], w['mapa'], int(w['decyzjaNr']) + 1))
            faza = 'Eskalacja'
        if j == jT:
            wynik.append('[PN-ARC] %s; dzien=%s; mapa=%s; decyzjaNr=%d; luk=PN_Luk_Wendeta; instancja=1; '
                         'zdarzenie=przejscie; z=Eskalacja; do=Rozwiazanie; powod=limitCzasu; wynik=-; frakcja=F7; komunikat=nie'
                         % (pre, w['dzien'], w['mapa'], int(w['decyzjaNr']) + 1))
            faza = 'Rozwiazanie'
        if j == jZ:
            wynik.append('[PN-ARC] %s; dzien=%s; mapa=%s; decyzjaNr=%d; luk=PN_Luk_Wendeta; instancja=1; '
                         'zdarzenie=zamkniecie; z=Rozwiazanie; do=-; powod=limitCzasu; wynik=wygaszony; frakcja=F7; komunikat=nie'
                         % (pre, w['dzien'], w['mapa'], int(w['decyzjaNr']) + 1))
            faza = None
    if ramiona > 1:
        # DRUGIE RAMIE eksperymentu: te same ticki, ta sama mapa, ten sam runId - kazde ramie
        # symulacji powtarza zakres na kopii pamieci sprzed eksperymentu. Bez ramienia w kluczu
        # analiza zlewa oba strumienie (blad znaleziony na pierwszych danych z gry 2026-09-22).
        run = next(l for l in wynik if l.startswith('[PN-DATA] ')).split('runId=')[1].split(';')[0]
        glowa = [l for l in wynik if not l.startswith(('[PN-DATA]', '[PN-ARC]', '[PN-EXEC]'))]
        cialo = [l for l in wynik if l.startswith(('[PN-DATA]', '[PN-ARC]', '[PN-EXEC]'))]
        stare = next(l for l in cialo if 'eksperyment=' in l).split('eksperyment=')[1].split(';')[0]
        drugie = [l.replace('eksperyment=' + stare, 'eksperyment=' + stare.rsplit('/', 1)[0] + '/K-kontrola') for l in cialo]
        wynik = glowa + ['[PN-EXP] start; eksperyment=%s; runId=%s' % (stare.rsplit('/', 1)[0], run)] \
                + cialo + ['[PN-EXP] ramie; eksperyment=%s; ramie=K-kontrola' % stare.rsplit('/', 1)[0]] + drugie
    if psuj:
        wynik = psuj(wynik)
    return wynik


def uruchom(nazwa, linie):
    p = os.path.join(OUT, 'syntetyczny_v7_%s.log' % nazwa)
    io.open(p, 'w', encoding='utf-8', newline='\n').write('\n'.join(linie) + '\n')
    r = subprocess.run([sys.executable, ROOT + '/Dev/Tools/analysis_v7.py', p], capture_output=True, text=True)
    naruszone = [l.strip()[:2] for l in r.stdout.splitlines() if 'NARUSZEN' in l]
    return r.returncode, naruszone, r.stdout


def przesun_pbrame(l, o=0.02):
    stara = l.split('; pBrama=')[1].split(';')[0]
    return l.replace('; pBrama=' + stara + ';', '; pBrama=%.3f;' % (float(stara) + o), 1)


def zamien_pierwsza(linie, warunek, f):
    for i, l in enumerate(linie):
        if warunek(l):
            linie[i] = f(l)
            break
    return linie


def main():
    kod, nar, out = uruchom('poprawny', zbuduj())
    print('POPRAWNY: kod %d, naruszone %s' % (kod, nar))
    if kod != 0:
        print(out[:3000])
    ok = kod == 0 and not nar

    # DWA RAMIONA eksperymentu na tych samych tickach - regresja bledu analizy z 2026-09-22.
    dwa = zbuduj(ramiona=2)
    klucze = [l.split('tick=')[1].split(';')[0] for l in dwa if l.startswith('[PN-EXEC]')]
    straznik = len(klucze) != len(set(klucze))     # ticki MUSZA sie powtarzac miedzy ramionami
    kod2, nar2, out2 = uruchom('dwa_ramiona', dwa)
    print('DWA RAMIONA: kod %d, naruszone %s, ticki powtorzone: %s' % (kod2, nar2, straznik))
    if kod2 != 0:
        print(out2[:2000])
    ok = ok and kod2 == 0 and not nar2 and straznik

    przypadki = [
        ({'22'}, 'nielegalna krawedz', lambda L: zamien_pierwsza(L, lambda l: 'zdarzenie=przejscie' in l,
                                                               lambda l: l.replace('z=Eskalacja; do=Rozwiazanie; powod=limitCzasu', 'z=Eskalacja; do=Rozwiazanie; powod=wykonanie'))),
        ({'25'}, 'brak [PN-EXEC]', lambda L: [l for i, l in enumerate(L) if not (l.startswith('[PN-EXEC]') and i == next(k for k, x in enumerate(L) if x.startswith('[PN-EXEC]')))]),
        ({'19'}, 'premia zla', lambda L: zamien_pierwsza(L, lambda l: ('; arcAlignment=0' in l or '; arcAlignment=1' in l) and 'losowan=3' in l,
                                                        lambda l: l.replace('premiaLuku=', 'premiaLuku=9'))),
        ({'23'}, 'lukFazy rozjechane', lambda L: zamien_pierwsza(L, lambda l: 'lukFazy=PN_Luk_Wendeta:Eskalacja' in l,
                                                                lambda l: l.replace('lukFazy=PN_Luk_Wendeta:Eskalacja', 'lukFazy=PN_Luk_Wendeta:Kulminacja'))),
        ({'27'}, 'wykonane w symulacji', lambda L: zamien_pierwsza(L, lambda l: l.startswith('[PN-EXEC]'),
                                                                  lambda l: l.replace('status=symulacja', 'status=wykonane'))),
        ({'24'}, 'aktywna pozytywna przy Escalate', lambda L: zamien_pierwsza(L, lambda l: 'Rozwiazanie:czeka' in l and 'intencja=Escalate' in l,
                                                                             lambda l: l.replace('Rozwiazanie:czeka', 'Rozwiazanie:aktywna'))),
        # Tolerancja 15 jest od 2026-09-23 wyprowadzona z zaokraglen - ten przypadek pilnuje, ze
        # nie stala sie pusta: pBrama przesuniete o 0.02 (osiem razy ponad granice zaokraglen).
        # 08 lamie sie RAZEM z 15, bo pRunda*(1-pBrama) ~ p tez zalezy od pBrama.
        ({'08', '15'}, 'pBrama rozjechane ze wzorem', lambda L: zamien_pierwsza(L, lambda l: l.startswith('[PN-DATA]') and 'passStlumiony=false' in l and '; pBrama=0.' in l,
                                                                         przesun_pbrame)),
    ]
    wzorzec = zbuduj()
    for oczek, opis, psuj in przypadki:
        zepsuty = zbuduj(psuj)
        # STRAZNIK: uszkodzenie musi naprawde zmienic plik - inaczej "brak naruszenia" niczego nie mowi.
        if zepsuty == wzorzec:
            print('%-34s *** USZKODZENIE NIE ZMIENILO PLIKU ***' % opis)
            ok = False
            continue
        kod, nar, out = uruchom('zly_' + '_'.join(sorted(oczek)), zepsuty)
        trafiony = kod == 1 and set(nar) == oczek
        ok = ok and trafiony
        print('%-34s oczekiwane %s -> kod %d, naruszone %s %s' % (opis, sorted(oczek), kod, nar, 'OK' if trafiony else '*** NIE ***'))
    print('WYNIK:', 'OK' if ok else 'BLAD')
    sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main()
