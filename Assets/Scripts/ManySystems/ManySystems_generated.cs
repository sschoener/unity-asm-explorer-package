using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[DisableAutoCreation]
[AlwaysUpdateSystem]
class HarmlessSystem : SystemBase
{
    protected override void OnUpdate()
    {
        Entities.ForEach((ref Translation t) => { t.Value += new float3(1);}).ScheduleParallel();
    }
}