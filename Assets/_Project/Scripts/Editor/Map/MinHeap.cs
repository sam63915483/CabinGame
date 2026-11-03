namespace Cabin.Editor.Map
{
    /// <summary>Binary min-heap of (key, cell).</summary>
    public sealed class MinHeap
    {
        readonly float[] keys;
        readonly int[] vals;
        public int Count { get; private set; }

        public MinHeap(int capacity)
        {
            keys = new float[capacity];
            vals = new int[capacity];
        }

        public void Push(float key, int val)
        {
            int i = Count++;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (keys[p] <= key) break;
                keys[i] = keys[p];
                vals[i] = vals[p];
                i = p;
            }
            keys[i] = key;
            vals[i] = val;
        }

        public int Pop()
        {
            int top = vals[0];
            float key = keys[--Count];
            int val = vals[Count];
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1;
                if (l >= Count) break;
                int r = l + 1;
                int m = r < Count && keys[r] < keys[l] ? r : l;
                if (keys[m] >= key) break;
                keys[i] = keys[m];
                vals[i] = vals[m];
                i = m;
            }
            keys[i] = key;
            vals[i] = val;
            return top;
        }
    }
}
