using ProceduralNarrator.Core.Model;

namespace ProceduralNarrator.Core.Composition
{
    /// <summary>
    /// Prog sita DOKLADNEGO punktow zagrozenia (krok 9, K2). Gra odrzuca incydent, gdy punkty sa ponizej
    /// IncidentDef.minThreatPoints (bazowy IncidentWorker.CanFireNow) ALBO ponizej minimum, ktore sprawdza sam worker
    /// (Block.WorkerMinPoints - np. roje trupow: punkty &lt; 40). Prog to wieksza z obu wartosci; 0 = bez progu.
    /// Integracja (IncidentParmsBuilder) porownuje z nim punkty kandydata juz przemnozone przez intensywnosc.
    /// </summary>
    public static class PointsSieve
    {
        public static float EffectiveMinimum(float defMinimum, ComposedEvent e)
        {
            float prog = defMinimum > 0f ? defMinimum : 0f;
            Block akcja = ActionOf(e);
            if (akcja != null && akcja.WorkerMinPoints > prog)
            {
                prog = akcja.WorkerMinPoints;
            }
            return prog;
        }

        private static Block ActionOf(ComposedEvent e)
        {
            if (e == null || e.Blocks == null)
            {
                return null;
            }
            for (int i = 0; i < e.Blocks.Count; i++)
            {
                if (e.Blocks[i] != null && e.Blocks[i].Type == BlockType.Action)
                {
                    return e.Blocks[i];
                }
            }
            return null;
        }
    }
}
