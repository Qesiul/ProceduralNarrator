using System.Collections.Generic;
using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Arcs
{
    /// <summary>
    /// Stan frakcji zwiazanej z lukiem, widziany przez rdzen. Integration wypelnia go przy
    /// kazdej obserwacji; rdzen nie zna typu Faction.
    /// </summary>
    public struct FactionStatus
    {
        public bool Exists;
        public bool Hostile;
        public bool Defeated;
    }

    /// <summary>
    /// Obserwacja swiata dla lukow - lekka, bez losowosci, budowana przy KAZDYM wywolaniu compa
    /// (co 1000 tickow), nie tylko w turach decyzji (decyzja autora nr 5). Celowo osobny typ
    /// niz WorldSnapshot: luk potrzebuje kilku liczb, a pelny snapshot liczy m.in. punkty
    /// zagrozenia i dach gorski - drozej i z ryzykiem, ze przyszla zmiana gry wciagnie losowanie.
    /// </summary>
    public sealed class ArcObservation
    {
        public int Tick;
        public float GameDay;

        /// <summary>Licznik strat kolonistow tej mapy (zgony + porwania) od poczatku rozgrywki.</summary>
        public int ColonistLosses;

        /// <summary>Ilu kolonistow przetrzymuja dzis wrogie frakcje.</summary>
        public int KidnappedCount;

        public DangerLevel Danger;

        /// <summary>Stan frakcji zwiazanych z aktywnymi lukami, po identyfikatorze.</summary>
        public Dictionary<string, FactionStatus> Factions = new Dictionary<string, FactionStatus>();

        public FactionStatus StatusOf(string factionId)
        {
            FactionStatus s;
            if (factionId != null && Factions != null && Factions.TryGetValue(factionId, out s))
            {
                return s;
            }
            return new FactionStatus { Exists = false };
        }
    }

    /// <summary>
    /// Straznik przejscia luku (decyzja autora nr 5: reakcja na gracza przez warunki na stanie
    /// swiata i na ROZNICACH wzgledem stanu zapamietanego przy wejsciu w faze). Typy wskazywane
    /// w XML przez Class= z pelna nazwa (ProceduralNarrator.Core.Arcs.Guard_*), tak jak warunki
    /// klockow. Bez Harmony: wszystko, co czytaja, daje obserwacja.
    /// </summary>
    public abstract class ArcGuard
    {
        public abstract bool Holds(ArcObservation now, ArcInstance arc);

        public virtual string Describe()
        {
            return GetType().Name;
        }
    }

    /// <summary>Od wejscia w faze kolonia stracila co najmniej `min` kolonistow (zgon albo porwanie).</summary>
    public class Guard_ColonistsLost : ArcGuard
    {
        public int min = 1;

        public override bool Holds(ArcObservation now, ArcInstance arc)
        {
            return now != null && arc != null && now.ColonistLosses - arc.BaseColonistLosses >= min;
        }

        public override string Describe()
        {
            return "Guard_ColonistsLost(" + min + ")";
        }
    }

    /// <summary>
    /// Frakcja zwiazana z lukiem ISTNIEJE, nie jest pokonana i NIE jest juz wroga - np. gracz
    /// zawarl z nia pokoj. Pokonana albo usunieta frakcja to osobny przypadek (Guard_BoundFactionGone).
    /// </summary>
    public class Guard_BoundFactionNotHostile : ArcGuard
    {
        public override bool Holds(ArcObservation now, ArcInstance arc)
        {
            if (now == null || arc == null || string.IsNullOrEmpty(arc.BoundFactionId))
            {
                return false;
            }
            FactionStatus s = now.StatusOf(arc.BoundFactionId);
            return s.Exists && !s.Defeated && !s.Hostile;
        }
    }

    /// <summary>Frakcja zwiazana z lukiem przestala istniec albo zostala pokonana.</summary>
    public class Guard_BoundFactionGone : ArcGuard
    {
        public override bool Holds(ArcObservation now, ArcInstance arc)
        {
            if (now == null || arc == null || string.IsNullOrEmpty(arc.BoundFactionId))
            {
                return false;
            }
            FactionStatus s = now.StatusOf(arc.BoundFactionId);
            return !s.Exists || s.Defeated;
        }
    }

    /// <summary>Od wejscia w faze liczba porwanych kolonistow spadla o co najmniej `min` (okup, odbicie).</summary>
    public class Guard_KidnappedDropped : ArcGuard
    {
        public int min = 1;

        public override bool Holds(ArcObservation now, ArcInstance arc)
        {
            return now != null && arc != null && arc.BaseKidnapped - now.KidnappedCount >= min;
        }

        public override string Describe()
        {
            return "Guard_KidnappedDropped(" + min + ")";
        }
    }

    /// <summary>
    /// Zagrozenie minelo: od wejscia w faze zagrozenie na mapie siegnelo co najmniej `peakAtLeast`,
    /// a teraz jest None. Szczyt sledzi dyrektor lukow przy kazdej obserwacji (ArcInstance.PeakDanger).
    /// Nie odroznia odparcia od ucieczki napastnikow - i tekst przejscia nie moze ich odrozniac.
    /// </summary>
    public class Guard_DangerPassed : ArcGuard
    {
        public DangerLevel peakAtLeast = DangerLevel.High;

        public override bool Holds(ArcObservation now, ArcInstance arc)
        {
            return now != null && arc != null
                   && (int)arc.PeakDanger >= (int)peakAtLeast
                   && now.Danger == DangerLevel.None;
        }

        public override string Describe()
        {
            return "Guard_DangerPassed(" + peakAtLeast + ")";
        }
    }
}
