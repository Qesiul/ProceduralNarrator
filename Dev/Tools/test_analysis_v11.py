# -*- coding: utf-8 -*-
# Uzycie: python test_analysis_v11.py  (kod wyjscia 1 przy bledzie)
# Test formatu v11 (krok 9, etap L) narzedzia analysis_v7.py: niezmienniki 47-54 oraz bramki wersji (wiersz v11 przechodzi
# reguly v7-v10). Fikstura: plik v10 z test_analysis_v10 przerobiony regulami z kodu moda (etap L):
#   - [PN-DATA]: lustroOdcina i czasMs po zablokowanychSilnik (EngineMirror.DataColumn: puste przy liczniku pustym, "-"
#     przy liczniku 0 albo lista ordynalna nie krotsza niz licznik); czasMs >= 0;
#   - po kazdej linii [PN-GRACZ] gry (granica doby): [PN-DZIEN] mapy 0 i [PN-PERF] z decyzji = wiersze gry od poprzedniej
#     linii [PN-PERF] albo [PN-LOAD] (PerfMonitor startuje od nowa przy wczytaniu);
#   - po kazdym [PN-LOAD] gry kotwica [PN-KOLONISTA] start, potem ubytek i dolaczenie;
#   - [PN-FIRED]: pora, noc, bogactwoWiek, frakcja (napady), listow, listyWspolne, listy (ostatnie pole);
#   - akcja wymuszona: [PN-EXEC] wymuszone=1 + [PN-FIRED] pn=1 kontekst=po w ticku spoza siatki;
#   - na koncu gra ewaluacyjna: [PN-EVAL] z nowym runId, kotwica skladu i pierwsza decyzja (kopia pierwszej decyzji gry).
# Plik poprawny -> 0 naruszen; kazde uszkodzenie -> DOKLADNIE oczekiwany zbior.
import collections, sys

sys.path.insert(0, 'D:/Games/RimWorld/Mods/ProceduralNarrator/Dev/Tools')
import test_analysis_v7 as t7
import test_analysis_v8 as t8
import test_analysis_v9 as t9
import test_analysis_k8 as tk8
import test_analysis_v10 as t10

NOWY_RUN = 'L2-PN_GenerativeNarrator-0badf00d'
PERF_BEZ_KUBELKOW = ('odpalen', 'styl', 'doba', 'kolonistow', 'interwalow', 'potwierdzen')


def kv_linii(l):
    return t7.kv(l.split('] ', 1)[1])


def fired_v11(l, dwa_listy=False):
    d = kv_linii(l)
    mapa = d.get('mapa') != '-1'
    napad = d.get('incydent') == 'RaidEnemy'
    # Etykieta ze srednikiem i znakiem rownosci: listy= musi byc czytane do konca linii, nie jako zwykle pole.
    etykiety = ['Napad; piraci=12', 'Drugi list'] if dwa_listy else (['List ' + d.get('incydent')] if mapa else [])
    linia = (l + '; pora=' + ('2' if mapa else '') + '; noc=' + ('0' if mapa else '')
             + '; bogactwoWiek=' + ('150' if d.get('bogactwo', '') != '' else '')
             + '; frakcja=' + ('12' if napad else '-') + '; frakcjaDef=' + ('Pirate' if napad else '-')
             + '; wymuszone=0; listow=' + str(len(etykiety)) + '; listyWspolne=nie'
             + '; listy=' + ' | '.join(etykiety))
    # [PN-LIST] dla kazdego listu tego odpalenia (tresc ze srednikiem i znakiem rownosci - czytana do konca linii).
    return [linia] + [lista_linia(d.get('runId'), int(d.get('tick')), d.get('mapa'), d.get('incydent'), '0', e,
                                  'Tekst listu; %s=szablon <b>%d</b>.' % (d.get('incydent'), i))
                      for i, e in enumerate(etykiety)]


def blok_wymuszony(run, tw, nr, inc, akcja, tytul):
    """[PN-EXEC], [PN-FIRED] i [PN-LIST] jednej akcji 'PN: wymus akcje' z numerem nr (jak LogForced)."""
    return [
        '[PN-EXEC] runId=%s; tryb=gra; eksperyment=; tick=%d; mapa=0; decyzjaNr=1; incydent=%s; '
        'klucz=-|PN_Aktor_Natura|%s|PN_Cel_Osada|-|PN_Kons_Pogoda; status=wykonane; ostatniPrzed=-1; ostatniPo=%d; '
        'frakcja=-; frakcjaZwiazana=-; frakcjaZrodlo=-; tickDecyzji=%d; list=dopisany; nowychListow=1; warianty=%s:z1; '
        'wymuszone=%d; tekstListu=Natura sprowadza na okolice cos.' % (run, tw, inc, akcja, tw, tw, akcja, nr),
        '[PN-FIRED] runId=%s; narrator=PN_GenerativeNarrator; tick=%d; dzien=%.3f; cel=map:0; mapa=0; dom=true; '
        'incydent=%s; kategoria=ThreatSmall; pn=1; kontekst=po; kolonisci=3; kolonisciNaMapie=3; powaleni=0; zagrozenie=0; '
        'bogactwo=; bogactwoWzgl=; punkty=; opoznienie=0; pora=2; noc=0; bogactwoWiek=; frakcja=-; frakcjaDef=-; '
        'wymuszone=%d; listow=1; listyWspolne=nie; listy=%s' % (run, tw, tw / 60000.0, inc, nr, tytul),
        lista_linia(run, tw, '0', inc, str(nr), tytul, 'Natura sprowadza na okolice cos.')]


def lista_linia(run, tick, mapa, incydenty, wymuszone, tytul, tresc):
    return ('[PN-LIST] runId=%s; narrator=PN_GenerativeNarrator; tick=%d; dzien=%.3f; mapa=%s; incydenty=%s; wymuszone=%s; '
            'typ=NegativeEvent; frakcja=-; pionki=Jan Kowalski | Jan; tytul=%s; tresc=%s'
            % (run, tick, tick / 60000.0, mapa, incydenty, wymuszone, tytul.replace(';', ','), tresc))


def perf(run, tick, decyzji):
    tik = 60000

    def grupa(p, n, suma, mx, jednostka, granice=None):
        s = '; %s=%d; %sSuma%s=%s; %sMax%s=%s' % (p, n, p, jednostka, suma, p, jednostka, mx)
        if granice is not None:
            kub = [0] * (len(granice) + 1)
            kub[1] = n
            s += '; %sKubelki=%s; %sGranice=%s' % (p, '/'.join(map(str, kub)), p, '/'.join(granice))
        return s
    linia = '[PN-PERF] runId=%s; narrator=PN_GenerativeNarrator; tick=%d; dzien=%d' % (run, tick, tick // 60000 - 1)
    linia += grupa('tickow', tik, '600000', '40', 'Us', ['10', '50', '100', '500', '1000'])
    for p in PERF_BEZ_KUBELKOW[:4]:
        linia += grupa(p, tik, '150000', '10', 'Us')
    linia += grupa('interwalow', 60, '6000', '200', 'Us')
    linia += grupa('decyzji', decyzji, '%.3f' % (0.8 * decyzji), '0.8' if decyzji else '0', 'Ms',
                   ['0.5', '1', '2', '5', '10', '50'])
    linia += grupa('potwierdzen', decyzji, '%d' % (50 * decyzji), '50' if decyzji else '0', 'Us')
    return linia


def dzien(run, tick):
    # Liczniki gry rosna z czasem gry: po wczytaniu wracaja do stanu zapisu, nie do zera.
    liczniki = tick // 600000
    return ('[PN-DZIEN] runId=%s; narrator=PN_GenerativeNarrator; tick=%d; dzien=%d; mapa=0; kolonisci=3; '
            'kolonisciNaMapie=3; powaleni=0; zagrozenie=0; bogactwo=14000; bogactwoWiek=1200; bogactwoWzgl=0.900; '
            'punkty=35.0; pora=1; temperatura=12.5; warunki=-; monolit=0; monolitDef=Inactive; zywnosc=120.5; '
            'nastroj=0.550; adaptacja=3.000; napadow=%d; threatBig=%d; poleglych=0' % (run, tick, tick // 60000 - 1,
                                                                                       liczniki, liczniki))


def kolonista(run, tick, zd, pionek, n):
    return ('[PN-KOLONISTA] runId=%s; narrator=PN_GenerativeNarrator; tick=%d; dzien=%.3f; zdarzenie=%s; pionek=%d; '
            'mapa=0; liczebnosc=%d' % (run, tick, tick / 60000.0, zd, pionek, n))


def stan(run, tick, zd, nm, pw, z, mapa=0):
    # Predykaty z liczb jak RuleStateTracker.Of - niezaleznie od analizatora.
    kryzys = 1 if nm >= 1 and pw >= 1 and 2 * pw >= nm else 0
    return ('[PN-STAN] runId=%s; narrator=PN_GenerativeNarrator; tick=%d; dzien=%.3f; mapa=%d; zdarzenie=%s; '
            'kolonisciNaMapie=%d; powaleni=%d; zagrozenie=%d; kryzys=%d; pusta=%d'
            % (run, tick, tick / 60000.0, mapa, zd, nm, pw, z, kryzys, 1 if nm == 0 else 0))


def stan_koniec(run, tick, mapa):
    return ('[PN-STAN] runId=%s; narrator=PN_GenerativeNarrator; tick=%d; dzien=%.3f; mapa=%d; zdarzenie=koniec; '
            'kolonisciNaMapie=; powaleni=; zagrozenie=; kryzys=; pusta=' % (run, tick, tick / 60000.0, mapa))


def na_v11(linie):
    wynik, s = [], collections.Counter()
    zakotwiczone = set()
    nr = 0
    odPerf = 0
    graczy = 0
    run = None
    pierwsza = None
    wymuszona = False
    widziane_perf = set()
    for l in linie:
        if l.startswith('[PN-DATA-COLS]'):
            cols = l.split('kolumny=')[1].split(',')
            j = cols.index('zablokowanychSilnik') + 1
            cols = cols[:j] + ['lustroOdcina', 'czasMs'] + cols[j:]
            wynik.append('[PN-DATA-COLS] wersja=11; kolumny=' + ','.join(cols))
            continue
        if l.startswith('[PN-SESSION]'):
            odPerf = 0
            zakotwiczone.clear()
            wynik.append(l.replace('wersjaLogu=10', 'wersjaLogu=11'))
            continue
        if l.startswith('[PN-DATA] '):
            w = t7.kv(l[len('[PN-DATA] '):])
            zb = w.get('zablokowanychSilnik', '')
            if zb == '':
                lo = ''
                s['lustro nieaktywne'] += 1
            elif zb == '0' and nr % 2 == 0:
                lo = '-'
                s['lustro nic'] += 1
            else:
                lo = ','.join(sorted('Payload%02d' % i for i in range(int(zb) + 1)))
                s['lustro lista'] += 1
            w2 = collections.OrderedDict()
            for a, b in w.items():
                w2[a] = '11' if a == 'wersjaLogu' else b
                if a == 'zablokowanychSilnik':
                    w2['lustroOdcina'], w2['czasMs'] = lo, '%.3f' % (0.4 + (nr % 7) * 0.3)
            nr += 1
            linia = '[PN-DATA] ' + '; '.join('%s=%s' % p for p in w2.items())
            wynik.append(linia)
            if w.get('tryb') == 'gra':
                odPerf += 1
                if pierwsza is None:
                    pierwsza = linia
            continue
        if l.startswith('[PN-FIRED] '):
            dwa = 'incydent=RaidEnemy;' in l and s['dwa listy'] == 0
            nowe_ = fired_v11(l, dwa)
            wynik.extend(nowe_)
            s['fired v11'] += 1
            s['dwa listy'] += 1 if dwa else 0
            s['linie [PN-LIST]'] += len(nowe_) - 1
            continue
        wynik.append(l)
        if l.startswith('[PN-LOAD] '):
            d = kv_linii(l)
            run = d.get('runId')
            if odPerf > 0:
                s['wiersze przed LOAD bez perf'] += 1
            odPerf = 0
            graczy = 0
            wynik.append(kolonista(run, int(d.get('tick')) + 1, 'start', -1, 3))
            s['kotwica'] += 1
            # [PN-STAN] kotwiczy w pierwszym ticku po wczytaniu - poza siatka 250 (dozwolone tylko dla start).
            # Linie stanu z tickiem pozniejszym niz wczytanie = porzucona galaz (bez przyciecia tick cofa sie).
            s['stan w porzuconej galezi'] += sum(1 for x in wynik if x.startswith('[PN-STAN] ') and 'runId=%s;' % run in x
                                                  and int(t8.pole(x, 'tick')) > int(d.get('tick')))
            wynik.append(stan(run, int(d.get('tick')) + 1, 'start', 3, 0, 0))
            zakotwiczone.add(run)
            s['stan start'] += 1
        elif l.startswith('[PN-GRACZ] ') and 'tryb=gra' in l:
            d = kv_linii(l)
            t_ = int(d.get('tick'))
            graczy += 1
            if d.get('runId') not in zakotwiczone:
                wynik.append(stan(d.get('runId'), t_, 'start', 3, 0, 0))
                zakotwiczone.add(d.get('runId'))
                s['stan start'] += 1
            wynik.append(dzien(d.get('runId'), t_))
            s['doby'] += 1
            # Co dobe napad: zagrozenie od +250, na drugiej dobie segmentu kryzys w +500 (dokladnie polowa: 1 z 2 na
            # mapie), spokoj od +750 - stan w granicy nastepnej doby znow zgodny z [PN-DZIEN] (zagrozenie=0, 3 na mapie).
            wynik.append(stan(d.get('runId'), t_ + 250, 'zmiana', 3, 0, 1))
            if graczy == 2:
                wynik.append(stan(d.get('runId'), t_ + 500, 'zmiana', 2, 1, 1))
                s['stan kryzys'] += 1
            wynik.append(stan(d.get('runId'), t_ + 750, 'zmiana', 3, 0, 0))
            s['stan zmiana'] += 2
            # [PN-PERF] co druga dobe (regula jest pozycyjna - wiersze miedzy liniami czasu, takze tuz przed wczytaniem).
            # Parzystosc DOBY (nie licznika), zeby doby porzuconej galezi wracaly po wczytaniu - przyciecie jest wtedy widoczne.
            if (t_ // 60000) % 2 == 1:
                if (d.get('runId'), t_) in widziane_perf:
                    s['perf tej samej doby po wczytaniu'] += 1
                widziane_perf.add((d.get('runId'), t_))
                wynik.append(perf(d.get('runId'), t_, odPerf))
                s['perf z decyzjami' if odPerf else 'perf bez decyzji'] += 1
                odPerf = 0
            if graczy == 2:
                wynik.append(kolonista(d.get('runId'), t_ + 250, 'zginal', 77, 2))
                wynik.append(kolonista(d.get('runId'), t_ + 500, 'dolaczyl', 78, 3))
                s['zmiany skladu'] += 2
                if not wymuszona:
                    wymuszona = True
                    tw = t_ + 500
                    # Trzy akcje wymuszone w JEDNEJ pauzie (ten sam tick): numery 1, 2, 3; 1 i 3 z tym samym incydentem.
                    for nr, inc, akcja, tyt in ((1, 'Flashstorm', 'PN_Akcja_Burza', 'Burza'),
                                                (2, 'ToxicFallout', 'PN_Akcja_Opad', 'Opad'),
                                                (3, 'Flashstorm', 'PN_Akcja_Burza', 'Burza')):
                        wynik.extend(blok_wymuszony(d.get('runId'), tw, nr, inc, akcja, tyt))
                    # List bez odpalenia w ticku (pozniejszy, zadanie) - incydenty=-.
                    wynik.append(lista_linia(d.get('runId'), t_ + 700, '-1', '-', '0', 'Oferta zadania', 'Ktos prosi o pomoc.'))
                    s['wymuszona'] += 1
                    s['list bez odpalenia'] += 1
    # Gra ewaluacyjna W TEJ SAMEJ SESJI co gra (przed nastepna linia [PN-SESSION] - konfiguracja profili jest per sesja):
    # nowy runId, kotwica, kopia pierwszej decyzji gry z jej [PN-EXEC] i [PN-FIRED].
    d0 = t7.kv(pierwsza[len('[PN-DATA] '):])
    run0, tick0 = d0.get('runId'), d0.get('tick')
    segment = ['[PN-EVAL] runIdPoprzedni=%s; runId=%s; etykieta=L2; narrator=PN_GenerativeNarrator; '
               'profil=PN_Profil_Zrownowazony; profilWymuszony=tak; tick=%s; dzien=1.000; trudnosc=Rough; mapy=0:9; '
               'luki=; fakty=; styl=dni:3,dzien:4,aktywny:0,mocne:,etykieta:' % (run0, NOWY_RUN, tick0),
               kolonista(NOWY_RUN, int(tick0) + 1, 'start', -1, 3),
               stan(NOWY_RUN, int(tick0) + 1, 'start', 3, 0, 0),
               # Druga mapa domowa porzucona na najblizszej siatce: koniec z pustymi polami.
               stan(NOWY_RUN, int(tick0) + 1, 'start', 0, 0, 0, mapa=5),
               stan_koniec(NOWY_RUN, (int(tick0) // 250 + 1) * 250, 5)]
    s['stan koniec'] += 1
    kopia = [x for x in wynik if (x.startswith('[PN-EXEC] ') or x.startswith('[PN-FIRED] ') or x.startswith('[PN-LIST] '))
             and 'runId=%s;' % run0 in x and 'tick=%s;' % tick0 in x and 'mapa=0;' in x and 'wymuszone=1' not in x]
    segment.append(t8.ustaw(t8.ustaw(pierwsza, 'runId', NOWY_RUN), 'stylDni', '0'))
    for x in kopia:
        segment.append(x.replace('runId=%s;' % run0, 'runId=%s;' % NOWY_RUN, 1))
    # Pierwsza zamknieta doba stylu nowej gry: dni=1 po kotwicy dni=0 z [PN-EVAL] (regula 40).
    gracz0 = next(x for x in wynik if x.startswith('[PN-GRACZ] ') and 'tryb=gra' in x)
    segment.append(t8.ustaw(t8.ustaw(gracz0, 'runId', NOWY_RUN), 'dni', '1'))
    # [PN-PERF] nowej gry: start gry ewaluacyjnej zeruje pomiar, wiec liczy tylko decyzje od [PN-EVAL] (tu 1).
    segment.append(perf(NOWY_RUN, 540000, 1))
    ip = wynik.index(pierwsza)
    koniec_sesji = next((i for i in range(ip, len(wynik)) if wynik[i].startswith('[PN-SESSION]')), len(wynik))
    # Wiersze gry miedzy ostatnia linia [PN-PERF]/[PN-LOAD] a [PN-EVAL] - bez nich brak zerowania bylby niewidoczny.
    for x in reversed(wynik[:koniec_sesji]):
        if x.startswith('[PN-PERF]') or x.startswith('[PN-LOAD]'):
            break
        if x.startswith('[PN-DATA] ') and 'tryb=gra' in x:
            s['wiersze przed EVAL bez perf'] += 1
    wynik[koniec_sesji:koniec_sesji] = segment
    s['gra ewaluacyjna'] += 1
    s['kopia exec+fired'] += len(kopia)
    return wynik, s


def main():
    ok = True
    baza, _ = t9.zbuduj()
    k8, _ = tk8.na_k8(baza)
    v10, _ = t10.na_v10(k8)
    wzorzec, s = na_v11(v10)
    for nazwa in ['lustro nieaktywne', 'lustro nic', 'lustro lista', 'fired v11', 'dwa listy', 'kotwica', 'doby', 'perf z decyzjami',
                  'perf bez decyzji', 'wiersze przed LOAD bez perf', 'zmiany skladu', 'wymuszona', 'gra ewaluacyjna',
                  'linie [PN-LIST]', 'list bez odpalenia', 'perf tej samej doby po wczytaniu', 'wiersze przed EVAL bez perf',
                  'stan start', 'stan zmiana', 'stan kryzys', 'stan koniec', 'stan w porzuconej galezi']:
        print('STRAZNIK: %-20s %d' % (nazwa, s[nazwa]))
        ok = ok and s[nazwa] > 0
    print('STRAZNIK: kopia exec+fired+list %d (oczekiwane 3)' % s['kopia exec+fired'])
    ok = ok and s['kopia exec+fired'] == 3
    kod, nar, out = t7.uruchom('v11_poprawny', wzorzec)
    print('POPRAWNY v11: kod %d, naruszone %s' % (kod, nar))
    if kod != 0 or nar:
        print(out[:6000])
    ok = ok and kod == 0 and not nar
    wszystkie = "wersjaLogu: ['11']" in out
    print('WSZYSTKIE WIERSZE v11:', 'OK' if wszystkie else '*** NIE ***')
    ok = ok and wszystkie
    for n in ('47 lustroOdcina', '48 czasMs', '49 [PN-DZIEN]', '50 [PN-KOLONISTA]', '51 [PN-FIRED] v11', '52 [PN-PERF]',
              '53 [PN-EVAL]', '54 [PN-EXEC] wymuszone', '55 [PN-LIST]', '56 [PN-STAN]'):
        wymieniony = n in out
        print('REGULA W TABELI %-24s %s' % (n, 'OK' if wymieniony else '*** NIE ***'))
        ok = ok and wymieniony

    def pierwszy(L, warunek):
        return next(i for i, l in enumerate(L) if warunek(l))

    def typu(prefiks, warunek=lambda l: True):
        return lambda l: l.startswith(prefiks) and warunek(l)

    def zmien(warunek, k, v):
        def f(L):
            i = pierwszy(L, warunek)
            L[i] = t8.ustaw(L[i], k, v)
            return L
        return f

    def gra(l):
        return 'tryb=gra' in l

    def z_lista(l):
        return t8.pole(l, 'lustroOdcina') not in ('', '-', None)

    def z_listem(l):
        return t8.pole(l, 'listow') == '1'

    def usun(warunek):
        def f(L):
            del L[pierwszy(L, warunek)]
            return L
        return f

    def dubel(warunek):
        def f(L):
            i = pierwszy(L, warunek)
            return L[:i + 1] + [L[i]] + L[i + 1:]
        return f

    def plus_int(warunek, k, o):
        def f(L):
            i = pierwszy(L, warunek)
            L[i] = t8.ustaw(L[i], k, str(int(t8.pole(L[i], k)) + o))
            return L
        return f

    def wymuszona_linia(prefiks, nr):
        return lambda l: l.startswith(prefiks) and ('wymuszone=%d;' % nr) in l

    def dwie_z_numerem_1(L):
        for prefiks in ('[PN-EXEC] ', '[PN-FIRED] ', '[PN-LIST] '):
            i = pierwszy(L, wymuszona_linia(prefiks, 2))
            L[i] = t8.ustaw(L[i], 'wymuszone', '1')
        return L

    def bez_odpalenia_3(L):
        for prefiks in ('[PN-FIRED] ', '[PN-LIST] '):
            del L[pierwszy(L, wymuszona_linia(prefiks, 3))]
        return L

    def odpalenie_9_bez_exec(L):
        i = pierwszy(L, wymuszona_linia('[PN-FIRED] ', 1))
        f9 = t8.ustaw(L[i], 'wymuszone', '9')
        l9 = t8.ustaw(L[pierwszy(L, wymuszona_linia('[PN-LIST] ', 1))], 'wymuszone', '9')
        return L[:i + 1] + [f9, l9] + L[i + 1:]

    def odpalenia_list(l):
        return 'wymuszone=0' in l and 'incydenty=-;' not in l

    def pole_obce(L):
        i = pierwszy(L, typu('[PN-LIST] '))
        L[i] = L[i].replace('; tresc=', '; obce=1; tresc=', 1)
        return L

    def decyzji_plus(L):
        # +1 decyzja razem z kubelkiem - lapac ma tylko porownanie z wierszami gry doby, nie suma kubelkow.
        i = pierwszy(L, typu('[PN-PERF] ', lambda l: 'decyzji=0;' not in l))
        kub = [int(v) for v in t8.pole(L[i], 'decyzjiKubelki').split('/')]
        kub[1] += 1
        L[i] = t8.ustaw(t8.ustaw(L[i], 'decyzji', str(int(t8.pole(L[i], 'decyzji')) + 1)),
                        'decyzjiKubelki', '/'.join(map(str, kub)))
        return L

    def listy_zle(L):
        i = pierwszy(L, typu('[PN-FIRED] ', z_listem))
        L[i] = L[i].rsplit('; listy=', 1)[0] + '; listy=A | B'
        return L

    def fakt_z_wymuszonej(L):
        i = pierwszy(L, lambda l: l.startswith('[PN-EXEC] ') and 'wymuszone=1' in l)
        d = kv_linii(L[i])
        f_ = ('[PN-FACT] runId=%s; tryb=gra; eksperyment=; tick=%s; mapa=0; zdarzenie=odrzucenie; klucz=pogoda.zla; '
              'wartosc=; dzien=%.6f; zycie=10; tickZrodla=%s; decyzjaZrodla=1; powod=niewykonane'
              % (d['runId'], d['tick'], int(d['tick']) / 60000.0, d['tick']))
        return L[:i + 1] + [f_] + L[i + 1:]

    def stanu(warunek=lambda l: True):
        return typu('[PN-STAN] ', warunek)

    def kryzys_bez_powalonych(L):
        # Kryzys konczy sie w tej samej linii (powaleni 0, kryzys 0): stan = poprzedni (zagrozenie), wiec zmiana bez zmiany.
        i = pierwszy(L, stanu(lambda l: 'kryzys=1' in l))
        L[i] = t8.ustaw(t8.ustaw(L[i], 'powaleni', '0'), 'kryzys', '0')
        return L

    def wiersz_przed_eval(L):
        i = pierwszy(L, lambda l: l.startswith('[PN-EVAL] '))
        j = pierwszy(L, lambda l: l.startswith('[PN-DATA] ') and NOWY_RUN in l)
        wiersz = L[j]
        return L[:i] + [wiersz] + L[i:j] + L[j + 1:]

    przypadki = [
        ('47 lista pusta przy liczniku', zmien(typu('[PN-DATA] ', z_lista), 'lustroOdcina', ''), {'47'}),
        ('47 lista nieposortowana', zmien(typu('[PN-DATA] ', z_lista), 'lustroOdcina', 'Zeta,Alfa'), {'47'}),
        ('47 KONTROLA: lista zamiast "-" przy liczniku 0',
         zmien(typu('[PN-DATA] ', lambda l: t8.pole(l, 'lustroOdcina') == '-'), 'lustroOdcina', 'A'), set()),
        ('47 "-" przy niezerowym liczniku', zmien(typu('[PN-DATA] ', lambda l: t8.pole(l, 'lustroOdcina') == '-'),
                                                  'zablokowanychSilnik', '2'), {'47'}),
        ('47 licznik wiekszy niz lista',
         zmien(typu('[PN-DATA] ', lambda l: z_lista(l) and int(t8.pole(l, 'zablokowanychSilnik')) >= 2),
               'lustroOdcina', 'Payload00'), {'47'}),
        ('48 czasMs pusty', zmien(typu('[PN-DATA] '), 'czasMs', ''), {'48'}),
        ('48 czasMs ujemny', zmien(typu('[PN-DATA] '), 'czasMs', '-0.100'), {'48'}),
        ('49 doba przesunieta', plus_int(typu('[PN-DZIEN] '), 'dzien', 1), {'49'}),
        ('49 tick niezgodny z doba (bez powtorzenia)', plus_int(typu('[PN-DZIEN] '), 'tick', 60000), {'49'}),
        ('49 doba powtorzona', dubel(typu('[PN-DZIEN] ')), {'49'}),
        # 5 powalonych przy 3 na mapie to tez kryzys wedlug liczb doby, a [PN-STAN] w tym ticku mowi spokoj (56).
        ('49 powaleni > na mapie', zmien(typu('[PN-DZIEN] '), 'powaleni', '5'), {'49', '56'}),
        ('49 licznik gry maleje', zmien(lambda l: l.startswith('[PN-DZIEN] ') and 'napadow=2;' in l, 'napadow', '0'), {'49'}),
        ('50 skok licznosci', zmien(typu('[PN-KOLONISTA] ', lambda l: 'zdarzenie=zginal' in l), 'liczebnosc', '1'), {'50'}),
        ('50 zmiana bez kotwicy', usun(typu('[PN-KOLONISTA] ', lambda l: 'zdarzenie=start' in l)), {'50'}),
        ('50 zdarzenie spoza listy', zmien(typu('[PN-KOLONISTA] ', lambda l: 'zdarzenie=zginal' in l), 'zdarzenie', 'znikl'),
         {'50'}),
        ('51 listow != etykiety', listy_zle, {'51'}),
        ('51 frakcja bez Defa', zmien(typu('[PN-FIRED] ', lambda l: 'frakcja=12;' in l), 'frakcjaDef', '-'), {'51'}),
        ('51 pora poza mapa', zmien(typu('[PN-FIRED] ', lambda l: 'mapa=-1;' in l), 'pora', '1'), {'51'}),
        ('52 decyzji +1 (kubelki zgodne)', decyzji_plus, {'52'}),
        ('52 kubelki nie sumuja sie', zmien(typu('[PN-PERF] '), 'tickowKubelki', '0/1/0/0/0/0'), {'52'}),
        ('52 max > suma', zmien(typu('[PN-PERF] '), 'stylMaxUs', '999999'), {'52'}),
        ('52 obserwator bez ticku', zmien(typu('[PN-PERF] '), 'doba', '59999'), {'52'}),
        ('53 runId bez zmiany', zmien(typu('[PN-EVAL] '), 'runIdPoprzedni', NOWY_RUN), {'53'}),
        ('53 pamiec nie od zera', zmien(typu('[PN-DATA] ', lambda l: NOWY_RUN in l), 'histWpisow', '3'), {'53'}),
        # Wiersz przed [PN-EVAL]: takze linia czasu nowej gry liczy o jedna decyzje mniej (52).
        ('53 wiersz przed linia', wiersz_przed_eval, {'52', '53'}),
        ('[PN-EVAL] kotwiczy styl: doba stylu dni=5 (40)',
         zmien(typu('[PN-GRACZ] ', lambda l: NOWY_RUN in l), 'dni', '5'), {'40'}),
        ('[PN-EVAL] kotwiczy styl: stylDni wiersza 3 (41)',
         zmien(typu('[PN-DATA] ', lambda l: NOWY_RUN in l), 'stylDni', '3'), {'41'}),
        ('54 fakt z akcji wymuszonej', fakt_z_wymuszonej, {'54'}),
        ('54 wykonana bez [PN-FIRED] po', zmien(typu('[PN-FIRED] ', lambda l: 'kontekst=po' in l and 'Flashstorm' in l),
                                               'kontekst', '-'), {'43', '51', '54'}),
        ('55 brak [PN-LIST] odpalenia', usun(typu('[PN-LIST] ', odpalenia_list)), {'55'}),
        ('55 incydenty listu inne', zmien(typu('[PN-LIST] ', odpalenia_list), 'incydenty', 'Obcy'), {'55'}),
        ('55 list z incydentem bez odpalenia', zmien(typu('[PN-LIST] ', lambda l: 'incydenty=-;' in l), 'incydenty',
                                                     'RaidEnemy'), {'55'}),
        ('55 brak listu akcji wymuszonej', usun(typu('[PN-LIST] ', lambda l: 'wymuszone=1' in l)), {'55'}),
        ('55 pole spoza listy', pole_obce, {'55'}),
        # Wartosc nieliczbowa: forma (51), brak [PN-EXEC] z takim numerem (54), osobna grupa listow ticku (55).
        ('51 wymuszone nie jest liczba', zmien(typu('[PN-FIRED] ', z_listem), 'wymuszone', 'x'), {'51', '54', '55'}),
        ('54 [PN-FIRED] wymuszone=1 bez [PN-EXEC] wymuszone=1',
         # Po zdjeciu numeru to zwykle wykonanie, a w ticku sa dwa odpalenia burzy pn=1 (akcje 1 i 3) - takze 42.
         zmien(lambda l: l.startswith('[PN-EXEC] ') and 'wymuszone=1;' in l, 'wymuszone', '0'), {'42', '54'}),
        ('55 dwie akcje z tym samym numerem', dwie_z_numerem_1, {'55'}),
        ('54 akcja 3 bez swojego [PN-FIRED] (jest 1 z tym incydentem)', bez_odpalenia_3, {'54'}),
        ('54 [PN-FIRED] akcji 9 bez [PN-EXEC]', odpalenie_9_bez_exec, {'54'}),
        ('56 zmiana bez zmiany predykatu', kryzys_bez_powalonych, {'56'}),
        ('56 kryzys niezgodny z liczbami', zmien(stanu(lambda l: 'kryzys=1' in l), 'powaleni', '0'), {'56'}),
        ('56 pusta niezgodna z liczbami', zmien(stanu(lambda l: 'zdarzenie=start' in l), 'pusta', '1'), {'56'}),
        ('56 zmiana bez startu', usun(stanu(lambda l: 'zdarzenie=start' in l)), {'56'}),
        ('56 zmiana poza siatka', plus_int(stanu(lambda l: 'zdarzenie=zmiana' in l), 'tick', 1), {'56'}),
        ('56 tick cofa sie', plus_int(stanu(lambda l: 'zdarzenie=zmiana' in l and 'zagrozenie=0' in l), 'tick', -1000),
         {'56'}),
        ('56 stan niezgodny z [PN-DZIEN]', zmien(typu('[PN-DZIEN] '), 'zagrozenie', '1'), {'56'}),
        ('56 koniec z polami', zmien(stanu(lambda l: 'zdarzenie=koniec' in l), 'kolonisciNaMapie', '2'), {'56'}),
        ('56 koniec bez stanu', usun(stanu(lambda l: 'mapa=5;' in l and 'zdarzenie=start' in l)), {'56'}),
        ('56 zdarzenie spoza listy', zmien(stanu(lambda l: 'zdarzenie=zmiana' in l), 'zdarzenie', 'inna'), {'56'}),
        # Bramki wersji: reguly starszych formatow obowiazuja w wierszu v11.
        ('v11: konsekwencja niezgodna (31)', zmien(typu('[PN-DATA] ', lambda l: t8.pole(l, 'decyzja') != 'PASS'),
                                                  'konsekwencja', 'PN_Kons_Obca'), {'31'}),
        ('v11: strona bramy pusta (46)', zmien(typu('[PN-DATA] '), 'anomaliaTura', ''), {'46'}),
    ]
    for opis, psuj, oczekiwane in przypadki:
        nazwa = 'v11_' + ''.join(c if c.isalnum() else '_' for c in opis)
        kod, nar, out = t7.uruchom(nazwa, psuj(list(wzorzec)))
        dobry = set(nar) == oczekiwane
        print('%-44s naruszone %-14s oczekiwane %-10s %s' % (opis, sorted(set(nar)), sorted(oczekiwane),
                                                            'OK' if dobry else '*** BLAD ***'))
        ok = ok and dobry

    print('WYNIK:', 'OK' if ok else 'BLAD')
    sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main()
