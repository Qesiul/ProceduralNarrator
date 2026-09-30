# -*- coding: utf-8 -*-
# Uzycie: python test_analysis_v10.py  (kod wyjscia 1 przy bledzie)
# Test formatu v10 (krok 9, K0) narzedzia analysis_v7.py: niezmiennik 46 (strona bramy Anomaly, szansa, akcje
# odciete lustrem sprawdzen gry) oraz bramki wersji - wiersz v10 ma przechodzic WSZYSTKIE reguly v7-v9 (luki,
# fakty, styl, pary [PN-EXEC]), a nie tylko nowa.
# Fikstura: plik kroku 8 z test_analysis_k8 (tryb gra z wczytaniem i porzucona galezia, sciezka spozniona, ramiona
# symulatora) przerobiony na v10 regulami z kodu moda (PNLog.Data):
#   - naglowek: anomaliaSzansa, anomaliaTura po stylKierunek, zablokowanychSilnik po budzetPrzekroczony;
#   - wiersze: trzy rodzaje tur na zmiane - bez DLC (szansa pusta, strona Regular), z DLC (szansa 0.300, strona
#     Regular albo Anomaly), lustro wylaczone bezpiecznikiem (zablokowanychSilnik puste).
# Plik poprawny -> 0 naruszen; kazde uszkodzenie -> DOKLADNIE oczekiwany zbior.
import collections, sys

sys.path.insert(0, 'D:/Games/RimWorld/Mods/ProceduralNarrator/Dev/Tools')
import test_analysis_v7 as t7
import test_analysis_v8 as t8
import test_analysis_v9 as t9
import test_analysis_k8 as tk8


def na_v10(linie):
    wynik, s = [], collections.Counter()
    nr = 0
    for l in linie:
        if l.startswith('[PN-DATA-COLS]'):
            cols = l.split('kolumny=')[1].split(',')
            i = cols.index('stylKierunek') + 1
            cols = cols[:i] + ['anomaliaSzansa', 'anomaliaTura'] + cols[i:]
            j = cols.index('budzetPrzekroczony') + 1
            cols = cols[:j] + ['zablokowanychSilnik'] + cols[j:]
            wynik.append('[PN-DATA-COLS] wersja=10; kolumny=' + ','.join(cols))
        elif l.startswith('[PN-DATA] '):
            w = t7.kv(l[len('[PN-DATA] '):])
            rodzaj = nr % 3
            if rodzaj == 0:
                sz, tura, zb = '', 'Regular', '0'
                s['bez DLC'] += 1
            elif rodzaj == 1:
                sz, tura, zb = '0.300', 'Anomaly' if nr % 2 else 'Regular', str(nr % 4)
                s['strona Anomaly' if tura == 'Anomaly' else 'z DLC zwykla'] += 1
            else:
                sz, tura, zb = '0.075', 'Regular', ''
                s['lustro wylaczone'] += 1
            nr += 1
            w2 = collections.OrderedDict()
            for a, b in w.items():
                w2[a] = '10' if a == 'wersjaLogu' else b
                if a == 'stylKierunek':
                    w2['anomaliaSzansa'], w2['anomaliaTura'] = sz, tura
                if a == 'budzetPrzekroczony':
                    w2['zablokowanychSilnik'] = zb
            wynik.append('[PN-DATA] ' + '; '.join('%s=%s' % p for p in w2.items()))
        else:
            wynik.append(l.replace('wersjaLogu=9', 'wersjaLogu=10') if l.startswith('[PN-SESSION]') else l)
    return wynik, s


def main():
    ok = True
    baza, _ = t9.zbuduj()
    k8, _ = tk8.na_k8(baza)
    wzorzec, s = na_v10(k8)
    for nazwa in ['bez DLC', 'z DLC zwykla', 'strona Anomaly', 'lustro wylaczone']:
        print('STRAZNIK: %-20s %d' % (nazwa, s[nazwa]))
        ok = ok and s[nazwa] > 0
    kod, nar, out = t7.uruchom('v10_poprawny', wzorzec)
    print('POPRAWNY v10: kod %d, naruszone %s' % (kod, nar))
    if kod != 0 or nar:
        print(out[:4000])
    ok = ok and kod == 0 and not nar
    wszystkie = "wersjaLogu: ['10']" in out
    print('WSZYSTKIE WIERSZE v10:', 'OK' if wszystkie else '*** NIE ***')
    ok = ok and wszystkie
    # 46 wymieniony w tabeli niezmiennikow (inaczej brak naruszen nie znaczylby, ze regule sprawdzono).
    wymieniony = '46 kolumny v10' in out
    print('REGULA 46 W TABELI:', 'OK' if wymieniony else '*** NIE ***')
    ok = ok and wymieniony

    def pierwszy(L, warunek):
        return next(i for i, l in enumerate(L) if warunek(l))

    def dane(warunek=lambda l: True):
        return lambda l: l.startswith('[PN-DATA] ') and warunek(l)

    def zmien(warunek, k, v):
        def f(L):
            i = pierwszy(L, warunek)
            L[i] = t8.ustaw(L[i], k, v)
            return L
        return f

    def bez_dlc(l):
        return t8.pole(l, 'anomaliaSzansa') == ''

    def z_dlc(l):
        return t8.pole(l, 'anomaliaSzansa') != ''

    def z_luku(l):
        return t8.pole(l, 'premiaLuku') not in ('', None)

    def ze_stylem(l):
        return t8.pole(l, 'premiaStylu') not in ('', None) and t8.pole(l, 'stylWartosc') not in ('', '0', '0.000')

    def zdarzenie(l):
        return t8.pole(l, 'decyzja') != 'PASS'

    def plus(k, o):
        def f(L):
            i = pierwszy(L, dane(lambda l: t8.pole(l, k) not in ('', None) and (k != 'premiaStylu' or ze_stylem(l))))
            L[i] = t8.ustaw(L[i], k, '%.3f' % (float(t8.pole(L[i], k)) + o))
            return L
        return f

    def zdublowany_exec(L):
        # Wiersz v10 ze zdarzeniem: jego [PN-EXEC] podwojony -> para 25 "dokladnie jedno" zlamana tylko dla v10.
        i = pierwszy(L, lambda l: l.startswith('[PN-EXEC] ') and t8.pole(l, 'status') == 'symulacja')
        return L[:i + 1] + [L[i]] + L[i + 1:]

    przypadki = [
        ('bez DLC strona Anomaly', zmien(dane(bez_dlc), 'anomaliaTura', 'Anomaly'), {'46'}),
        ('strona spoza slownika', zmien(dane(z_dlc), 'anomaliaTura', 'Zwykla'), {'46'}),
        ('strona pusta', zmien(dane(), 'anomaliaTura', ''), {'46'}),
        ('szansa ponad 1', zmien(dane(z_dlc), 'anomaliaSzansa', '1.500'), {'46'}),
        ('szansa ujemna', zmien(dane(z_dlc), 'anomaliaSzansa', '-0.100'), {'46'}),
        ('zablokowanych ujemne', zmien(dane(), 'zablokowanychSilnik', '-1'), {'46'}),
        ('zablokowanych ulamkowe', zmien(dane(), 'zablokowanychSilnik', '1.5'), {'46'}),
        # Bramki wersji: regula starszego formatu nadal obowiazuje w wierszu v10.
        ('v10: premiaLuku przesunieta (19)', plus('premiaLuku', 0.05), {'19'}),
        ('v10: konsekwencja niezgodna z kluczem (31)', zmien(dane(zdarzenie), 'konsekwencja', 'PN_Kons_Obca'), {'31'}),
        ('v10: premiaStylu przesunieta (32)', plus('premiaStylu', 0.05), {'32'}),
        ('v10: zdarzenie z dwoma [PN-EXEC] (25)', zdublowany_exec, {'25'}),
    ]
    for opis, psuj, oczekiwane in przypadki:
        nazwa = 'v10_' + ''.join(c if c.isalnum() else '_' for c in opis)
        kod, nar, out = t7.uruchom(nazwa, psuj(list(wzorzec)))
        dobry = set(nar) == oczekiwane
        print('%-45s naruszone %-12s oczekiwane %-8s %s' % (opis, sorted(set(nar)), sorted(oczekiwane),
                                                           'OK' if dobry else '*** BLAD ***'))
        ok = ok and dobry

    print('WYNIK:', 'OK' if ok else 'BLAD')
    sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main()
