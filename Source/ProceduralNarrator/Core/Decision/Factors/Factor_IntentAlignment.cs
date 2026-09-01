using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Decision
{
    /// <summary>
    /// Czynnik "zgodnosc z intencja narratora" po stronie ZDARZEN.
    ///
    /// ZASLEPKA KROKU 3 - I JEST NIA CELOWO, A NIE PRZEZ NIEDOKONCZENIE.
    /// Intencja (eskaluj / oddech / utrzymaj / cisza) jest wyjsciem KRZYWEJ DRAMATURGICZNEJ,
    /// a ta powstaje dopiero w kroku 4 planu. W kroku 3 warstwa integracji ustawia
    /// DecisionContext.Intent na stale Intent.Hold, wiec czynnik nie ma czego porownywac:
    /// KAZDY kandydat jest tak samo (nie)zgodny z jedyna wystepujaca intencja. Zwracanie
    /// czegokolwiek innego niz stalej bylo by tu wymyslaniem pomiaru, ktorego nie ma.
    ///
    /// DLACZEGO WPINAMY GO JUZ DZIS, SKORO NIC NIE LICZY. Domyslna waga w XML wynosi 0,
    /// a czynnik z waga 0 jest w normalizacji przez sume wag DOKLADNIE neutralny: wnosi 0
    /// do licznika i 0 do mianownika, wiec nie rozciencza pozostalych czynnikow. Zysk jest
    /// w danych badawczych: zestaw i kolejnosc kolumn logu sa identyczne w kroku 3 i 4,
    /// wiec skrypty agregujace w Pythonie (krok 8) nie wymagaja przepisania, a serie
    /// rozgrywek sprzed i po wlaczeniu krzywej daja sie porownac wprost.
    ///
    /// DLACZEGO 0.5, A NIE 1.0 ALBO 0.0. 0.5 jest w tym projekcie umowna wartoscia
    /// "brak informacji" - ta sama zwraca czynnik swiezosci przy pustej historii i czynnik
    /// kontrastu przy braku rytmu. Skrajnosci mialyby konsekwencje nawet przy wadze 0:
    /// gdyby ktos podniosl wage przed krokiem 4, 1.0 podnioslo by uzytecznosc wszystkich
    /// zdarzen wobec PASS-a, a 0.0 obnizylo - i to bez zadnego zwiazku z intencja.
    /// Stala neutralna jest jedyna wartoscia, ktora nie przesuwa niczego w zadna strone.
    ///
    /// UWAGA NA BLIZNIAKA: identycznie nazwany czynnik istnieje w ROZLACZNEJ przestrzeni
    /// czynnikow PASS (zgodnosc CISZY z intencja) i ma wlasna wage w PassScoringParams.
    /// To dwa rozne pytania o niezaleznej kalibracji - nie wolno ich unifikowac.
    /// </summary>
    public class Factor_IntentAlignment : IScoringFactor
    {
        /// <summary>
        /// Nazwa wyprowadzona z nazwy pola wagi (nie literal), zeby niezmiennika
        /// "nazwa czynnika == nazwa pola wagi == nazwa wezla XML" pilnowal kompilator.
        /// Wiaze czynnik z waga ScoringWeights.intentAlignment, a NIE z jego imiennikiem
        /// z przestrzeni PASS.
        /// </summary>
        public const string FactorName = nameof(ScoringWeights.intentAlignment);

        /// <summary>Umowna wartosc "brak informacji", wspolna dla calej warstwy decyzyjnej.</summary>
        public const float NeutralValue = 0.5f;

        public string Name
        {
            get { return FactorName; }
        }

        /// <summary>
        /// Zwraca stala neutralna niezaleznie od kandydata i kontekstu.
        ///
        /// Slad WYMIENIA biezaca intencje, mimo ze wartosc od niej nie zalezy. To nie jest
        /// ozdobnik: gdy krok 4 zacznie ustawiac intencje inne niz Hold, log od razu pokaze,
        /// ze intencje juz plyna, a czynnik jeszcze ich nie czyta - i bedzie widac, ktora
        /// czesc lancucha zostala podmieniona, a ktora nie.
        /// </summary>
        public float Evaluate(ScoredCandidate candidate, DecisionContext context, out string explanation)
        {
            Intent intent = context == null ? Intent.Hold : context.Intent;

            explanation = "zaslepka kroku 3 - krzywa dramaturgiczna wchodzi w kroku 4 (intencja="
                          + intent + ", wartosc neutralna 0.50)";
            return NeutralValue;
        }
    }
}
