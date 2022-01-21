using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace AsmExplorer.Profiler
{
    interface ITree<T>
    {
        int GetParent(ref T data);
        int GetDepth(ref T data);
    }

    struct TreeIndex : System.IDisposable
    {
        // Records for every entry how many children there are and where they begin in the data indices array.
        // The array has length [length of input data + 1]. The element at position 0 is the root of the tree,
        // all other elements are at position [1 + their index in the input data].
        // The offset in the child data is an index into the ChildToDataIndex array.
        public NativeArray<Children> DataIndexToChildren;
        // Records for every entry which data item belongs to it. This array has the length of the input data.
        // The entries in this array are indices into the data you used to create the tree.
        public NativeArray<int> ChildToDataIndex;

        public Children Root => DataIndexToChildren[0];
        public Children LookUpChildren(int dataIndex) => DataIndexToChildren[dataIndex + 1];

        public void Dispose()
        {
            if (ChildToDataIndex.IsCreated)
                ChildToDataIndex.Dispose();
            if (DataIndexToChildren.IsCreated)
                DataIndexToChildren.Dispose();
        }

        [DebuggerDisplay("Offset = {Offset}, Count = {Count}")]
        public struct Children
        {
            public int Offset;
            public int Count;

            public override string ToString() => $"Offset = {Offset}, Count = {Count}";
        }
    }

    [BurstCompile]
    unsafe struct ComputeTreeIndices<TElem, TTree> : IJob where TElem : unmanaged where TTree : ITree<TElem>
    {
        // Inputs
        [ReadOnly]
        public NativeArray<TElem> Data;
        public TTree Tree;

        // Outputs
        public TreeIndex Indices;

        public void Execute()
        {
            // Insert a root element at the first index.
            var indices = (int*)Indices.ChildToDataIndex.GetUnsafePtr();
            for (int i = 0, n = Data.Length; i < n; i++)
                indices[i] = i;

            // Sort the data indices by depth first, then by caller index.
            var stacks = (TElem*)Data.GetUnsafeReadOnlyPtr();
            NativeSortExtension.Sort(indices, Data.Length, new Comp
            {
                Stacks = stacks,
                Tree = Tree
            });

            // Go over all indices and collect all ranges of consecutive items with the same parent.
            var children = (TreeIndex.Children*)Indices.DataIndexToChildren.GetUnsafePtr();
            int lastCaller = 0;
            int runStart = 0;
            for (int i = 0, n = Data.Length; i < n; i++)
            {
                int caller = 1 + Tree.GetParent(ref stacks[indices[i]]);
                if (lastCaller != caller)
                {
                    children[lastCaller].Offset = runStart;
                    children[lastCaller].Count = i - runStart;
                    runStart = i;
                    lastCaller = caller;
                }
            }
            children[lastCaller].Offset = runStart;
            children[lastCaller].Count = Data.Length - runStart;
        }

        public void AllocateTreeIndex(Allocator allocator)
        {
            Debug.Assert(Data.IsCreated);
            Indices = new TreeIndex
            {
                DataIndexToChildren = new NativeArray<TreeIndex.Children>(Data.Length + 1, allocator),
                ChildToDataIndex = new NativeArray<int>(Data.Length, allocator)
            };
        }

        private struct Comp : IComparer<int>
        {
            [NativeDisableUnsafePtrRestriction]
            public TElem* Stacks;
            public TTree Tree;

            public int Compare(int x, int y)
            {
                ref var lhs = ref Stacks[x];
                ref var rhs = ref Stacks[y];
                int lhsDepth = Tree.GetDepth(ref lhs);
                int rhsDepth = Tree.GetDepth(ref rhs);
                if (lhsDepth < rhsDepth)
                    return -1;
                if (lhsDepth > rhsDepth)
                    return 1;
                return Tree.GetParent(ref lhs) > Tree.GetParent(ref rhs) ? 1 : -1;
            }
        }
    }
}
