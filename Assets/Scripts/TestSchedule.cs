using System.Collections.Generic;
using UnityEngine;

public class TestSchedule : MonoBehaviour
{
    private List<HarmlessSystem> m_Systems = new List<HarmlessSystem>();

    private void Start(){
        var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
        for (int i = 0; i < 1000; i++) {
            m_Systems.Add(w.AddSystem(new HarmlessSystem()));
        }
    }

    private void Update() {
        foreach (var s in m_Systems)
            s.Update();
    }
}
