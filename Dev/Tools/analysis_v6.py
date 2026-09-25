# -*- coding: utf-8 -*-
"""ANALIZA PLIKU DANYCH NARRATORA (format [PN-DATA] v6) - niezmienniki + podsumowanie per grupa.

Uzycie:
    python analysis_v6.py                                   # domyslny PN_decyzje.log gracza
    python analysis_v6.py sciezka/PN_decyzje.log
    python analysis_v6.py plik --tryb gra|symulacja|wszystko   (domyslnie: wszystko)

Grupy: symulacja -> po kolumnie "eksperyment" (ramie); gra -> po (runId, mapa, odcinek).
Odcinek konczy linia [PN-LOAD] albo [PN-RESET] tego runId. Wczytanie zapisu ROZWIDLA rozgrywke:
wiersze mapy z decyzjaNr >= liczbie decyzji tej mapy z pola "mapy" linii [PN-LOAD] sa porzucona
galezia i wypadaja z analizy (regula z CLAUDE.md, sekcja o formacie danych).

Parametry (wagi profili, progi, wagi PASS, temperatura bramy, budzet rund) sa czytane z linii
[PN-CONFIG] tego samego pliku, NIE z literalow - skrypt przezywa kalibracje. Tolerancje wynikaja
z zaokraglenia kolumn do 3 miejsc (uzasadnienia w CLAUDE.md).

Kod wyjscia 1, gdy jakis niezmiennik jest naruszony.
"""
import collections, io, math, re, sys

DOMYSLNY = 'C:/Users/Luis/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/PN_decyzje.log'
THREAT_BIG = {'RaidEnemy', 'ManhunterPack', 'Infestation', 'PsychicEmanatorShipPartCrash'}
FIT = {'Escalate': 0.0, 'Hold': 0.5, 'Breathe': 1.0, 'Pass': 1.0}
PROFIL_DOMYSLNY = dict(wN=1.0, wS=1.0, calm=0.30, tense=0.60, span=1.0)


def kv(fragment):
    """'a=1; b=2' -> dict; wartosc moze zawierac '=' (split tylko na pierwszym)."""
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


def wczytaj(path):
    cols, profile, stc = None, {}, {}
    rows, extras = [], collections.defaultdict(list)
    odcinek = collections.Counter()           # runId -> numer odcinka
    porzucone = 0
    for l in io.open(path, encoding='utf-8').read().splitlines():
        if l.startswith('[PN-DATA-COLS]'):
            cols = l.split('kolumny=')[1].split(',')
        elif l.startswith('[PN-CONFIG]'):
            t = l[len('[PN-CONFIG] '):]
            if t.startswith('storyteller='):
                stc = dict(
                    wR=liczba(t, 'weightRestraint', 0.55), wB=liczba(t, 'weightBaseline', 0.20),
                    wI=liczba(t, 'weightIntentAlignment', 0.25), floor=liczba(t, 'densityFloor', 1.5),
                    sat=liczba(t, 'densitySaturation', 4.5), T=liczba(t, 'gateTemperature', 0.1),
                    rundy=liczba(t, 'maxSelectionRounds', 8))
            elif t.startswith('profil='):
                pid = t.split(';')[0].split('=')[1]
                profile[pid] = dict(wN=liczba(t, 'wNarr', 1), wS=liczba(t, 'wSyt', 1),
                                    calm=liczba(t, 'spokojPonizej', 0.3), tense=liczba(t, 'napieciePowyzej', 0.6),
                                    span=liczba(t, 'rozpietoscMocy', 1))
        elif l.startswith('[PN-LOAD]') or l.startswith('[PN-RESET]'):
            d = kv(l.split('] ', 1)[1])
            extras[l.split(']')[0] + ']'].append(d)
            run = d.get('runId', '?')
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
        elif l.startswith('[PN-DATA] '):
            d = kv(l[len('[PN-DATA] '):])
            d['_ksztalt_ok'] = cols is not None and list(k for k in d.keys()) == cols
            d['_odcinek'] = odcinek[d.get('runId', '?')]
            rows.append(d)
        elif l.startswith('[PN-WARN]') or l.startswith('[PN-ERR]') or l.startswith('[PN-EXP]'):
            extras[l.split(']')[0] + ']'].append(l)
    return cols, profile, stc, rows, extras, porzucone


def f(r, k):
    v = r.get(k, '')
    return float(v) if v not in ('', None) else None


def main():
    args = sys.argv[1:]
    tryb = 'wszystko'
    if '--tryb' in args:
        i = args.index('--tryb')
        tryb = args[i + 1]
        del args[i:i + 2]
    path = args[0] if args else DOMYSLNY
    cols, profile, stc, rows, extras, porzucone = wczytaj(path)
    if tryb != 'wszystko':
        rows = [r for r in rows if r.get('tryb') == tryb]
    if not stc:
        stc = dict(wR=0.55, wB=0.20, wI=0.25, floor=1.5, sat=4.5, T=0.1, rundy=8)
        print('UWAGA: brak linii [PN-CONFIG] storyteller - parametry PASS/bramy domyslne z kodu')

    print('plik:', path)
    print('kolumn w naglowku:', len(cols) if cols else 'BRAK', '| wierszy:', len(rows),
          '| zly ksztalt:', sum(1 for r in rows if not r['_ksztalt_ok']),
          '| porzucona galaz po wczytaniu:', porzucone)
    print('wersjaLogu:', sorted(set(r.get('wersjaLogu') for r in rows)), '| tryb:', dict(collections.Counter(r.get('tryb') for r in rows)))
    print('profile z [PN-CONFIG]:', ', '.join(sorted(profile)) or 'BRAK')

    grupy = collections.OrderedDict()
    for r in rows:
        if r.get('tryb') == 'symulacja':
            klucz = 'sym ' + r.get('eksperyment', '?')
        else:
            klucz = 'gra run=%s mapa=%s odc=%d' % (r.get('runId'), r.get('mapa'), r['_odcinek'])
        grupy.setdefault(klucz, []).append(r)

    narus, przyklad = collections.Counter(), {}

    def narusz(n, opis):
        narus[n] += 1
        przyklad.setdefault(n, opis)

    for klucz, rr in grupy.items():
        seria = None
        poprzedni = None
        for r in rr:
            nr = int(r['decyzjaNr'])
            prof = profile.get(r['profil'], PROFIL_DOMYSLNY)
            los = int(r['losowan'])
            rundy = (los - 1) / 2.0
            kryzys = r.get('kryzys') == 'true'
            if los < 3 or (los - 1) % 2:
                narusz('losowan = 1 + 2*rundy', (klucz, nr))
            if int(r['niedostepnych']) == 0 and rundy != 1 + int(r['odmowSilnika']) - int(r['odmowCzola']):
                narusz('rundy = 1 + odmowSilnika - odmowCzola (przy niedostepnych=0)', (klucz, nr))
            if int(r['pytanDoGry']) > stc['rundy']:
                narusz('pytanDoGry <= maxSelectionRounds', (klucz, nr))
            if int(r['histDecyzji']) != nr:
                narusz('histDecyzji == decyzjaNr', (klucz, nr))
            if poprzedni is not None and nr != poprzedni + 1:
                narusz('decyzjaNr ciagly w grupie', (klucz, poprzedni, nr))
            if int(r['wygenerowanych']) < int(r['kandydatow']):
                narusz('wygenerowanych >= kandydatow', (klucz, nr))
            pas = r['decyzja'] == 'PASS'
            if pas != (r['pRunda'] == ''):
                narusz('pRunda puste <=> PASS', (klucz, nr))
            if not pas and los == 3 and abs(f(r, 'pRunda') * (1 - f(r, 'pBrama')) - f(r, 'p')) > 0.0015:
                narusz('pRunda*(1-pBrama) ~ p (tol 0.0015, losowan=3)', (klucz, nr))
            t = f(r, 'napiecie')
            nap = (prof['wN'] * f(r, 'napiecieNarr') + prof['wS'] * f(r, 'napiecieSyt')) / (prof['wN'] + prof['wS'])
            if abs(nap - t) > 0.0015:
                narusz('napiecie = srednia wazona czlonow (wagi profilu)', (klucz, nr, round(nap, 4), t))
            oczek = 'Escalate' if t < prof['calm'] else ('Breathe' if t > prof['tense'] else 'Hold')
            moc = prof['span'] * (1 - 2 * min(1.0, max(0.0, t)))
            if kryzys:
                if r['intencja'] != 'Breathe':
                    narusz('kryzys -> intencja Breathe', (klucz, nr, r['intencja']))
                if abs(min(moc, 0.0) - f(r, 'docelowaMoc')) > 0.002 or f(r, 'docelowaMoc') > 0.0005:
                    narusz('kryzys -> docelowaMoc = min(moc profilu, 0)', (klucz, nr, r['docelowaMoc']))
            else:
                if r['intencja'] != oczek:
                    narusz('intencja z progow profilu', (klucz, nr, t, r['intencja']))
                if abs(moc - f(r, 'docelowaMoc')) > 0.002:
                    narusz('docelowaMoc = span*(1-2t) (tol 0.002)', (klucz, nr, round(moc, 4), r['docelowaMoc']))
            rr_ = min(1.0, max(0.0, (f(r, 'gestosc') - stc['floor']) / (stc['sat'] - stc['floor'])))
            up = (stc['wR'] * rr_ + stc['wB'] + stc['wI'] * FIT.get(r['intencja'], 0.5)) / (stc['wR'] + stc['wB'] + stc['wI'])
            if abs(up - f(r, 'passWynik')) > 0.0015:
                narusz('passWynik = pelny wzor z czlonem intencji', (klucz, nr, round(up, 4), r['passWynik']))
            if r['best'] != '' and r['passStlumiony'] == 'false' and r['pBrama'] != '':
                pb = 1.0 / (1.0 + math.exp((f(r, 'best') - f(r, 'passWynik')) / stc['T']))
                # TOLERANCJA WYPROWADZONA, nie stala: best i passWynik sa w logu zaokraglone do 3 miejsc
                # (+-0.0005 kazde, roznica +-0.001), a wykladnik dzieli ja przez T, wiec pBrama moze
                # odejsc o p(1-p)*0.001/T; do tego zaokraglenie samego pBrama (+-0.0005). Stala 0.002
                # dawala falszywe naruszenia (dane z gry 2026-09-23: roznica 0.00212 przy granicy 0.00249).
                tol = pb * (1 - pb) * (0.001 / stc['T']) + 0.0005 + 1e-6
                if abs(pb - f(r, 'pBrama')) > tol:
                    narusz('pBrama = 1/(1+exp((best-passWynik)/T))', (klucz, nr, round(pb, 4), r['pBrama']))
            if seria is not None and poprzedni is not None and nr == poprzedni + 1 and int(r['ciszaSwiadoma']) != seria:
                narusz('ciszaSwiadoma z poprzedniego wiersza', (klucz, nr, seria, r['ciszaSwiadoma']))
            s0 = int(r['ciszaSwiadoma'])
            seria = (s0 + 1 if (r['powodPass'] == 'Competitive' and not kryzys) else s0) if pas else 0
            if (r.get('straznikZawieszony') == 'true') and not kryzys:
                narusz('straznikZawieszony tylko w kryzysie', (klucz, nr))
            poprzedni = nr

    print('\nNIEZMIENNIKI (%d wierszy, %d grup):' % (len(rows), len(grupy)))
    nazwy = ['losowan = 1 + 2*rundy', 'rundy = 1 + odmowSilnika - odmowCzola (przy niedostepnych=0)',
             'pytanDoGry <= maxSelectionRounds', 'histDecyzji == decyzjaNr', 'decyzjaNr ciagly w grupie',
             'wygenerowanych >= kandydatow', 'pRunda puste <=> PASS', 'pRunda*(1-pBrama) ~ p (tol 0.0015, losowan=3)',
             'napiecie = srednia wazona czlonow (wagi profilu)', 'intencja z progow profilu',
             'docelowaMoc = span*(1-2t) (tol 0.002)', 'kryzys -> intencja Breathe',
             'kryzys -> docelowaMoc = min(moc profilu, 0)', 'passWynik = pelny wzor z czlonem intencji',
             'pBrama = 1/(1+exp((best-passWynik)/T))', 'ciszaSwiadoma z poprzedniego wiersza',
             'straznikZawieszony tylko w kryzysie']
    for n in nazwy:
        print('  %-66s %s' % (n, 'OK' if narus[n] == 0 else 'NARUSZEN %d, np. %s' % (narus[n], przyklad[n])))

    print('\nGRUPY:')
    for klucz, rr in grupy.items():
        zd = [r for r in rr if r['decyzja'] != 'PASS']
        pas = [r for r in rr if r['decyzja'] == 'PASS']
        dni = [f(r, 'dzien') for r in rr]
        nap = [f(r, 'napiecie') for r in rr]
        pb = [f(r, 'pBrama') for r in rr if r['pBrama'] != '']
        tb = [r for r in zd if r['wybor'] in THREAT_BIG]
        akcje = [r['klucz'].split('|')[2] for r in zd if r['klucz'].count('|') >= 2]
        powt = sum(1 for a, b in zip(akcje, akcje[1:]) if a == b)
        okres = max(dni) - min(dni) if len(dni) > 1 else 0.0
        print(' %s  [profil %s]' % (klucz, sorted(set(r['profil'] for r in rr))))
        print('   decyzji %d (dni %.1f-%.1f), zdarzen %d, PASS %d %s' % (
            len(rr), min(dni), max(dni), len(zd), len(pas), dict(collections.Counter(r['powodPass'] for r in pas))))
        print('   intencje %s | napiecie %.3f-%.3f sr. %.3f | kryzys w %d turach | sr. pBrama %.3f' % (
            dict(collections.Counter(r['intencja'] for r in rr)), min(nap), max(nap), sum(nap) / len(nap),
            sum(1 for r in rr if r.get('kryzys') == 'true'), sum(pb) / len(pb) if pb else float('nan')))
        print('   ThreatBig %d (pierwszy dz. %s) | odmow silnika %d (przed brama %d) | niedostepnych %d | '
              'tur z sitem/filtrem %d | bezposrednich powtorzen akcji %d' % (
                  len(tb), ('%.1f' % f(tb[0], 'dzien')) if tb else '-', sum(int(r['odmowSilnika']) for r in rr),
                  sum(int(r['odmowCzola']) for r in rr), sum(int(r['niedostepnych']) for r in rr),
                  sum(1 for r in rr if int(r['wygenerowanych']) > int(r['kandydatow'])), powt))
        if zd:
            print('   incydenty %s | unikalnych kompozycji %d/%d' % (
                dict(collections.Counter(r['wybor'] for r in zd).most_common()), len(set(r['klucz'] for r in zd)), len(zd)))
        if okres > 0:
            print('   tempo zdarzen %.3f/dzien (miedzy pierwsza a ostatnia decyzja)' % (len(zd) / okres))

    print('\nLINIE POZA [PN-DATA]:')
    for typ in ['[PN-LOAD]', '[PN-RESET]', '[PN-EXP]', '[PN-WARN]', '[PN-ERR]']:
        wpisy = extras.get(typ, [])
        print('  %-11s %d' % (typ, len(wpisy)))
        if typ in ('[PN-WARN]', '[PN-ERR]'):
            for tekst, n in collections.Counter(
                    (w.split('tekst=', 1)[1] if 'tekst=' in w else w)[:90] for w in wpisy).most_common(6):
                print('      %3dx %s' % (n, tekst))
        if typ == '[PN-EXP]':
            for w in wpisy:
                if ' end;' in w:
                    print('      ' + w[:200])
        if typ in ('[PN-LOAD]', '[PN-RESET]'):
            for d in wpisy[-6:]:
                print('      ' + '; '.join('%s=%s' % (k, v) for k, v in d.items())[:200])

    sys.exit(1 if sum(narus.values()) else 0)


if __name__ == '__main__':
    main()
