﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogFlow.DataModel.Algorithm
{
    public class IdentifierCache<T> : IReadOnlyList<T>
    {
        private List<T> cache = new List<T>();
        private Dictionary<T, int> idCache = new Dictionary<T, int>();

        public int Count => this.cache.Count;

        public IEnumerator<T> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public int Put(T t)
        {
            if (this.idCache.TryGetValue(t, out int id))
            {
                return id;
            }

            this.cache.Add(t);
            return this.idCache[t] = this.cache.Count - 1;
        }

        public T this[int index]
        {
            get { return this.cache[index]; }
        }
    }
}
