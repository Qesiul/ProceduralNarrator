using ProceduralNarrator.Core.Model;
using ProceduralNarrator.Core.Util;

namespace ProceduralNarrator.Core.PlayerModel
{
    /// <summary>
    /// KIERUNEK stylu (decyzje autora nr 2 i 13): d = clamp(wo*o + wr*r) w [-1, 1].
    ///   o - orientacja osobowosci narratora (Powsciagliwy +1, Zrownowazony 0, Napastliwy -1; XML profilu),
    ///   r - rytm krzywej (oddech +1, utrzymanie 0, eskalacja -1),
    ///   wo/wr - wagi 0.3/0.7 (rytm wazniejszy - wybor autora wbrew rekomendacji 0.6/0.4).
    /// d &gt; 0: gra na MOCNE strony gracza (wiecej tego, co lubi; proby w dziedzinie, w ktora inwestuje);
    /// d &lt; 0: gra na SLABE strony (ciosy tam, gdzie slaby; pomoc tam, gdzie ma braki); 0: styl nie dziala.
    ///
    /// Intencja jest ta PO regule kryzysu (kryzys wymusza oddech, wiec r = +1). Intent.Pass jest
    /// zarezerwowany i nigdy nie zwracany; gdyby sie pojawil, traktujemy go jak oddech - tak samo
    /// jak ArcIntentRules.
    /// </summary>
    public static class StyleDirection
    {
        public static float Rhythm(Intent intent)
        {
            switch (intent)
            {
                case Intent.Escalate: return -1f;
                case Intent.Hold: return 0f;
                default: return 1f;
            }
        }

        public static float Compute(float orientation, Intent intent, PlayerStyleParams p)
        {
            if (p == null)
            {
                p = PlayerStyleParams.Default();
            }
            float o = float.IsNaN(orientation) ? 0f : orientation;
            return Curves.ClampSigned(p.orientationWeight * o + p.rhythmWeight * Rhythm(intent));
        }
    }
}
