# -*- coding: utf-8 -*-
# Uzycie: python test_analysis_k8.py  (kod wyjscia 1 przy bledzie)
# Test niezmiennikow KROKU 8 (42-45) narzedzia analysis_v7.py: log odpalen [PN-FIRED], list gracza w [PN-EXEC]
# (list=, nowychListow=, warianty=, tekstListu= do konca linii) i kolizje cache'u [PN-CACHE].
# Fikstura: plik v9 z test_analysis_v9 (tryb gra z wczytaniem i porzucona galezia, sciezka spozniona, ramiona
# symulatora) przerobiony na krok 8 regulami z kodu moda:
#   - kazda sesja ma linie [PN-CONFIG] srodowisko= (obserwator odpalen dziala), storyteller ma zlozonyList=tak;
#   - [PN-EXEC] dostaje pola listu wedlug statusu i sciezki (StorytellerComp_Generative.ZlozList);
#   - wykonanie (takze spoznione) ma linie [PN-FIRED] pn=1 w ticku DECYZJI (rejestracja przed yield);
#   - dochodza odpalenia wanilii (pn=0, takze cel "world") i kolizje [PN-CACHE] w tickach decyzji.
# Plik poprawny -> 0 naruszen; kazde uszkodzenie -> DOKLADNIE oczekiwany zbior. Tekst listu celowo ma srednik.
import collections, sys

sys.path.insert(0, 'D:/Games/RimWorld/Mods/ProceduralNarrator/Dev/Tools')
import test_analysis_v7 as t7
import test_analysis_v8 as t8
import test_analysis_v9 as t9

SRODOWISKO = ('[PN-CONFIG] srodowisko=gra; mody=ludeon.rimworld,luis.proceduralnarrator; obceMody=-; jezyk=English; '
              'wersjaGry=1.5.4063 rev1072; izolacjaCache=tak')
TEKST = 'Wroga grupa zbrojna uderza; na kolonie pod oslona nocy.'


def fired(run, tick, mapa, incydent, pn, narrator='PN_GenerativeNarrator', cel=None, kategoria='ThreatBig'):
    domowa = mapa != '-1'
    kontekst = '-' if not domowa else ('przed' if tick % 1000 == 0 else 'po')
    ctx = ('kolonisci=3; kolonisciNaMapie=3; powaleni=0; zagrozenie=0; bogactwo=12000; bogactwoWzgl=1.100; punkty=250.0'
           if domowa else 'kolonisci=; kolonisciNaMapie=; powaleni=; zagrozenie=; bogactwo=; bogactwoWzgl=; punkty=')
    return ('[PN-FIRED] runId=%s; narrator=%s; tick=%d; dzien=%.3f; cel=%s; mapa=%s; dom=%s; incydent=%s; kategoria=%s; '
            'pn=%d; kontekst=%s; %s; opoznienie=0'
            % (run, narrator, tick, tick / 60000.0, cel or ('map:' + mapa), mapa, 'true' if domowa else 'false',
               incydent, kategoria, pn, kontekst, ctx))


def ustaw_exec(l, k, v):
    """ustaw() z zachowaniem tekstListu na koncu (tekst ma srednik - kv by go pocial)."""
    glowa, tekst = (l.split('; tekstListu=', 1) + [None])[:2]
    wynik = t8.ustaw(glowa, k, v)
    return wynik + ('' if tekst is None else '; tekstListu=' + tekst)


def na_k8(linie, obserwator=True):
    wynik, s = [], collections.Counter()
    run = t8.pole(next(l for l in linie if l.startswith('[PN-DATA] ')), 'runId')
    cache_dodane = 0
    for l in linie:
        if l.startswith('[PN-CONFIG] storyteller='):
            # Krok 8: list gracza domyslnie wlaczony (K8-4) - stare linie fikstury maja zlozonyList=nie.
            l = l.replace('zlozonyList=nie', 'zlozonyList=tak') if 'zlozonyList=' in l else l + ' | zlozonyList=tak'
        if l.startswith('[PN-EXEC] '):
            st, tick = t8.pole(l, 'status'), t8.pole(l, 'tick')
            td = t8.pole(l, 'tickDecyzji') or tick
            if td != tick:
                li, war, tekst, nowych = 'pozno', '-', '', 0
            elif st == 'wykonane':
                li, war, tekst, nowych = 'dopisany', 'PN_Aktor_Piraci:baza,PN_Akcja_Napad:noc', TEKST, 1
            elif st == 'symulacja':
                li, war, tekst, nowych = 'symulacja', 'PN_Akcja_Napad:z1', 'Tekst symulacji.', 0
            else:
                li, war, tekst, nowych = 'niewykonane', '-', '', 0
            for k, v in (('list', li), ('nowychListow', str(nowych)), ('warianty', war)):
                l = t8.ustaw(l, k, v)
            wynik.append(l + '; tekstListu=' + tekst)
            s['exec ' + li] += 1
            if obserwator and t8.pole(l, 'tryb') == 'gra' and st in ('wykonane', 'pozno-wykonane'):
                wynik.append(fired(t8.pole(l, 'runId'), int(td), t8.pole(l, 'mapa'), t8.pole(l, 'incydent'), 1))
                s['fired pn=1'] += 1
            continue
        wynik.append(l)
        if l.startswith('[PN-DATA-COLS]') and obserwator:
            wynik.append(SRODOWISKO)
        if obserwator and l.startswith('[PN-DATA] ') and t8.pole(l, 'tryb') == 'gra':
            tick, mapa = int(t8.pole(l, 'tick')), t8.pole(l, 'mapa')
            if cache_dodane < 2:
                wynik.append('[PN-CACHE] runId=%s; tryb=gra; eksperyment=; tick=%d; mapa=%s; incydent=RaidEnemy; '
                             'werdyktGry=false; nasz=true; rozny=true' % (run, tick, mapa))
                cache_dodane += 1
                s['cache'] += 1
            if s['fired pn=0'] < 2:
                wynik.append(fired(run, tick + 1, mapa, 'TraderCaravanArrival', 0, kategoria='FactionArrival'))
                wynik.append(fired(run, tick + 2, '-1', 'GiveQuest_Random', 0, cel='world', kategoria='GiveQuest'))
                s['fired pn=0'] += 2
    return wynik, s


RUN = 'gal8'
STORY = '[PN-CONFIG] storyteller=PN_GenerativeNarrator; wersja=test | zlozonyList=tak'


def ex(tick, status, lista, warianty, tekst, nowych, tick_decyzji=None, nr=1):
    return ('[PN-EXEC] runId=%s; tryb=gra; eksperyment=; tick=%d; mapa=0; decyzjaNr=%d; incydent=RaidEnemy; klucz=-; '
            'status=%s; ostatniPrzed=-1; ostatniPo=%d; frakcja=-; frakcjaZwiazana=-; frakcjaZrodlo=-; tickDecyzji=%d; '
            'list=%s; nowychListow=%d; warianty=%s; tekstListu=%s'
            % (RUN, tick, nr, status, tick, tick if tick_decyzji is None else tick_decyzji, lista, nowych,
               warianty, tekst))


def galezie():
    """Minimalna fikstura GALEZI 42-45 (przeglad S10: 15 z 16 regresji analizatora przechodzilo, bo fikstura v9 nie ma
    statusow niewykonane / niejednoznaczne / pozno-niewykonane ani drugiej sesji). Bez wierszy [PN-DATA], lukow
    i faktow - stare niezmienniki (25, 26, 30) iteruja po nich, wiec zostaja puste. Wzorzec z kodu moda:
    ZlozList (status -> list), rejestracja pn=1 przed yield (takze sciezka spozniona), brak pn=1 po NotExecuted."""
    W = 'PN_Aktor_Piraci:frakcja,PN_Akcja_Napad:z1'
    L = ['[PN-SESSION] start=test1', STORY, SRODOWISKO,
         # Kolizja z galezi porzuconej: tick 5000 > tick wczytania 3000, linia PRZED [PN-LOAD] -> przycieta.
         '[PN-CACHE] runId=%s; tryb=gra; eksperyment=; tick=5000; mapa=0; incydent=RaidEnemy; werdyktGry=false; '
         'nasz=true; rozny=true' % RUN,
         '[PN-LOAD] runId=%s; zrodlo=zapis; profil=PN_Profil_Zrownowazony; map=1; wpisow=0; decyzji=0; odrzuconych=0; '
         'narrator=PN_GenerativeNarrator; tick=3000; dzien=0.050; mapy=0:0; luki=; fakty=; styl=' % RUN,
         ex(60000, 'wykonane', 'dopisany', W, TEKST, 1), fired(RUN, 60000, '0', 'RaidEnemy', 1),
         fired(RUN, 60500, '-1', 'GiveQuest_Random', 0, cel='world', kategoria='GiveQuest'),
         ex(61000, 'niewykonane', 'niewykonane', '-', '', 0),
         ex(62000, 'niejednoznaczne', 'niewykonane', '-', '', 0), fired(RUN, 62000, '0', 'RaidEnemy', 1),
         ex(64000, 'pozno-wykonane', 'pozno', '-', '', 0, tick_decyzji=63000), fired(RUN, 63000, '0', 'RaidEnemy', 1),
         ex(66000, 'pozno-niewykonane', 'pozno', '-', '', 0, tick_decyzji=65000),
         ex(67000, 'wykonane', 'brak', W, 'Tekst bez listu.', 0), fired(RUN, 67000, '0', 'RaidEnemy', 1),
         ex(68000, 'wykonane', 'blad', '-', '', 0), fired(RUN, 68000, '0', 'RaidEnemy', 1),
         # Druga sesja BEZ linii srodowisko= - obserwatora nie bylo, wykonanie bez [PN-FIRED] jest poprawne.
         '[PN-SESSION] start=test2', STORY,
         ex(70000, 'wykonane', 'dopisany', W, TEKST, 1, nr=9)]
    return L


def main():
    ok = True
    baza, _ = t9.zbuduj()
    wzorzec, s = na_k8(baza)
    for nazwa in ['exec dopisany', 'exec symulacja', 'exec pozno', 'fired pn=1', 'fired pn=0', 'cache']:
        print('STRAZNIK: %-20s %d' % (nazwa, s[nazwa]))
        ok = ok and s[nazwa] > 0
    # Porzucona galaz musi objac tez [PN-FIRED]: fikstura v9 ma wykonania w galezi porzuconej.
    kod, nar, out = t7.uruchom('k8_poprawny', wzorzec)
    print('POPRAWNY k8: kod %d, naruszone %s' % (kod, nar))
    if kod != 0 or nar:
        print(out[:4000])
    ok = ok and kod == 0 and not nar
    # tekstListu jest OSTATNIM polem i biegnie do konca linii - tekst ze srednikiem musi dojsc caly.
    caly = ('  przyklad tekstu: ' + TEKST) in out
    print('TEKST LISTU ZE SREDNIKIEM PRZECZYTANY W CALOSCI:', 'OK' if caly else '*** NIE ***')
    ok = ok and caly

    # Stary plik (bez obserwatora w sesji): wykonania bez [PN-FIRED] nie sa naruszeniem 42.
    stary, _ = na_k8(baza, obserwator=False)
    kod, nar, _ = t7.uruchom('k8_bez_obserwatora', stary)
    print('BEZ OBSERWATORA: kod %d, naruszone %s' % (kod, nar))
    ok = ok and kod == 0 and not nar

    def pierwszy(L, warunek):
        return next(i for i, l in enumerate(L) if warunek(l))

    def usun_fired(L):
        i = pierwszy(L, lambda l: l.startswith('[PN-FIRED] ') and t8.pole(l, 'pn') == '1')
        return L[:i] + L[i + 1:]

    def fired_obcy_jako_nasz(L):
        i = pierwszy(L, lambda l: l.startswith('[PN-FIRED] ') and t8.pole(l, 'incydent') == 'TraderCaravanArrival')
        L[i] = t8.ustaw(L[i], 'pn', '1')
        return L

    def przed_poza_interwalem(L):
        i = pierwszy(L, lambda l: l.startswith('[PN-FIRED] ') and t8.pole(l, 'mapa') != '-1'
                     and int(t8.pole(l, 'tick')) % 1000 != 0)
        L[i] = t8.ustaw(L[i], 'kontekst', 'przed')
        return L

    def powaleni_ponad(L):
        i = pierwszy(L, lambda l: l.startswith('[PN-FIRED] ') and t8.pole(l, 'mapa') != '-1')
        L[i] = t8.ustaw(L[i], 'powaleni', '4')
        return L

    def brak_w_symulatorze(L):
        # 'brak', a nie 'dopisany': dopisany bez nowego listu lapie inna regula 44 i przypadek nie odroznilby
        # reguly symulatora od tamtej (uprzaz: regresja '44 symulator dowolny' przechodzila).
        i = pierwszy(L, lambda l: l.startswith('[PN-EXEC] ') and t8.pole(l, 'list') == 'symulacja')
        L[i] = ustaw_exec(L[i], 'list', 'brak')
        return L

    def dopisany_na_sciezce_spoznionej(L):
        i = pierwszy(L, lambda l: l.startswith('[PN-EXEC] ') and t8.pole(l, 'list') == 'pozno')
        L[i] = ustaw_exec(L[i], 'list', 'brak')
        return L

    def bez_wariantow(L):
        i = pierwszy(L, lambda l: l.startswith('[PN-EXEC] ') and t8.pole(l, 'list') == 'dopisany')
        L[i] = ustaw_exec(L[i], 'warianty', '-')
        return L

    def wylaczony_przy_wlaczonym(L):
        i = pierwszy(L, lambda l: l.startswith('[PN-EXEC] ') and t8.pole(l, 'list') == 'dopisany')
        L[i] = ustaw_exec(L[i], 'list', 'wylaczony')
        return L

    def cache_rozny_klamie(L):
        i = pierwszy(L, lambda l: l.startswith('[PN-CACHE] '))
        L[i] = t8.ustaw(L[i], 'rozny', 'false')
        return L

    def cache_poza_decyzja(L):
        i = pierwszy(L, lambda l: l.startswith('[PN-CACHE] '))
        L[i] = t8.ustaw(L[i], 'tick', str(int(t8.pole(L[i], 'tick')) + 7))
        return L

    przypadki = [
        ('brak linii pn=1 przy wykonaniu', usun_fired, {'42'}),
        ('odpalenie wanilii oznaczone jako nasze', fired_obcy_jako_nasz, {'42'}),
        ('kontekst=przed poza tickiem interwalu', przed_poza_interwalem, {'43'}),
        ('powaleni > kolonisci na mapie', powaleni_ponad, {'43'}),
        ('list=brak w symulatorze', brak_w_symulatorze, {'44'}),
        ('list=brak na sciezce spoznionej', dopisany_na_sciezce_spoznionej, {'44'}),
        ('wykonane bez sladu wariantow', bez_wariantow, {'44'}),
        ('list=wylaczony przy zlozonyList=tak', wylaczony_przy_wlaczonym, {'44'}),
        ('[PN-CACHE] rozny niespojne', cache_rozny_klamie, {'45'}),
        ('[PN-CACHE] poza tickiem decyzji', cache_poza_decyzja, {'45'}),
    ]
    for opis, psuj, oczekiwane in przypadki:
        nazwa = 'k8_' + ''.join(c if c.isalnum() else '_' for c in opis)
        kod, nar, out = t7.uruchom(nazwa, psuj(list(wzorzec)))
        dobry = set(nar) == oczekiwane
        print('%-45s naruszone %-12s oczekiwane %-8s %s' % (opis, sorted(set(nar)), sorted(oczekiwane),
                                                           'OK' if dobry else '*** BLAD ***'))
        ok = ok and dobry
    # ---- Fikstura galezi (przeglad S10): kazda regula 42-45 osobno.
    G = galezie()
    kod, nar, out = t7.uruchom('k8_galezie', G)
    print('GALEZIE POPRAWNE: kod %d, naruszone %s' % (kod, nar))
    if kod != 0 or nar:
        print(out[:3000])
    ok = ok and kod == 0 and not nar

    def g_zmien(warunek, k, v):
        def f(L):
            i = pierwszy(L, warunek)
            L[i] = ustaw_exec(L[i], k, v) if L[i].startswith('[PN-EXEC] ') else t8.ustaw(L[i], k, v)
            return L
        return f

    def g_exec(status, lista=None):
        return lambda l: l.startswith('[PN-EXEC] ') and t8.pole(l, 'status') == status \
            and (lista is None or t8.pole(l, 'list') == lista)

    def g_fired(tick):
        return lambda l: l.startswith('[PN-FIRED] ') and t8.pole(l, 'tick') == str(tick)

    def g_dodaj(linia):
        return lambda L: L + [linia]

    def g_usun(warunek):
        def f(L):
            i = pierwszy(L, warunek)
            return L[:i] + L[i + 1:]
        return f

    przypadki_g = [
        ('niewykonane z list=brak', g_zmien(g_exec('niewykonane'), 'list', 'brak'), {'44'}),
        ('wykonane z list spoza slownika', g_zmien(g_exec('wykonane', 'brak'), 'list', 'zly'), {'44'}),
        ('dopisany bez nowego listu', g_zmien(g_exec('wykonane', 'dopisany'), 'nowychListow', '0'), {'44'}),
        ('warianty bez dwukropka', g_zmien(g_exec('wykonane', 'dopisany'), 'warianty', 'PN_Akcja_Napad'), {'44'}),
        ('blad z wypelnionym sladem', g_zmien(g_exec('wykonane', 'blad'), 'warianty', 'PN_Akcja_Napad:z1'), {'44'}),
        ('zdublowane pn=1 wykonania', g_dodaj(fired(RUN, 60000, '0', 'RaidEnemy', 1)), {'42'}),
        ('pn=1 przy niewykonanym', g_dodaj(fired(RUN, 61000, '0', 'RaidEnemy', 1)), {'42'}),
        ('pn=1 przy pozno-niewykonanym', g_dodaj(fired(RUN, 65000, '0', 'RaidEnemy', 1)), {'42'}),
        ('spoznione bez pn=1', g_usun(g_fired(63000)), {'42'}),
        ('spoznione z dwoma pn=1', g_dodaj(fired(RUN, 63000, '0', 'RaidEnemy', 1)), {'42'}),
        ('kontekst - na mapie', g_zmien(g_fired(60000), 'kontekst', '-'), {'43'}),
        ('zagrozenie spoza 0/1', g_zmien(g_fired(60000), 'zagrozenie', '2'), {'43'}),
        ('opoznienie > 0', g_zmien(g_fired(60000), 'opoznienie', '5'), {'43'}),
        ('pn=1 u Cassandry', g_zmien(g_fired(60000), 'narrator', 'Cassandra'), {'43'}),
        ('dzien != tick/60000', g_zmien(g_fired(60000), 'dzien', '9.000'), {'43'}),
        ('kontekst spoza slownika', g_zmien(g_fired(60000), 'kontekst', 'wczoraj'), {'43'}),
        ('swiat z dom=true', g_zmien(g_fired(60500), 'dom', 'true'), {'43'}),
        ('cel innej mapy', g_zmien(g_fired(60000), 'cel', 'map:7'), {'43'}),
    ]
    for opis, psuj, oczekiwane in przypadki_g:
        nazwa = 'k8g_' + ''.join(c if c.isalnum() else '_' for c in opis)
        kod, nar, out = t7.uruchom(nazwa, psuj(list(G)))
        dobry = set(nar) == oczekiwane
        print('GALEZ %-40s naruszone %-12s oczekiwane %-8s %s' % (opis, sorted(set(nar)), sorted(oczekiwane),
                                                                  'OK' if dobry else '*** BLAD ***'))
        ok = ok and dobry

    print('WYNIK:', 'OK' if ok else 'BLAD')
    sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main()
