using System.Collections.Generic;

namespace Cabin.Build
{
    /// <summary>Connected components of pieces joined by nails. Pure, so structures can be tested without physics.</summary>
    public static class NailGraph
    {
        public static List<List<int>> Components(IEnumerable<int> pieceIds, IEnumerable<(int a, int b)> nails)
        {
            var parent = new Dictionary<int, int>();
            foreach (int id in pieceIds) parent[id] = id;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            foreach (var (a, b) in nails)
            {
                if (!parent.ContainsKey(a) || !parent.ContainsKey(b)) continue;
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[ra] = rb;
            }

            var groups = new Dictionary<int, List<int>>();
            var order = new List<int>();
            foreach (int id in new List<int>(parent.Keys))
            {
                int r = Find(id);
                if (!groups.TryGetValue(r, out var list))
                {
                    groups[r] = list = new List<int>();
                    order.Add(r);
                }
                list.Add(id);
            }
            var result = new List<List<int>>();
            foreach (int r in order) result.Add(groups[r]);
            return result;
        }
    }
}
