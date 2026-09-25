using System.Globalization;

namespace ProceduralNarrator.Core.PlayerModel
{
    /// <summary>
    /// Czego "dotyczy" zdarzenie w jezyku cech stylu (decyzja autora nr 20): waga kazdej cechy
    /// dla klocka AKCJI. Deklaruje ja WYLACZNIE klocek akcji - ta sama zasada co osie Theme/Valence/
    /// Scale (CLAUDE.md 2.8). Tabele pilnuje walidator (TestsComposition.WagiStyluAkcji): nowa akcja bez
    /// wpisu to blad, a klocek innego typu z niezerowa waga tez.
    /// </summary>
    public class StyleWeights
    {
        public float walka;
        public float gospodarka;
        public float ekspansja;
        public float reaktywnosc;

        public float Get(StyleDimension d)
        {
            switch (d)
            {
                case StyleDimension.Walka: return walka;
                case StyleDimension.Gospodarka: return gospodarka;
                case StyleDimension.Ekspansja: return ekspansja;
                default: return reaktywnosc;
            }
        }

        public float Total()
        {
            return walka + gospodarka + ekspansja + reaktywnosc;
        }

        public bool AnyInvalid()
        {
            for (int d = 0; d < StyleDimensions.Count; d++)
            {
                float v = Get((StyleDimension)d);
                if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f)
                {
                    return true;
                }
            }
            return false;
        }

        public StyleWeights Clone()
        {
            return new StyleWeights { walka = walka, gospodarka = gospodarka, ekspansja = ekspansja, reaktywnosc = reaktywnosc };
        }

        public string Describe()
        {
            return "W" + N(walka) + "/G" + N(gospodarka) + "/E" + N(ekspansja) + "/R" + N(reaktywnosc);
        }

        private static string N(float v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
