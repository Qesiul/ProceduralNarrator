using System;
using System.Globalization;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Arcs
{
    /// <summary>
    /// Stan jednego otwartego luku na jednej mapie - "watek" z modelu danych koncepcji
    /// (identyfikator, status, faza luku). Zamkniety luk nie ma instancji; zostaje po nim tylko
    /// dzien zamkniecia w ArcLedger (odstep przed ponownym otwarciem) i linia [PN-ARC] w danych.
    ///
    /// FAZA ZAPISYWANA PO ID, NIE PO INDEKSIE. Indeks przesunalby sie po cichu, gdyby autor tresci
    /// dopisal faze w srodku luku - a zapis gry przezywa zmiane Defow. Nieznany luk albo faza
    /// przy wczytaniu to odrzucenie instancji z logiem, nie cicha zamiana na inna faze.
    /// </summary>
    public sealed class ArcInstance
    {
        public const char FieldSeparator = '|';
        public const string EmptyToken = "-";

        /// <summary>Znacznik typu linii w pamieci gry (ArcLedger.ToPersistableLines).</summary>
        public const string LineTag = "A";

        /// <summary>Liczba pol linii WLACZNIE ze znacznikiem - TryDecode odrzuca kazda inna.</summary>
        public const int FieldCount = 11;

        /// <summary>defName luku (NarrativeArcDef).</summary>
        public string ArcId;

        /// <summary>Kolejny numer instancji na mapie - odroznia dwa przebiegi tego samego luku w danych.</summary>
        public int Number;

        /// <summary>Id fazy, na ktorej zdarzenie luk CZEKA (patrz ArcPhase: zasiew spelnia sie otwarciem).</summary>
        public string PhaseId;

        public float OpenedDay;
        public float PhaseEnteredDay;
        public int PhaseEnteredTick;

        /// <summary>Frakcja zwiazana z lukiem (Faction.loadID jako tekst) albo null.</summary>
        public string BoundFactionId;

        /// <summary>Licznik strat kolonistow mapy w chwili wejscia w faze (baza Guard_ColonistsLost).</summary>
        public int BaseColonistLosses;

        /// <summary>Liczba porwanych w chwili wejscia w faze (baza Guard_KidnappedDropped).</summary>
        public int BaseKidnapped;

        /// <summary>Najwyzsze zagrozenie widziane od wejscia w faze (baza Guard_DangerPassed).</summary>
        public DangerLevel PeakDanger;

        public string Encode()
        {
            return string.Join(FieldSeparator.ToString(), new[]
            {
                LineTag,
                Escape(ArcId),
                Number.ToString(CultureInfo.InvariantCulture),
                Escape(PhaseId),
                OpenedDay.ToString("G9", CultureInfo.InvariantCulture),
                PhaseEnteredDay.ToString("G9", CultureInfo.InvariantCulture),
                PhaseEnteredTick.ToString(CultureInfo.InvariantCulture),
                Escape(BoundFactionId),
                BaseColonistLosses.ToString(CultureInfo.InvariantCulture),
                BaseKidnapped.ToString(CultureInfo.InvariantCulture),
                PeakDanger.ToString()
            });
        }

        /// <summary>Nigdy nie rzuca - uszkodzona linia to false (ten sam kontrakt co EventHistoryEntry).</summary>
        public static bool TryDecode(string line, out ArcInstance arc)
        {
            arc = null;
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }
            string[] p = line.Split(FieldSeparator);
            if (p.Length != FieldCount || p[0] != LineTag)
            {
                return false;
            }
            int number, enteredTick, baseLosses, baseKidnapped;
            float openedDay, enteredDay;
            DangerLevel peak;
            if (!Int(p[2], out number) || !Flt(p[4], out openedDay) || !Flt(p[5], out enteredDay)
                || !Int(p[6], out enteredTick) || !Int(p[8], out baseLosses) || !Int(p[9], out baseKidnapped)
                || !EnumName(p[10], out peak))
            {
                return false;
            }
            string arcId = Unescape(p[1]);
            string phaseId = Unescape(p[3]);
            if (arcId == null || phaseId == null)
            {
                return false;
            }
            arc = new ArcInstance
            {
                ArcId = arcId,
                Number = number,
                PhaseId = phaseId,
                OpenedDay = openedDay,
                PhaseEnteredDay = enteredDay,
                PhaseEnteredTick = enteredTick,
                BoundFactionId = Unescape(p[7]),
                BaseColonistLosses = baseLosses,
                BaseKidnapped = baseKidnapped,
                PeakDanger = peak
            };
            return true;
        }

        public override string ToString()
        {
            return ArcId + "#" + Number.ToString(CultureInfo.InvariantCulture) + ":" + PhaseId
                   + (string.IsNullOrEmpty(BoundFactionId) ? string.Empty : " frakcja=" + BoundFactionId);
        }

        // ---- wspolne narzedzia kodeka (uzywa ich tez ArcLedger i PendingExecution) ----

        internal static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return EmptyToken;
            }
            return value.IndexOf(FieldSeparator) >= 0 ? value.Replace(FieldSeparator, '/') : value;
        }

        internal static string Unescape(string value)
        {
            return string.IsNullOrEmpty(value) || value == EmptyToken ? null : value;
        }

        internal static bool Int(string s, out int v)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }

        internal static bool Flt(string s, out float v)
        {
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                return false;
            }
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        /// <summary>Enum po NAZWIE, z rozroznianiem wielkosci liter, bez wartosci liczbowych (jak EventHistoryEntry).</summary>
        internal static bool EnumName<T>(string text, out T value) where T : struct
        {
            value = default(T);
            if (string.IsNullOrEmpty(text) || char.IsDigit(text[0]) || text[0] == '-')
            {
                return false;
            }
            T parsed;
            if (!Enum.TryParse(text, false, out parsed) || !Enum.IsDefined(typeof(T), parsed))
            {
                return false;
            }
            value = parsed;
            return true;
        }
    }
}
