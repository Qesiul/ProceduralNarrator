# -*- coding: utf-8 -*-
"""Kontrakt linii [PN-DATA]: lista zadeklarowana w PNLog.DataColumns == kolumny faktycznie
budowane (PNLog.Data + NarratorDecision.ToDataFragment), bez duplikatow; drukuje wersje formatu.
Statyczna kontrola zrodel - to samo w runtime robi PNLog.VerifyFormatOnce przy pierwszym wierszu.
Uzycie: python check_columns.py   (kod wyjscia 1 przy niezgodnosci)"""
import io, re, sys

ROOT = 'D:/Games/RimWorld/Mods/ProceduralNarrator/Source/ProceduralNarrator/'
pn = io.open(ROOT + 'Integration/PNLog.cs', encoding='utf-8').read()
nd = io.open(ROOT + 'Core/Decision/NarratorDecision.cs', encoding='utf-8').read()

decl_block = pn[pn.index('private static readonly string[] DataColumns'):]
decl_block = decl_block[:decl_block.index('};')]
declared = re.findall(r'"([A-Za-z]+)"', decl_block)

data = pn[pn.index('public static void Data('):]
data = data[:data.index('VerifyFormatOnce(linia);')]
pre = re.findall(r'Append\(sb, "([A-Za-z]+)"', data)

frag = nd[nd.index('public string ToDataFragment()'):]
frag = frag[:frag.index('return sb.ToString();')]
dec = []
for m in re.finditer(r'Append\(sb, "([A-Za-z]+)"|AppendFactor\(sb, [^,]+, nameof\(ScoringWeights\.([A-Za-z]+)\)\)'
                     r'|AppendFactorAs\(sb, p, [^,]+, (Col[A-Za-z]+)\)', frag):
    if m.group(1):
        dec.append(m.group(1))
    elif m.group(2):
        dec.append(m.group(2))
    else:
        const = m.group(3)
        dec.append(re.search(const + r'\s*=\s*"([A-Za-z]+)"', nd).group(1))

built = pre + dec
ok = declared == built
print('zadeklarowanych', len(declared), '| budowanych', len(built), '| zgodne:', ok)
if not ok:
    for i, (a, b) in enumerate(zip(declared, built)):
        if a != b:
            print('pierwsza roznica na pozycji', i, a, b)
            break
dups = set(x for x in declared if declared.count(x) > 1)
print('duplikaty:', dups or 'brak')
print('wersja formatu:', re.search(r'DataFormatVersion = (\d+)', pn).group(1))
sys.exit(0 if ok and not dups else 1)
