# -*- coding: utf-8 -*-
"""Kod moda (C# i XML Defow) ma byc czystym ASCII - polskie znaki tylko w dokumentacji .md.
Uzycie: python check_ascii.py   (kod wyjscia 1, gdy cos znajdzie)"""
import io, sys, glob, os

ROOT = 'D:/Games/RimWorld/Mods/ProceduralNarrator'
files = glob.glob(ROOT + '/Source/ProceduralNarrator/**/*.cs', recursive=True) \
    + glob.glob(ROOT + '/Defs/**/*.xml', recursive=True)
n = 0
for p in files:
    if '/obj/' in p.replace(os.sep, '/') or '/bin/' in p.replace(os.sep, '/'):
        continue
    s = io.open(p, encoding='utf-8').read()
    bad = [i for i, c in enumerate(s) if ord(c) > 127]
    if bad:
        n += 1
        ctx = s[max(0, bad[0] - 30):bad[0] + 5]
        print(p, len(bad), ctx.encode('ascii', 'backslashreplace').decode())
print('plikow sprawdzonych:', len(files), '| z nie-ASCII:', n)
sys.exit(1 if n else 0)
