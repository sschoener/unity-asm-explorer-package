using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace AsmExplorer.Profiler
{
    [BurstCompile]
    struct SortTree<T> : IJobParallelFor where T : IComparer<int>
    {
        public T Sorter;
        public TreeIndex Tree;

        public SortTree(T sorter)  {
            Sorter = sorter;
            Tree = default;
        }

        public unsafe void Execute(int index)
        {
            var children = Tree.DataIndexToChildren[index];
            if (children.Count <= 1)
                return;
            var ptr = (int*)Tree.ChildToDataIndex.GetUnsafePtr();
            NativeSortExtension.Sort(ptr + children.Offset, children.Count, Sorter);
        }
    }
}
