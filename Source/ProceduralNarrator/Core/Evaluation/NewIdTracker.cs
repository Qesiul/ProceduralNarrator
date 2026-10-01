using System.Collections.Generic;

namespace ProceduralNarrator.Core.Evaluation
{
    /// <summary>
    /// Nowe identyfikatory miedzy przegladami (listy gracza do [PN-FIRED]; krok 9, etap L). Pierwszy przeglad
    /// tylko zapamietuje stan - listy sprzed wczytania nie sa nowe. Zbior zastepowany co przeglad, wiec usuniete
    /// identyfikatory znikaja i pamiec nie rosnie. Wynik rosnaco.
    /// </summary>
    public sealed class NewIdTracker
    {
        private HashSet<int> znane = new HashSet<int>();
        private bool zainicjowany;

        public List<int> Update(IEnumerable<int> current)
        {
            var teraz = new HashSet<int>();
            if (current != null)
            {
                foreach (int id in current)
                {
                    teraz.Add(id);
                }
            }
            var nowe = new List<int>();
            if (zainicjowany)
            {
                foreach (int id in teraz)
                {
                    if (!znane.Contains(id))
                    {
                        nowe.Add(id);
                    }
                }
                nowe.Sort();
            }
            znane = teraz;
            zainicjowany = true;
            return nowe;
        }

        public void Reset()
        {
            znane = new HashSet<int>();
            zainicjowany = false;
        }
    }
}
