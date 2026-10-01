# -*- coding: utf-8 -*-
# Uprzaz regresji analizatora (krok 7, S7; krok 8, S9; krok 9, K0 i L): kazda mutacja regul 32-41 w analysis_v7.py musi
# wywrocic test_analysis_v9.py, regul 42-45 - test_analysis_k8.py, reguly 46 i bramek v10 - test_analysis_v10.py,
# a regul 47-56 i parsera etapu L - test_analysis_v11.py
# (czwarty element pozycji). Oryginal przywracany w finally - ale NIE przy zabitym procesie.
# Uzycie: python Dev/Tools/analysis_harness.py  (w tle z limitem >= 2 h; 30 min nie wystarcza od 2026-09-30)
#         python Dev/Tools/analysis_harness.py --sprawdz  (po przerwaniu: czy analizator nie zostal zmutowany)
import io, subprocess, sys

A = 'D:/Games/RimWorld/Mods/ProceduralNarrator/Dev/Tools/analysis_v7.py'
T = 'D:/Games/RimWorld/Mods/ProceduralNarrator/Dev/Tools/test_analysis_v9.py'
T8 = 'D:/Games/RimWorld/Mods/ProceduralNarrator/Dev/Tools/test_analysis_k8.py'
T10 = 'D:/Games/RimWorld/Mods/ProceduralNarrator/Dev/Tools/test_analysis_v10.py'
T11 = 'D:/Games/RimWorld/Mods/ProceduralNarrator/Dev/Tools/test_analysis_v11.py'

MUT = [
    ('bramka v9 wylaczona', "            if r.get('wersjaLogu') in OD_V9:\n                styl_wiersza(", "            if r.get('wersjaLogu') == 'X':\n                styl_wiersza("),
    ('stare bramki bez v9', "            if r.get('wersjaLogu') in OD_V7:\n                if not r['_ksztalt_ok']:", "            if r.get('wersjaLogu') in ('7', '8'):\n                if not r['_ksztalt_ok']:"),
    ('32 bez mnoznika v', "oczek = 0.0 if tlumiona else (f(r, 'best') - f(r, 'pasmo')) * f(r, 'stylWartosc')", "oczek = 0.0 if tlumiona else (f(r, 'best') - f(r, 'pasmo'))"),
    ('36 bez orientacji', "cfg['wO'] * prof.get('o', 0.0) + cfg['wR']", "cfg['wO'] * 0.0 + cfg['wR']"),
    ('37 prog pominiety', "        if c >= prog:\n            mocne.add(CECHY[i])", "        if c > 0:\n            mocne.add(CECHY[i])"),
    ('37 tolerancja szeroka', 'return 0.001 / skala + 1e-4', 'return 0.5'),
    ('38 remis dokladny = niepewny', 'abs(odl[j] - odl[k]) <= TOL_ODL and rozne_na_znanych(j)', 'abs(odl[j] - odl[k]) <= TOL_ODL'),
    ('39 wiersz nieszukany', "        if r is None or r.get('wersjaLogu') not in OD_V9:\n            continue\n        mocne = zbior_mocnych", "        if True:\n            continue\n        mocne = zbior_mocnych"),
    # ---- przeglad S8: galezie, ktorych pierwsza wersja uprzezy nie pilnowala ----
    ('[PN-GRACZ] bez sprawdzen', "    for g in gracze:\n        styl_gracza(g, g['_styl'], narusz)", "    for g in gracze:\n        pass"),
    ('34 warstwa nieobecna bez sprawdzenia', "    if dni == '':\n        pelne = [k for k in KOLUMNY_STYLU if r.get(k, '') != '']\n        if pelne:",
     "    if dni == '':\n        pelne = [k for k in KOLUMNY_STYLU if r.get(k, '') != '']\n        if False:"),
    ('34 zdarzenie bez sprawdzenia', "    elif not pas:\n        # Aktywny styl i zdarzenie", "    elif False:\n        # Aktywny styl i zdarzenie"),
    ('33 zakres cech wylaczony', "        if z is not None and not (0.0 <= z <= 1.0):\n            zle.append('styl' + c)", "        if False:\n            zle.append('styl' + c)"),
    ('33 |v| <= |d| wylaczone', "abs(f(r, 'stylWartosc')) > abs(f(r, 'stylKierunek')) + 0.001", "False"),
    ('33 liczniki pasma wylaczone', "int(r[k]) > int(r['wSoftmaksie'])", "False"),
    ('32 PASS z wartoscia przepuszczony', "if (v == '') != (pr == '') or (pas and v != ''):", "if (v == '') != (pr == ''):"),
    ('32 znow tylko losowan=3', "    elif v != '' and r['best'] != '' and r['pasmo'] != '':", "    elif v != '' and los == 3 and r['best'] != '' and r['pasmo'] != '':"),
    ('19 znow tylko losowan=3', "                elif aa != '' and r['best'] != '' and \\", "                elif aa != '' and los == 3 and r['best'] != '' and \\"),
    ('32 bez pierwszenstwa luku', "        tlumiona = (f(r, 'arcAlignment') or 0.0) > 0.0 and f(r, 'stylWartosc') < 0.0", "        tlumiona = False"),
    ('39 negacja ignorowana', "if (w[1:] in mocne) if w.startswith('!') else (w not in mocne):", "if (w.lstrip('!') not in mocne):"),
    ('39 laczenie po ticku otwarcia', "        r = a.get('_wiersz')",
     "        r = next((x for x in rows if x.get('tick') == a.get('tick') and x.get('mapa') == a.get('mapa') and x.get('eksperyment', '') == a.get('eksperyment', '')), None)"),
    ('41 ramie gry bez sprawdzenia', "                    d['_stylDniOczek'] = stylPrzedEksp.get(eks.split('/')[0])", "                    d['_stylDniOczek'] = None"),
    ('[PN-LOAD] bez przyciecia [PN-GRACZ]', "                gracze[:] = [x for x in gracze if not z_przyszlosci(x)]", "                pass"),
    ('skasujStyl jak zwykla kotwica', "kot = (0, None) if d.get('akcja') == 'skasujStyl' else", "kot = (ks['dni'], None) if d.get('akcja') == 'skasujStyl' else"),
    ('konfiguracja ostatniej sesji dla wszystkich', "        if l.startswith('[PN-SESSION]'):\n            profile, stc, luki, styl_cfg = {}, {}, {}, {}", "        if l.startswith('[PN-SESSION]'):\n            pass"),
    ('40 bez limitu pojemnosci', 'dni != min(oczek + 1, pojemnosc)', 'dni != oczek + 1'),
    ('40 kotwica ignorowana', "                oczek, dzien0 = e[1], (e[2] - 1 if e[2] is not None else None)\n                continue", "                continue"),
    ('41 ramie S bez sprawdzenia', "    if oczek == '' and dni != '':", "    if False:"),
    ('41 porzadek: po ostatnim pliku', "            if (d.get('dni') or '').isdigit():\n                ostatnieDni[d.get('runId', '?')] = int(d['dni'])", "            pass"),
    ('35 bez rozgrzewki z CONFIG', "aktywny != (int(dni) >= cfg['warmup'])", "aktywny != (int(dni) >= 1)"),
    ('34 rozgrzewka bez kierunku', "        if pelne or r.get('stylKierunek', '') == '':", "        if pelne:"),
    # ---- krok 8, S9: niezmienniki 42-45 (test_analysis_k8.py) ----
    ('42 wykonanie bez pary', "        if pn1[k] != 1:\n            narusz(n42,", "        if False:\n            narusz(n42,", T8),
    ('42 pn=1 bez wykonania pominiete', "        if x.get('pn') == '1' and (x.get('runId'), x.get('mapa'), x.get('incydent'), x.get('tick')) not in potw:",
     "        if False:", T8),
    ('[PN-LOAD] bez przyciecia [PN-FIRED]', "                extras['[PN-FIRED]'][:] = [x for x in extras['[PN-FIRED]'] if not z_przyszlosci(x)]",
     "                pass", T8),
    ('sesja bez obserwatora', "            elif t.startswith('srodowisko='):\n                sesjaObserwator = True",
     "            elif t.startswith('srodowisko='):\n                sesjaObserwator = False", T8),
    ('43 kontekst przed bez sprawdzenia', "        if kon == 'przed' and t_ % 1000 != 0:", "        if False:", T8),
    ('43 powaleni bez sprawdzenia', "                and int(x['powaleni']) > int(x['kolonisciNaMapie']):", "                and False:", T8),
    ('44 sciezka spozniona dowolna', "        if pozno:\n            ok = li == 'pozno'", "        if pozno:\n            ok = True", T8),
    ('44 symulator dowolny', "            ok = li in ('symulacja', 'blad')", "            ok = True", T8),
    ('44 warianty bez sprawdzenia', "        if liczony != (war not in ('-', '')):", "        if False:", T8),
    ('44 konfiguracja listu ignorowana', "stc['zlozonyList'] = ('zlozonyList=tak' in t) if 'zlozonyList=' in t else None",
     "stc['zlozonyList'] = None", T8),
    ('tekstListu ciety na sredniku', "            if '; tekstListu=' in tresc:", "            if False:", T8),
    ('45 rozny bez sprawdzenia', "        if (c.get('werdyktGry') != c.get('nasz')) != (c.get('rozny') == 'true'):", "        if False:", T8),
    ('45 tick bez sprawdzenia', "        if (c.get('runId'), c.get('eksperyment', ''), c.get('mapa'), c.get('tick')) not in ticki_decyzji:",
     "        if False:", T8),
    # ---- przeglad S10: regresje, ktore przechodzily (recenzent: 15 z 16), i nowe reguly ----
    ('44 niewykonane dowolne', "        else:\n            ok = li == 'niewykonane'", "        else:\n            ok = True", T8),
    ('44 wykonane dowolne', "            ok = li in ('dopisany', 'odroczony', 'brak', 'wylaczony', 'pustyOpis', 'blad', 'ukryty')", "            ok = True", T8),
    # Krok 9, K2-b: zdarzenie ukryte z liczonym listem ma byc naruszeniem.
    ('44 ukryty z listem przepuszczony', "        if li == 'ukryty' and (int(e.get('nowychListow') or 0) != 0 or not e.get('tekstListu')):",
     "        if False:", T8),
    ('44 dopisany bez listu przepuszczony', "        if li in ('dopisany', 'odroczony') and (int(e.get('nowychListow') or 0) < 1 or not e.get('tekstListu')):",
     "        if False:", T8),
    ('44 format warianty wylaczony', "        if war not in ('-', '') and any(w.count(':') != 1 for w in war.split(',')):", "        if False:", T8),
    ('44 blad liczony jak zdarzenie', "        liczony = st in ('wykonane', 'symulacja') and not pozno and li != 'blad'",
     "        liczony = st in ('wykonane', 'symulacja') and not pozno", T8),
    ('42 duplikaty pn=1 dozwolone', "        if pn1[k] != 1:\n            narusz(n42,", "        if pn1[k] < 1:\n            narusz(n42,", T8),
    ('42 potw z niewykonane', "status') in ('wykonane', 'pozno-wykonane', 'niejednoznaczne'))",
     "status') in ('wykonane', 'pozno-wykonane', 'niejednoznaczne', 'niewykonane', 'pozno-niewykonane'))", T8),
    ('42 potw bez niejednoznaczne', "status') in ('wykonane', 'pozno-wykonane', 'niejednoznaczne'))",
     "status') in ('wykonane', 'pozno-wykonane'))", T8),
    ('42 sciezka spozniona pominieta',
     "        if not ((e.get('status') == 'wykonane' and normalna) or (e.get('status') == 'pozno-wykonane' and not normalna)):",
     "        if not (e.get('status') == 'wykonane' and normalna):", T8),
    ('43 kontekst a cel wylaczony', "        if (kon == '-') != (x.get('mapa') == '-1'):", "        if False:", T8),
    ('43 zagrozenie wylaczone', "        if x.get('zagrozenie', '') not in ('', '0', '1'):", "        if False:", T8),
    ('43 opoznienie wylaczone', "        if (x.get('opoznienie') or '0') != '0':", "        if False:", T8),
    ('43 pn=1 narrator wylaczone', "        if x.get('pn') == '1' and (x.get('narrator') != 'PN_GenerativeNarrator' or x.get('dom') != 'true'):",
     "        if False:", T8),
    ('43 dzien wylaczony', "        if abs(float(x.get('dzien') or 0) - t_ / 60000.0) > 0.0006:", "        if False:", T8),
    ('43 kontekst slownik wylaczony', "        if kon not in ('przed', 'po', '-'):", "        if False:", T8),
    ('43 swiat z domem przepuszczony', "            if x.get('dom') != 'false' or (x.get('cel') or '').startswith('map:'):", "            if False:", T8),
    ('43 cel innej mapy przepuszczony', "        elif x.get('cel') != 'map:' + (x.get('mapa') or ''):", "        elif False:", T8),
    ('[PN-LOAD] bez przyciecia [PN-CACHE]', "                extras['[PN-CACHE]'][:] = [x for x in extras['[PN-CACHE]'] if not z_przyszlosci(x)]",
     "                pass", T8),
    ('sesja nie zeruje obserwatora', "            profile, stc, luki, styl_cfg = {}, {}, {}, {}\n            sesjaObserwator = False",
     "            profile, stc, luki, styl_cfg = {}, {}, {}, {}", T8),
    ('[PN-FIRED] bez trybu gra', "            d['tryb'] = 'gra'\n            extras['[PN-FIRED]'].append(d)",
     "            extras['[PN-FIRED]'].append(d)", T8),
    # ---- krok 9, K0: format v10 (test_analysis_v10.py) ----
    ('46 strona dowolna', "                if tura not in ('Regular', 'Anomaly'):", "                if False:", T10),
    ('46 bez DLC dowolna strona', "                elif sz == '' and tura != 'Regular':", "                elif False:", T10),
    ('46 szansa bez zakresu', "                if sz != '' and not (0.0 <= f(r, 'anomaliaSzansa') <= 1.0):", "                if False:", T10),
    ('46 lustro bez sprawdzenia', "                if zb != '' and not (zb.isdigit()):", "                if False:", T10),
    ('bramka v10 wylaczona', "            if r.get('wersjaLogu') in OD_V10:\n                tura,", "            if r.get('wersjaLogu') == 'X':\n                tura,", T10),
    ('luki bez v10', "            if r.get('wersjaLogu') in OD_V7:\n                if not r['_ksztalt_ok']:",
     "            if r.get('wersjaLogu') in ('7', '8', '9'):\n                if not r['_ksztalt_ok']:", T10),
    ('fakty bez v10', "            if r.get('wersjaLogu') in OD_V8:\n                if r.get('faktow', '')",
     "            if r.get('wersjaLogu') in ('8', '9'):\n                if r.get('faktow', '')", T10),
    ('styl bez v10', "            if r.get('wersjaLogu') in OD_V9:\n                styl_wiersza(", "            if r.get('wersjaLogu') == '9':\n                styl_wiersza(", T10),
    ('25 bez v10', "        if r.get('wersjaLogu') in OD_V7 and r['decyzja'] != 'PASS':",
     "        if r.get('wersjaLogu') in ('7', '8', '9') and r['decyzja'] != 'PASS':", T10),
    # ---- krok 9, etap L: format v11 i linie etapu L (test_analysis_v11.py) ----
    ('47 puste razem wylaczone', "                if (lo == '') != (zb == ''):\n                    narusz(N47,", "                if False:\n                    narusz(N47,", T11),
    ('47 "-" bez licznika 0', "                    if zb != '0':\n                        narusz(N47,", "                    if False:\n                        narusz(N47,", T11),
    ('47 lista niekanoniczna', "if len(set(nazwy_)) != len(nazwy_) or nazwy_ != sorted(nazwy_) or '' in nazwy_:", "if False:", T11),
    ('47 licznik > lista', "if zb.isdigit() and int(zb) > len(nazwy_):", "if False:", T11),
    ('48 wylaczona', "if cz == '' or float(cz) < 0:", "if False:", T11),
    ('49 doba', "        if str(t_ // 60000 - 1) != x.get('dzien'):\n            narusz(N49,", "        if False:\n            narusz(N49,", T11),
    ('49 powtorzona doba', "        if wid[(x.get('runId'), x.get('mapa'), x.get('dzien'))] != 1:", "        if False:", T11),
    ('49 licznik maleje', "if pop is not None and any(a is not None and b is not None and b < a for a, b in zip(pop, licz)):", "if False:", T11),
    ('50 skok licznosci', "        elif n_ != stanKol[run] + (1 if zd == 'dolaczyl' else -1):", "        elif False:", T11),
    ('50 zdarzenie spoza listy', "        if zd not in KOLONISCI_ZDARZENIA or n_ < 0:", "        if n_ < 0:", T11),
    ('51 listow', "        if str(len(et)) != x.get('listow'):", "        if False:", T11),
    ('51 frakcja bez Defa', "        if (x.get('frakcja') == '-') != (x.get('frakcjaDef') == '-'):", "        if False:", T11),
    ('51 listy nie jako ostatnie pole', "            if '; listy=' in tresc:\n                tresc, listy_", "            if False:\n                tresc, listy_", T11),
    ('52 decyzji', "        if x.get('decyzji', '').isdigit() and int(x['decyzji']) != x['_decyzjiWPliku']:", "        if False:", T11),
    ('52 kubelki', "                if sum(kub) != int(n_) or len(kub) != len(gr) + 1:", "                if False:", T11),
    ('52 licznik nie zerowany przy wczytaniu', "            if l.startswith('[PN-LOAD]'):\n                decyzjiOdPerf = 0\n", "            if False:\n                decyzjiOdPerf = 0\n", T11),
    ('52 [PN-PERF] w porzuconej galezi', "                for typ_ in ('[PN-DZIEN]', '[PN-KOLONISTA]', '[PN-PERF]', '[PN-LIST]', '[PN-STAN]'):", "                for typ_ in ('[PN-DZIEN]', '[PN-KOLONISTA]', '[PN-LIST]', '[PN-STAN]'):", T11),
    ('53 runId bez zmiany', "        if run == x.get('runIdPoprzedni') or x.get('etykieta')", "        if False or x.get('etykieta')", T11),
    ('53 pamiec nie od zera', "            if r_.get('decyzjaNr') != '0' or r_.get('histWpisow') != '0':", "            if False:", T11),
    ('53 kolejnosc w pliku', "        if any(r_.get('runId') == run and r_['_lin'] < x['_lin'] for r_ in rows):", "        if False:", T11),
    ('[PN-EVAL] bez kotwicy stylu', "            sekw[nowy].append(('k', 0, None))\n            ostatnieDni[nowy] = 0\n", "", T11),
    ('54 fakt z akcji wymuszonej', "        if any((x.get('runId'), x.get('mapa'), x.get('tickZrodla')) == k_ for x in facts) \\", "        if False \\", T11),
    ('54 wymuszona bez [PN-FIRED] po', "        if e.get('status') == 'wykonane' and e.get('_obserwator') and not any(", "        if False and not any(", T11),
    ('25 wymuszona liczona jako decyzja', "    execs_dec = [e for e in execs if not wymuszona(e)]", "    execs_dec = list(execs)", T11),
    ('55 listow != linie ticku', "            if int(o.get('listow') or 0) != len(ll):", "            if False:", T11),
    ('55 incydenty listu', "            if set((x.get('incydenty') or '').split(',')) != incydenty_:", "            if False:", T11),
    ('55 list bez odpalenia', "        if k_ not in odp_ and any(x.get('incydenty') != '-' for x in ll):", "        if False:", T11),
    ('55 pola listu', "        if not set(x.keys()) <= POLA_LISTU or 'tresc' not in x or not (x.get('wymuszone') or '').isdigit():", "        if False:", T11),
    ('55 tresc nie do konca linii', "            if '; tresc=' in tresc_:", "            if False:", T11),
    ('55 [PN-LIST] w porzuconej galezi', "                for typ_ in ('[PN-DZIEN]', '[PN-KOLONISTA]', '[PN-PERF]', '[PN-LIST]', '[PN-STAN]'):", "                for typ_ in ('[PN-DZIEN]', '[PN-KOLONISTA]', '[PN-PERF]', '[PN-STAN]'):", T11),
    ('51 wymuszone bez sprawdzenia', "        if not (x.get('wymuszone', '0') or '').isdigit() or (wymuszona(x) and", "        if False and (wymuszona(x) and", T11),
    ('54 bez kierunku [PN-FIRED] -> [PN-EXEC]', "                             x.get('wymuszone')) not in wym_exec:", "                             x.get('wymuszone')) not in wym_exec and False:", T11),
    ('54 numer ignorowany ([PN-EXEC] -> [PN-FIRED])', "                x.get('pn') == '1' and x.get('kontekst') == 'po' and x.get('wymuszone') == e.get('wymuszone')\n", "                x.get('pn') == '1' and x.get('kontekst') == 'po' and wymuszona(x)\n", T11),
    ('56 parser bez [PN-STAN]', "        elif l.startswith('[PN-DZIEN] ') or l.startswith('[PN-KOLONISTA] ') or l.startswith('[PN-PERF] ') \\\n                or l.startswith('[PN-STAN] '):", "        elif l.startswith('[PN-DZIEN] ') or l.startswith('[PN-KOLONISTA] ') or l.startswith('[PN-PERF] '):", T11),
    ('56 [PN-STAN] w porzuconej galezi', "'[PN-LIST]', '[PN-STAN]'):", "'[PN-LIST]'):", T11),
    ('56 zdarzenie spoza listy', "        if zd not in STAN_ZDARZENIA:", "        if False:", T11),
    ('56 tick cofa sie', "        if histStan[k_] and t_ < histStan[k_][-1][0]:", "        if False:", T11),
    ('56 siatka 250', "        if zd != 'start' and t_ % 250 != 0:", "        if False:", T11),
    ('56 koniec z polami', "            if any(x.get(p) not in ('', None) for p in ('kolonisciNaMapie', 'powaleni') + STAN_PREDYKATY):", "            if False:", T11),
    ('56 koniec bez stanu', "            if k_ not in obowStan:\n                narusz(N56, ('koniec bez stanu'", "            if False:\n                narusz(N56, ('koniec bez stanu'", T11),
    ('56 kryzys ostro (> zamiast >=)', "        kryzys_ = pw >= 1 and 2 * pw >= nm", "        kryzys_ = pw >= 1 and 2 * pw > nm", T11),
    ('56 predykaty z liczb', "        if s_[1] != ('1' if kryzys_ else '0') or s_[2] != ('1' if nm == 0 else '0'):", "        if False:", T11),
    ('56 zmiana bez startu', "            if k_ not in obowStan:\n                narusz(N56, ('zmiana bez startu'", "            if False:\n                narusz(N56, ('zmiana bez startu'", T11),
    ('56 zmiana bez zmiany', "            elif obowStan[k_] == s_:", "            elif False:", T11),
    ('56 zgodnosc z [PN-DZIEN]', "        if h_[i_][1] != oczek:", "        if False:", T11),
    ('56 stan z nastepnej linii zamiast obowiazujacej', "        i_ = bisect.bisect_right([e[0] for e in h_], t_) - 1", "        i_ = bisect.bisect_right([e[0] for e in h_], t_ + 250) - 1", T11),
    ('52 [PN-EVAL] nie zeruje licznika decyzji', "            # Start gry ewaluacyjnej zeruje pomiar czasu (PerfMonitor.Reset) - licznik decyzji do [PN-PERF] tez.\n            decyzjiOdPerf = 0\n", "            pass\n", T11),
    ('42 akcje wymuszone jak decyzje', "        if e.get('tryb') != 'gra' or not e.get('_obserwator') or wymuszona(e):", "        if e.get('tryb') != 'gra' or not e.get('_obserwator'):", T11),
]

if '--sprawdz' in sys.argv:
    # Po przerwaniu (zabity proces nie wykonuje finally, analizator zostaje zmutowany): kazdy wzorzec dokladnie raz.
    s = io.open(A, encoding='utf-8', newline='').read().replace('\r\n', '\n')
    zle = [p[0] for p in MUT if s.count(p[1]) != 1]
    print('WZORCE: %d/%d OK%s' % (len(MUT) - len(zle), len(MUT), '' if not zle else ' - do sprawdzenia: ' + ', '.join(zle)))
    sys.exit(1 if zle else 0)

oryginal = io.open(A, encoding='utf-8', newline='').read()
wykryte = 0
try:
    for poz in MUT:
        nazwa, stare, nowe = poz[:3]
        test = poz[3] if len(poz) > 3 else T
        s = oryginal.replace('\r\n', '\n')
        n = s.count(stare)
        if n != 1:
            print('%-34s WZORZEC %d wystapien' % (nazwa, n))
            continue
        io.open(A, 'w', encoding='utf-8', newline='').write(s.replace(stare, nowe))
        r = subprocess.run([sys.executable, test], capture_output=True, text=True)
        ok = r.returncode != 0
        wykryte += ok
        print('%-34s %s' % (nazwa, 'WYKRYTA' if ok else '*** NIEWYKRYTA ***'))
finally:
    io.open(A, 'w', encoding='utf-8', newline='').write(oryginal)
print('WYKRYTE %d/%d' % (wykryte, len(MUT)))
