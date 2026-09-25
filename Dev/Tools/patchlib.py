# -*- coding: utf-8 -*-
"""Bezpieczne latanie plikow tekstowych projektu.

Kazda zmiana to para (stary_tekst, nowy_tekst); stary tekst MUSI wystapic w pliku DOKLADNIE RAZ
(assert), inaczej nic nie jest zapisywane. Konce linii sa zachowywane (pliki projektu sa CRLF).
To jest jedyny dopuszczony sposob hurtowej edycji - nigdy wyrazenia regularne ani wyszukiwanie
wsteczne (incydent z rindex, ktory skasowal pol compa: patrz Docs/DZIENNIK_ROZWOJU.md).

Uzycie z innego skryptu (skrypt pisac narzedziem Write, NIE heredokiem w bashu - heredok psuje
ukosniki i cudzyslowy):

    import sys; sys.path.insert(0, r'D:/Games/RimWorld/Mods/ProceduralNarrator/Dev/Tools')
    from patchlib import patch
    patch('D:/.../Plik.cs', [(stary, nowy), ...])
"""
import io


def patch(path, edits, dry_run=False):
    raw = io.open(path, encoding='utf-8', newline='').read()
    crlf = '\r\n' in raw
    s = raw.replace('\r\n', '\n')
    for i, (old, new) in enumerate(edits):
        n = s.count(old)
        assert n == 1, (path, 'zmiana nr %d' % i, 'wystapien: %d' % n, old[:120])
        s = s.replace(old, new)
    if crlf:
        s = s.replace('\n', '\r\n')
    if not dry_run:
        io.open(path, 'w', encoding='utf-8', newline='').write(s)
    print(path.replace('\\', '/').split('/')[-1], 'OK' if not dry_run else 'OK (na sucho)',
          'crlf' if crlf else 'lf', '%d zmian' % len(edits))
