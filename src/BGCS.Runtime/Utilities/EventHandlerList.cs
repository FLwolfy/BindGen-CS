using System.Collections.Generic;
using System.Threading;

namespace BGCS.Runtime;

using System;
using System.Collections;

internal class EventHandlerList<T> : IEnumerable<T> where T : Delegate
{
    private readonly List<T> m_delegates = [];
    private readonly Lock m_lock = new();
    public void Add(T value)
    {
        lock (this.m_lock)
        {
            this.m_delegates.Add(value);
        }
    }

    public void Remove(T value)
    {
        lock (this.m_lock)
        {
            this.m_delegates.Remove(value);
        }
    }

    public void Invoke<TUserdata>(
        TUserdata userdata,
        Func<T, TUserdata, bool> action
    ) {
        foreach (var item in this.m_delegates)
        {
            if (action.Invoke(item, userdata))
            {
                break;
            }
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        return this.m_delegates.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
