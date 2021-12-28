using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BundleSystem
{
    public static partial class BundleManager
    {
        class IndexedDictionary<TKey, TValue>
        {
            public int CurrentIndex { get; private set; } = -1;


            List<KeyValuePair<TKey, TValue>> m_InnerList;
            Dictionary<TKey, int> m_KeyDictionary;

            public int Count => m_InnerList.Count;

            public TValue this[TKey key]
            {
                get => m_InnerList[m_KeyDictionary[key]].Value;
                set
                {
                    if (m_KeyDictionary.TryGetValue(key, out var index)) m_InnerList[index] = new KeyValuePair<TKey, TValue>(key, value);
                    else Add(key, value);
                }
            }

            public KeyValuePair<TKey, TValue> GetAtIndex(int index) => m_InnerList[index];

            public IndexedDictionary(int capacity)
            {
                m_InnerList = new List<KeyValuePair<TKey, TValue>>(capacity);
                m_KeyDictionary = new Dictionary<TKey, int>(capacity);
            }

            public void FillNormalDictionary(Dictionary<TKey, TValue> targetDict)
            {
                for (int i = 0; i < m_InnerList.Count; i++)
                {
                    targetDict.Add(m_InnerList[i].Key, m_InnerList[i].Value);
                }
            }

            public bool TryGetValue(TKey key, out TValue value)
            {
                if (!m_KeyDictionary.TryGetValue(key, out var index))
                {
                    value = default;
                    return false;
                }
                value = m_InnerList[index].Value;
                return true;
            }

            public bool ContainsKey(TKey key) => m_KeyDictionary.ContainsKey(key);

            public void Add(TKey key, TValue value)
            {
                m_KeyDictionary.Add(key, m_InnerList.Count); //add key with list count
                m_InnerList.Add(new KeyValuePair<TKey, TValue>(key, value)); //index will be list count
            }

            public bool Remove(TKey key)
            {
                if (!m_KeyDictionary.TryGetValue(key, out var index)) return false;
                if (Count == 1) //only one
                {
                    m_InnerList.Clear();
                    m_KeyDictionary.Clear();
                }
                else if (index == Count - 1) //last index
                {
                    m_InnerList.RemoveAt(m_InnerList.Count - 1); //remote last
                    m_KeyDictionary.Remove(key); //remove key
                }
                else
                {
                    m_InnerList[index] = m_InnerList[m_InnerList.Count - 1]; //last object to remove index
                    m_InnerList.RemoveAt(m_InnerList.Count - 1); //remove last as it's duplicated
                    m_KeyDictionary[m_InnerList[index].Key] = index; //update last index's key
                    m_KeyDictionary.Remove(key); //remove key
                }
                return true;
            }

            public void ResetCurrentIndex()
            {
                CurrentIndex = -1;
            }

            public bool TryGetNext(out KeyValuePair<TKey, TValue> value)
            {
                if (Count == 0)
                {
                    value = default;
                    return false;
                }

                CurrentIndex = ++CurrentIndex % m_InnerList.Count;
                value = m_InnerList[CurrentIndex];
                return true;
            }
            
            public void Clear()
            {
                ResetCurrentIndex();
                m_InnerList.Clear();
                m_KeyDictionary.Clear();
            }
        }
    }
}
