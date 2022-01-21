using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace AsmExplorer.Profiler.Tests
{
    public class ComputeTreeIndicesTests
    {
        struct TestNode
        {
            public TestNode(int parent)
            {
                Depth = -1;
                Parent = parent;
            }
            public int Depth;
            public int Parent;
        }

        struct TestTree : ITree<TestNode>
        {
            public int GetDepth(ref TestNode data) => data.Depth;

            public int GetParent(ref TestNode data) => data.Parent;
        }

        static void RunComputeTreeIndices(TestNode[] nodes, out TreeIndex tree, Allocator allocator = Allocator.TempJob)
        {
            using (var tmpNodes = new NativeArray<TestNode>(nodes, Allocator.TempJob))
            {
                var job = new ComputeTreeIndices<TestNode, TestTree>
                {
                    Tree = new TestTree(),
                    Data = tmpNodes,
                };
                job.AllocateTreeIndex(Allocator.Persistent);
                job.Run();
                tree = job.Indices;
            }
        }

        static void ComputeDepth(TestNode[] nodes)
        {
            bool hasChanges;
            do
            {
                hasChanges = false;
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (nodes[i].Parent == -1)
                    {
                        if (nodes[i].Depth != 1)
                        {
                            nodes[i].Depth = 1;
                            hasChanges = true;
                        }
                    }
                    else
                    {
                        int d = nodes[nodes[i].Parent].Depth;
                        if (d != -1 && nodes[i].Depth != d + 1)
                        {
                            nodes[i].Depth = d + 1;
                            hasChanges = true;
                        }
                    }

                }
            } while (hasChanges);
        }

        [Test]
        public void SimpleTreeWorks()
        {
            var nodes = new[] {
                new TestNode(1),
                new TestNode(-1),
                new TestNode(0),
            };
            ComputeDepth(nodes);

            RunComputeTreeIndices(nodes, out var tree);
            using (tree)
            {
                Assert.AreEqual(3, tree.ChildToDataIndex.Length);
                Assert.AreEqual(4, tree.DataIndexToChildren.Length);

                Assert.AreEqual(1, tree.ChildToDataIndex[0]);
                Assert.AreEqual(0, tree.ChildToDataIndex[1]);
                Assert.AreEqual(2, tree.ChildToDataIndex[2]);
                Assert.AreEqual(new TreeIndex.Children { Offset = 0, Count = 1 }, tree.Root);
                Assert.AreEqual(new TreeIndex.Children { Offset = 2, Count = 1 }, tree.LookUpChildren(0));
                Assert.AreEqual(new TreeIndex.Children { Offset = 1, Count = 1 }, tree.LookUpChildren(1));
                Assert.AreEqual(new TreeIndex.Children { Offset = 0, Count = 0 }, tree.LookUpChildren(2));
            }
        }
    }
}