using System;
using System.Globalization;

namespace ProceduralNarrator.Core.Model
{
    /// <summary>
    /// Jeden wpis pamieci narratora: zdarzenie, ktore narrator postanowil wyemitowac.
    ///
    /// TRZY ZNACZNIKI CZASU, bo mierza trzy rozne rzeczy i ZADNEGO nie da sie wyliczyc
    /// z pozostalych:
    ///   DecisionIndex - numer decyzji. Po nim starzeje sie czynnik swiezosci. Jest odporny
    ///                   na zmiane tempa narratora: dwie decyzje wstecz to dwie decyzje wstecz
    ///                   niezaleznie od tego, czy dzielilo je pol dnia, czy piec dni.
    ///   GameDay       - dzien gry jako FLOAT. Po nim liczy sie gestosc zdarzen (uzytecznosc
    ///                   PASS-u) i wiek rytmu. Musi byc floatem, bo odstepy miedzy decyzjami
    ///                   to ulamki dnia, a WorldSnapshot.DaysPassed (int) sklejalby wszystkie
    ///                   zdarzenia jednego dnia w jeden moment.
    ///   GameTick      - WYLACZNIE log i analiza w Pythonie. Zaden wzor go nie uzywa.
    ///
    /// POLA IsPass NIE MA. Decyzje PASS nie zajmuja slotu w buforze - licza je skalary
    /// w EventHistory. Wpis PASS wniosl by do sredniej rytmu smieciowy punkt (Neutral/Minor),
    /// ktorego nikt nie zaobserwowal, i wypychalby z okna 24 wpisow realne zdarzenia.
    ///
    /// KLUCZEM SWIEZOSCI JEST ActionBlockId, nie ActionPayload. Payload (defName incydentu)
    /// nie musi byc roznowartosciowy - dwa klocki akcji moga kiedys wskazac ten sam
    /// IncidentDef i wtedy os akcji przestalaby je rozrozniac. Payload jest tu wylacznie
    /// do logu i diagnostyki.
    /// </summary>
    public class EventHistoryEntry
    {
        /// <summary>Separator pol w postaci tekstowej. Nie moze wystapic w zadnym polu - patrz Escape().</summary>
        public const char FieldSeparator = '|';

        /// <summary>
        /// Liczba pol w linii Encode(). Stala, bo TryDecode odrzuca kazda linie o innej dlugosci -
        /// to jedyna obrona przed cichym przesunieciem sie kolumn po zmianie formatu.
        /// </summary>
        public const int FieldCount = 8;

        /// <summary>Zastepnik pola pustego. Ten sam znak co brak klocka w ComposedEvent.Signature.</summary>
        public const string EmptyToken = "-";

        /// <summary>Numer decyzji, w ktorej zdarzenie zostalo wyemitowane (0-based).</summary>
        public int DecisionIndex;

        /// <summary>Dzien gry w chwili emisji (TicksGame / 60000f).</summary>
        public float GameDay;

        /// <summary>Tick gry w chwili emisji - tylko log i analiza, zaden wzor tego nie czyta.</summary>
        public int GameTick;

        /// <summary>Block.Id klocka akcji, np. "PN_Akcja_Napad". Klucz osi akcji w czynniku swiezosci.</summary>
        public string ActionBlockId;

        /// <summary>defName incydentu - tylko log i diagnostyka. NIE jest kluczem swiezosci.</summary>
        public string ActionPayload;

        /// <summary>Os tematu - czyta ja czynnik swiezosci.</summary>
        public Theme Theme;

        /// <summary>Os walencji - czyta ja czynnik kontrastu dramaturgicznego.</summary>
        public Valence Valence;

        /// <summary>Os skali - czyta ja czynnik kontrastu dramaturgicznego.</summary>
        public EventScale Scale;

        /// <summary>Konstruktor bezparametrowy - wymagany przez TryDecode.</summary>
        public EventHistoryEntry()
        {
        }

        /// <summary>
        /// Postac tekstowa wpisu: jedna linia, osiem pol rozdzielonych znakiem '|'.
        /// Format: decisionIndex|gameDay|gameTick|actionBlockId|actionPayload|Theme|Valence|Scale
        ///
        /// Jedyny float w linii jest zapisywany formatem "G9" przy CultureInfo.InvariantCulture.
        /// Dwie decyzje, obie celowe:
        ///  - InvariantCulture, bo przy polskiej kulturze separatorem dziesietnym jest przecinek,
        ///    ktory rozjechalby zarowno wlasny dekoder, jak i parser danych badawczych w Pythonie;
        ///  - "G9" zamiast "R", mimo ze specyfikacja pisze "R". Format "R" na .NET Framework nie
        ///    gwarantuje round-tripu dla typu Single (znany blad platformy, naprawiony dopiero
        ///    w .NET Core 3.0); dziewiec cyfr znaczacych round-trip dla Single gwarantuje zawsze.
        ///    Intencja specyfikacji - "zapis odwracalny i niezalezny od kultury" - jest zachowana
        ///    co do joty, zmienia sie tylko sposob jej zapisania.
        /// </summary>
        public string Encode()
        {
            return string.Join(FieldSeparator.ToString(), new[]
            {
                DecisionIndex.ToString(CultureInfo.InvariantCulture),
                GameDay.ToString("G9", CultureInfo.InvariantCulture),
                GameTick.ToString(CultureInfo.InvariantCulture),
                Escape(ActionBlockId),
                Escape(ActionPayload),
                Theme.ToString(),
                Valence.ToString(),
                Scale.ToString()
            });
        }

        /// <summary>
        /// Odtwarza wpis z linii Encode(). NIGDY nie rzuca wyjatkiem - kazda porazka to
        /// zwrocone false i entry rowne null.
        ///
        /// Powod takiego kontraktu: dekodowanie dzieje sie podczas WCZYTYWANIA ZAPISU GRY
        /// (krok 6). Wyjatek w tym miejscu wywala wczytywanie calej rozgrywki z powodu jednej
        /// uszkodzonej linijki pamieci narratora, a to jest kara niewspolmierna do szkody.
        /// Zmiana nazwy wartosci enuma po patchu gry ma dac odrzucony wpis, nie utracony zapis;
        /// liczbe odrzuconych raportuje EventHistory.RestoreFromLines, a loguje ja Integration.
        /// </summary>
        public static bool TryDecode(string line, out EventHistoryEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            string[] parts = line.Split(FieldSeparator);
            if (parts.Length != FieldCount)
            {
                return false;
            }

            int decisionIndex;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out decisionIndex))
            {
                return false;
            }

            float gameDay;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out gameDay))
            {
                return false;
            }

            // "NaN" i "Infinity" sa poprawnym wejsciem dla float.TryParse, a wpuszczone dalej
            // zatrulyby sume gestosci zdarzen (NaN propaguje przez kazde dodawanie i przezywa
            // kazde porownanie). Uszkodzona linia ma zostac odrzucona, a nie zamieniona
            // w cicha awarie calego czynnika PASS.
            if (float.IsNaN(gameDay) || float.IsInfinity(gameDay))
            {
                return false;
            }

            int gameTick;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out gameTick))
            {
                return false;
            }

            Theme theme;
            if (!TryParseEnum(parts[5], out theme))
            {
                return false;
            }

            Valence valence;
            if (!TryParseEnum(parts[6], out valence))
            {
                return false;
            }

            EventScale scale;
            if (!TryParseEnum(parts[7], out scale))
            {
                return false;
            }

            entry = new EventHistoryEntry
            {
                DecisionIndex = decisionIndex,
                GameDay = gameDay,
                GameTick = gameTick,
                ActionBlockId = Unescape(parts[3]),
                ActionPayload = Unescape(parts[4]),
                Theme = theme,
                Valence = valence,
                Scale = scale
            };
            return true;
        }

        public override string ToString()
        {
            return "dec=" + DecisionIndex.ToString(CultureInfo.InvariantCulture)
                   + " dzien=" + GameDay.ToString("0.00", CultureInfo.InvariantCulture)
                   + " " + (ActionBlockId ?? EmptyToken)
                   + " [" + Theme + "/" + Valence + "/" + Scale + "]";
        }

        /// <summary>
        /// Przygotowuje pole tekstowe do zapisu: wartosc pusta zastepuje znacznikiem,
        /// a separator - ukosnikiem.
        ///
        /// Podmiana separatora nie jest paranoja: Payload pochodzi z XML-a, wiec jego tresc
        /// jest poza kontrola kodu. Jeden znak '|' w defNamie rozjechalby liczbe pol i po cichu
        /// uniewaznil CALY wpis przy wczytaniu (TryDecode odrzuca po dlugosci). Kodowanie
        /// stratne jest tu wlasciwe, bo pole jest wylacznie diagnostyczne - klucz swiezosci
        /// (ActionBlockId) ma wlasny, kontrolowany przez nas prefiks PN_.
        /// </summary>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return EmptyToken;
            }
            return value.IndexOf(FieldSeparator) >= 0
                ? value.Replace(FieldSeparator, '/')
                : value;
        }

        private static string Unescape(string value)
        {
            return string.IsNullOrEmpty(value) || value == EmptyToken ? null : value;
        }

        /// <summary>
        /// Parsuje wartosc enuma po NAZWIE, z rozroznianiem wielkosci liter i z odrzuceniem
        /// wartosci spoza zakresu.
        ///
        /// Sam Enum.TryParse nie wystarczy z dwoch powodow. Po pierwsze przepuszcza zapis
        /// LICZBOWY ("17" dla Theme daje wartosc 17, ktorej w typie nie ma) - stad Enum.IsDefined.
        /// Po drugie w trybie ignorujacym wielkosc liter zaczelyby przechodzic linie zapisane
        /// w innej konwencji przez cudze narzedzie, co jest cichym rozluznieniem formatu -
        /// stad jawne false w drugim argumencie.
        /// </summary>
        private static bool TryParseEnum<T>(string text, out T value) where T : struct
        {
            value = default(T);
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            T parsed;
            if (!Enum.TryParse(text, false, out parsed))
            {
                return false;
            }
            if (!Enum.IsDefined(typeof(T), parsed))
            {
                return false;
            }

            value = parsed;
            return true;
        }
    }
}
