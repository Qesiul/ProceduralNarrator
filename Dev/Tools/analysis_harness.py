# -*- coding: utf-8 -*-
# Uprzaz regresji analizatora (krok 7, S7; krok 8, S9): kazda mutacja regul 32-41 w analysis_v7.py musi wywrocic
# test_analysis_v9.py, a regul 42-45 - test_analysis_k8.py (czwarty element pozycji). Oryginal przywracany w finally.
# Uzycie: python Dev/Tools/analysis_harness.py
import io, subprocess, sys

A = 'D:/Games/RimWorld/Mods/ProceduralNarrator/Dev/Tools/analysis_v7.py'
T = 'D:/Games/RimWorld/Mods/ProceduralNarrator/Dev/Tools/test_analysis_v9.py'
T8 = 'D:/Games/RimWorld/Mods/ProceduralNarrator/Dev/Tools/test_analysis_k8.py'

MUT = [
    ('bramka v9 wylaczona', "            if r.get('wersjaLogu') == '9':\n                styl_wiersza(", "            if r.get('wersjaLogu') == 'X':\n                styl_wiersza("),
    ('stare bramki bez v9', "            if r.get('wersjaLogu') in ('7', '8', '9'):\n                if not r['_ksztalt_ok']:", "            if r.get('wersjaLogu') in ('7', '8'):\n                if not r['_ksztalt_ok']:"),
    ('32 bez mnoznika v', "oczek = 0.0 if tlumiona else (f(r, 'best') - f(r, 'pasmo')) * f(r, 'stylWartosc')", "oczek = 0.0 if tlumiona else (f(r, 'best') - f(r, 'pasmo'))"),
    ('36 bez orientacji', "cfg['wO'] * prof.get('o', 0.0) + cfg['wR']", "cfg['wO'] * 0.0 + cfg['wR']"),
    ('37 prog pominiety', "        if c >= prog:\n            mocne.add(CECHY[i])", "        if c > 0:\n            mocne.add(CECHY[i])"),
    ('37 tolerancja szeroka', 'return 0.001 / skala + 1e-4', 'return 0.5'),
    ('38 remis dokladny = niepewny', 'abs(odl[j] - odl[k]) <= TOL_ODL and rozne_na_znanych(j)', 'abs(odl[j] - odl[k]) <= TOL_ODL'),
    ('39 wiersz nieszukany', "        if r is None or r.get('wersjaLogu') != '9':\n            continue\n        mocne = zbior_mocnych", "        if True:\n            continue\n        mocne = zbior_mocnych"),
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
    ('44 wykonane dowolne', "            ok = li in ('dopisany', 'odroczony', 'brak', 'wylaczony', 'pustyOpis', 'blad')", "            ok = True", T8),
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
]

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
