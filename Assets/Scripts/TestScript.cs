using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestScript : MonoBehaviour
{
    private List<int> m_List = new List<int>();
    // Update is called once per frame
    void Update()
    {
        m_List.Add(5);
        m_List.Clear();
        m_List.DoThings();
    }
}

public static class ExtensionMethods
{
    public static void DoThings<T>(this List<T> xs) where T : struct {
        xs.Add(default);
    }
}
